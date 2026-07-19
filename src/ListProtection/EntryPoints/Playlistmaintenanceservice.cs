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
            _logger = logManager.GetLogger(nameof(PlaylistMaintenanceService));
        }

        public void Run()
        {
            _libraryManager.ItemUpdated += OnItemUpdated;
            _playlistManager.PlaylistItemsAdded += OnPlaylistItemsAdded;
            _playlistManager.PlaylistItemsRemoved += OnPlaylistItemsRemoved;

            _logger.Info("[PlaylistMaintenanceService] Subscribed to playlist events");
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

            _logger.Info(
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

            _logger.Info(
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

                    var added = 0;

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
                            _logger.Info(
                                "[PlaylistMaintenanceService] Member ListItemEntryId={0} already in ground truth for playlist {1} — skipping",
                                item.ListItemEntryId,
                                playlistIdN);
                            continue;
                        }

                        entry.Members.Add(new GroundTruthMember
                        {
                            InternalId = item.InternalId,
                            Id = item.Id.ToString("N"),
                            Name = item.Name ?? string.Empty,
                            Path = item.Path ?? string.Empty,
                            ListItemEntryId = item.ListItemEntryId
                        });

                        _logger.Info(
                            "[PlaylistMaintenanceService] Added member '{0}' | InternalId={1} | ListItemEntryId={2} | playlist={3}",
                            item.Name ?? "(null)",
                            item.InternalId,
                            item.ListItemEntryId,
                            playlistIdN);

                        added++;
                    }

                    if (added > 0)
                    {
                        plugin.GroundTruthStore.Save(entries);
                        _logger.Info(
                            "[PlaylistMaintenanceService] Saved {0} new member(s) to ground truth for playlist {1}",
                            added,
                            playlistIdN);
                    }
                    else
                    {
                        _logger.Info(
                            "[PlaylistMaintenanceService] No new members matched pending add list for playlist {0} — store unchanged",
                            playlistIdN);
                    }
                }
                finally
                {
                    plugin.WriterLock.Release();
                }
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

            _logger.Info(
                "[PlaylistMaintenanceService] PlaylistItemsRemoved — protected playlist '{0}' ({1}) | {2} entry id(s) to remove",
                playlist.Name ?? "(null)",
                playlistIdN,
                e.ListItemEntryIds.Length);

            try
            {
                var plugin = ListProtectionPlugin.Instance;
                if (plugin == null)
                {
                    _logger.Error("[PlaylistMaintenanceService] Plugin instance is null — cannot update ground truth");
                    return;
                }

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

                    var removed = 0;

                    foreach (var entryId in e.ListItemEntryIds)
                    {
                        // Iterate backwards — safe removal from List<T>
                        for (var i = entry.Members.Count - 1; i >= 0; i--)
                        {
                            if (entry.Members[i].ListItemEntryId != entryId)
                                continue;

                            _logger.Info(
                                "[PlaylistMaintenanceService] Removing member '{0}' | ListItemEntryId={1} | playlist={2}",
                                entry.Members[i].Name ?? "(null)",
                                entryId,
                                playlistIdN);

                            entry.Members.RemoveAt(i);
                            removed++;
                            break; // ListItemEntryId is unique — stop after first match
                        }

                        if (removed == 0 || entry.Members.Count == 0)
                            continue;
                    }

                    if (removed > 0)
                    {
                        plugin.GroundTruthStore.Save(entries);
                        _logger.Info(
                            "[PlaylistMaintenanceService] Removed {0} member(s) from ground truth for playlist {1}",
                            removed,
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
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[PlaylistMaintenanceService] Remove failed", ex);
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private bool IsProtected(string playlistIdN)
        {
            var plugin = ListProtectionPlugin.Instance;
            if (plugin == null) return false;

            var protectedIds = plugin.PlaylistStore.Load();
            return protectedIds.Contains(playlistIdN);
        }

        // ── Cleanup ────────────────────────────────────────────────────────

        public void Dispose()
        {
            _libraryManager.ItemUpdated -= OnItemUpdated;
            _playlistManager.PlaylistItemsAdded -= OnPlaylistItemsAdded;
            _playlistManager.PlaylistItemsRemoved -= OnPlaylistItemsRemoved;

            _logger.Info("[PlaylistMaintenanceService] Disposed — unsubscribed from all events");
        }
    }
}