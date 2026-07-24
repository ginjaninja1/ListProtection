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
    /// Called by MissingMemberDetectionService after CandidateDiscoverer.RunDiscovery
    /// completes — only when AutoRepairEnabled is true in PluginConfiguration.
    ///
    /// For each missing member, walks candidates in descending score order and picks
    /// the first that passes the IAutoRepairEligibility gate for its media type.
    ///
    /// Audio gate (AudioAutoRepairEligibility):
    ///   All three must match: Name (exact) + Artist (exact membership) + Album (exact).
    ///   Score selects the best candidate when multiple pass the gate.
    ///
    /// Other media types: no IAutoRepairEligibility implementation is registered —
    /// auto-repair does not fire for them. Register a new implementation in
    /// _eligibilityGates below to extend support.
    ///
    /// Static — all state lives in stores via ListProtectionPlugin.Instance.
    /// </summary>
    internal static class AutoRepairer
    {
        // ── Eligibility gates ──────────────────────────────────────────────
        // Keyed by MediaType. Add new implementations here when supporting
        // additional media types — no other code changes required.

        private static readonly Dictionary<string, IAutoRepairEligibility> _eligibilityGates =
            new Dictionary<string, IAutoRepairEligibility>(StringComparer.OrdinalIgnoreCase)
            {
                { "Audio", new AudioAutoRepairEligibility() },
                // { "Episode", new EpisodeAutoRepairEligibility() },
                // { "Movie",   new MovieAutoRepairEligibility()   },
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

                // Resolve all candidate InternalIds to live BaseItem instances in one
                // library query — the eligibility gate needs live metadata.
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
                    var mediaType = record.Member.MediaType ?? "Audio";

                    if (!_eligibilityGates.TryGetValue(mediaType, out var gate))
                    {
                        logger.Info(
                            "[AutoRepairer] No eligibility gate for MediaType='{0}' — skipping '{1}'",
                            mediaType, record.Member.Name ?? "(unnamed)");
                        continue;
                    }

                    var memberCandidates = candidates
                        .Where(c =>
                            c.PlaylistId == record.PlaylistId &&
                            c.MissingMember?.InternalId == record.Member.InternalId)
                        .OrderByDescending(c => c.Score)
                        .ToList();

                    if (memberCandidates.Count == 0)
                    {
                        logger.Info(
                            "[AutoRepairer] No candidates for '{0}' — skipping",
                            record.Member.Name ?? "(unnamed)");
                        continue;
                    }

                    // Walk candidates best-first, pick first that passes the gate
                    CandidateEntry chosen = null;
                    foreach (var candidateEntry in memberCandidates)
                    {
                        if (!itemLookup.TryGetValue(candidateEntry.CandidateInternalId, out var liveItem))
                        {
                            logger.Info(
                                "[AutoRepairer]   Candidate InternalId={0} not found in library — skipping",
                                candidateEntry.CandidateInternalId);
                            continue;
                        }

                        if (gate.IsEligible(record.Member, liveItem))
                        {
                            chosen = candidateEntry;
                            break;
                        }

                        logger.Info(
                            "[AutoRepairer]   Candidate '{0}' (score={1}) did not pass eligibility gate",
                            candidateEntry.CandidateName ?? "(unnamed)", candidateEntry.Score);
                    }

                    if (chosen == null)
                    {
                        logger.Info(
                            "[AutoRepairer] No eligible candidate for '{0}' — skipping",
                            record.Member.Name ?? "(unnamed)");
                        continue;
                    }

                    logger.Info(
                        "[AutoRepairer] Queuing auto-repair | member='{0}' → candidate='{1}' | score={2}",
                        record.Member.Name ?? "(unnamed)",
                        chosen.CandidateName ?? "(unnamed)",
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
                                Key           = key + "_" + chosen.CandidateInternalId,
                                CandidateName = chosen.CandidateName ?? "(unnamed)",
                                CandidatePath = chosen.CandidatePath ?? string.Empty,
                                Score         = chosen.Score,
                                Signals       = string.Join(", ", chosen.MatchedSignals ?? new List<string>()),
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