using ListProtection.Scoring;
using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ListProtection.EntryPoints
{
    /// <summary>
    /// Shared candidate discovery logic — called by event-driven detection
    /// (MissingMemberDetectionService), PostScanCandidateTask, and the manual
    /// CandidateDiscoveryTask dashboard task.
    ///
    /// Static — all state lives in the plugin stores.
    ///
    /// Architecture:
    ///   Evidence collection and scoring are fully separated from the discovery loop.
    ///   BaseItemEvidenceCollector runs for all item types.
    ///   AudioEvidenceCollector chains on top when gt.MediaType == "Audio".
    ///   CandidateScorer is stateless — it sums EvidenceFact weights from ScoringWeights.
    ///
    /// Candidate cap:
    ///   At most 3 candidates are retained per (PlaylistId, MissingMember) pair —
    ///   the 3 highest-scoring. This keeps the store focused and the UI uncluttered.
    ///   On each discovery run, lower-scoring candidates are evicted if a better one
    ///   arrives and there is already a full set of 3.
    ///
    /// Deduplication:
    ///   If a (PlaylistId, MissingMember.InternalId, CandidateInternalId) triple already
    ///   exists and the new score is higher, the stored record is updated (score +
    ///   signals + LastScoredAt). If the new score is equal or lower, the record is
    ///   left unchanged. This means CandidateRefreshTask re-runs are always meaningful.
    ///
    /// Candidate pool:
    ///   Items are queried by MediaType from the GT member. Legacy GT members with a
    ///   null MediaType fall back to querying Audio (preserving prior behaviour).
    ///   When a gt.MediaType is not yet supported, discovery logs and skips.
    ///
    /// Belt-and-braces note:
    ///   PostScanCandidateTask (ILibraryPostScanTask + scheduled 04:00 daily) is the
    ///   load-bearing safety net for the upgrade scenario (MP3 → FLAC). In that case
    ///   the replacement item does not exist at ItemRemoved time — the event-driven
    ///   fast path produces zero candidates. The post-scan task runs after Emby has
    ///   fully ingested the new file and its metadata, at which point the audio-specific
    ///   signals (Album, AlbumArtist, Duration) are available and scoring is reliable.
    /// </summary>
    internal static class CandidateDiscoverer
    {
        private const int MaxCandidatesPerMember = 3;

        // Registered evidence collectors — order matters for logging only.
        // BaseItem collector is always applied; audio collector chains on top.
        private static readonly IEvidenceCollector BaseCollector = new BaseItemEvidenceCollector();
        private static readonly IEvidenceCollector AudioCollector = new AudioEvidenceCollector();

        // ── Entry point ────────────────────────────────────────────────────

        internal static void RunDiscovery(
            string targetPlaylistIdN,
            ILibraryManager libraryManager,
            ILogger logger)
        {
            logger.Info(
                "[CandidateDiscoverer] RunDiscovery starting | target={0}",
                targetPlaylistIdN ?? "ALL");

            try
            {
                var plugin = ListProtectionPlugin.Instance;
                if (plugin == null)
                {
                    logger.Error("[CandidateDiscoverer] Plugin instance is null — aborting");
                    return;
                }

                var missing = plugin.MissingMembersStore.Load();
                var gtStore = plugin.GroundTruthStore.Load();
                var existing = plugin.CandidateStore.Load();
                var changed = false;

                if (missing == null || missing.Count == 0)
                {
                    logger.Info("[CandidateDiscoverer] No missing members — nothing to discover");
                    return;
                }

                // Build the candidate pool once per media type encountered.
                var itemPoolByType = new Dictionary<string, BaseItem[]>(StringComparer.OrdinalIgnoreCase);

                foreach (var missingEntry in missing)
                {
                    if (targetPlaylistIdN != null && missingEntry.PlaylistId != targetPlaylistIdN)
                        continue;

                    var mediaType = missingEntry.Member?.MediaType ?? "Audio";

                    if (!itemPoolByType.ContainsKey(mediaType))
                    {
                        var pool = QueryItemPool(mediaType, libraryManager, logger);
                        if (pool != null)
                            itemPoolByType[mediaType] = pool;
                    }
                }

                foreach (var missingEntry in missing)
                {
                    if (targetPlaylistIdN != null && missingEntry.PlaylistId != targetPlaylistIdN)
                        continue;

                    var mediaType = missingEntry.Member?.MediaType ?? "Audio";

                    if (!itemPoolByType.TryGetValue(mediaType, out var pool) || pool.Length == 0)
                    {
                        logger.Info(
                            "[CandidateDiscoverer] No item pool for MediaType='{0}' — skipping member '{1}'",
                            mediaType, missingEntry.Member?.Name ?? "(null)");
                        continue;
                    }

                    ProcessMissingMember(
                        missingEntry,
                        gtStore,
                        pool,
                        existing,
                        mediaType,
                        logger,
                        ref changed);
                }

                if (changed)
                {
                    existing.Sort((a, b) => b.Score.CompareTo(a.Score));
                    plugin.CandidateStore.Save(existing);
                    logger.Info("[CandidateDiscoverer] Discovery complete — store updated");

                    WriteCandidateFoundEvents(existing, gtStore, plugin, logger);
                }
                else
                {
                    logger.Info("[CandidateDiscoverer] Discovery complete — no changes");
                }
            }
            catch (Exception ex)
            {
                logger.ErrorException("[CandidateDiscoverer] RunDiscovery failed", ex);
            }
        }

        // ── Per-member discovery ───────────────────────────────────────────

        private static void ProcessMissingMember(
            MissingMemberEntry missingEntry,
            Dictionary<string, GroundTruthEntry> gtStore,
            BaseItem[] pool,
            List<CandidateEntry> existing,
            string mediaType,
            ILogger logger,
            ref bool changed)
        {
            var member = missingEntry.Member;

            logger.Info(
                "[CandidateDiscoverer] Processing missing member: '{0}' | InternalId={1} | MediaType={2} | playlist='{3}' ({4})",
                member.Name, member.InternalId, mediaType,
                missingEntry.PlaylistName, missingEntry.PlaylistId);

            // Exclude items already in this playlist's ground truth
            var excludedIds = new HashSet<long>();
            if (gtStore.TryGetValue(missingEntry.PlaylistId, out var gtEntry) && gtEntry.Members != null)
                foreach (var m in gtEntry.Members)
                    excludedIds.Add(m.InternalId);

            var typeCollector = GetTypeCollector(mediaType);

            var candidatesFound = 0;
            var candidatesUpdated = 0;

            foreach (var item in pool)
            {
                if (excludedIds.Contains(item.InternalId)) continue;
                if (item.InternalId == member.InternalId) continue;

                // ── Collect evidence ───────────────────────────────────────
                var facts = new List<EvidenceFact>();
                facts.AddRange(BaseCollector.Collect(member, item));
                if (typeCollector != null)
                    facts.AddRange(typeCollector.Collect(member, item));

                // ── Score ──────────────────────────────────────────────────
                CandidateScorer.Score(facts, out var score, out var signals);

                if (score == 0) continue;

                // ── Deduplication — update-on-improvement ──────────────────
                var existingEntry = existing.FirstOrDefault(c =>
                    c.PlaylistId == missingEntry.PlaylistId &&
                    c.MissingMember?.InternalId == member.InternalId &&
                    c.CandidateInternalId == item.InternalId);

                if (existingEntry != null)
                {
                    if (score > existingEntry.Score)
                    {
                        logger.Info(
                            "[CandidateDiscoverer]   Updated candidate '{0}' | InternalId={1} | Score {2}→{3} | Signals=[{4}]",
                            item.Name, item.InternalId, existingEntry.Score, score,
                            string.Join(", ", signals));

                        existingEntry.Score = score;
                        existingEntry.MatchedSignals = signals;
                        existingEntry.LastScoredAt = DateTime.UtcNow;
                        changed = true;
                        candidatesUpdated++;
                    }
                    else
                    {
                        existingEntry.LastScoredAt = DateTime.UtcNow;
                    }
                    continue;
                }

                // ── Top-3 cap ──────────────────────────────────────────────
                // Count current candidates for this (playlist, member) pair.
                var currentForMember = existing
                    .Where(c =>
                        c.PlaylistId == missingEntry.PlaylistId &&
                        c.MissingMember?.InternalId == member.InternalId)
                    .ToList();

                if (currentForMember.Count >= MaxCandidatesPerMember)
                {
                    // Only displace the lowest-scoring existing candidate if the
                    // new score beats it — otherwise discard the new candidate.
                    var lowestExisting = currentForMember
                        .OrderBy(c => c.Score)
                        .First();

                    if (score <= lowestExisting.Score)
                        continue;

                    // Evict the lowest scorer to make room
                    existing.Remove(lowestExisting);
                    changed = true;
                }

                existing.Add(new CandidateEntry
                {
                    PlaylistId = missingEntry.PlaylistId,
                    PlaylistName = missingEntry.PlaylistName,
                    MissingMember = member,
                    CandidateInternalId = item.InternalId,
                    CandidateId = item.Id.ToString("N"),
                    CandidateName = item.Name,
                    CandidatePath = item.Path,
                    Score = score,
                    MatchedSignals = signals,
                    DiscoveredAt = DateTime.UtcNow,
                    LastScoredAt = DateTime.UtcNow
                });

                logger.Info(
                    "[CandidateDiscoverer]   Candidate recorded: '{0}' | InternalId={1} | Score={2} | Signals=[{3}]",
                    item.Name, item.InternalId, score, string.Join(", ", signals));

                candidatesFound++;
                changed = true;
            }

            logger.Info(
                "[CandidateDiscoverer]   Done — {0} new, {1} updated candidate(s) for '{2}'",
                candidatesFound, candidatesUpdated, member.Name);
        }

        // ── Item pool query ────────────────────────────────────────────────

        private static BaseItem[] QueryItemPool(
            string mediaType,
            ILibraryManager libraryManager,
            ILogger logger)
        {
            string embyType;
            switch (mediaType)
            {
                case "Audio": embyType = "Audio"; break;
                case "Episode": embyType = "Episode"; break;
                case "Movie": embyType = "Movie"; break;
                default:
                    logger.Warn(
                        "[CandidateDiscoverer] Unsupported MediaType '{0}' — no pool built",
                        mediaType);
                    return null;
            }

            var pool = libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { embyType },
                Recursive = true
            });

            logger.Info(
                "[CandidateDiscoverer] Queried MediaType='{0}' ({1}) — {2} item(s)",
                mediaType, embyType, pool?.Length ?? 0);

            return pool;
        }

        // ── Collector selection ────────────────────────────────────────────

        private static IEvidenceCollector GetTypeCollector(string mediaType)
        {
            switch (mediaType)
            {
                case "Audio": return AudioCollector;
                default: return null;
            }
        }

        // ── Event writing ──────────────────────────────────────────────────

        private static void WriteCandidateFoundEvents(
            List<CandidateEntry> all,
            Dictionary<string, GroundTruthEntry> gtStore,
            ListProtectionPlugin plugin,
            ILogger logger)
        {
            try
            {
                var byPlaylist = new Dictionary<string, List<CandidateEntry>>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in all)
                {
                    if (!byPlaylist.TryGetValue(c.PlaylistId, out var list))
                        byPlaylist[c.PlaylistId] = list = new List<CandidateEntry>();
                    list.Add(c);
                }

                foreach (var kvp in byPlaylist)
                {
                    gtStore.TryGetValue(kvp.Key, out var gtEntry);

                    var payloadLines = new List<string>();
                    foreach (var c in kvp.Value)
                    {
                        var pos = GetGroundTruthPosition(c.MissingMember?.InternalId ?? -1, gtEntry);
                        var posPrefix = pos >= 0 ? "[POS " + (pos + 1) + "] " : string.Empty;
                        payloadLines.Add(
                            posPrefix +
                            (c.MissingMember?.Name ?? "(unnamed)") +
                            " → " + (c.CandidateName ?? "(unnamed)") +
                            " | score=" + c.Score +
                            " | " + (c.CandidatePath ?? string.Empty));
                    }

                    plugin.EventStore.Append(new EventEntry
                    {
                        EventType = "CandidateFound",
                        PlaylistId = kvp.Key,
                        PlaylistName = kvp.Value[0].PlaylistName ?? string.Empty,
                        OccurredAt = DateTime.UtcNow,
                        Payload = string.Join("\n", payloadLines)
                    });
                }
            }
            catch (Exception ex)
            {
                logger.ErrorException("[CandidateDiscoverer] Failed to write CandidateFound event", ex);
            }
        }

        // ── Position helper ────────────────────────────────────────────────

        private static int GetGroundTruthPosition(long internalId, GroundTruthEntry gtEntry)
        {
            if (gtEntry?.Members == null || internalId <= 0) return -1;
            for (var i = 0; i < gtEntry.Members.Count; i++)
                if (gtEntry.Members[i].InternalId == internalId)
                    return i;
            return -1;
        }
    }
}