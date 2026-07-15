using ListProtection.Services;
using ListProtection.Storage;
using ListProtection.UI.EventHistoryDialog;
using ListProtection.UI.GroundTruthDialog;
using ListProtection.UI.MissingMembers;
using ListProtection.UI.RepairDialog;
using ListProtection.UI.UnprotectConfirmDialog;
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
using System.Linq;
using System.Threading.Tasks;

namespace ListProtection.UI.PlaylistManagement
{
    internal class PlaylistManagementPageView : PluginPageView
    {
        private readonly PluginInfo _pluginInfo;
        private readonly PlaylistManagementStore _store;
        private readonly GroundTruthStore _groundTruthStore;
        private readonly ILibraryManager _libraryManager;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly ILogger _logger;
        private readonly PlaylistRepairService _repairService;

        // Carries the playlistId that triggered the unprotect confirm dialog,
        // so OnDialogResult can complete the action if confirmed.
        private string _pendingUnprotectId;
        private string _pendingUnprotectName;

        public PlaylistManagementPageView(
            PluginInfo pluginInfo,
            PlaylistManagementStore store,
            GroundTruthStore groundTruthStore,
            ILibraryManager libraryManager,
            IJsonSerializer jsonSerializer,
            ILogger logger,
            PlaylistRepairService repairService)
            : base(pluginInfo.Id)
        {
            _pluginInfo = pluginInfo;
            _store = store;
            _groundTruthStore = groundTruthStore;
            _libraryManager = libraryManager;
            _jsonSerializer = jsonSerializer;
            _logger = logger;
            _repairService = repairService;

            ShowSave = false;
            ShowBack = false;

            ContentData = BuildOptions();
        }

        // ── RunCommand ─────────────────────────────────────────────────────

        public override async Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
        {
            _logger.Info(
                "[PlaylistManagementPageView] RunCommand RAW | itemId={0} | commandId={1}",
                itemId ?? "(null)",
                commandId ?? "(null)");

            try
            {
                if (string.IsNullOrEmpty(data))
                {
                    _logger.Warn("[PlaylistManagementPageView] RunCommand — data was null or empty, ignoring");
                    ContentData = BuildOptions();
                    return this;
                }

                var ui = _jsonSerializer.DeserializeFromString<PlaylistManagementUI>(data);

                if (ui?.PlaylistRows == null)
                {
                    _logger.Warn("[PlaylistManagementPageView] RunCommand — could not parse PlaylistManagementUI from data");
                    ContentData = BuildOptions();
                    return this;
                }

                // ── Action: Open History Dialog ────────────────────────────
                var openHistoryRow = ui.PlaylistRows.FirstOrDefault(r => r.OpenHistory && !string.IsNullOrEmpty(r.Id));

                if (openHistoryRow != null)
                {
                    // Only meaningful for protected playlists
                    var protectedIds = _store.Load();
                    if (!protectedIds.Contains(openHistoryRow.Id))
                    {
                        _logger.Info("[PlaylistManagementPageView] OpenHistory on unprotected row ignored");
                        ContentData = BuildOptions();
                        return this;
                    }

                    var gtEntry = _groundTruthStore.Load().TryGetValue(openHistoryRow.Id, out var gt) ? gt : null;
                    var playlistName = gtEntry?.PlaylistName ?? openHistoryRow.Name ?? "(unnamed)";

                    _logger.Info(
                        "[PlaylistManagementPageView] OpenHistory — launching dialog for playlist '{0}' (GuidN={1})",
                        playlistName, openHistoryRow.Id);

                    return new EventHistoryDialogView(
                        _pluginInfo,
                        openHistoryRow.Id,
                        playlistName,
                        ListProtectionPlugin.Instance.EventStore,
                        _logger);
                }

                // ── Action: Open Ground Truth Dialog ──────────────────────
                var openGtRow = ui.PlaylistRows.FirstOrDefault(r => r.OpenGroundTruth && !string.IsNullOrEmpty(r.Id));

                if (openGtRow != null)
                {
                    // Only meaningful for protected playlists
                    var protectedIds = _store.Load();
                    if (!protectedIds.Contains(openGtRow.Id))
                    {
                        _logger.Info("[PlaylistManagementPageView] OpenGroundTruth on unprotected row ignored");
                        ContentData = BuildOptions();
                        return this;
                    }

                    var gtEntry = _groundTruthStore.Load().TryGetValue(openGtRow.Id, out var gt) ? gt : null;
                    var playlistName = gtEntry?.PlaylistName ?? openGtRow.Name ?? "(unnamed)";

                    _logger.Info(
                        "[PlaylistManagementPageView] OpenGroundTruth — launching dialog for playlist '{0}' (GuidN={1})",
                        playlistName, openGtRow.Id);

                    return new GroundTruthDialogView(
                        _pluginInfo,
                        openGtRow.Id,
                        playlistName,
                        _groundTruthStore,
                        _logger);
                }

                // ── Action: Open Repair Dialog ─────────────────────────────
                var openRepairRow = ui.PlaylistRows.FirstOrDefault(r => r.OpenRepair && !string.IsNullOrEmpty(r.Id));

                if (openRepairRow != null)
                {
                    var protectedIds = _store.Load();
                    if (!protectedIds.Contains(openRepairRow.Id))
                    {
                        _logger.Info("[PlaylistManagementPageView] OpenRepair on unprotected row ignored");
                        ContentData = BuildOptions();
                        return this;
                    }

                    var gtEntry = _groundTruthStore.Load().TryGetValue(openRepairRow.Id, out var gt) ? gt : null;
                    var playlistName = gtEntry?.PlaylistName ?? openRepairRow.Name ?? "(unnamed)";

                    _logger.Info(
                        "[PlaylistManagementPageView] OpenRepair — launching dialog for playlist '{0}' (GuidN={1})",
                        playlistName, openRepairRow.Id);

                    return new RepairDialogView(
                        _pluginInfo,
                        openRepairRow.Id,
                        playlistName,
                        ListProtectionPlugin.Instance.MissingMembersStore,
                        _groundTruthStore,
                        _store,
                        _repairService,
                        _jsonSerializer,
                        _logger);
                }

                // ── Action: Repair All ─────────────────────────────────────
                var repairAllRows = ui.PlaylistRows.Where(r => r.RepairAll && !string.IsNullOrEmpty(r.Id)).ToArray();

                if (repairAllRows.Length > 0)
                {
                    _logger.Info(
                        "[PlaylistManagementPageView] RepairAll triggered for {0} playlist(s)",
                        repairAllRows.Length);

                    var syntheticRows = BuildRepairAllRows(repairAllRows.Select(r => r.Id).ToArray());

                    if (syntheticRows.Length > 0)
                        await _repairService.ExecuteRepairs(syntheticRows);
                    else
                        _logger.Info("[PlaylistManagementPageView] RepairAll — no candidates available to repair");

                    ContentData = BuildOptions();
                    return this;
                }

                // ── Action: Toggle Protection ──────────────────────────────
                // Determine which playlists are being protected vs. unprotected
                // compared to the current store state.
                var currentProtectedIds = _store.Load();
                var incomingProtectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in ui.PlaylistRows)
                {
                    if (row.IsProtected && !string.IsNullOrEmpty(row.Id))
                        incomingProtectedIds.Add(row.Id);
                }

                // Detect newly unprotected playlists
                var beingUnprotected = currentProtectedIds
                    .Where(id => !incomingProtectedIds.Contains(id))
                    .ToArray();

                // Detect newly protected playlists
                var beingProtected = incomingProtectedIds
                    .Where(id => !currentProtectedIds.Contains(id))
                    .ToArray();

                // If exactly one playlist is being unprotected, launch confirm dialog
                if (beingUnprotected.Length == 1 && beingProtected.Length == 0)
                {
                    var unprotectId = beingUnprotected[0];
                    var gtEntry = _groundTruthStore.Load().TryGetValue(unprotectId, out var gt) ? gt : null;
                    var unprotectName = gtEntry?.PlaylistName
                        ?? ui.PlaylistRows.FirstOrDefault(r => r.Id == unprotectId)?.Name
                        ?? "(unnamed)";

                    _logger.Info(
                        "[PlaylistManagementPageView] Unprotect requested for '{0}' — launching confirm dialog",
                        unprotectName);

                    _pendingUnprotectId = unprotectId;
                    _pendingUnprotectName = unprotectName;

                    return new UnprotectConfirmDialogView(
                        _pluginInfo,
                        unprotectId,
                        unprotectName,
                        _jsonSerializer,
                        _logger);
                }

                // Save new protected set and handle protect events
                _logger.Info(
                    "[PlaylistManagementPageView] Saving {0} protected playlist(s)",
                    incomingProtectedIds.Count);

                // Write Protect events for newly protected playlists
                foreach (var newId in beingProtected)
                {
                    var nameForEvent = ui.PlaylistRows.FirstOrDefault(r => r.Id == newId)?.Name ?? "(unnamed)";
                    WriteEvent("Protect", newId, nameForEvent, string.Empty);
                }

                _store.Save(incomingProtectedIds);
                ReconcileGroundTruth(incomingProtectedIds);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[PlaylistManagementPageView] RunCommand failed", ex);
            }

            ContentData = BuildOptions();
            return this;
        }

        // ── Dialog result ──────────────────────────────────────────────────

        public override void OnDialogResult(IPluginUIView dialogView, bool completedOk, object data)
        {
            if (dialogView is UnprotectConfirmDialogView confirmDialog)
            {
                _logger.Info(
                    "[PlaylistManagementPageView] UnprotectConfirmDialog closed | Confirmed={0}",
                    confirmDialog.Confirmed);

                if (confirmDialog.Confirmed && !string.IsNullOrEmpty(_pendingUnprotectId))
                {
                    var currentIds = _store.Load();
                    currentIds.Remove(_pendingUnprotectId);

                    WriteEvent("Unprotect", _pendingUnprotectId, _pendingUnprotectName, string.Empty);

                    _store.Save(currentIds);
                    ReconcileGroundTruth(currentIds);

                    _logger.Info(
                        "[PlaylistManagementPageView] Unprotect completed for '{0}'",
                        _pendingUnprotectName);
                }

                _pendingUnprotectId = null;
                _pendingUnprotectName = null;

                ContentData = BuildOptions();
                RaiseUIViewInfoChanged();
            }
            else if (dialogView is RepairDialogView || dialogView is GroundTruthDialogView || dialogView is EventHistoryDialogView)
            {
                _logger.Info(
                    "[PlaylistManagementPageView] {0} closed — refreshing Tab 1",
                    dialogView.GetType().Name);
                ContentData = BuildOptions();
                RaiseUIViewInfoChanged();
            }

            base.OnDialogResult(dialogView, completedOk, data);
        }

        // ── Repair All helper ──────────────────────────────────────────────

        private MissingMemberRow[] BuildRepairAllRows(string[] playlistIds)
        {
            var missingRecords = ListProtectionPlugin.Instance.MissingMembersStore.Load();
            var candidateRecords = ListProtectionPlugin.Instance.CandidateStore.Load();
            var groundTruth = _groundTruthStore.Load();

            var rows = new List<MissingMemberRow>();

            foreach (var playlistId in playlistIds)
            {
                var playlistMissing = missingRecords
                    .Where(r => r.PlaylistId == playlistId && r.Member != null)
                    .ToList();

                if (playlistMissing.Count == 0)
                {
                    _logger.Info("[PlaylistManagementPageView] RepairAll — no missing members for playlist {0}", playlistId);
                    continue;
                }

                groundTruth.TryGetValue(playlistId, out var gtEntry);
                var playlistName = gtEntry?.PlaylistName ?? "(unnamed)";

                foreach (var missing in playlistMissing)
                {
                    var candidates = candidateRecords
                        .Where(c => c.PlaylistId == playlistId && c.MissingMember?.InternalId == missing.Member.InternalId)
                        .OrderByDescending(c => c.Score)
                        .ToList();

                    if (candidates.Count == 0)
                    {
                        _logger.Info(
                            "[PlaylistManagementPageView] RepairAll — no candidates for member '{0}' in playlist '{1}', skipping",
                            missing.Member.Name ?? "(null)", playlistName);
                        continue;
                    }

                    var best = candidates[0];

                    _logger.Info(
                        "[PlaylistManagementPageView] RepairAll — selecting candidate '{0}' (score={1}) for member '{2}' in playlist '{3}'",
                        best.CandidateName ?? "(null)", best.Score, missing.Member.Name ?? "(null)", playlistName);

                    var candidateRows = candidates.Select(c => new CandidateRow
                    {
                        Key = playlistId + "_" + missing.Member.InternalId + "_" + c.CandidateInternalId,
                        CandidateName = c.CandidateName ?? "(unnamed)",
                        CandidatePath = c.CandidatePath ?? string.Empty,
                        Score = c.Score,
                        Signals = string.Join(", ", c.MatchedSignals ?? new List<string>()),
                        Repair = c.CandidateInternalId == best.CandidateInternalId
                    }).ToArray();

                    rows.Add(new MissingMemberRow
                    {
                        Key = playlistId + "_" + missing.Member.InternalId,
                        PlaylistName = playlistName,
                        MemberName = missing.Member.Name ?? "(unnamed)",
                        Path = missing.Member.Path ?? string.Empty,
                        DetectedAt = missing.DetectedAt.ToString("yyyy-MM-dd HH:mm") + " UTC",
                        Forget = false,
                        IsSynthetic = false,
                        Candidates = candidateRows
                    });
                }
            }

            _logger.Info("[PlaylistManagementPageView] RepairAll — built {0} repair row(s)", rows.Count);
            return rows.ToArray();
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
            var missingRecords = ListProtectionPlugin.Instance.MissingMembersStore.Load();

            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Playlist" },
                Recursive = true
            });

            _logger.Info(
                "[PlaylistManagementPageView] BuildRows — found {0} playlist(s) in Emby library",
                items?.Length ?? 0);

            var liveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (items != null)
                foreach (var item in items)
                    liveIds.Add(item.Id.ToString("N"));

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

                // Status: GT/MM/MC  — member count / missing count / candidate count
                var memberCount = gtEntry?.Members?.Count ?? 0;
                var missingCount = missingRecords.Count(r =>
                    string.Equals(r.PlaylistId, idString, StringComparison.OrdinalIgnoreCase));
                var candidateCount = ListProtectionPlugin.Instance.CandidateStore.Load().Count(c =>
                    string.Equals(c.PlaylistId, idString, StringComparison.OrdinalIgnoreCase));
                var status = memberCount + "/" + missingCount + "/" + candidateCount;

                // Single-row detail for the child grid (troubleshooting metadata)
                var detailRows = new[]
                {
                    new PlaylistDetailRow
                    {
                        PlaylistId = idString,
                        Path = item.Path ?? string.Empty,
                        CapturedAt = gtEntry != null
                            ? gtEntry.CapturedAt.ToString("yyyy-MM-dd HH:mm") + " UTC"
                            : string.Empty
                    }
                };

                rows.Add(new PlaylistRow
                {
                    Id = idString,
                    InternalId = item.InternalId,
                    Name = item.Name ?? "(unnamed)",
                    Status = status,
                    IsProtected = isProtected,
                    RepairAll = false,
                    OpenRepair = false,
                    OpenGroundTruth = false,
                    OpenHistory = false,
                    Detail = detailRows
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

                foreach (var key in new List<string>(entries.Keys))
                {
                    if (!protectedIds.Contains(key) && entries[key].IsActive)
                    {
                        entries[key].IsActive = false;
                        _logger.Info("[GroundTruthStore] Soft-deleted entry for playlist {0}", key);
                    }
                }

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
                    if (p.Id == guid) { playlist = p; break; }
                }

                if (playlist == null)
                {
                    _logger.Warn("[GroundTruthStore] CaptureMembers — playlist not found: {0}", playlistIdN);
                    return null;
                }

                _logger.Info(
                    "[GroundTruthStore] CaptureMembers — found playlist '{0}' | InternalId={1} | Path={2}",
                    playlist.Name ?? "(unnamed)", playlist.InternalId, playlist.Path ?? "(no path)");

                var members = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    ListIds = new[] { playlist.InternalId },
                    Recursive = true
                });

                _logger.Info(
                    "[GroundTruthStore] CaptureMembers — captured {0} member(s) for '{1}'",
                    members.Length, playlist.Name ?? "(unnamed)");

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

        // ── Event helpers ──────────────────────────────────────────────────

        private void WriteEvent(string eventType, string playlistId, string playlistName, string payload)
        {
            try
            {
                ListProtectionPlugin.Instance.EventStore.Append(new EventEntry
                {
                    EventType = eventType,
                    PlaylistId = playlistId,
                    PlaylistName = playlistName,
                    OccurredAt = DateTime.UtcNow,
                    Payload = payload ?? string.Empty
                });
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[PlaylistManagementPageView] WriteEvent failed", ex);
            }
        }

        private class CaptureResult
        {
            public string PlaylistName { get; set; }
            public List<GroundTruthMember> Members { get; set; }
        }
    }
}