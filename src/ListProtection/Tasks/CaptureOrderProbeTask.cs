using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ListProtection.Tasks
{
    /// <summary>
    /// PROBE TASK — compares GetItemList order via ILibraryManager vs Playlist.GetItemList
    /// against the current GT member order for all protected playlists.
    ///
    /// Run against a playlist where member order is known to be wrong in GT.
    /// Discard after capture order bug is confirmed and fixed.
    /// </summary>
    public class CaptureOrderProbeTask : IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        public string Name => "PROBE — Playlist Capture Order";
        public string Key => "ListProtectionCaptureOrderProbe";
        public string Description => "Compares ILibraryManager.GetItemList vs Playlist.GetItemList order against GT. Discard after fix.";
        public string Category => "List Protection";

        public CaptureOrderProbeTask(
            ILibraryManager libraryManager,
            ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _logger = logManager.GetLogger(nameof(CaptureOrderProbeTask));
        }

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            progress?.Report(0);
            _logger.Info("[CaptureOrderProbe] Starting");

            try
            {
                var plugin = ListProtectionPlugin.Instance;
                if (plugin == null) { _logger.Error("[CaptureOrderProbe] Plugin null"); return Task.CompletedTask; }

                var protectedIds = plugin.PlaylistStore.Load();
                var groundTruth = plugin.GroundTruthStore.Load();

                var allPlaylists = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { "Playlist" },
                    Recursive = true
                });

                foreach (var item in allPlaylists)
                {
                    var idN = item.Id.ToString("N");
                    if (!protectedIds.Contains(idN)) continue;

                    var playlist = item as Playlist;
                    if (playlist == null) continue;

                    _logger.Info("[CaptureOrderProbe] ── Playlist '{0}' ({1}) ──", item.Name, idN);

                    // ── GT order ───────────────────────────────────────────
                    if (groundTruth.TryGetValue(idN, out var gtEntry) && gtEntry.Members != null)
                    {
                        _logger.Info("[CaptureOrderProbe] GT has {0} member(s):", gtEntry.Members.Count);
                        for (var i = 0; i < gtEntry.Members.Count; i++)
                        {
                            var m = gtEntry.Members[i];
                            _logger.Info(
                                "[CaptureOrderProbe] GT[{0}] InternalId={1} | ListItemEntryId={2} | Name={3}",
                                i, m.InternalId, m.ListItemEntryId, m.Name);
                        }
                    }

                    // ── Path A: ILibraryManager.GetItemList with ListIds ───
                    var byLibraryManager = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        ListIds = new[] { item.InternalId },
                        Recursive = true
                    });

                    _logger.Info("[CaptureOrderProbe] LibraryManager.GetItemList returned {0} member(s):", byLibraryManager?.Length ?? 0);
                    if (byLibraryManager != null)
                    {
                        for (var i = 0; i < byLibraryManager.Length; i++)
                        {
                            var m = byLibraryManager[i];
                            _logger.Info(
                                "[CaptureOrderProbe] LibMgr[{0}] InternalId={1} | ListItemEntryId={2} | Name={3}",
                                i, m.InternalId, m.ListItemEntryId, m.Name);
                        }
                    }

                    // ── Path B: Playlist.GetItemList ───────────────────────
                    var byPlaylistEntity = playlist.GetItemList(new InternalItemsQuery());

                    _logger.Info("[CaptureOrderProbe] Playlist.GetItemList returned {0} member(s):", byPlaylistEntity?.Length ?? 0);
                    if (byPlaylistEntity != null)
                    {
                        for (var i = 0; i < byPlaylistEntity.Length; i++)
                        {
                            var m = byPlaylistEntity[i];
                            _logger.Info(
                                "[CaptureOrderProbe] PlEntity[{0}] InternalId={1} | ListItemEntryId={2} | Name={3}",
                                i, m.InternalId, m.ListItemEntryId, m.Name);
                        }
                    }

                    // ── Comparison summary ─────────────────────────────────
                    _logger.Info("[CaptureOrderProbe] ── Order match summary for '{0}' ──", item.Name);

                    var libMgrMatchesGt = true;
                    var plEntityMatchesGt = true;

                    if (gtEntry?.Members != null && byLibraryManager != null)
                    {
                        for (var i = 0; i < Math.Min(gtEntry.Members.Count, byLibraryManager.Length); i++)
                        {
                            if (gtEntry.Members[i].InternalId != byLibraryManager[i].InternalId)
                            {
                                libMgrMatchesGt = false;
                                _logger.Info(
                                    "[CaptureOrderProbe] LibMgr mismatch at [{0}]: GT={1} ('{2}') vs LibMgr={3} ('{4}')",
                                    i,
                                    gtEntry.Members[i].InternalId, gtEntry.Members[i].Name,
                                    byLibraryManager[i].InternalId, byLibraryManager[i].Name);
                            }
                        }
                    }

                    if (gtEntry?.Members != null && byPlaylistEntity != null)
                    {
                        for (var i = 0; i < Math.Min(gtEntry.Members.Count, byPlaylistEntity.Length); i++)
                        {
                            if (gtEntry.Members[i].InternalId != byPlaylistEntity[i].InternalId)
                            {
                                plEntityMatchesGt = false;
                                _logger.Info(
                                    "[CaptureOrderProbe] PlEntity mismatch at [{0}]: GT={1} ('{2}') vs PlEntity={2} ('{3}')",
                                    i,
                                    gtEntry.Members[i].InternalId, gtEntry.Members[i].Name,
                                    byPlaylistEntity[i].InternalId, byPlaylistEntity[i].Name);
                            }
                        }
                    }

                    _logger.Info(
                        "[CaptureOrderProbe] LibraryManager matches GT: {0} | PlaylistEntity matches GT: {1}",
                        libMgrMatchesGt, plEntityMatchesGt);
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[CaptureOrderProbe] Failed", ex);
            }

            progress?.Report(100);
            _logger.Info("[CaptureOrderProbe] Complete");
            return Task.CompletedTask;
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
            => Array.Empty<TaskTriggerInfo>();
    }
}