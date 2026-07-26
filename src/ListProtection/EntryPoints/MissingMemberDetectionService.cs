using ListProtection.Services;
using ListProtection.Storage;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.IO;

namespace ListProtection.EntryPoints
{
    public class MissingMemberDetectionService : IServerEntryPoint
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IProviderManager _providerManager;
        private readonly IPlaylistManager _playlistManager;
        private readonly ICollectionManager _collectionManager;
        private readonly IUserManager _userManager;
        private readonly ILogger _logger;

        private readonly Dictionary<string, List<string>> _pendingCandidateDiscovery
            = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public MissingMemberDetectionService(
            ILibraryManager libraryManager,
            IProviderManager providerManager,
            IPlaylistManager playlistManager,
            ICollectionManager collectionManager,
            IUserManager userManager,
            ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _providerManager = providerManager;
            _playlistManager = playlistManager;
            _collectionManager = collectionManager;
            _userManager = userManager;
            _logger = logManager.GetLogger(nameof(MissingMemberDetectionService));
        }

        public void Run()
        {
            _libraryManager.ItemRemoved += OnItemRemoved;
            _libraryManager.ItemAdded += OnItemAdded;
            _libraryManager.ItemUpdated += OnItemUpdated;
            _providerManager.RefreshCompleted += OnRefreshCompleted;

            _logger.Info("[MissingMemberDetectionService] Started — ItemRemoved + ItemAdded + ItemUpdated + RefreshCompleted active");
        }

        private bool IsEventDrivenRepairEnabled()
        {
            try
            {
                var config = ListProtectionPlugin.Instance?.Configuration;
                return config == null || config.EventDrivenRepairEnabled;
            }
            catch
            {
                return true;
            }
        }

        private void OnItemRemoved(object sender, ItemChangeEventArgs e)
        {
            if (!IsEventDrivenRepairEnabled()) return;

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
                QueueCandidateDiscovery("__audio__" + playlistId, playlistId);
            }
        }

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

            var discoveryKey = Path.GetDirectoryName(normalised) ?? normalised;

            foreach (var playlistId in affectedPlaylists)
            {
                _logger.Info(
                    "[MissingMemberDetectionService] Running detection for playlist {0}",
                    playlistId);
                MissingMemberDetector.RunDetection(playlistId, _libraryManager, _logger);
                QueueCandidateDiscovery(discoveryKey, playlistId);
            }
        }

        private void OnItemAdded(object sender, ItemChangeEventArgs e)
        {
            if (!IsEventDrivenRepairEnabled()) return;

            try
            {
                var item = e?.Item;
                if (item == null) return;

                var typeName = item.GetType().Name;
                if (typeName != "Folder" && typeName != "MusicAlbum")
                    return;

                if (string.IsNullOrEmpty(item.Path)) return;

                var plugin = ListProtectionPlugin.Instance;
                if (plugin == null) return;

                HandleFolderAdded(item.Path, plugin);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[MissingMemberDetectionService] OnItemAdded failed", ex);
            }
        }

        private void OnItemUpdated(object sender, ItemChangeEventArgs e)
        {
            if (!IsEventDrivenRepairEnabled()) return;

            try
            {
                var item = e?.Item;
                if (item == null) return;

                if (item.GetType().Name != "Audio") return;

                var plugin = ListProtectionPlugin.Instance;
                if (plugin == null) return;

                var missing = plugin.MissingMembersStore.Load();
                if (missing == null || missing.Count == 0) return;

                if (string.IsNullOrEmpty(item.Path)) return;

                var itemParent = System.IO.Path.GetDirectoryName(item.Path.TrimEnd('\\', '/'));
                if (string.IsNullOrEmpty(itemParent)) return;

                var itemGrandparent = System.IO.Path.GetDirectoryName(itemParent);
                if (string.IsNullOrEmpty(itemGrandparent)) return;

                var affectedPlaylists = new List<string>();

                foreach (var entry in missing)
                {
                    if (string.IsNullOrEmpty(entry.Member?.Path)) continue;

                    var memberParent = System.IO.Path.GetDirectoryName(
                        entry.Member.Path.TrimEnd('\\', '/'));
                    if (string.IsNullOrEmpty(memberParent)) continue;

                    var memberGrandparent = System.IO.Path.GetDirectoryName(memberParent);
                    if (string.IsNullOrEmpty(memberGrandparent)) continue;

                    if (!string.Equals(itemGrandparent, memberGrandparent,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!affectedPlaylists.Contains(entry.PlaylistId))
                        affectedPlaylists.Add(entry.PlaylistId);
                }

                if (affectedPlaylists.Count == 0) return;

                _logger.Info(
                    "[MissingMemberDetectionService] ItemUpdated '{0}' — grandparent matches missing member path(s) — queuing discovery for {1} playlist(s)",
                    item.Name ?? "(null)", affectedPlaylists.Count);

                foreach (var playlistId in affectedPlaylists)
                    QueueCandidateDiscovery(itemGrandparent, playlistId);
            }
            catch (Exception ex)
            {
                _logger.ErrorException(
                    "[MissingMemberDetectionService] OnItemUpdated failed", ex);
            }
        }

        private void HandleFolderAdded(string addedFolderPath, ListProtectionPlugin plugin)
        {
            var normalised = addedFolderPath.TrimEnd('\\', '/');
            var addedParent = Path.GetDirectoryName(normalised);

            if (string.IsNullOrEmpty(addedParent)) return;

            var missing = plugin.MissingMembersStore.Load();
            if (missing == null || missing.Count == 0) return;

            var affectedPlaylists = new List<string>();

            foreach (var entry in missing)
            {
                if (string.IsNullOrEmpty(entry.Member?.Path)) continue;

                var memberParent = Path.GetDirectoryName(entry.Member.Path.TrimEnd('\\', '/'));

                if (string.Equals(addedParent, memberParent, StringComparison.OrdinalIgnoreCase))
                {
                    if (!affectedPlaylists.Contains(entry.PlaylistId))
                        affectedPlaylists.Add(entry.PlaylistId);
                }
            }

            if (affectedPlaylists.Count == 0) return;

            _logger.Info(
                "[MissingMemberDetectionService] Folder added '{0}' — parent matches missing member paths — queuing discovery for {1} playlist(s)",
                addedFolderPath, affectedPlaylists.Count);

            foreach (var playlistId in affectedPlaylists)
                QueueCandidateDiscovery(addedParent, playlistId);
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

        private void OnRefreshCompleted(object sender, GenericEventArgs<RefreshProgressInfo> e)
        {
            try
            {
                var refreshedItem = e?.Argument?.Item;
                if (refreshedItem == null) return;

                var refreshedPath = refreshedItem.Path ?? string.Empty;
                var typeName = refreshedItem.GetType().Name;

                if (typeName != "Folder" && typeName != "MusicAlbum" && typeName != "MusicArtist")
                    return;

                List<string> playlistsToDiscover = null;

                lock (_pendingCandidateDiscovery)
                {
                    var toRemove = new List<string>();

                    foreach (var kvp in _pendingCandidateDiscovery)
                    {
                        var key = kvp.Key;
                        var isAudioKey = key.StartsWith("__audio__", StringComparison.Ordinal);

                        var isAncestorOrSelf = !isAudioKey &&
                            key.StartsWith(refreshedPath, StringComparison.OrdinalIgnoreCase);

                        if (isAudioKey || isAncestorOrSelf)
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

                    _logger.Info(
                        "[MissingMemberDetectionService] Attempting auto-repair for playlist {0}",
                        playlistId);

                    AutoRepairer.RunAutoRepair(
                        playlistId,
                        _libraryManager,
                        _playlistManager,
                        _collectionManager,
                        _userManager,
                        _logger)
                        .ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                                _logger.ErrorException(
                                    "[MissingMemberDetectionService] AutoRepairer.RunAutoRepair faulted for playlist {0}",
                                    t.Exception,
                                    playlistId);
                        });
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[MissingMemberDetectionService] OnRefreshCompleted failed", ex);
            }
        }

        public void Dispose()
        {
            _libraryManager.ItemRemoved -= OnItemRemoved;
            _libraryManager.ItemAdded -= OnItemAdded;
            _libraryManager.ItemUpdated -= OnItemUpdated;
            _providerManager.RefreshCompleted -= OnRefreshCompleted;
            _logger.Info("[MissingMemberDetectionService] Disposed");
        }
    }
}