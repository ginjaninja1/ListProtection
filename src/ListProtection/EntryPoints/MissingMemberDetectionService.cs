using ListProtection.Storage;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;

namespace ListProtection.EntryPoints
{
    /// <summary>
    /// IServerEntryPoint — real-time missing member detection.
    ///
    /// Two fast paths, both proven by probe:
    ///
    ///   1. ItemRemoved (Type=Audio)
    ///      Fires when a single file is renamed or deleted. The removed
    ///      InternalId is matched directly against GT members.
    ///      Proven: Task 5, file rename probe 2026-07-18.
    ///
    ///   2. ItemRemoved (Type=Folder)
    ///      Fires when a folder is renamed. Emby removes the old folder entity
    ///      and creates new Audio entities with new InternalIds underneath the
    ///      new folder — no individual Audio ItemRemoved fires for the tracks.
    ///      We match via path prefix: any GT member whose Path starts with the
    ///      removed folder's Path is affected.
    ///      Proven: folder rename probe 2026-07-18.
    ///
    ///   3. RefreshCompleted (Type=Folder)
    ///      Fires after Emby has committed new items to the DB following a
    ///      folder rename. By this point the replacement tracks are discoverable.
    ///      We trigger CandidateDiscoverer for playlists affected by the rename.
    ///      The affected set is tracked in _pendingCandidateDiscovery, keyed by
    ///      the removed folder path, populated in path 2 above.
    ///      Proven: folder rename probe 2026-07-18.
    ///
    /// Belt-and-braces sweeps:
    ///   PostScanDetectionTask  — runs detection after every library scan + daily 03:00
    ///   PostScanCandidateTask  — runs discovery after every library scan + daily 04:00
    ///   DetectMissingMembersTask / CandidateDiscoveryTask — manual dashboard runs
    /// </summary>
    public class MissingMemberDetectionService : IServerEntryPoint
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IProviderManager _providerManager;
        private readonly ILogger _logger;

        /// <summary>
        /// Keyed by removed folder path (normalised, lower).
        /// Value: playlist IDs that had GT members under that folder.
        /// Populated by OnItemRemoved (Folder), consumed by OnRefreshCompleted.
        /// </summary>
        private readonly Dictionary<string, List<string>> _pendingCandidateDiscovery
            = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public MissingMemberDetectionService(
            ILibraryManager libraryManager,
            IProviderManager providerManager,
            ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _providerManager = providerManager;
            _logger = logManager.GetLogger(nameof(MissingMemberDetectionService));
        }

        public void Run()
        {
            _libraryManager.ItemRemoved += OnItemRemoved;
            _providerManager.RefreshCompleted += OnRefreshCompleted;

            _logger.Info("[MissingMemberDetectionService] Started — ItemRemoved + RefreshCompleted active");
        }

        // ── ItemRemoved ────────────────────────────────────────────────────

        private void OnItemRemoved(object sender, ItemChangeEventArgs e)
        {
            try
            {
                var item = e?.Item;
                if (item == null) return;

                var plugin = ListProtectionPlugin.Instance;
                if (plugin == null) return;

                var typeName = item.GetType().Name;

                if (typeName == "Audio")
                    HandleAudioRemoved(item.InternalId, plugin);
                else if (typeName == "Folder" || typeName == "MusicAlbum")
                    HandleFolderRemoved(item.Path, plugin);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[MissingMemberDetectionService] OnItemRemoved failed", ex);
            }
        }

        /// <summary>
        /// PROVEN path — single file rename/delete.
        /// Match the removed InternalId directly against GT members.
        /// </summary>
        private void HandleAudioRemoved(long removedInternalId, ListProtectionPlugin plugin)
        {
            if (removedInternalId == 0) return;

            var groundTruth = plugin.GroundTruthStore.Load();
            var affectedPlaylists = new List<string>();

            foreach (var kvp in groundTruth)
            {
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
                    "[MissingMemberDetectionService] Audio removed — running detection for playlist {0}",
                    playlistId);
                MissingMemberDetector.RunDetection(playlistId, _libraryManager, _logger);

                // Queue candidate discovery — new file may be in DB already (file rename)
                QueueCandidateDiscovery("__audio__" + playlistId, playlistId);
            }
        }

        /// <summary>
        /// PROVEN path — folder rename.
        /// Match GT member paths by prefix against the removed folder path.
        /// Queue affected playlists for candidate discovery on RefreshCompleted.
        /// </summary>
        private void HandleFolderRemoved(string removedFolderPath, ListProtectionPlugin plugin)
        {
            if (string.IsNullOrEmpty(removedFolderPath)) return;

            var normalised = removedFolderPath.TrimEnd('\\', '/');
            var groundTruth = plugin.GroundTruthStore.Load();
            var affectedPlaylists = new List<string>();

            foreach (var kvp in groundTruth)
            {
                foreach (var member in kvp.Value.Members)
                {
                    if (!string.IsNullOrEmpty(member.Path) &&
                        member.Path.StartsWith(normalised + "\\", StringComparison.OrdinalIgnoreCase) ||
                        member.Path.StartsWith(normalised + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        affectedPlaylists.Add(kvp.Key);
                        break;
                    }
                }
            }

            if (affectedPlaylists.Count == 0)
            {
                _logger.Info(
                    "[MissingMemberDetectionService] Folder removed but no GT members under '{0}' — skipping",
                    removedFolderPath);
                return;
            }

            _logger.Info(
                "[MissingMemberDetectionService] Folder removed '{0}' — {1} affected playlist(s)",
                removedFolderPath, affectedPlaylists.Count);

            foreach (var playlistId in affectedPlaylists)
            {
                _logger.Info(
                    "[MissingMemberDetectionService] Running detection for playlist {0}",
                    playlistId);
                MissingMemberDetector.RunDetection(playlistId, _libraryManager, _logger);

                // Queue for candidate discovery — new tracks won't be in DB until
                // RefreshCompleted fires for the parent folder.
                QueueCandidateDiscovery(normalised, playlistId);
            }
        }

        private void QueueCandidateDiscovery(string key, string playlistId)
        {
            lock (_pendingCandidateDiscovery)
            {
                if (!_pendingCandidateDiscovery.TryGetValue(key, out var list))
                    _pendingCandidateDiscovery[key] = list = new List<string>();

                if (!list.Contains(playlistId))
                    list.Add(playlistId);
            }
        }

        // ── RefreshCompleted ───────────────────────────────────────────────

        /// <summary>
        /// PROVEN path — fires after Emby commits new items to DB.
        /// For folder renames, the replacement tracks are now discoverable.
        /// Drain _pendingCandidateDiscovery entries whose folder path is an
        /// ancestor of (or equal to) the refreshed item's path.
        /// </summary>
        private void OnRefreshCompleted(object sender, GenericEventArgs<RefreshProgressInfo> e)
        {
            try
            {
                var refreshedItem = e?.Argument?.Item;
                if (refreshedItem == null) return;

                var refreshedPath = refreshedItem.Path ?? string.Empty;
                var typeName = refreshedItem.GetType().Name;

                // We only care about folder refreshes — audio item refreshes are
                // handled synchronously in the ItemRemoved audio path above.
                if (typeName != "Folder" && typeName != "MusicAlbum" && typeName != "MusicArtist")
                    return;

                List<string> playlistsToDiscover = null;

                lock (_pendingCandidateDiscovery)
                {
                    var toRemove = new List<string>();

                    foreach (var kvp in _pendingCandidateDiscovery)
                    {
                        // Match: pending folder key is under (or equal to) the refreshed folder,
                        // or is the special __audio__ prefix (file rename — discover immediately)
                        var key = kvp.Key;
                        var isAudioKey = key.StartsWith("__audio__", StringComparison.Ordinal);
                        var isAncestor = !isAudioKey && (
                            refreshedPath.StartsWith(key, StringComparison.OrdinalIgnoreCase) ||
                            key.StartsWith(refreshedPath, StringComparison.OrdinalIgnoreCase));

                        if (isAudioKey || isAncestor)
                        {
                            if (playlistsToDiscover == null)
                                playlistsToDiscover = new List<string>();

                            foreach (var id in kvp.Value)
                                if (!playlistsToDiscover.Contains(id))
                                    playlistsToDiscover.Add(id);

                            toRemove.Add(key);
                        }
                    }

                    foreach (var key in toRemove)
                        _pendingCandidateDiscovery.Remove(key);
                }

                if (playlistsToDiscover == null) return;

                _logger.Info(
                    "[MissingMemberDetectionService] RefreshCompleted '{0}' — running candidate discovery for {1} playlist(s)",
                    refreshedItem.Name ?? "(null)", playlistsToDiscover.Count);

                foreach (var playlistId in playlistsToDiscover)
                {
                    _logger.Info(
                        "[MissingMemberDetectionService] Running candidate discovery for playlist {0}",
                        playlistId);
                    CandidateDiscoverer.RunDiscovery(playlistId, _libraryManager, _logger);
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[MissingMemberDetectionService] OnRefreshCompleted failed", ex);
            }
        }

        // ── Cleanup ────────────────────────────────────────────────────────

        public void Dispose()
        {
            _libraryManager.ItemRemoved -= OnItemRemoved;
            _providerManager.RefreshCompleted -= OnRefreshCompleted;
            _logger.Info("[MissingMemberDetectionService] Disposed");
        }
    }
}