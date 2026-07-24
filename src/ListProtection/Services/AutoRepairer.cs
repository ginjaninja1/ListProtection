using ListProtection.Scoring;
using ListProtection.Storage;
using ListProtection.UI.MissingMembers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ListProtection.Services
{
    /// <summary>
    /// Runs automatic repairs after candidate discovery.
    ///
    /// For each missing member, builds a ranked ScoredCandidate list and passes
    /// it to the registered IAutoRepairEligibility gate for that media type.
    /// The gate enforces score threshold, min-distance, and semantic conditions.
    ///
    /// Eligibility gates are registered in _eligibilityGates below.
    /// Adding support for a new media type requires only a new gate registration —
    /// no other code changes.
    ///
    /// Evidence collectors (for re-scoring at repair time) are constructed with
    /// duration tolerances sourced from PluginConfiguration.
    /// </summary>
    internal static class AutoRepairer
    {
        // ── Eligibility gates ──────────────────────────────────────────────

        private static readonly Dictionary<string, IAutoRepairEligibility> _eligibilityGates =
            new Dictionary<string, IAutoRepairEligibility>(StringComparer.OrdinalIgnoreCase)
            {
                { "Audio",   new AudioAutoRepairEligibility()   },
                { "Episode", new EpisodeAutoRepairEligibility() },
                { "Movie",   new MovieAutoRepairEligibility()   },
            };

        internal static async Task RunAutoRepair(
            string targetPlaylistIdN,
            ILibraryManager libraryManager,
            IPlaylistManager playlistManager,
            IUserManager userManager,
            ILogger logger)
        {
            logger.Info(
                "[AutoRepairer] RunAutoRepair starting | target={0}",
                targetPlaylistIdN ?? "ALL");

            try
            {
                var plugin = ListProtectionPlugin.Instance;
                if (plugin == null)
                {
                    logger.Error("[AutoRepairer] Plugin instance is null — aborting");
                    return;
                }

                var config = plugin.Configuration;
                if (config == null || !config.AutoRepairEnabled)
                {
                    logger.Info("[AutoRepairer] Auto-repair is disabled — skipping");
                    return;
                }

                var threshold = config.AutoRepairScoreThreshold;
                var minDistance = config.AutoRepairMinCandidateDistance;

                var missing = plugin.MissingMembersStore.Load();
                var candidates = plugin.CandidateStore.Load();

                if (missing == null || missing.Count == 0)
                {
                    logger.Info("[AutoRepairer] No missing members — nothing to repair");
                    return;
                }

                var relevantMissing = missing
                    .Where(m => targetPlaylistIdN == null || m.PlaylistId == targetPlaylistIdN)
                    .Where(m => m.Member != null)
                    .ToList();

                if (relevantMissing.Count == 0)
                {
                    logger.Info("[AutoRepairer] No missing members for target playlist — nothing to repair");
                    return;
                }

                var itemLookup = BuildCandidateItemLookup(candidates, libraryManager, logger);

                var repairService = new PlaylistRepairService(
                    plugin.MissingMembersStore,
                    plugin.GroundTruthStore,
                    plugin.PlaylistStore,
                    libraryManager,
                    playlistManager,
                    userManager,
                    logger);

                var repairRows = new List<MissingMemberRow>();

                foreach (var record in relevantMissing)
                {
                    var mediaType = record.Member.MediaType ?? string.Empty;

                    if (!_eligibilityGates.TryGetValue(mediaType, out var gate))
                    {
                        logger.Info(
                            "[AutoRepairer] No eligibility gate for MediaType='{0}' — skipping '{1}'",
                            mediaType, record.Member.Name ?? "(unnamed)");
                        continue;
                    }

                    // Build ranked candidate list — only entries where live item resolved
                    var rankedCandidates = candidates
                        .Where(c =>
                            c.PlaylistId == record.PlaylistId &&
                            c.MissingMember?.InternalId == record.Member.InternalId)
                        .OrderByDescending(c => c.Score)
                        .Select(c => itemLookup.TryGetValue(c.CandidateInternalId, out var item)
                            ? new ScoredCandidate(c, item)
                            : null)
                        .Where(sc => sc != null)
                        .ToList();

                    if (rankedCandidates.Count == 0)
                    {
                        logger.Info(
                            "[AutoRepairer] No resolvable candidates for '{0}' — skipping",
                            record.Member.Name ?? "(unnamed)");
                        continue;
                    }

                    if (!gate.IsEligible(record.Member, rankedCandidates, threshold, minDistance))
                    {
                        logger.Info(
                            "[AutoRepairer] Eligibility gate rejected '{0}' (top score={1}, candidates={2})",
                            record.Member.Name ?? "(unnamed)",
                            rankedCandidates[0].Score,
                            rankedCandidates.Count);
                        continue;
                    }

                    var chosen = rankedCandidates[0];

                    logger.Info(
                        "[AutoRepairer] Queuing auto-repair | member='{0}' → candidate='{1}' | score={2}",
                        record.Member.Name ?? "(unnamed)",
                        chosen.Entry.CandidateName ?? "(unnamed)",
                        chosen.Score);

                    var key = record.PlaylistId + "_" + record.Member.InternalId;

                    repairRows.Add(new MissingMemberRow
                    {
                        Key = key,
                        PlaylistName = record.PlaylistName,
                        MemberName = record.Member.Name ?? "(unnamed)",
                        Path = record.Member.Path ?? string.Empty,
                        DetectedAt = record.DetectedAt.ToString("yyyy-MM-dd HH:mm") + " UTC",
                        IsSynthetic = false,
                        Candidates = new[]
                        {
                            new CandidateRow
                            {
                                Key           = key + "_" + chosen.Entry.CandidateInternalId,
                                CandidateName = chosen.Entry.CandidateName ?? "(unnamed)",
                                CandidatePath = chosen.Entry.CandidatePath ?? string.Empty,
                                Score         = chosen.Score,
                                Signals       = string.Join(", ", chosen.Entry.MatchedSignals ?? new List<string>()),
                                Repair        = true
                            }
                        }
                    });
                }

                if (repairRows.Count == 0)
                {
                    logger.Info("[AutoRepairer] No repairs to execute — done");
                    return;
                }

                logger.Info("[AutoRepairer] Executing {0} auto-repair(s)", repairRows.Count);
                await repairService.ExecuteRepairs(repairRows.ToArray());
                logger.Info("[AutoRepairer] Auto-repair complete");
            }
            catch (Exception ex)
            {
                logger.ErrorException("[AutoRepairer] RunAutoRepair failed", ex);
            }
        }

        private static Dictionary<long, BaseItem> BuildCandidateItemLookup(
            List<CandidateEntry> candidates,
            ILibraryManager libraryManager,
            ILogger logger)
        {
            var lookup = new Dictionary<long, BaseItem>();
            var uniqueIds = candidates.Select(c => c.CandidateInternalId).Distinct().ToArray();

            if (uniqueIds.Length == 0) return lookup;

            try
            {
                var items = libraryManager.GetItemList(new InternalItemsQuery
                {
                    ItemIds = uniqueIds,
                    Recursive = true
                });

                foreach (var item in items)
                    lookup[item.InternalId] = item;

                logger.Info(
                    "[AutoRepairer] Resolved {0}/{1} candidate item(s) from library",
                    lookup.Count, uniqueIds.Length);
            }
            catch (Exception ex)
            {
                logger.ErrorException("[AutoRepairer] Failed to resolve candidate items", ex);
            }

            return lookup;
        }
    }
}