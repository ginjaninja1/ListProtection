using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ListProtection.EntryPoints
{
    /// <summary>
    /// PROBE — two questions:
    ///
    ///   Q1. Does ItemAdding fire for playlist membership changes, and if so
    ///       what does its payload look like vs ItemUpdated?
    ///       Does ItemAdding carry a populated ListItemEntryId or is it 0?
    ///
    ///   Q2. Does ILibraryPostScanTask.Run() get called by Emby after a library scan?
    ///       If the log shows the Q2 line after a scan, post-scan hooking is VIABLE.
    ///
    /// HOW TO USE:
    ///   1. Build and deploy. Check log for subscription confirmation on startup.
    ///   2. Add a track to any playlist. Look for ItemAdding entries — does it fire?
    ///      What is ListItemEntryId at that point vs at ItemUpdated readback?
    ///   3. Run a manual library scan (Dashboard → Scan All Libraries).
    ///      Check log for the Q2 line after the scan completes.
    ///
    /// DELETE once findings recorded in Handover.md.
    /// </summary>
    public class ScanAndAddEventProbe : IServerEntryPoint, ILibraryPostScanTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IPlaylistManager _playlistManager;
        private readonly ILogger _logger;

        private readonly ConcurrentDictionary<long, List<long>> _pendingAdds
            = new ConcurrentDictionary<long, List<long>>();

        public ScanAndAddEventProbe(
            ILibraryManager libraryManager,
            IPlaylistManager playlistManager,
            ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _playlistManager = playlistManager;
            _logger = logManager.GetLogger(nameof(ScanAndAddEventProbe));
        }

        // ── IServerEntryPoint ──────────────────────────────────────────────

        public void Run()
        {
            _libraryManager.ItemAdding += OnItemAdding;
            _libraryManager.ItemAdded += OnItemAdded;
            _libraryManager.ItemUpdated += OnItemUpdated;

            _playlistManager.PlaylistItemsAdded += OnPlaylistItemsAdded;

            _logger.Info("[ScanAndAddEventProbe] Subscribed to ItemAdding, ItemAdded, ItemUpdated, PlaylistItemsAdded");
        }

        // ── ILibraryPostScanTask ───────────────────────────────────────────

        public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.Info("[ScanAndAddEventProbe] Q2: ILibraryPostScanTask.Run() called — post-scan hook is VIABLE");
            return Task.CompletedTask;
        }

        // ── Q1: PlaylistItemsAdded ─────────────────────────────────────────

        private void OnPlaylistItemsAdded(object sender, PlaylistItemsAddedEventArgs e)
        {
            var playlist = e.Playlist;
            if (playlist == null) return;

            _logger.Info(
                "[ScanAndAddEventProbe] Q1: PlaylistItemsAdded | Playlist={0} | InternalId={1} | ItemCount={2}",
                playlist.Name ?? "(null)",
                playlist.InternalId,
                e.ListItems?.Length ?? 0);

            var pendingIds = new List<long>();
            if (e.ListItems != null)
            {
                foreach (var item in e.ListItems)
                {
                    _logger.Info(
                        "[ScanAndAddEventProbe] Q1:   ListItem | ListItemId={0} | ListItemEntryId={1} — expect EntryId=0 here",
                        item.ListItemId,
                        item.ListItemEntryId);
                    pendingIds.Add(item.ListItemId);
                }
            }

            if (pendingIds.Count > 0)
                _pendingAdds.AddOrUpdate(
                    playlist.InternalId,
                    pendingIds,
                    (_, existing) => { existing.AddRange(pendingIds); return existing; });
        }

        // ── Q1: ItemAdding ─────────────────────────────────────────────────

        private void OnItemAdding(object sender, ItemChangeEventArgs e)
        {
            if (e?.Item == null) return;
            // Log everything — we want to know if this fires for Playlist types
            // and whether it carries any ListItemEntryId information
            _logger.Info(
                "[ScanAndAddEventProbe] Q1: ItemAdding | Type={0} | Name={1} | InternalId={2} | ListItemEntryId={3}",
                e.Item.GetType().Name,
                e.Item.Name ?? "(null)",
                e.Item.InternalId,
                e.Item.ListItemEntryId);
        }

        // ── Q1: ItemAdded ──────────────────────────────────────────────────

        private void OnItemAdded(object sender, ItemChangeEventArgs e)
        {
            if (e?.Item == null) return;
            _logger.Info(
                "[ScanAndAddEventProbe] Q1: ItemAdded | Type={0} | Name={1} | InternalId={2} | ListItemEntryId={3}",
                e.Item.GetType().Name,
                e.Item.Name ?? "(null)",
                e.Item.InternalId,
                e.Item.ListItemEntryId);
        }

        // ── Q1: ItemUpdated readback ───────────────────────────────────────

        private void OnItemUpdated(object sender, ItemChangeEventArgs e)
        {
            if (e?.Item == null) return;

            if (!(e.Item is Playlist playlist)) return;

            if (!_pendingAdds.TryRemove(playlist.InternalId, out var pendingIds)) return;

            _logger.Info(
                "[ScanAndAddEventProbe] Q1: ItemUpdated readback | Playlist={0} | InternalId={1} | PendingCount={2}",
                playlist.Name ?? "(null)",
                playlist.InternalId,
                pendingIds.Count);

            try
            {
                var members = playlist.GetItemList(new InternalItemsQuery());
                if (members == null || members.Length == 0)
                {
                    _logger.Info("[ScanAndAddEventProbe] Q1:   Readback returned empty");
                    return;
                }

                foreach (var m in members)
                {
                    _logger.Info(
                        "[ScanAndAddEventProbe] Q1:   Member | Name={0} | InternalId={1} | ListItemEntryId={2} | IsPending={3}",
                        m.Name ?? "(null)",
                        m.InternalId,
                        m.ListItemEntryId,
                        pendingIds.Contains(m.InternalId));
                }
            }
            catch (Exception ex)
            {
                _logger.Error("[ScanAndAddEventProbe] Q1: Readback threw — {0}", ex.Message);
            }
        }

        // ── Cleanup ────────────────────────────────────────────────────────

        public void Dispose()
        {
            _libraryManager.ItemAdding -= OnItemAdding;
            _libraryManager.ItemAdded -= OnItemAdded;
            _libraryManager.ItemUpdated -= OnItemUpdated;

            _playlistManager.PlaylistItemsAdded -= OnPlaylistItemsAdded;

            _logger.Info("[ScanAndAddEventProbe] Disposed");
        }
    }
}