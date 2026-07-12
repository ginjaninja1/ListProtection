using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Logging;

namespace PlaylistProtection.EntryPoints
{
    public class PlaylistEventProbe : IServerEntryPoint
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IPlaylistManager _playlistManager;
        private readonly ILogger _logger;

        // Tracks playlist IDs that have had items added but not yet read back.
        // Key: playlist InternalId, Value: ListItemId(s) from the add event
        private readonly ConcurrentDictionary<long, List<long>> _pendingAddedByPlaylistInternalId
            = new ConcurrentDictionary<long, List<long>>();

        public PlaylistEventProbe(
            ILibraryManager libraryManager,
            IPlaylistManager playlistManager,
            ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _playlistManager = playlistManager;
            _logger = logManager.GetLogger(nameof(PlaylistEventProbe));
        }

        public void Run()
        {
            _logger.Info("[PlaylistEventProbe] Subscribing to ILibraryManager and IPlaylistManager events");

            _libraryManager.ItemAdded += OnItemAdded;
            _libraryManager.ItemUpdated += OnItemUpdated;
            _libraryManager.ItemRemoved += OnItemRemoved;

            _playlistManager.PlaylistItemsAdded += OnPlaylistItemsAdded;
            _playlistManager.PlaylistItemsRemoved += OnPlaylistItemsRemoved;
            _playlistManager.PlaylistItemsMoved += OnPlaylistItemsMoved;

            _logger.Info("[PlaylistEventProbe] Subscribed to 6 events — awaiting triggers");
        }

        // ── ILibraryManager ────────────────────────────────────────────────

        private void OnItemAdded(object sender, ItemChangeEventArgs e)
            => LogLibraryEvent("ILibraryManager.ItemAdded", e.Item);

        private void OnItemUpdated(object sender, ItemChangeEventArgs e)
        {
            LogLibraryEvent("ILibraryManager.ItemUpdated", e.Item);

            // If this is a playlist update following a PlaylistItemsAdded event,
            // now read back the membership to find the assigned ListItemEntryIds.
            if (!(e.Item is Playlist playlist))
                return;

            var internalId = playlist.InternalId;

            if (!_pendingAddedByPlaylistInternalId.TryRemove(internalId, out var pendingListItemIds))
                return;

            _logger.Info(
                "[PlaylistEventProbe] ItemUpdated triggered readback for playlist InternalId={0} Name={1}",
                internalId,
                playlist.Name ?? "(null)");

            try
            {
                var query = new InternalItemsQuery();
                var result = playlist.GetItemList(query);

                if (result == null || result.Length == 0)
                {
                    _logger.Info("[PlaylistEventProbe] Readback — GetItemList returned empty");
                    return;
                }

                foreach (var item in result)
                {
                    var isPending = pendingListItemIds.Contains(item.InternalId);

                    _logger.Info(
                        "[PlaylistEventProbe] Readback member | InternalId={0} | ListItemEntryId={1} | Name={2} | IsPendingAdd={3}",
                        item.InternalId,
                        item.ListItemEntryId,
                        item.Name ?? "(null)",
                        isPending);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("[PlaylistEventProbe] Readback failed: {0}", ex.Message);
            }
        }

        private void OnItemRemoved(object sender, ItemChangeEventArgs e)
            => LogLibraryEvent("ILibraryManager.ItemRemoved", e.Item);

        private void LogLibraryEvent(string eventName, BaseItem item)
        {
            if (item == null)
            {
                _logger.Info("[PlaylistEventProbe] {0} — item was null", eventName);
                return;
            }

            _logger.Info(
                "[PlaylistEventProbe] {0} | Type={1} | Name={2} | InternalId={3} | Id={4} | Path={5}",
                eventName,
                item.GetType().Name,
                item.Name ?? "(null)",
                item.InternalId,
                item.Id,
                item.Path ?? "(null)"
            );
        }

        // ── IPlaylistManager ───────────────────────────────────────────────

        private void OnPlaylistItemsAdded(object sender, PlaylistItemsAddedEventArgs e)
        {
            var playlist = e.Playlist;
            var playlistLabel = playlist != null
                ? $"Name={playlist.Name} | InternalId={playlist.InternalId} | Id={playlist.Id}"
                : "(null playlist)";

            if (e.ListItems == null || e.ListItems.Length == 0)
            {
                _logger.Info(
                    "[PlaylistEventProbe] IPlaylistManager.PlaylistItemsAdded | {0} | ListItems=(empty)",
                    playlistLabel);
                return;
            }

            var pendingIds = new List<long>();

            foreach (var item in e.ListItems)
            {
                _logger.Info(
                    "[PlaylistEventProbe] IPlaylistManager.PlaylistItemsAdded | {0} | ListItemEntryId={1} | ListItemId={2}",
                    playlistLabel,
                    item.ListItemEntryId,
                    item.ListItemId);

                pendingIds.Add(item.ListItemId);
            }

            // Queue readback — will fire when the subsequent ItemUpdated arrives
            if (playlist != null)
            {
                _pendingAddedByPlaylistInternalId.AddOrUpdate(
                    playlist.InternalId,
                    pendingIds,
                    (_, existing) => { existing.AddRange(pendingIds); return existing; });
            }
        }

        private void OnPlaylistItemsRemoved(object sender, PlaylistItemsRemovedEventArgs e)
        {
            var playlist = e.Playlist;
            var playlistLabel = playlist != null
                ? $"Name={playlist.Name} | InternalId={playlist.InternalId} | Id={playlist.Id}"
                : "(null playlist)";

            if (e.ListItemEntryIds == null || e.ListItemEntryIds.Length == 0)
            {
                _logger.Info(
                    "[PlaylistEventProbe] IPlaylistManager.PlaylistItemsRemoved | {0} | ListItemEntryIds=(empty)",
                    playlistLabel);
                return;
            }

            foreach (var entryId in e.ListItemEntryIds)
            {
                _logger.Info(
                    "[PlaylistEventProbe] IPlaylistManager.PlaylistItemsRemoved | {0} | ListItemEntryId={1}",
                    playlistLabel,
                    entryId);
            }
        }

        private void OnPlaylistItemsMoved(object sender, PlaylistItemsMovedEventArgs e)
        {
            var playlist = e.Playlist;
            var playlistLabel = playlist != null
                ? $"Name={playlist.Name} | InternalId={playlist.InternalId} | Id={playlist.Id}"
                : "(null playlist)";

            if (e.ListItemEntryIds == null || e.ListItemEntryIds.Length == 0)
            {
                _logger.Info(
                    "[PlaylistEventProbe] IPlaylistManager.PlaylistItemsMoved | {0} | ListItemEntryIds=(empty) | NewIndex={1}",
                    playlistLabel, e.NewIndex);
                return;
            }

            foreach (var entryId in e.ListItemEntryIds)
            {
                _logger.Info(
                    "[PlaylistEventProbe] IPlaylistManager.PlaylistItemsMoved | {0} | ListItemEntryId={1} | NewIndex={2}",
                    playlistLabel,
                    entryId,
                    e.NewIndex);
            }
        }

        // ── Cleanup ────────────────────────────────────────────────────────

        public void Dispose()
        {
            _libraryManager.ItemAdded -= OnItemAdded;
            _libraryManager.ItemUpdated -= OnItemUpdated;
            _libraryManager.ItemRemoved -= OnItemRemoved;

            _playlistManager.PlaylistItemsAdded -= OnPlaylistItemsAdded;
            _playlistManager.PlaylistItemsRemoved -= OnPlaylistItemsRemoved;
            _playlistManager.PlaylistItemsMoved -= OnPlaylistItemsMoved;

            _logger.Info("[PlaylistEventProbe] Disposed — unsubscribed from all events");
        }
    }
}