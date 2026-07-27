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

        // Item types that are direct GT members and detected by InternalId lookup.
        // Folder-like types are handled separately by path-prefix scan.
        //
        // MusicAlbum moved here from _folderTypes: when a MusicAlbum is itself a
        // direct collection member (whole album added to a Collection), its own
        // item.Path is frequently empty (confirmed via diagnostic dump — it's an
        // aggregate entity, not reliably backed by a single folder path), so the
        // path-prefix scan in HandleFolderRemoved could never match it. InternalId
        // lookup is the only reliable signal for this case.
        private static readonly HashSet<string> _directMemberTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Audio", "Movie", "Episode", "Series", "BoxSet", "MusicAlbum"
        };

        // Item types that represent containers — detected by scanning GT member paths.
        private static readonly HashSet<string> _folderTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Folder", "MusicArtist"
        };

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

                if (_directMemberTypes.Contains(typeName))
                    HandleDirectItemRemoved(item.InternalId, typeName, plugin);
                else if (_folderTypes.Contains(typeName))
                    HandleFolderRemoved(item.Path, plugin);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[MissingMemberDetectionService] OnItemRemoved failed", ex);
            }
        }

        /// <summary>
        /// Handles removal of any item that may appear directly as a GT member (Audio, Movie,
        /// Episode, Series, BoxSet). Matches by InternalId across all GT entries.
        /// </summary>
        private void HandleDirectItemRemoved(long removedInternalId, string typeName, ListProtectionPlugin plugin)
        {
            if (removedInternalId == 0) return;

            var groundTruth = plugin.GroundTruthStore.Load();
            var affectedLists = new List<string>();

            foreach (var kvp in groundTruth)
            {
                foreach (var member in kvp.Value.Members)
                {
                    if (member.InternalId == removedInternalId)
                    {
                        affectedLists.Add(kvp.Key);
                        break;
                    }
                }
            }

            if (affectedLists.Count == 0) return;

            foreach (var listId in affectedLists)
            {
                _logger.Info(
                    "[MissingMemberDetectionService] {0} removed (InternalId={1}) — running detection for list {2}",
                    typeName, removedInternalId, listId);
                MissingMemberDetector.RunDetection(listId, _libraryManager, _logger);
                QueueCandidateDiscovery("__direct__" + listId, listId);
            }
        }

        private void HandleFolderRemoved(string removedFolderPath, ListProtectionPlugin plugin)
        {
            if (string.IsNullOrEmpty(removedFolderPath)) return;

            var normalised = removedFolderPath.TrimEnd('\\', '/');
            var groundTruth = plugin.GroundTruthStore.Load();
            var affectedLists = new List<string>();

            foreach (var kvp in groundTruth)
            {
                foreach (var member in kvp.Value.Members)
                {
                    if (!string.IsNullOrEmpty(member.Path) &&
                        member.Path.StartsWith(normalised + "\\", StringComparison.OrdinalIgnoreCase) ||
                        member.Path.StartsWith(normalised + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        affectedLists.Add(kvp.Key);
                        break;
                    }
                }
            }

            if (affectedLists.Count == 0)
            {
                _logger.Info(
                    "[MissingMemberDetectionService] Folder removed but no GT members under '{0}' — skipping",
                    removedFolderPath);
                return;
            }

            _logger.Info(
                "[MissingMemberDetectionService] Folder removed '{0}' — {1} affected list(s)",
                removedFolderPath, affectedLists.Count);

            var discoveryKey = Path.GetDirectoryName(normalised) ?? normalised;

            foreach (var listId in affectedLists)
            {
                _logger.Info(
                    "[MissingMemberDetectionService] Running detection for list {0}", listId);
                MissingMemberDetector.RunDetection(listId, _libraryManager, _logger);
                QueueCandidateDiscovery(discoveryKey, listId);
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

                var affectedLists = new List<string>();

                foreach (var entry in missing)
                {
                    if (string.IsNullOrEmpty(entry.Member?.Path)) continue;

                    var memberParent = System.IO.Path.GetDirectoryName(
                        entry.Member.Path.TrimEnd('\\', '/'));
                    if (string.IsNullOrEmpty(memberParent)) continue;

                    var memberGrandparent = System.IO.Path.GetDirectoryName(memberParent);
                    if (string.IsNullOrEmpty(memberGrandparent)) continue;

                    if (!string.Equals(itemGrandparent, memberGrandparent, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!affectedLists.Contains(entry.PlaylistId))
                        affectedLists.Add(entry.PlaylistId);
                }

                if (affectedLists.Count == 0) return;

                _logger.Info(
                    "[MissingMemberDetectionService] ItemUpdated '{0}' — grandparent matches missing member path(s) — queuing discovery for {1} list(s)",
                    item.Name ?? "(null)", affectedLists.Count);

                foreach (var listId in affectedLists)
                    QueueCandidateDiscovery(itemGrandparent, listId);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[MissingMemberDetectionService] OnItemUpdated failed", ex);
            }
        }

        private void HandleFolderAdded(string addedFolderPath, ListProtectionPlugin plugin)
        {
            var normalised = addedFolderPath.TrimEnd('\\', '/');
            var addedParent = Path.GetDirectoryName(normalised);

            if (string.IsNullOrEmpty(addedParent)) return;

            var missing = plugin.MissingMembersStore.Load();
            if (missing == null || missing.Count == 0) return;

            var affectedLists = new List<string>();

            foreach (var entry in missing)
            {
                if (string.IsNullOrEmpty(entry.Member?.Path)) continue;

                var memberParent = Path.GetDirectoryName(entry.Member.Path.TrimEnd('\\', '/'));

                if (string.Equals(addedParent, memberParent, StringComparison.OrdinalIgnoreCase))
                {
                    if (!affectedLists.Contains(entry.PlaylistId))
                        affectedLists.Add(entry.PlaylistId);
                }
            }

            if (affectedLists.Count == 0) return;

            _logger.Info(
                "[MissingMemberDetectionService] Folder added '{0}' — parent matches missing member paths — queuing discovery for {1} list(s)",
                addedFolderPath, affectedLists.Count);

            foreach (var listId in affectedLists)
                QueueCandidateDiscovery(addedParent, listId);
        }

        private void QueueCandidateDiscovery(string key, string listId)
        {
            lock (_pendingCandidateDiscovery)
            {
                if (!_pendingCandidateDiscovery.TryGetValue(key, out var list))
                    _pendingCandidateDiscovery[key] = list = new List<string>();

                if (!list.Contains(listId))
                    list.Add(listId);
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

                List<string> listsToDiscover = null;

                lock (_pendingCandidateDiscovery)
                {
                    var toRemove = new List<string>();

                    foreach (var kvp in _pendingCandidateDiscovery)
                    {
                        var key = kvp.Key;
                        var isDirectKey = key.StartsWith("__direct__", StringComparison.Ordinal);

                        var isAncestorOrSelf = !isDirectKey &&
                            key.StartsWith(refreshedPath, StringComparison.OrdinalIgnoreCase);

                        if (isDirectKey || isAncestorOrSelf)
                        {
                            if (listsToDiscover == null)
                                listsToDiscover = new List<string>();

                            foreach (var id in kvp.Value)
                                if (!listsToDiscover.Contains(id))
                                    listsToDiscover.Add(id);

                            toRemove.Add(key);
                        }
                    }

                    foreach (var key in toRemove)
                        _pendingCandidateDiscovery.Remove(key);
                }

                if (listsToDiscover == null) return;

                _logger.Info(
                    "[MissingMemberDetectionService] RefreshCompleted '{0}' — running candidate discovery for {1} list(s)",
                    refreshedItem.Name ?? "(null)", listsToDiscover.Count);

                foreach (var listId in listsToDiscover)
                {
                    _logger.Info(
                        "[MissingMemberDetectionService] Running candidate discovery for list {0}", listId);
                    CandidateDiscoverer.RunDiscovery(listId, _libraryManager, _logger);

                    _logger.Info(
                        "[MissingMemberDetectionService] Attempting auto-repair for list {0}", listId);

                    AutoRepairer.RunAutoRepair(
                        listId,
                        _libraryManager,
                        _playlistManager,
                        _collectionManager,
                        _userManager,
                        _logger)
                        .ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                                _logger.ErrorException(
                                    "[MissingMemberDetectionService] AutoRepairer.RunAutoRepair faulted for list {0}",
                                    t.Exception,
                                    listId);
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