using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using System;
using System.Collections.Generic;

namespace ListProtection.EntryPoints
{
    /// <summary>
    /// Shared detection logic for both Playlists and Collections.
    ///
    /// Playlist path: Playlist.GetItemList(new InternalItemsQuery()) — proven to return
    /// members in ListItemOrder (correct playlist position).
    ///
    /// Collection path: BoxSet.GetItemList(new InternalItemsQuery()) — enumerates members
    /// via CollectionIds DB filter. No ListItemOrder equivalent; order is metadata-derived.
    /// Membership absence is detected by InternalId set difference against GT, same as playlists.
    ///
    /// Both paths write MissingDetected events and update MissingMembersStore.
    /// </summary>
    internal static class MissingMemberDetector
    {
        internal static void RunDetection(
            string targetListIdN,
            ILibraryManager libraryManager,
            ILogger logger)
        {
            logger.Info(
                "[MissingMemberDetector] RunDetection starting | target={0}",
                targetListIdN ?? "ALL");

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

                // Resolve all playlists and collections once
                var allPlaylists = libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { "Playlist" },
                    Recursive = true
                });
                var allCollections = libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { "BoxSet" },
                    Recursive = true
                });

                foreach (var kvp in groundTruth)
                {
                    if (targetListIdN != null && kvp.Key != targetListIdN)
                        continue;

                    var listIdN = kvp.Key;
                    var entry = kvp.Value;

                    if (!Guid.TryParseExact(listIdN, "N", out var guid))
                    {
                        logger.Warn("[MissingMemberDetector] Could not parse Guid: {0}", listIdN);
                        continue;
                    }

                    MediaBrowser.Controller.Entities.BaseItem[] liveMembers;

                    if (entry.IsCollection)
                    {
                        var collection = FindById(allCollections, guid) as BoxSet;
                        if (collection == null)
                        {
                            logger.Warn("[MissingMemberDetector] Collection not found: {0} — skipping", listIdN);
                            continue;
                        }
                        liveMembers = collection.GetItemList(new InternalItemsQuery());
                        logger.Info("[MissingMemberDetector] Live readback for collection '{0}' — {1} member(s)",
                            entry.PlaylistName, liveMembers?.Length ?? 0);
                    }
                    else
                    {
                        var playlist = FindById(allPlaylists, guid) as Playlist;
                        if (playlist == null)
                        {
                            logger.Warn("[MissingMemberDetector] Playlist not found: {0} — skipping", listIdN);
                            continue;
                        }
                        // PROVEN: Playlist.GetItemList returns members in playlist order (ListItemOrder).
                        liveMembers = playlist.GetItemList(new InternalItemsQuery());
                        logger.Info("[MissingMemberDetector] Live readback for playlist '{0}' — {1} member(s)",
                            entry.PlaylistName, liveMembers?.Length ?? 0);
                    }

                    var liveIds = new HashSet<long>();
                    if (liveMembers != null)
                        foreach (var m in liveMembers)
                            liveIds.Add(m.InternalId);

                    for (var pos = 0; pos < entry.Members.Count; pos++)
                    {
                        var member = entry.Members[pos];
                        if (liveIds.Contains(member.InternalId)) continue;

                        logger.Info(
                            "[MissingMemberDetector] Member absent: '{0}' | InternalId={1} | pos={2} | list={3}",
                            member.Name, member.InternalId, pos + 1, listIdN);

                        var alreadyRecorded = false;
                        foreach (var existing in missing)
                        {
                            if (existing.PlaylistId == listIdN && existing.Member.InternalId == member.InternalId)
                            {
                                alreadyRecorded = true;
                                break;
                            }
                        }

                        if (alreadyRecorded) continue;

                        var newEntry = new MissingMemberEntry
                        {
                            PlaylistId = listIdN,
                            PlaylistName = entry.PlaylistName,
                            DetectedAt = DateTime.UtcNow,
                            Member = member
                        };

                        missing.Add(newEntry);
                        newlyAdded.Add(newEntry);
                        changed = true;
                    }
                }

                if (changed)
                {
                    plugin.WriterLock.Wait();
                    try { plugin.MissingMembersStore.Save(missing); }
                    finally { plugin.WriterLock.Release(); }

                    try
                    {
                        var byList = new Dictionary<string, List<MissingMemberEntry>>(StringComparer.OrdinalIgnoreCase);
                        foreach (var record in newlyAdded)
                        {
                            if (!byList.TryGetValue(record.PlaylistId, out var list))
                                byList[record.PlaylistId] = list = new List<MissingMemberEntry>();
                            list.Add(record);
                        }

                        foreach (var evKvp in byList)
                        {
                            groundTruth.TryGetValue(evKvp.Key, out var gtEntry);
                            var payloadLines = new List<string>();
                            foreach (var r in evKvp.Value)
                            {
                                var pos = GetGroundTruthPosition(r.Member?.InternalId ?? -1, gtEntry);
                                var posPrefix = pos >= 0 ? "[POS " + (pos + 1) + "] " : string.Empty;
                                payloadLines.Add(posPrefix + (r.Member?.Name ?? "(unnamed)") + " | " + (r.Member?.Path ?? string.Empty));
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

        private static MediaBrowser.Controller.Entities.BaseItem FindById(
            MediaBrowser.Controller.Entities.BaseItem[] items, Guid guid)
        {
            if (items == null) return null;
            foreach (var item in items)
                if (item.Id == guid) return item;
            return null;
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