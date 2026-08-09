using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace ListProtection.EntryPoints
{
    /// <summary>
    /// Production IServerEntryPoint — keeps ground truth in sync as playlist
    /// membership changes via Emby events.
    ///
    /// Add flow (two-event):
    ///   1. PlaylistItemsAdded fires → ListItemEntryId is 0 (not yet assigned)
    ///      Record ListItemId in _pendingAdds and wait.
    ///   2. ItemUpdated fires for same playlist → DB write complete.
    ///      Readback via playlist.GetItemList(), match InternalId → ListItemEntryId.
    ///      Write new GroundTruthMember to store.
    ///
    /// Remove flow (single-event):
    ///   PlaylistItemsRemoved fires with ListItemEntryIds[] already populated.
    ///   Match against store, remove member, save.
    ///
    /// Add/remove/move events are treated as benign/intentional — GT is updated
    /// silently (no missing-member event raised). Each successful GT update is
    /// logged to EventStore as MemberAdded / MemberRemoved / MemberReordered for
    /// user visibility.
    ///
    /// Repair suppression:
    ///   When ListRepairService is executing an atomic remove→add cycle it
    ///   registers the playlist InternalId in Plugin.RepairSuppressedLists.
    ///   Both OnPlaylistItemsAdded and OnPlaylistItemsRemoved skip suppressed
    ///   playlists entirely — repair owns the GT update for that window.
    ///   A warning is logged if a suppressed event is dropped, so the edge case
    ///   of a simultaneous user action during a repair is visible in logs.
    ///
    /// PROVEN behaviours used here:
    ///   - ItemUpdated does NOT fire after a remove (no readback on remove path).
    ///   - ListItemEntryId is always 0 at PlaylistItemsAdded event time.
    ///   - ListItemEntryId is correctly populated on readback after ItemUpdated.
    ///   - PlaylistItemsRemoved carries ListItemEntryIds[] (not ListItemIds).
    ///
    /// Stores are accessed via ListProtectionPlugin.Instance (singleton on Plugin.cs).
    /// </summary>
    public class PlaylistMaintenanceService : IServerEntryPoint
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IPlaylistManager _playlistManager;
        private readonly ILogger _logger;

        // Key: playlist InternalId (long)
        // Value: list of ListItemIds from the add event, awaiting readback
        private readonly ConcurrentDictionary<long, List<long>> _pendingAdds
            = new ConcurrentDictionary<long, List<long>>();

        public PlaylistMaintenanceService(
            ILibraryManager libraryManager,
            IPlaylistManager playlistManager,
            ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _playlistManager = playlistManager;
            _logger = logManager.GetLogger("List Protection");
        }

        public void Run()
        {
            _libraryManager.ItemUpdated += OnItemUpdated;
            _playlistManager.PlaylistItemsAdded += OnPlaylistItemsAdded;
            _playlistManager.PlaylistItemsRemoved += OnPlaylistItemsRemoved;
            _playlistManager.PlaylistItemsMoved += OnPlaylistItemsMoved;

            _logger.Debug("[PlaylistMaintenanceService] Subscribed to playlist events");
        }

        // ── PlaylistItemsAdded ─────────────────────────────────────────────

        private void OnPlaylistItemsAdded(object sender, PlaylistItemsAddedEventArgs e)
        {
            var playlist = e.Playlist;

            if (playlist == null || e.ListItems == null || e.ListItems.Length == 0)
                return;

            var playlistIdN = playlist.Id.ToString("N");

            if (!IsProtected(playlistIdN))
                return;

            // Repair owns the GT update for this add cycle — skip maintenance queuing.
            // Warning logged so simultaneous user actions during repair are visible.
            var plugin = ListProtectionPlugin.Instance;
            if (plugin != null && plugin.RepairSuppressedLists.ContainsKey(playlist.InternalId))
            {
                _logger.Warn(
                    "[PlaylistMaintenanceService] PlaylistItemsAdded — repair in progress for '{0}' ({1}) — skipping readback queue (repair owns GT update)",
                    playlist.Name ?? "(null)",
                    playlistIdN);
                return;
            }

            _logger.Debug(
                "[PlaylistMaintenanceService] PlaylistItemsAdded — protected playlist '{0}' ({1}) | {2} item(s) — queuing readback",
                playlist.Name ?? "(null)",
                playlistIdN,
                e.ListItems.Length);

            var pendingIds = new List<long>(e.ListItems.Length);
            foreach (var item in e.ListItems)
                pendingIds.Add(item.ListItemId);

            _pendingAdds.AddOrUpdate(
                playlist.InternalId,
                pendingIds,
                (_, existing) => { existing.AddRange(pendingIds); return existing; });
        }

        // ── ItemUpdated ────────────────────────────────────────────────────

        private void OnItemUpdated(object sender, ItemChangeEventArgs e)
        {
            if (!(e.Item is Playlist playlist))
                return;

            if (!_pendingAdds.TryRemove(playlist.InternalId, out var pendingListItemIds))
                return;

            var playlistIdN = playlist.Id.ToString("N");

            _logger.Debug(
                "[PlaylistMaintenanceService] ItemUpdated — readback for playlist '{0}' ({1}) | expecting {2} new member(s)",
                playlist.Name ?? "(null)",
                playlistIdN,
                pendingListItemIds.Count);

            try
            {
                var members = playlist.GetItemList(new InternalItemsQuery());

                if (members == null || members.Length == 0)
                {
                    _logger.Warn(
                        "[PlaylistMaintenanceService] Readback returned empty for playlist {0} — cannot add member(s)",
                        playlistIdN);
                    return;
                }

                var plugin = ListProtectionPlugin.Instance;
                if (plugin == null)
                {
                    _logger.Error("[PlaylistMaintenanceService] Plugin instance is null — cannot update ground truth");
                    return;
                }

                List<GroundTruthMember> addedMembers;

                plugin.WriterLock.Wait();
                try
                {
                    var entries = plugin.GroundTruthStore.Load();

                    if (!entries.TryGetValue(playlistIdN, out var entry))
                    {
                        _logger.Warn(
                            "[PlaylistMaintenanceService] No ground truth entry for playlist {0} — skipping add",
                            playlistIdN);
                        return;
                    }

                    addedMembers = new List<GroundTruthMember>();

                    foreach (var item in members)
                    {
                        if (!pendingListItemIds.Contains(item.InternalId))
                            continue;

                        // Duplicate guard — defend against double-fire or re-entry
                        var alreadyPresent = false;
                        foreach (var existing in entry.Members)
                        {
                            if (existing.ListItemEntryId == item.ListItemEntryId)
                            {
                                alreadyPresent = true;
                                break;
                            }
                        }

                        if (alreadyPresent)
                        {
                            _logger.Debug(
                                "[PlaylistMaintenanceService] Member ListItemEntryId={0} already in ground truth for playlist {1} — skipping",
                                item.ListItemEntryId,
                                playlistIdN);
                            continue;
                        }

                        var newMember = new GroundTruthMember
                        {
                            InternalId = item.InternalId,
                            Id = item.Id.ToString("N"),
                            Name = item.Name ?? string.Empty,
                            Path = item.Path ?? string.Empty,
                            ListItemEntryId = item.ListItemEntryId
                        };

                        entry.Members.Add(newMember);
                        addedMembers.Add(newMember);

                        _logger.Info(
                            "[List Protection] Added '{0}' to '{1}'",
                            item.Name ?? "(null)",
                            playlist.Name ?? "(unnamed)");
                    }

                    if (addedMembers.Count > 0)
                    {
                        plugin.GroundTruthStore.Save(entries);
                        _logger.Debug(
                            "[PlaylistMaintenanceService] Saved {0} new member(s) to ground truth for playlist {1}",
                            addedMembers.Count,
                            playlistIdN);
                    }
                    else
                    {
                        _logger.Debug(
                            "[PlaylistMaintenanceService] No new members matched pending add list for playlist {0} — store unchanged",
                            playlistIdN);
                    }
                }
                finally
                {
                    plugin.WriterLock.Release();
                }

                if (addedMembers.Count > 0)
                    AppendMemberEvent("MemberAdded", playlistIdN, playlist.Name, addedMembers);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[PlaylistMaintenanceService] Readback/add failed", ex);
            }
        }

        // ── PlaylistItemsRemoved ───────────────────────────────────────────

        private void OnPlaylistItemsRemoved(object sender, PlaylistItemsRemovedEventArgs e)
        {
            var playlist = e.Playlist;

            if (playlist == null || e.ListItemEntryIds == null || e.ListItemEntryIds.Length == 0)
                return;

            var playlistIdN = playlist.Id.ToString("N");

            if (!IsProtected(playlistIdN))
                return;

            // Repair owns the GT update for this remove cycle — skip maintenance GT remove.
            // Warning logged so simultaneous user actions during repair are visible.
            var plugin = ListProtectionPlugin.Instance;
            if (plugin != null && plugin.RepairSuppressedLists.ContainsKey(playlist.InternalId))
            {
                _logger.Warn(
                    "[PlaylistMaintenanceService] PlaylistItemsRemoved — repair in progress for '{0}' ({1}) — skipping GT remove (repair owns GT update)",
                    playlist.Name ?? "(null)",
                    playlistIdN);
                return;
            }

            _logger.Debug(
                "[PlaylistMaintenanceService] PlaylistItemsRemoved — protected playlist '{0}' ({1}) | {2} entry id(s) to remove",
                playlist.Name ?? "(null)",
                playlistIdN,
                e.ListItemEntryIds.Length);

            try
            {
                List<GroundTruthMember> removedMembers;

                plugin.WriterLock.Wait();
                try
                {
                    var entries = plugin.GroundTruthStore.Load();

                    if (!entries.TryGetValue(playlistIdN, out var entry))
                    {
                        _logger.Warn(
                            "[PlaylistMaintenanceService] No ground truth entry for playlist {0} — skipping remove",
                            playlistIdN);
                        return;
                    }

                    removedMembers = new List<GroundTruthMember>();

                    foreach (var entryId in e.ListItemEntryIds)
                    {
                        // Iterate backwards — safe removal from List<T>
                        for (var i = entry.Members.Count - 1; i >= 0; i--)
                        {
                            if (entry.Members[i].ListItemEntryId != entryId)
                                continue;

                            _logger.Info(
                                "[List Protection] Removed '{0}' from '{1}'",
                                entry.Members[i].Name ?? "(null)",
                                playlist.Name ?? "(unnamed)");

                            removedMembers.Add(entry.Members[i]);
                            entry.Members.RemoveAt(i);
                            break; // ListItemEntryId is unique — stop after first match
                        }
                    }

                    if (removedMembers.Count > 0)
                    {
                        plugin.GroundTruthStore.Save(entries);
                        _logger.Debug(
                            "[PlaylistMaintenanceService] Removed {0} member(s) from ground truth for playlist {1}",
                            removedMembers.Count,
                            playlistIdN);
                    }
                    else
                    {
                        _logger.Warn(
                            "[PlaylistMaintenanceService] PlaylistItemsRemoved fired but no matching ListItemEntryIds found in ground truth for playlist {0} — store unchanged",
                            playlistIdN);
                    }
                }
                finally
                {
                    plugin.WriterLock.Release();
                }

                if (removedMembers.Count > 0)
                    AppendMemberEvent("MemberRemoved", playlistIdN, playlist.Name, removedMembers);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[PlaylistMaintenanceService] Remove failed", ex);
            }
        }

        // ── PlaylistItemsMoved ──────────────────────────────────────────────

        private void OnPlaylistItemsMoved(object sender, PlaylistItemsMovedEventArgs e)
        {
            var playlist = e.Playlist;

            if (playlist == null || e.ListItemEntryIds == null || e.ListItemEntryIds.Length == 0)
                return;

            var playlistIdN = playlist.Id.ToString("N");

            if (!IsProtected(playlistIdN))
                return;

            // Repair owns the GT update for this cycle — skip maintenance GT reorder.
            // Warning logged so simultaneous user actions during repair are visible.
            var plugin = ListProtectionPlugin.Instance;
            if (plugin != null && plugin.RepairSuppressedLists.ContainsKey(playlist.InternalId))
            {
                _logger.Warn(
                    "[PlaylistMaintenanceService] PlaylistItemsMoved — repair in progress for '{0}' ({1}) — skipping GT reorder (repair owns GT update)",
                    playlist.Name ?? "(null)",
                    playlistIdN);
                return;
            }

            _logger.Debug(
                "[PlaylistMaintenanceService] PlaylistItemsMoved — protected playlist '{0}' ({1}) | {2} entry id(s) moving to index {3}",
                playlist.Name ?? "(null)",
                playlistIdN,
                e.ListItemEntryIds.Length,
                e.NewIndex);

            try
            {
                List<GroundTruthMember> moving;
                int insertAt;

                plugin.WriterLock.Wait();
                try
                {
                    var entries = plugin.GroundTruthStore.Load();

                    if (!entries.TryGetValue(playlistIdN, out var entry))
                    {
                        _logger.Warn(
                            "[PlaylistMaintenanceService] No ground truth entry for playlist {0} — skipping reorder",
                            playlistIdN);
                        return;
                    }

                    // Pull the moved members out (preserving their relative order),
                    // then reinsert as a contiguous block starting at NewIndex.
                    moving = new List<GroundTruthMember>();

                    foreach (var entryId in e.ListItemEntryIds)
                    {
                        var member = entry.Members.Find(m => m.ListItemEntryId == entryId);
                        if (member == null)
                        {
                            _logger.Warn(
                                "[PlaylistMaintenanceService] PlaylistItemsMoved — ListItemEntryId={0} not found in ground truth for playlist {1} — skipping that entry",
                                entryId,
                                playlistIdN);
                            continue;
                        }

                        moving.Add(member);
                    }

                    if (moving.Count == 0)
                    {
                        _logger.Warn(
                            "[PlaylistMaintenanceService] PlaylistItemsMoved fired but no matching ListItemEntryIds found in ground truth for playlist {0} — store unchanged",
                            playlistIdN);
                        return;
                    }

                    foreach (var member in moving)
                        entry.Members.Remove(member);

                    insertAt = Math.Min(Math.Max(e.NewIndex, 0), entry.Members.Count);
                    entry.Members.InsertRange(insertAt, moving);

                    plugin.GroundTruthStore.Save(entries);

                    _logger.Info(
                        "[List Protection] Reordered {0} member(s) in '{1}'",
                        moving.Count,
                        playlist.Name ?? "(unnamed)");
                }
                finally
                {
                    plugin.WriterLock.Release();
                }

                AppendMemberEvent("MemberReordered", playlistIdN, playlist.Name, moving);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[PlaylistMaintenanceService] Reorder failed", ex);
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private bool IsProtected(string playlistIdN)
        {
            var plugin = ListProtectionPlugin.Instance;
            if (plugin == null) return false;

            var protectedIds = plugin.ListStore.Load();
            return protectedIds.Contains(playlistIdN);
        }

        /// <summary>
        /// Appends a MemberAdded/MemberRemoved event entry for a batch of members,
        /// matching the "Name | Path" payload convention used elsewhere in EventStore.
        /// </summary>
        private void AppendMemberEvent(string eventType, string listIdN, string listName, List<GroundTruthMember> members)
        {
            try
            {
                var plugin = ListProtectionPlugin.Instance;
                if (plugin == null) return;

                var payloadLines = new List<string>();
                foreach (var member in members)
                    payloadLines.Add((member.Name ?? "(unnamed)") + " | " + (member.Path ?? string.Empty));

                plugin.EventStore.Append(new EventEntry
                {
                    EventType = eventType,
                    PlaylistId = listIdN,
                    ListName = listName ?? string.Empty,
                    OccurredAt = DateTime.UtcNow,
                    Payload = string.Join("\n", payloadLines)
                });
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[PlaylistMaintenanceService] Failed to write " + eventType + " event", ex);
            }
        }

        // ── Cleanup ────────────────────────────────────────────────────────

        public void Dispose()
        {
            _libraryManager.ItemUpdated -= OnItemUpdated;
            _playlistManager.PlaylistItemsAdded -= OnPlaylistItemsAdded;
            _playlistManager.PlaylistItemsRemoved -= OnPlaylistItemsRemoved;
            _playlistManager.PlaylistItemsMoved -= OnPlaylistItemsMoved;

            _logger.Debug("[PlaylistMaintenanceService] Disposed — unsubscribed from all events");
        }
    }
}