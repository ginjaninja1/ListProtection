using ListProtection.Storage;
using ListProtection.UI.MissingMembers;
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
    /// completes — only when AutoRepairEnabled is true in ConfigStore.
    ///
    /// For each missing member in the target playlist, picks the highest-scoring
    /// candidate that meets or exceeds AutoRepairThreshold and calls
    /// PlaylistRepairService.ExecuteRepairs with that candidate marked Repair=true.
    ///
    /// AutoRepairMaxPerRun caps the total number of repairs in a single pass.
    /// Set to 0 in config to remove the cap.
    ///
    /// Static — all state lives in stores via ListProtectionPlugin.Instance.
    /// This mirrors the CandidateDiscoverer pattern exactly.
    /// </summary>
    internal static class AutoRepairer
    {
        /// <summary>
        /// Entry point called after candidate discovery completes.
        ///
        /// targetPlaylistIdN — if non-null, only processes that playlist.
        ///                     If null, processes all playlists with missing members.
        /// </summary>
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

                // Read config — bail early if auto-repair is disabled
                var config = plugin.Configuration;
                if (config == null || !config.AutoRepairEnabled)
                {
                    logger.Info("[AutoRepairer] Auto-repair is disabled — skipping");
                    return;
                }

                var threshold = config.AutoRepairThreshold;
                var maxPerRun = config.AutoRepairMaxPerRun;

                logger.Info(
                    "[AutoRepairer] Config | threshold={0} | maxPerRun={1}",
                    threshold, maxPerRun == 0 ? "unlimited" : maxPerRun.ToString());

                var missing = plugin.MissingMembersStore.Load();
                var candidates = plugin.CandidateStore.Load();

                if (missing == null || missing.Count == 0)
                {
                    logger.Info("[AutoRepairer] No missing members — nothing to repair");
                    return;
                }

                // Filter to target playlist if specified
                var relevantMissing = missing
                    .Where(m => targetPlaylistIdN == null || m.PlaylistId == targetPlaylistIdN)
                    .Where(m => m.Member != null)
                    .ToList();

                if (relevantMissing.Count == 0)
                {
                    logger.Info("[AutoRepairer] No missing members for target playlist — nothing to repair");
                    return;
                }

                // Build repair rows — one per missing member that has a qualifying candidate
                var repairService = new PlaylistRepairService(
                    plugin.MissingMembersStore,
                    plugin.GroundTruthStore,
                    plugin.PlaylistStore,
                    libraryManager,
                    playlistManager,
                    userManager,
                    logger);

                var repairRows = new List<MissingMemberRow>();
                var repairCount = 0;

                foreach (var record in relevantMissing)
                {
                    if (maxPerRun > 0 && repairCount >= maxPerRun)
                    {
                        logger.Info(
                            "[AutoRepairer] Reached AutoRepairMaxPerRun limit ({0}) — stopping",
                            maxPerRun);
                        break;
                    }

                    var best = candidates
                        .Where(c =>
                            c.PlaylistId == record.PlaylistId &&
                            c.MissingMember?.InternalId == record.Member.InternalId &&
                            c.Score >= threshold)
                        .OrderByDescending(c => c.Score)
                        .FirstOrDefault();

                    if (best == null)
                    {
                        logger.Info(
                            "[AutoRepairer] No qualifying candidate for '{0}' (threshold={1}) — skipping",
                            record.Member.Name ?? "(unnamed)", threshold);
                        continue;
                    }

                    logger.Info(
                        "[AutoRepairer] Queuing auto-repair | member='{0}' → candidate='{1}' | score={2}",
                        record.Member.Name ?? "(unnamed)",
                        best.CandidateName ?? "(unnamed)",
                        best.Score);

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
                                Key = record.PlaylistId + "_" + record.Member.InternalId + "_" + best.CandidateInternalId,
                                CandidateName = best.CandidateName ?? "(unnamed)",
                                CandidatePath = best.CandidatePath ?? string.Empty,
                                Score = best.Score,
                                Signals = string.Join(", ", best.MatchedSignals ?? new List<string>()),
                                Repair = true
                            }
                        }
                    });

                    repairCount++;
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
    }
}