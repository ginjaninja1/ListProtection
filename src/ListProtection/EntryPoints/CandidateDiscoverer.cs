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
    /// Scoring architecture — three tiers:
    ///   Tier 1 — Media-type collector (Audio/Episode/Movie) emits atomic facts.
    ///             CandidateScorer applies prioritised rule table → ContentScore.
    ///   Tier 2 — FolderEvidenceCollector emits depth facts. Always runs.
    ///             Contributes LocationScore only when ContentScore > 0 or FallbackScore > 0.
    ///   Tier 3 — FallbackEvidenceCollector emits name/filename facts.
    ///             Consulted only when ContentScore == 0 → FallbackScore.
    ///
    /// Candidate cap: at most 3 candidates per (PlaylistId, MissingMember) pair.
    /// Deduplication: update-on-improvement — existing record updated if new score is higher.
    /// </summary>
    internal static class CandidateDiscoverer
    {
        private const int MaxCandidatesPerMember = 3;

        private static readonly IEvidenceCollector _audioCollector = new AudioEvidenceCollector();
        private static readonly IEvidenceCollector _episodeCollector = new EpisodeEvidenceCollector();
        private static readonly IEvidenceCollector _movieCollector = new MovieEvidenceCollector();
        private static readonly IEvidenceCollector _seriesCollector = new SeriesEvidenceCollector();
        private static readonly IEvidenceCollector _musicAlbumCollector = new MusicAlbumEvidenceCollector();
        private static readonly FolderEvidenceCollector _folderCollector = new FolderEvidenceCollector();
        private static readonly FallbackEvidenceCollector _fallbackCollector = new FallbackEvidenceCollector();

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

                // Build item pool once per media type
                var itemPoolByType = new Dictionary<string, BaseItem[]>(StringComparer.OrdinalIgnoreCase);
                foreach (var missingEntry in missing)
                {
                    if (targetPlaylistIdN != null && missingEntry.PlaylistId != targetPlaylistIdN) continue;
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
                    if (targetPlaylistIdN != null && missingEntry.PlaylistId != targetPlaylistIdN) continue;
                    var mediaType = missingEntry.Member?.MediaType ?? "Audio";
                    if (!itemPoolByType.TryGetValue(mediaType, out var pool) || pool.Length == 0)
                    {
                        logger.Info(
                            "[CandidateDiscoverer] No item pool for MediaType='{0}' — skipping member '{1}'",
                            mediaType, missingEntry.Member?.Name ?? "(null)");
                        continue;
                    }

                    ProcessMissingMember(missingEntry, gtStore, pool, existing, mediaType, logger, ref changed);
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
                missingEntry.ListName, missingEntry.PlaylistId);

            var excludedIds = new HashSet<long>();
            if (gtStore.TryGetValue(missingEntry.PlaylistId, out var gtEntry) && gtEntry.Members != null)
                foreach (var m in gtEntry.Members)
                    excludedIds.Add(m.InternalId);

            var tier1Collector = GetTier1Collector(mediaType);

            var candidatesFound = 0;
            var candidatesUpdated = 0;

            foreach (var item in pool)
            {
                if (excludedIds.Contains(item.InternalId)) continue;
                if (item.InternalId == member.InternalId) continue;

                // ── Collect facts per tier ─────────────────────────────────
                var tier1Facts = tier1Collector != null
                    ? tier1Collector.Collect(member, item).ToList()
                    : new List<EvidenceFact>();

                var tier2Facts = _folderCollector.Collect(member, item).ToList();

                // Tier 3 collection — pass always; scorer suppresses when ContentScore > 0
                var tier3Facts = _fallbackCollector.Collect(member, item).ToList();

                // ── Score ──────────────────────────────────────────────────
                var result = CandidateScorer.Score(tier1Facts, tier2Facts, tier3Facts, mediaType);

                if (result.CompositeScore == 0) continue;

                // ── Deduplication — update-on-improvement ──────────────────
                var existingEntry = existing.FirstOrDefault(c =>
                    c.PlaylistId == missingEntry.PlaylistId &&
                    c.MissingMember?.InternalId == member.InternalId &&
                    c.CandidateInternalId == item.InternalId);

                if (existingEntry != null)
                {
                    if (result.CompositeScore > existingEntry.Score)
                    {
                        existingEntry.ContentScore = result.ContentScore;
                        existingEntry.FallbackScore = result.FallbackScore;
                        existingEntry.LocationScore = result.LocationScore;
                        existingEntry.Score = result.CompositeScore;
                        existingEntry.MatchedSignals = result.MatchedSignals;
                        existingEntry.LastScoredAt = DateTime.UtcNow;
                        changed = true;
                        candidatesUpdated++;

                        logger.Info(
                            "[CandidateDiscoverer]   Updated candidate '{0}' | InternalId={1} | Score→{2} (C={3} L={4} F={5})",
                            item.Name, item.InternalId, result.CompositeScore,
                            result.ContentScore, result.LocationScore, result.FallbackScore);
                    }
                    else
                    {
                        existingEntry.LastScoredAt = DateTime.UtcNow;
                    }
                    continue;
                }

                // ── Top-3 cap ──────────────────────────────────────────────
                var currentForMember = existing
                    .Where(c =>
                        c.PlaylistId == missingEntry.PlaylistId &&
                        c.MissingMember?.InternalId == member.InternalId)
                    .ToList();

                if (currentForMember.Count >= MaxCandidatesPerMember)
                {
                    var lowest = currentForMember.OrderBy(c => c.Score).First();
                    if (result.CompositeScore <= lowest.Score) continue;
                    existing.Remove(lowest);
                    changed = true;
                }

                var entry = new CandidateEntry
                {
                    PlaylistId = missingEntry.PlaylistId,
                    ListName = missingEntry.ListName,
                    MissingMember = member,
                    CandidateInternalId = item.InternalId,
                    CandidateId = item.Id.ToString("N"),
                    CandidateName = item.Name,
                    CandidatePath = item.Path,
                    ContentScore = result.ContentScore,
                    FallbackScore = result.FallbackScore,
                    LocationScore = result.LocationScore,
                    Score = result.CompositeScore,
                    MatchedSignals = result.MatchedSignals,
                    DiscoveredAt = DateTime.UtcNow,
                    LastScoredAt = DateTime.UtcNow
                };

                existing.Add(entry);
                candidatesFound++;
                changed = true;

                logger.Info(
                    "[CandidateDiscoverer]   Candidate recorded: '{0}' | InternalId={1} | Score={2} (C={3} L={4} F={5}) | Signals=[{6}]",
                    item.Name, item.InternalId, result.CompositeScore,
                    result.ContentScore, result.LocationScore, result.FallbackScore,
                    string.Join(", ", result.MatchedSignals));
            }

            logger.Info(
                "[CandidateDiscoverer]   Done — {0} new, {1} updated candidate(s) for '{2}'",
                candidatesFound, candidatesUpdated, member.Name);
        }

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
                case "Series": embyType = "Series"; break;
                case "MusicAlbum": embyType = "MusicAlbum"; break;
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
                "[CandidateDiscoverer] Queried MediaType='{0}' — {1} item(s)",
                mediaType, pool?.Length ?? 0);

            return pool;
        }

        private static IEvidenceCollector GetTier1Collector(string mediaType)
        {
            switch (mediaType)
            {
                case "Audio": return _audioCollector;
                case "Episode": return _episodeCollector;
                case "Movie": return _movieCollector;
                case "Series": return _seriesCollector;
                case "MusicAlbum": return _musicAlbumCollector;
                default: return null;
            }
        }

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
                            " (C=" + c.ContentScore + " L=" + c.LocationScore + " F=" + c.FallbackScore + ")" +
                            " | " + (c.CandidatePath ?? string.Empty));
                    }

                    plugin.EventStore.Append(new EventEntry
                    {
                        EventType = "CandidateFound",
                        PlaylistId = kvp.Key,
                        ListName = kvp.Value[0].ListName ?? string.Empty,
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