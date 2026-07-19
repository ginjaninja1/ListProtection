using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;

namespace ListProtection.EntryPoints
{
    public class PlaylistEventProbe : IServerEntryPoint
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IPlaylistManager _playlistManager;
        private readonly IProviderManager _providerManager;
        private readonly ILogger _logger;

        private readonly ConcurrentDictionary<long, List<long>> _pendingAddedByPlaylistInternalId
            = new ConcurrentDictionary<long, List<long>>();

        public PlaylistEventProbe(
            ILibraryManager libraryManager,
            IPlaylistManager playlistManager,
            IProviderManager providerManager,
            ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _playlistManager = playlistManager;
            _providerManager = providerManager;
            _logger = logManager.GetLogger(nameof(PlaylistEventProbe));
        }

        public void Run()
        {
            _logger.Info("[PlaylistEventProbe] Subscribing to all library, playlist and provider events");

            _libraryManager.ItemAdding += OnItemAdding;
            _libraryManager.ItemAdded += OnItemAdded;
            _libraryManager.ItemUpdated += OnItemUpdated;
            _libraryManager.ItemRemoved += OnItemRemoved;

            _playlistManager.PlaylistItemsAdded += OnPlaylistItemsAdded;
            _playlistManager.PlaylistItemsRemoved += OnPlaylistItemsRemoved;
            _playlistManager.PlaylistItemsMoved += OnPlaylistItemsMoved;

            _providerManager.RefreshStarted += OnRefreshStarted;
            _providerManager.RefreshCompleted += OnRefreshCompleted;

            _logger.Info("[PlaylistEventProbe] Subscribed to 9 events — awaiting triggers");
        }

        // ── ILibraryManager ────────────────────────────────────────────────

        private void OnItemAdding(object sender, ItemChangeEventArgs e)
            => LogLibraryEvent("ILibraryManager.ItemAdding", e?.Item);

        private void OnItemAdded(object sender, ItemChangeEventArgs e)
            => LogLibraryEvent("ILibraryManager.ItemAdded", e?.Item);

        private void OnItemUpdated(object sender, ItemChangeEventArgs e)
        {
            LogLibraryEvent("ILibraryManager.ItemUpdated", e?.Item);

            if (!(e?.Item is Playlist playlist))
                return;

            var internalId = playlist.InternalId;
            if (!_pendingAddedByPlaylistInternalId.TryRemove(internalId, out var pendingListItemIds))
                return;

            _logger.Info(
                "[PlaylistEventProbe] ItemUpdated — readback for playlist InternalId={0} Name={1}",
                internalId, playlist.Name ?? "(null)");

            try
            {
                var result = playlist.GetItemList(new InternalItemsQuery());

                if (result == null || result.Length == 0)
                {
                    _logger.Info("[PlaylistEventProbe] Readback — GetItemList returned empty");
                    return;
                }

                foreach (var item in result)
                {
                    _logger.Info(
                        "[PlaylistEventProbe] Readback member | InternalId={0} | ListItemEntryId={1} | Name={2} | IsPendingAdd={3}",
                        item.InternalId,
                        item.ListItemEntryId,
                        item.Name ?? "(null)",
                        pendingListItemIds.Contains(item.InternalId));
                }
            }
            catch (Exception ex)
            {
                _logger.Error("[PlaylistEventProbe] Readback failed: {0}", ex.Message);
            }
        }

        private void OnItemRemoved(object sender, ItemChangeEventArgs e)
            => LogLibraryEvent("ILibraryManager.ItemRemoved", e?.Item);

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
                item.Path ?? "(null)");
        }

        // ── IPlaylistManager ───────────────────────────────────────────────

        private void OnPlaylistItemsAdded(object sender, PlaylistItemsAddedEventArgs e)
        {
            var playlist = e.Playlist;
            var label = PlaylistLabel(playlist);

            if (e.ListItems == null || e.ListItems.Length == 0)
            {
                _logger.Info("[PlaylistEventProbe] IPlaylistManager.PlaylistItemsAdded | {0} | ListItems=(empty)", label);
                return;
            }

            var pendingIds = new List<long>();
            foreach (var item in e.ListItems)
            {
                _logger.Info(
                    "[PlaylistEventProbe] IPlaylistManager.PlaylistItemsAdded | {0} | ListItemEntryId={1} | ListItemId={2}",
                    label, item.ListItemEntryId, item.ListItemId);
                pendingIds.Add(item.ListItemId);
            }

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
            var label = PlaylistLabel(e.Playlist);

            if (e.ListItemEntryIds == null || e.ListItemEntryIds.Length == 0)
            {
                _logger.Info("[PlaylistEventProbe] IPlaylistManager.PlaylistItemsRemoved | {0} | ListItemEntryIds=(empty)", label);
                return;
            }

            foreach (var entryId in e.ListItemEntryIds)
            {
                _logger.Info(
                    "[PlaylistEventProbe] IPlaylistManager.PlaylistItemsRemoved | {0} | ListItemEntryId={1}",
                    label, entryId);
            }
        }

        private void OnPlaylistItemsMoved(object sender, PlaylistItemsMovedEventArgs e)
        {
            var label = PlaylistLabel(e.Playlist);

            if (e.ListItemEntryIds == null || e.ListItemEntryIds.Length == 0)
            {
                _logger.Info(
                    "[PlaylistEventProbe] IPlaylistManager.PlaylistItemsMoved | {0} | ListItemEntryIds=(empty) | NewIndex={1}",
                    label, e.NewIndex);
                return;
            }

            foreach (var entryId in e.ListItemEntryIds)
            {
                _logger.Info(
                    "[PlaylistEventProbe] IPlaylistManager.PlaylistItemsMoved | {0} | ListItemEntryId={1} | NewIndex={2}",
                    label, entryId, e.NewIndex);
            }
        }

        // ── IProviderManager ───────────────────────────────────────────────

        private void OnRefreshStarted(object sender, GenericEventArgs<RefreshProgressInfo> e)
        {
            _logger.Info(
                "[PlaylistEventProbe] IProviderManager.RefreshStarted | Item={0} | Type={1} | Progress={2}",
                e?.Argument?.Item?.Name ?? "(null)",
                e?.Argument?.Item?.GetType().Name ?? "(null)",
                e?.Argument?.Progress ?? 0);
        }

        private void OnRefreshCompleted(object sender, GenericEventArgs<RefreshProgressInfo> e)
        {
            _logger.Info(
                "[PlaylistEventProbe] IProviderManager.RefreshCompleted | Item={0} | Type={1} | Progress={2}",
                e?.Argument?.Item?.Name ?? "(null)",
                e?.Argument?.Item?.GetType().Name ?? "(null)",
                e?.Argument?.Progress ?? 0);
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static string PlaylistLabel(Playlist playlist)
        {
            return playlist != null
                ? $"Name={playlist.Name} | InternalId={playlist.InternalId} | Id={playlist.Id}"
                : "(null playlist)";
        }

        // ── Cleanup ────────────────────────────────────────────────────────

        public void Dispose()
        {
            _libraryManager.ItemAdding -= OnItemAdding;
            _libraryManager.ItemAdded -= OnItemAdded;
            _libraryManager.ItemUpdated -= OnItemUpdated;
            _libraryManager.ItemRemoved -= OnItemRemoved;

            _playlistManager.PlaylistItemsAdded -= OnPlaylistItemsAdded;
            _playlistManager.PlaylistItemsRemoved -= OnPlaylistItemsRemoved;
            _playlistManager.PlaylistItemsMoved -= OnPlaylistItemsMoved;

            _providerManager.RefreshStarted -= OnRefreshStarted;
            _providerManager.RefreshCompleted -= OnRefreshCompleted;

            _logger.Info("[PlaylistEventProbe] Disposed — unsubscribed from all 9 events");
        }
    }
}