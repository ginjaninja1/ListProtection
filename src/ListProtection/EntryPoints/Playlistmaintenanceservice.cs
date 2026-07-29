using ListProtection.Services;
using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using System;

namespace ListProtection.EntryPoints
{
    /// <summary>
    /// Production IServerEntryPoint — keeps ground truth in sync as playlist
    /// membership changes via Emby events.
    ///
    /// Add flow (reconciliation, not two-event hand-off):
    ///   PlaylistItemsAdded fires with ListItemEntryId == 0 (not yet assigned),
    ///   so it is logged only — it carries no usable identity to act on.
    ///   ItemUpdated then fires for the playlist once the DB write is complete.
    ///   On every ItemUpdated for a protected playlist we do a full readback via
    ///   playlist.GetItemList() and reconcile: any live member whose
    ///   ListItemEntryId is not already present in ground truth is added via
    ///   GroundTruthMemberFactory, so it is captured with full type-specific
    ///   metadata identically to every other capture site.
    ///
    ///   This replaces an earlier design that queued ListItemIds from
    ///   PlaylistItemsAdded in an in-memory dictionary and only acted on
    ///   ItemUpdated if a matching queue entry existed. That hand-off was a
    ///   single point of failure — any restart, missed event, or event-order
    ///   surprise between the two events silently dropped the add forever,
    ///   with no self-correction. Reconciliation is idempotent and safe to run
    ///   on every ItemUpdated for a protected playlist: it only ever adds
    ///   members that are live in the playlist but absent from ground truth,
    ///   so there is no path left to silently lose a genuine add.
    ///
    /// Remove flow (single-event):
    ///   PlaylistItemsRemoved fires with ListItemEntryIds[] already populated.
    ///   Match against store, remove member, save.
    ///
    /// Repair suppression:
    ///   When ListRepairService is executing an atomic remove→add cycle it
    ///   registers the playlist InternalId in Plugin.RepairSuppressedLists.
    ///   Both OnItemUpdated and OnPlaylistItemsRemoved skip suppressed
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
            // ListItemEntryId is always 0 at this point (not yet assigned by the
            // DB write), so this event carries no identity we can act on.
            // Logged only for diagnostics — the actual ground truth update
            // happens in OnItemUpdated via reconciliation, once the write has
            // landed and ListItemEntryId is real.
            var playlist = e.Playlist;

            if (playlist == null || e.ListItems == null || e.ListItems.Length == 0)
                return;

            _logger.Info(
                "[PlaylistMaintenanceService] PlaylistItemsAdded — playlist '{0}' ({1}) | {2} item(s) — awaiting ItemUpdated for reconciliation",
                playlist.Name ?? "(null)",
                playlist.Id.ToString("N"),
                e.ListItems.Length);
        }

        // ── ItemUpdated ────────────────────────────────────────────────────

        private void OnItemUpdated(object sender, ItemChangeEventArgs e)
        {
            if (!(e.Item is Playlist playlist))
                return;

            var playlistIdN = playlist.Id.ToString("N");

            if (!IsProtected(playlistIdN))
                return;

            var plugin = ListProtectionPlugin.Instance;
            if (plugin == null)
            {
                _logger.Error("[PlaylistMaintenanceService] Plugin instance is null — cannot update ground truth");
                return;
            }

            // Repair owns the GT update for this window — skip reconciliation.
            // Warning logged so simultaneous user actions during repair are visible.
            if (plugin.RepairSuppressedLists.ContainsKey(playlist.InternalId))
            {
                _logger.Warn(
                    "[PlaylistMaintenanceService] ItemUpdated — repair in progress for '{0}' ({1}) — skipping reconciliation (repair owns GT update)",
                    playlist.Name ?? "(null)",
                    playlistIdN);
                return;
            }

            try
            {
                var members = playlist.GetItemList(new InternalItemsQuery());

                if (members == null)
                {
                    _logger.Warn(
                        "[PlaylistMaintenanceService] Readback returned null for playlist {0}",
                        playlistIdN);
                    return;
                }

                plugin.WriterLock.Wait();
                try
                {
                    var entries = plugin.GroundTruthStore.Load();

                    if (!entries.TryGetValue(playlistIdN, out var entry))
                    {
                        _logger.Warn(
                            "[PlaylistMaintenanceService] No ground truth entry for playlist {0} — skipping reconciliation",
                            playlistIdN);
                        return;
                    }

                    var added = 0;

                    foreach (var item in members)
                    {
                        // ListItemEntryId is the durable per-slot identity within a
                        // playlist (see class remarks) — a member already tracked
                        // under this ListItemEntryId is not a new add.
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
                            continue;

                        var member = GroundTruthMemberFactory.FromItem(item);
                        entry.Members.Add(member);

                        _logger.Info(
                            "[PlaylistMaintenanceService] Reconciled new member '{0}' | InternalId={1} | ListItemEntryId={2} | MediaType={3} | playlist={4}",
                            item.Name ?? "(null)",
                            item.InternalId,
                            item.ListItemEntryId,
                            member.MediaType ?? "(null)",
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
                }
                finally
                {
                    plugin.WriterLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[PlaylistMaintenanceService] Readback/reconcile failed", ex);
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

            _logger.Info(
                "[PlaylistMaintenanceService] PlaylistItemsRemoved — protected playlist '{0}' ({1}) | {2} entry id(s) to remove",
                playlist.Name ?? "(null)",
                playlistIdN,
                e.ListItemEntryIds.Length);

            try
            {
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