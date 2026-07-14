using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ListProtection.EntryPoints
{
    /// <summary>
    /// Shared candidate discovery logic called by CandidateDiscoveryTask.
    ///
    /// Static to avoid any temptation to hold state here — all state
    /// lives in the stores accessed via ListProtectionPlugin.Instance.
    ///
    /// Scoring signals (all derived from GroundTruthMember.Name and .Path):
    ///
    ///   FilenameStemExact      100 — candidate FileNameWithoutExtension matches GT path stem exactly (case-insensitive)
    ///   FilenameStemNormalized  70 — after stripping leading track-number prefix ("02. "), stems match
    ///   NameExact               60 — candidate Name matches GT Name exactly (case-insensitive)
    ///   NameNormalized          40 — after normalising whitespace/case, names match
    ///   ParentFolderMatch       20 — immediate parent folder name matches (album folder)
    ///
    /// A candidate must score > 0 to be recorded.
    /// Candidates are stored sorted by Score descending, highest first.
    /// Deduplication: same (MissingMember.InternalId, PlaylistId, CandidateInternalId) triple is not re-recorded.
    /// </summary>
    internal static class CandidateDiscoverer
    {
        // ── Signal weights ─────────────────────────────────────────────────

        private const int W_FILENAME_STEM_EXACT = 100;
        private const int W_FILENAME_STEM_NORMALIZED = 70;
        private const int W_NAME_EXACT = 60;
        private const int W_NAME_NORMALIZED = 40;
        private const int W_PARENT_FOLDER_MATCH = 20;

        // Matches leading track-number prefixes: "02. ", "02 - ", "02-", "2. ", etc.
        private static readonly Regex TrackPrefixRegex =
            new Regex(@"^\d{1,3}[\s\.\-]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // ── Entry point ────────────────────────────────────────────────────

        /// <summary>
        /// Discovers and scores candidates for all active missing members.
        /// If targetPlaylistIdN is non-null, only processes that playlist.
        /// </summary>
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

                // Query all Audio items once — shared across all missing members
                var allAudio = libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { "Audio" },
                    Recursive = true
                });

                logger.Info("[CandidateDiscoverer] Library returned {0} Audio item(s)", allAudio?.Length ?? 0);

                if (allAudio == null || allAudio.Length == 0)
                {
                    logger.Warn("[CandidateDiscoverer] No Audio items found — aborting");
                    return;
                }

                foreach (var missingEntry in missing)
                {
                    if (targetPlaylistIdN != null && missingEntry.PlaylistId != targetPlaylistIdN)
                        continue;

                    ProcessMissingMember(
                        missingEntry,
                        gtStore,
                        allAudio,
                        existing,
                        logger,
                        ref changed);
                }

                if (changed)
                {
                    // Re-sort entire store by Score descending before saving
                    existing.Sort((a, b) => b.Score.CompareTo(a.Score));
                    plugin.CandidateStore.Save(existing);
                    logger.Info("[CandidateDiscoverer] Discovery complete — store updated");

                    // Write CandidateFound events — one per affected playlist
                    try
                    {
                        var byPlaylist = new Dictionary<string, List<CandidateEntry>>(StringComparer.OrdinalIgnoreCase);
                        foreach (var c in existing)
                        {
                            if (!byPlaylist.TryGetValue(c.PlaylistId, out var list))
                                byPlaylist[c.PlaylistId] = list = new List<CandidateEntry>();
                            list.Add(c);
                        }

                        foreach (var kvp in byPlaylist)
                        {
                            var payloadLines = new List<string>();
                            foreach (var c in kvp.Value)
                            {
                                payloadLines.Add(
                                    (c.CandidateName ?? "(unnamed)") +
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
                    catch (Exception evEx)
                    {
                        logger.ErrorException("[CandidateDiscoverer] Failed to write CandidateFound event", evEx);
                    }
                }
                else
                {
                    logger.Info("[CandidateDiscoverer] Discovery complete — no new candidates found");
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
            BaseItem[] allAudio,
            List<CandidateEntry> existing,
            ILogger logger,
            ref bool changed)
        {
            var member = missingEntry.Member;

            logger.Info(
                "[CandidateDiscoverer] Processing missing member: '{0}' | InternalId={1} | playlist='{2}' ({3})",
                member.Name,
                member.InternalId,
                missingEntry.PlaylistName,
                missingEntry.PlaylistId);

            // Build exclusion set from ground truth for this playlist
            var excludedIds = new HashSet<long>();
            if (gtStore.TryGetValue(missingEntry.PlaylistId, out var gtEntry) && gtEntry.Members != null)
            {
                foreach (var m in gtEntry.Members)
                    excludedIds.Add(m.InternalId);
            }

            // Pre-compute GT-derived comparison values once per missing member
            var gtStem = GetFilenameStem(member.Path);         // e.g. "02. New Heights"
            var gtStemNorm = NormalizeStem(gtStem);                // e.g. "new heights"
            var gtNameNorm = NormalizeName(member.Name);           // e.g. "new heights"
            var gtParentDir = GetParentFolderName(member.Path);     // e.g. "Bomb in a Birdcage"

            logger.Info(
                "[CandidateDiscoverer]   GT stem='{0}' | stemNorm='{1}' | nameNorm='{2}' | parentDir='{3}'",
                gtStem, gtStemNorm, gtNameNorm, gtParentDir);

            var candidatesFound = 0;

            foreach (var item in allAudio)
            {
                // Never suggest an item that is already in the playlist's ground truth
                if (excludedIds.Contains(item.InternalId))
                    continue;

                // Never suggest the missing item itself (shouldn't appear since it's absent,
                // but guard against stale InternalId reuse)
                if (item.InternalId == member.InternalId)
                    continue;

                var score = 0;
                var signals = new List<string>();

                var candidateStem = item.FileNameWithoutExtension ?? string.Empty;
                var candidateStemNorm = NormalizeStem(candidateStem);
                var candidateNameNorm = NormalizeName(item.Name ?? string.Empty);
                var candidateParentDir = GetParentFolderName(item.Path);

                // Signal 1 — FilenameStemExact
                if (!string.IsNullOrEmpty(gtStem) &&
                    string.Equals(gtStem, candidateStem, StringComparison.OrdinalIgnoreCase))
                {
                    score += W_FILENAME_STEM_EXACT;
                    signals.Add("FilenameStemExact:" + W_FILENAME_STEM_EXACT);
                }
                // Signal 2 — FilenameStemNormalized (only if exact didn't already match)
                else if (!string.IsNullOrEmpty(gtStemNorm) &&
                         string.Equals(gtStemNorm, candidateStemNorm, StringComparison.OrdinalIgnoreCase))
                {
                    score += W_FILENAME_STEM_NORMALIZED;
                    signals.Add("FilenameStemNormalized:" + W_FILENAME_STEM_NORMALIZED);
                }

                // Signal 3 — NameExact
                if (!string.IsNullOrEmpty(member.Name) &&
                    string.Equals(member.Name, item.Name, StringComparison.OrdinalIgnoreCase))
                {
                    score += W_NAME_EXACT;
                    signals.Add("NameExact:" + W_NAME_EXACT);
                }
                // Signal 4 — NameNormalized (only if exact didn't already match)
                else if (!string.IsNullOrEmpty(gtNameNorm) &&
                         string.Equals(gtNameNorm, candidateNameNorm, StringComparison.OrdinalIgnoreCase))
                {
                    score += W_NAME_NORMALIZED;
                    signals.Add("NameNormalized:" + W_NAME_NORMALIZED);
                }

                // Signal 5 — ParentFolderMatch
                if (!string.IsNullOrEmpty(gtParentDir) && !string.IsNullOrEmpty(candidateParentDir) &&
                    string.Equals(gtParentDir, candidateParentDir, StringComparison.OrdinalIgnoreCase))
                {
                    score += W_PARENT_FOLDER_MATCH;
                    signals.Add("ParentFolderMatch:" + W_PARENT_FOLDER_MATCH);
                }

                if (score == 0)
                    continue;

                // Deduplication — skip if already recorded
                var alreadyRecorded = false;
                foreach (var c in existing)
                {
                    if (c.PlaylistId == missingEntry.PlaylistId
                        && c.MissingMember?.InternalId == member.InternalId
                        && c.CandidateInternalId == item.InternalId)
                    {
                        alreadyRecorded = true;
                        break;
                    }
                }

                if (alreadyRecorded)
                {
                    logger.Info(
                        "[CandidateDiscoverer]   Already recorded — skipping candidate InternalId={0}",
                        item.InternalId);
                    continue;
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
                    DiscoveredAt = DateTime.UtcNow
                });

                logger.Info(
                    "[CandidateDiscoverer]   Candidate recorded: '{0}' | InternalId={1} | Score={2} | Signals=[{3}]",
                    item.Name,
                    item.InternalId,
                    score,
                    string.Join(", ", signals));

                candidatesFound++;
                changed = true;
            }

            logger.Info(
                "[CandidateDiscoverer]   Done — {0} new candidate(s) recorded for '{1}'",
                candidatesFound,
                member.Name);
        }

        // ── Scoring helpers ────────────────────────────────────────────────

        /// <summary>
        /// Extracts FileNameWithoutExtension from a full path.
        /// e.g. "D:\Music\Album\02. New Heights.flac" → "02. New Heights"
        /// Returns empty string on null/empty input — never throws.
        /// </summary>
        private static string GetFilenameStem(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return string.Empty;

            try
            {
                return Path.GetFileNameWithoutExtension(fullPath) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Strips leading track-number prefix and lowercases.
        /// "02. New Heights" → "new heights"
        /// "03 - Whisper"    → "whisper"
        /// </summary>
        private static string NormalizeStem(string stem)
        {
            if (string.IsNullOrEmpty(stem))
                return string.Empty;

            var stripped = TrackPrefixRegex.Replace(stem.Trim(), string.Empty);
            return stripped.ToLowerInvariant();
        }

        /// <summary>
        /// Lowercases and collapses whitespace for loose name comparison.
        /// </summary>
        private static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            return Regex.Replace(name.Trim().ToLowerInvariant(), @"\s+", " ");
        }

        /// <summary>
        /// Returns the immediate parent folder name from a full file path.
        /// "D:\Music\[2009] Bomb in a Birdcage\02. New Heights.flac" → "Bomb in a Birdcage"
        /// Strips leading year prefix "[YYYY] " if present.
        /// Returns empty string on null/empty input or if no parent — never throws.
        /// </summary>
        private static string GetParentFolderName(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return string.Empty;

            try
            {
                var dir = Path.GetDirectoryName(fullPath);
                if (string.IsNullOrEmpty(dir))
                    return string.Empty;

                var folderName = Path.GetFileName(dir) ?? string.Empty;

                // Strip leading "[YYYY] " or "(YYYY) " album-year prefix
                folderName = Regex.Replace(folderName, @"^[\[\(]\d{4}[\]\)]\s*", string.Empty).Trim();

                return folderName;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}