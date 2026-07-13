using ListProtection.Storage;
using ListProtection.UIBaseClasses.Views;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ListProtection.UI.PlaylistManagement
{
    internal class PlaylistManagementPageView : PluginPageView
    {
        private readonly PlaylistManagementStore _store;
        private readonly GroundTruthStore _groundTruthStore;
        private readonly ILibraryManager _libraryManager;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly ILogger _logger;

        public PlaylistManagementPageView(
            PluginInfo pluginInfo,
            PlaylistManagementStore store,
            GroundTruthStore groundTruthStore,
            ILibraryManager libraryManager,
            IJsonSerializer jsonSerializer,
            ILogger logger)
            : base(pluginInfo.Id)
        {
            _store = store;
            _groundTruthStore = groundTruthStore;
            _libraryManager = libraryManager;
            _jsonSerializer = jsonSerializer;
            _logger = logger;

            ShowSave = false;
            ShowBack = false;

            ContentData = BuildOptions();
        }

        // ── RunCommand ─────────────────────────────────────────────────────

        public override Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
        {
            _logger.Info(
                "[PlaylistManagementPageView] RunCommand RAW | itemId={0} | commandId={1} | data={2}",
                itemId ?? "(null)",
                commandId ?? "(null)",
                data ?? "(null)");

            try
            {
                if (string.IsNullOrEmpty(data))
                {
                    _logger.Warn("[PlaylistManagementPageView] RunCommand — data was null or empty, ignoring");
                    ContentData = BuildOptions();
                    return Task.FromResult<IPluginUIView>(this);
                }

                var ui = _jsonSerializer.DeserializeFromString<PlaylistManagementUI>(data);

                if (ui?.PlaylistRows == null)
                {
                    _logger.Warn("[PlaylistManagementPageView] RunCommand — could not parse PlaylistManagementUI from data");
                    ContentData = BuildOptions();
                    return Task.FromResult<IPluginUIView>(this);
                }

                var protectedIds = new HashSet<string>();
                foreach (var row in ui.PlaylistRows)
                {
                    if (row.IsProtected && !string.IsNullOrEmpty(row.Id))
                        protectedIds.Add(row.Id);
                }

                _logger.Info(
                    "[PlaylistManagementPageView] Saving {0} protected playlist(s)",
                    protectedIds.Count);

                _store.Save(protectedIds);
                ReconcileGroundTruth(protectedIds);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[PlaylistManagementPageView] RunCommand failed", ex);
            }

            ContentData = BuildOptions();
            return Task.FromResult<IPluginUIView>(this);
        }

        // ── Build ──────────────────────────────────────────────────────────

        private PlaylistManagementUI BuildOptions()
        {
            try
            {
                var protectedIds = _store.Load();
                var rows = BuildRows(protectedIds);
                return PlaylistManagementUI.Build(rows);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[PlaylistManagementPageView] BuildOptions failed", ex);
                return PlaylistManagementUI.Build(Array.Empty<PlaylistRow>());
            }
        }

        private PlaylistRow[] BuildRows(HashSet<string> protectedIds)
        {
            var groundTruth = _groundTruthStore.Load();

            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Playlist" },
                Recursive = true
            };

            var items = _libraryManager.GetItemList(query);

            _logger.Info(
                "[PlaylistManagementPageView] BuildRows — found {0} playlist(s) in Emby library",
                items?.Length ?? 0);

            // Log every protected ID and whether Emby can resolve it — ghost detection
            var liveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (items != null)
            {
                foreach (var item in items)
                    liveIds.Add(item.Id.ToString("N"));
            }

            foreach (var protectedId in protectedIds)
            {
                var isLive = liveIds.Contains(protectedId);
                var gtEntry = groundTruth.TryGetValue(protectedId, out var gt) ? gt : null;
                _logger.Info(
                    "[PlaylistManagementPageView] Protected playlist | GuidN={0} | Name={1} | LiveInEmby={2} | GroundTruthExists={3} | GroundTruthActive={4} | MemberCount={5}",
                    protectedId,
                    gtEntry?.PlaylistName ?? "(no ground truth)",
                    isLive,
                    gtEntry != null,
                    gtEntry?.IsActive ?? false,
                    gtEntry?.Members?.Count ?? 0);

                if (!isLive)
                    _logger.Warn(
                        "[PlaylistManagementPageView] GHOST DETECTED — protected GuidN={0} ({1}) has no matching playlist in Emby library",
                        protectedId,
                        gtEntry?.PlaylistName ?? "(unknown)");
            }

            if (items == null || items.Length == 0)
                return Array.Empty<PlaylistRow>();

            var rows = new List<PlaylistRow>(items.Length);

            foreach (var item in items)
            {
                var idString = item.Id.ToString("N");
                var isProtected = protectedIds.Contains(idString);
                var gtEntry = groundTruth.TryGetValue(idString, out var gt) ? gt : null;

                _logger.Info(
                    "[PlaylistManagementPageView] Playlist row | GuidN={0} | Name={1} | InternalId={2} | Path={3} | IsProtected={4} | GroundTruthExists={5} | MemberCount={6}",
                    idString,
                    item.Name ?? "(unnamed)",
                    item.InternalId,
                    item.Path ?? "(no path)",
                    isProtected,
                    gtEntry != null,
                    gtEntry?.Members?.Count ?? 0);

                rows.Add(new PlaylistRow
                {
                    Id = idString,
                    Name = item.Name ?? "(unnamed)",
                    Path = item.Path ?? string.Empty,
                    InternalId = item.InternalId,
                    IsProtected = isProtected,
                    MemberCount = gtEntry?.Members?.Count ?? 0,
                    CapturedAt = gtEntry != null
                        ? gtEntry.CapturedAt.ToString("yyyy-MM-dd HH:mm") + " UTC"
                        : string.Empty
                });
            }

            return rows.ToArray();
        }

        // ── Ground Truth ───────────────────────────────────────────────────

        private void ReconcileGroundTruth(HashSet<string> protectedIds)
        {
            try
            {
                var entries = _groundTruthStore.Load();

                // Soft-delete entries no longer in protected set
                foreach (var key in new List<string>(entries.Keys))
                {
                    if (!protectedIds.Contains(key) && entries[key].IsActive)
                    {
                        entries[key].IsActive = false;
                        _logger.Info("[GroundTruthStore] Soft-deleted entry for playlist {0}", key);
                    }
                }

                // Capture or restore entries for protected playlists
                foreach (var playlistId in protectedIds)
                {
                    if (entries.TryGetValue(playlistId, out var existing))
                    {
                        if (!existing.IsActive)
                        {
                            existing.IsActive = true;
                            _logger.Info(
                                "[GroundTruthStore] Restored soft-deleted entry for playlist {0} (captured {1})",
                                playlistId,
                                existing.CapturedAt);
                        }
                        continue;
                    }

                    // No entry exists — capture fresh snapshot
                    var capture = CaptureMembers(playlistId);
                    if (capture == null) continue;

                    entries[playlistId] = new GroundTruthEntry
                    {
                        PlaylistName = capture.PlaylistName,
                        CapturedAt = DateTime.UtcNow,
                        IsActive = true,
                        Members = capture.Members
                    };

                    _logger.Info(
                        "[GroundTruthStore] Captured {0} member(s) for playlist {1} ({2})",
                        capture.Members.Count,
                        playlistId,
                        capture.PlaylistName);
                }

                _groundTruthStore.Save(entries);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[PlaylistManagementPageView] ReconcileGroundTruth failed", ex);
            }
        }

        private CaptureResult CaptureMembers(string playlistIdN)
        {
            try
            {
                if (!Guid.TryParseExact(playlistIdN, "N", out var guid))
                {
                    _logger.Warn("[GroundTruthStore] CaptureMembers — could not parse playlist id: {0}", playlistIdN);
                    return null;
                }

                var playlistItems = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { "Playlist" },
                    Recursive = true
                });

                BaseItem playlist = null;
                foreach (var p in playlistItems)
                {
                    if (p.Id == guid)
                    {
                        playlist = p;
                        break;
                    }
                }

                if (playlist == null)
                {
                    _logger.Warn("[GroundTruthStore] CaptureMembers — playlist not found: {0}", playlistIdN);
                    return null;
                }

                _logger.Info(
                    "[GroundTruthStore] CaptureMembers — found playlist '{0}' | InternalId={1} | Path={2}",
                    playlist.Name ?? "(unnamed)",
                    playlist.InternalId,
                    playlist.Path ?? "(no path)");

                var members = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    ListIds = new[] { playlist.InternalId },
                    Recursive = true
                });

                _logger.Info(
                    "[GroundTruthStore] CaptureMembers — captured {0} member(s) for '{1}'",
                    members.Length,
                    playlist.Name ?? "(unnamed)");

                var result = new List<GroundTruthMember>(members.Length);
                foreach (var m in members)
                {
                    result.Add(new GroundTruthMember
                    {
                        InternalId = m.InternalId,
                        Id = m.Id.ToString("N"),
                        Name = m.Name ?? string.Empty,
                        Path = m.Path ?? string.Empty,
                        ListItemEntryId = m.ListItemEntryId
                    });
                }

                return new CaptureResult
                {
                    PlaylistName = playlist.Name ?? "(unnamed)",
                    Members = result
                };
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[GroundTruthStore] CaptureMembers failed for playlist " + playlistIdN, ex);
                return null;
            }
        }

        // ── Private types ──────────────────────────────────────────────────

        private class CaptureResult
        {
            public string PlaylistName { get; set; }
            public List<GroundTruthMember> Members { get; set; }
        }
    }
}