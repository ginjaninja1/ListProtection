using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;

namespace ListProtection.EntryPoints
{
    /// <summary>
    /// IServerEntryPoint — fast-path missing member detection.
    ///
    /// Responsibilities:
    ///   ItemRemoved fast path: fires RunDetection for affected playlists
    ///   immediately when a tracked library item is deleted. This is the
    ///   only real-time detection path needed, since file moves/renames
    ///   are caught by the post-scan task.
    ///
    /// Periodic sweep:
    ///   Removed — the 60-min timer is replaced by PostScanDetectionTask,
    ///   which runs as a proper IScheduledTask after every library scan.
    ///   Manual runs are available via DetectMissingMembersTask in the
    ///   Emby dashboard.
    ///
    /// PROVEN behaviours:
    ///   ItemRemoved payload confirmed (Task 5).
    ///   ItemRemoved does NOT fire ItemUpdated.
    /// </summary>
    public class MissingMemberDetectionService : IServerEntryPoint
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

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

            _logger.Info("[MissingMemberDetectionService] Started — ItemRemoved fast path active");
        }

        // ── ItemRemoved fast path ──────────────────────────────────────────

        /// <summary>
        /// PROVEN (Task 5): ItemRemoved fires with ItemChangeEventArgs.
        /// Item.InternalId is populated and correct for deleted library items.
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
            _logger.Info("[MissingMemberDetectionService] Disposed");
        }
    }
}