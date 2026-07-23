using ListProtection.Services;
using ListProtection.Storage;
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
    /// <summary>
    /// IServerEntryPoint — real-time missing member detection.
    ///
    /// Three fast paths, all proven by probe:
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
    ///   3. ItemAdded (Type=Folder)
    ///      Fires when a new folder appears. We check whether the added folder
    ///      shares the same PARENT directory as any missing member's stored path.
    ///      Matching on parent (rather than path prefix) catches replacement
    ///      folders with different names from the original — e.g. original
    ///      "[2009] Bomb in a Birdcage" replaced by "[2009] Bomb in a birdcage change".
    ///      If a match is found, we queue discovery keyed on the shared parent
    ///      so it drains after the artist-level RefreshCompleted fires (by which
    ///      point FFProbe has settled all metadata).
    ///      Proven: different-name folder replacement log 2026-07-22.
    ///
    ///   4. RefreshCompleted (Type=Folder / MusicArtist)
    ///      Drains _pendingCandidateDiscovery when the refreshed item's path
    ///      is the queued key or a true ancestor of it (refreshedPath is a
    ///      prefix of key). Sibling album folders are intentionally excluded —
    ///      only the artist-level folder (or higher) triggers drainage so that
    ///      all child track metadata is settled before discovery runs.
    ///      Proven: folder rename probe 2026-07-18; drain-too-early fix 2026-07-22.
    ///
    /// Belt-and-braces sweeps:
    ///   PostScanDetectionTask  — runs detection after every library scan + daily 03:00
    ///   PostScanCandidateTask  — runs discovery after every library scan + daily 04:00
    ///   DetectMissingMembersTask / CandidateDiscoveryTask — manual dashboard runs
    ///
    /// Auto-repair:
    ///   After candidate discovery, if AutoRepairEnabled is true in config,
    ///   AutoRepairer.RunAutoRepair is called for the affected playlists.
    ///   AutoDiscoverCandidates in config controls whether discovery is queued at all.
    /// </summary>
    public class MissingMemberDetectionService : IServerEntryPoint
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IProviderManager _providerManager;
        private readonly IPlaylistManager _playlistManager;
        private readonly IUserManager _userManager;
        private readonly ILogger _logger;

        /// <summary>
        /// Keyed by the PARENT of the removed/added folder (the artist/show folder).
        /// Value: playlist IDs that have missing members under that folder.
        /// Populated by OnItemRemoved (Folder) and OnItemAdded (Folder).
        /// Consumed by OnRefreshCompleted.
        /// </summary>
        private readonly Dictionary<string, List<string>> _pendingCandidateDiscovery
            = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public MissingMemberDetectionService(
            ILibraryManager libraryManager,
            IProviderManager providerManager,
            IPlaylistManager playlistManager,
            IUserManager userManager,
            ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _providerManager = providerManager;
            _playlistManager = playlistManager;
            _userManager = userManager;
            _logger = logManager.GetLogger(nameof(MissingMemberDetectionService));
        }

        public void Run()
        {
            _libraryManager.ItemRemoved += OnItemRemoved;
            _libraryManager.ItemAdded += OnItemAdded;
            _providerManager.RefreshCompleted += OnRefreshCompleted;

            _logger.Info("[MissingMemberDetectionService] Started — ItemRemoved + ItemAdded + RefreshCompleted active");
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

                if (IsAutoDiscoverEnabled())
                    QueueCandidateDiscovery("__audio__" + playlistId, playlistId);
            }
        }

        /// <summary>
        /// PROVEN path — folder rename/removal.
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

            // Key on the parent of the removed folder (the artist/show folder) so that
            // discovery drains only after the artist-level RefreshCompleted fires — by
            // which point FFProbe has settled all child track metadata to the DB.
            var discoveryKey = Path.GetDirectoryName(normalised) ?? normalised;

            foreach (var playlistId in affectedPlaylists)
            {
                _logger.Info(
                    "[MissingMemberDetectionService] Running detection for playlist {0}",
                    playlistId);
                MissingMemberDetector.RunDetection(playlistId, _libraryManager, _logger);

                if (IsAutoDiscoverEnabled())
                    QueueCandidateDiscovery(discoveryKey, playlistId);
            }
        }

        // ── ItemAdded ──────────────────────────────────────────────────────

        private void OnItemAdded(object sender, ItemChangeEventArgs e)
        {
            try
            {
                var item = e?.Item;
                if (item == null) return;

                var typeName = item.GetType().Name;
                if (typeName != "Folder" && typeName != "MusicAlbum")
                    return;

                if (string.IsNullOrEmpty(item.Path)) return;

                if (!IsAutoDiscoverEnabled()) return;

                var plugin = ListProtectionPlugin.Instance;
                if (plugin == null) return;

                HandleFolderAdded(item.Path, plugin);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[MissingMemberDetectionService] OnItemAdded failed", ex);
            }
        }

        /// <summary>
        /// PROVEN path — folder added/restored.
        /// Matches when the added folder's PARENT directory equals the parent
        /// directory of any missing member's stored path. This handles both:
        ///   - Same-name restore: added folder path prefix matches GT member path
        ///     (parent match is a superset of this)
        ///   - Different-name replacement: original "[2009] Bomb in a Birdcage"
        ///     replaced by "[2009] Bomb in a birdcage change" — prefix match would
        ///     fail, but parent match succeeds because both sit under the same
        ///     artist folder.
        /// Discovery is queued on the shared parent (the artist folder) so it
        /// drains after the artist-level RefreshCompleted, by which point FFProbe
        /// has settled all child track metadata.
        /// Proven: different-name folder replacement log 2026-07-22.
        /// </summary>
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

        // ── Queue helper ───────────────────────────────────────────────────

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
        /// Drains _pendingCandidateDiscovery when the refreshed item's path IS
        /// the queued key or is a true ancestor of it (i.e. refreshedPath is a
        /// prefix of key — the refreshed item contains the keyed folder).
        ///
        /// Crucially: we do NOT drain when key is a prefix of refreshedPath
        /// (which would mean the refreshed item is a CHILD or SIBLING of the
        /// keyed folder). This prevents sibling album folders from draining the
        /// queue before the artist-level RefreshCompleted fires.
        ///
        /// Example (fix for drain-too-early bug, 2026-07-22):
        ///   key = "\\x99\music\A Fine Frenzy" (artist folder, the parent)
        ///   refreshedPath = "\\x99\music\A Fine Frenzy\[2007] One Cell in the Sea" → NO DRAIN (child)
        ///   refreshedPath = "\\x99\music\A Fine Frenzy" → DRAIN (exact match / ancestor)
        ///   refreshedPath = "\\x99\music" → DRAIN (true ancestor)
        /// </summary>
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

                        // For path-keyed entries: drain only when the refreshed item IS
                        // the keyed path or is a true ancestor (refreshedPath is a prefix
                        // of key). Never drain on a child/sibling RefreshCompleted.
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

        // ── Config helpers ─────────────────────────────────────────────────

        private bool IsAutoDiscoverEnabled()
        {
            try
            {
                var config = ListProtectionPlugin.Instance?.Configuration;
                return config == null || config.AutoDiscoverCandidates;
            }
            catch
            {
                return true;
            }
        }

        // ── Cleanup ────────────────────────────────────────────────────────

        public void Dispose()
        {
            _libraryManager.ItemRemoved -= OnItemRemoved;
            _libraryManager.ItemAdded -= OnItemAdded;
            _providerManager.RefreshCompleted -= OnRefreshCompleted;
            _logger.Info("[MissingMemberDetectionService] Disposed");
        }
    }
}