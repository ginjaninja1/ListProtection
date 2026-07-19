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

                    foreach (var member in entry.Members)
                    {
                        if (liveIds.Contains(member.InternalId))
                            continue;

                        logger.Info(
                            "[MissingMemberDetector] Member absent from live playlist: '{0}' | InternalId={1} | playlist={2}",
                            member.Name, member.InternalId, playlistIdN);

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

                        missing.Add(new MissingMemberEntry
                        {
                            PlaylistId = playlistIdN,
                            PlaylistName = entry.PlaylistName,
                            DetectedAt = DateTime.UtcNow,
                            Member = member
                        });

                        logger.Info(
                            "[MissingMemberDetector] Recorded missing member: '{0}' | InternalId={1} | playlist='{2}' ({3})",
                            member.Name, member.InternalId, entry.PlaylistName, playlistIdN);

                        changed = true;
                    }
                }

                if (changed)
                {
                    plugin.WriterLock.Wait();
                    try { plugin.MissingMembersStore.Save(missing); }
                    finally { plugin.WriterLock.Release(); }

                    logger.Info("[MissingMemberDetector] Detection complete — store updated");

                    try
                    {
                        var byPlaylist = new Dictionary<string, List<MissingMemberEntry>>(StringComparer.OrdinalIgnoreCase);
                        foreach (var record in missing)
                        {
                            if (!byPlaylist.TryGetValue(record.PlaylistId, out var list))
                                byPlaylist[record.PlaylistId] = list = new List<MissingMemberEntry>();
                            list.Add(record);
                        }

                        foreach (var kvp in byPlaylist)
                        {
                            var payloadLines = new List<string>();
                            foreach (var r in kvp.Value)
                                payloadLines.Add((r.Member?.Name ?? "(unnamed)") + " | " + (r.Member?.Path ?? string.Empty));

                            plugin.EventStore.Append(new EventEntry
                            {
                                EventType = "MissingDetected",
                                PlaylistId = kvp.Key,
                                PlaylistName = kvp.Value[0].PlaylistName ?? string.Empty,
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
    }
}