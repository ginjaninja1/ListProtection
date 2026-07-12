using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.Threading;

namespace ListProtection.EntryPoints
{
    /// <summary>
    /// IServerEntryPoint — detects missing members in protected playlists.
    ///
    /// Two detection paths:
    ///
    ///   Timer (belt-and-braces): fires RunDetection(null) every 60 minutes.
    ///   Full scan of all active ground truth entries.
    ///
    ///   Fast path (ILibraryManager.ItemRemoved): fires RunDetection for affected
    ///   playlists immediately when a tracked item is removed from the library.
    ///   PROVEN: ItemRemoved payload confirmed (Task 5). Fast path now active.
    ///
    /// Detection logic lives in MissingMemberDetector (shared with DetectMissingMembersTask).
    /// Stores are accessed via ListProtectionPlugin.Instance (DI cannot inject them).
    /// Constructor mirrors PlaylistMaintenanceService exactly.
    /// </summary>
    public class MissingMemberDetectionService : IServerEntryPoint
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        private Timer _timer;

        private const int DetectionIntervalMinutes = 60;

        public MissingMemberDetectionService(
            ILibraryManager libraryManager,
            ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _logger = logManager.GetLogger(nameof(MissingMemberDetectionService));
        }

        public void Run()
        {
            _libraryManager.ItemRemoved += OnItemRemoved;

            // Fire first detection after a short delay to let Emby fully settle,
            // then repeat every DetectionIntervalMinutes.
            var interval = TimeSpan.FromMinutes(DetectionIntervalMinutes);
            var initialDelay = TimeSpan.FromMinutes(2);

            _timer = new Timer(_ => MissingMemberDetector.RunDetection(null, _libraryManager, _logger),
                null, initialDelay, interval);

            _logger.Info(
                "[MissingMemberDetectionService] Started — detection interval: {0} min, initial delay: {1} min",
                DetectionIntervalMinutes,
                (int)initialDelay.TotalMinutes);
        }

        // ── ItemRemoved fast path ──────────────────────────────────────────

        /// <summary>
        /// PROVEN (Task 5): ItemRemoved fires with ItemChangeEventArgs.
        /// Item.InternalId is populated and correct for deleted library items.
        ///
        /// Fast path: check whether the removed item appears in any active ground
        /// truth entry. If so, run targeted detection for each affected playlist.
        ///
        /// Guard is essential — ItemRemoved fires for ANY library deletion,
        /// not only playlist members.
        /// </summary>
        private void OnItemRemoved(object sender, ItemChangeEventArgs e)
        {
            try
            {
                var removedInternalId = e?.Item?.InternalId ?? 0;
                if (removedInternalId == 0) return;

                var plugin = ListProtectionPlugin.Instance;
                if (plugin == null) return;

                var groundTruth = plugin.GroundTruthStore.Load();
                var affectedPlaylists = new List<string>();

                foreach (var kvp in groundTruth)
                {
                    if (!kvp.Value.IsActive) continue;
                    foreach (var member in kvp.Value.Members)
                    {
                        if (member.InternalId == removedInternalId)
                        {
                            affectedPlaylists.Add(kvp.Key);
                            break;
                        }
                    }
                }

                foreach (var playlistId in affectedPlaylists)
                {
                    _logger.Info(
                        "[MissingMemberDetectionService] Fast path — running detection for playlist {0}",
                        playlistId);
                    MissingMemberDetector.RunDetection(playlistId, _libraryManager, _logger);
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[MissingMemberDetectionService] OnItemRemoved failed", ex);
            }
        }

        // ── Cleanup ────────────────────────────────────────────────────────

        public void Dispose()
        {
            _libraryManager.ItemRemoved -= OnItemRemoved;

            _timer?.Dispose();
            _timer = null;

            _logger.Info("[MissingMemberDetectionService] Disposed");
        }
    }
}