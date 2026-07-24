using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using System;
using System.Collections.Generic;

namespace ListProtection.EntryPoints
{
    /// <summary>
    /// Shared detection logic called by MissingMemberDetectionService (fast path)
    /// and PostScanDetectionTask (post-scan / scheduled).
    ///
    /// Static — all state lives in stores accessed via ListProtectionPlugin.Instance.
    ///
    /// PROVEN: Playlist.GetItemList(new InternalItemsQuery()) returns members in
    /// correct playlist order (ListItemOrder ascending). Do NOT use
    /// ILibraryManager.GetItemList with ListIds — that returns in ListItemEntryId
    /// ascending order which reflects DB insertion order, not playlist position.
    /// Proven by CaptureOrderProbeTask 2026-07-18.
    ///
    /// Event payload format (MissingDetected):
    ///   "[POS X] Track Name | /path/to/file.flac"
    ///   X = 1-based position in the playlist's ground truth Members list.
    /// </summary>
    internal static class MissingMemberDetector
    {
        internal static void RunDetection(
            string targetPlaylistIdN,
            ILibraryManager libraryManager,
            ILogger logger)
        {
            logger.Info(
                "[MissingMemberDetector] RunDetection starting | target={0}",
                targetPlaylistIdN ?? "ALL");

            try
            {
                var plugin = ListProtectionPlugin.Instance;
                if (plugin == null)
                {
                    logger.Error("[MissingMemberDetector] Plugin instance is null — aborting");
                    return;
                }

                plugin.WriterLock.Wait();
                Dictionary<string, GroundTruthEntry> groundTruth;
                List<MissingMemberEntry> missing;
                try
                {
                    groundTruth = plugin.GroundTruthStore.Load();
                    missing = plugin.MissingMembersStore.Load();
                }
                finally
                {
                    plugin.WriterLock.Release();
                }

                var changed = false;
                var newlyAdded = new List<MissingMemberEntry>();

                // Resolve all playlists once
                var allPlaylists = libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { "Playlist" },
                    Recursive = true
                });

                foreach (var kvp in groundTruth)
                {
                    if (targetPlaylistIdN != null && kvp.Key != targetPlaylistIdN)
                        continue;

                    var playlistIdN = kvp.Key;
                    var entry = kvp.Value;

                    if (!Guid.TryParseExact(playlistIdN, "N", out var guid))
                    {
                        logger.Warn("[MissingMemberDetector] Could not parse playlist Guid: {0}", playlistIdN);
                        continue;
                    }

                    Playlist playlist = null;
                    foreach (var p in allPlaylists)
                    {
                        if (p.Id == guid)
                        {
                            playlist = p as Playlist;
                            break;
                        }
                    }

                    if (playlist == null)
                    {
                        logger.Warn(
                            "[MissingMemberDetector] Playlist not found in library: {0} — skipping",
                            playlistIdN);
                        continue;
                    }

                    // PROVEN: Playlist.GetItemList returns members in playlist order.
                    // ILibraryManager.GetItemList with ListIds returns DB insertion order — do not use.
                    var liveMembers = playlist.GetItemList(new InternalItemsQuery());

                    logger.Info(
                        "[MissingMemberDetector] Live readback for '{0}' ({1}) — {2} live member(s)",
                        entry.PlaylistName, playlistIdN, liveMembers?.Length ?? 0);

                    var liveIds = new HashSet<long>();
                    if (liveMembers != null)
                        foreach (var m in liveMembers)
                            liveIds.Add(m.InternalId);

                    // Walk Members by index so we have the ground truth position
                    for (var pos = 0; pos < entry.Members.Count; pos++)
                    {
                        var member = entry.Members[pos];

                        if (liveIds.Contains(member.InternalId))
                            continue;

                        logger.Info(
                            "[MissingMemberDetector] Member absent from live playlist: '{0}' | InternalId={1} | pos={2} | playlist={3}",
                            member.Name, member.InternalId, pos + 1, playlistIdN);

                        var alreadyRecorded = false;
                        foreach (var existing in missing)
                        {
                            if (existing.PlaylistId == playlistIdN
                                && existing.Member.InternalId == member.InternalId)
                            {
                                alreadyRecorded = true;
                                break;
                            }
                        }

                        if (alreadyRecorded)
                        {
                            logger.Info(
                                "[MissingMemberDetector] Already recorded as missing — skipping: '{0}' | playlist={1}",
                                member.Name, playlistIdN);
                            continue;
                        }

                        var newEntry = new MissingMemberEntry
                        {
                            PlaylistId = playlistIdN,
                            PlaylistName = entry.PlaylistName,
                            DetectedAt = DateTime.UtcNow,
                            Member = member
                        };

                        missing.Add(newEntry);
                        newlyAdded.Add(newEntry);
                        changed = true;

                        logger.Info(
                            "[MissingMemberDetector] Recorded missing member: '{0}' | InternalId={1} | playlist='{2}' ({3})",
                            member.Name, member.InternalId, entry.PlaylistName, playlistIdN);
                    }
                }

                if (changed)
                {
                    plugin.WriterLock.Wait();
                    try { plugin.MissingMembersStore.Save(missing); }
                    finally { plugin.WriterLock.Release(); }

                    logger.Info("[MissingMemberDetector] Detection complete — store updated");

                    // Write one MissingDetected event per playlist containing only
                    // the members newly detected in THIS run (not the full store).
                    try
                    {
                        var byPlaylist = new Dictionary<string, List<MissingMemberEntry>>(StringComparer.OrdinalIgnoreCase);
                        foreach (var record in newlyAdded)
                        {
                            if (!byPlaylist.TryGetValue(record.PlaylistId, out var list))
                                byPlaylist[record.PlaylistId] = list = new List<MissingMemberEntry>();
                            list.Add(record);
                        }

                        foreach (var evKvp in byPlaylist)
                        {
                            groundTruth.TryGetValue(evKvp.Key, out var gtEntry);

                            var payloadLines = new List<string>();
                            foreach (var r in evKvp.Value)
                            {
                                var pos = GetGroundTruthPosition(r.Member?.InternalId ?? -1, gtEntry);
                                var posPrefix = pos >= 0 ? "[POS " + (pos + 1) + "] " : string.Empty;
                                payloadLines.Add(
                                    posPrefix +
                                    (r.Member?.Name ?? "(unnamed)") +
                                    " | " +
                                    (r.Member?.Path ?? string.Empty));
                            }

                            plugin.EventStore.Append(new EventEntry
                            {
                                EventType = "MissingDetected",
                                PlaylistId = evKvp.Key,
                                PlaylistName = evKvp.Value[0].PlaylistName ?? string.Empty,
                                OccurredAt = DateTime.UtcNow,
                                Payload = string.Join("\n", payloadLines)
                            });
                        }
                    }
                    catch (Exception evEx)
                    {
                        logger.ErrorException("[MissingMemberDetector] Failed to write MissingDetected event", evEx);
                    }
                }
                else
                {
                    logger.Info("[MissingMemberDetector] Detection complete — no new missing members found");
                }
            }
            catch (Exception ex)
            {
                logger.ErrorException("[MissingMemberDetector] RunDetection failed", ex);
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the 0-based index of the member in the GT Members list,
        /// or -1 if not found or gtEntry is null.
        /// </summary>
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