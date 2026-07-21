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
                // Available for any playlist that has event history — not just currently protected ones.
                var openHistoryRow = ui.PlaylistRows.FirstOrDefault(r => r.OpenHistory && !string.IsNullOrEmpty(r.Id));

                if (openHistoryRow != null)
                {
                    // Check whether this playlist has any history before opening
                    var historyCheck = ListProtectionPlugin.Instance.EventStore.LoadForPlaylist(openHistoryRow.Id);
                    if (historyCheck.Count == 0)
                    {
                        _logger.Info("[PlaylistManagementPageView] OpenHistory — no events recorded for {0}, ignoring", openHistoryRow.Id);
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
                        this,
                        () => { ContentData = BuildOptions(); RaiseUIViewInfoChanged(); },
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
                var currentProtectedIds = _store.Load();
                var incomingProtectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in ui.PlaylistRows)
                {
                    if (row.IsProtected && !string.IsNullOrEmpty(row.Id))
                        incomingProtectedIds.Add(row.Id);
                }

                var beingUnprotected = currentProtectedIds
                    .Where(id => !incomingProtectedIds.Contains(id))
                    .ToArray();

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

                    var capturedId = unprotectId;
                    var capturedName = unprotectName;

                    return new UnprotectConfirmDialogView(
                        _pluginInfo,
                        unprotectId,
                        unprotectName,
                        parentPageView: this,
                        executeUnprotect: () =>
                        {
                            var currentIds = _store.Load();
                            currentIds.Remove(capturedId);
                            WriteEvent("Unprotect", capturedId, capturedName, string.Empty);
                            _store.Save(currentIds);

                            // Hard-delete the GT entry — user confirmed intent via name dialog.
                            // No soft-delete: re-protecting always captures fresh ground truth.
                            var entries = _groundTruthStore.Load();
                            if (entries.Remove(capturedId))
                            {
                                _groundTruthStore.Save(entries);
                                _logger.Info(
                                    "[PlaylistManagementPageView] Hard-deleted GT entry for '{0}' ({1})",
                                    capturedName, capturedId);
                            }

                            ReconcileGroundTruth(currentIds);

                            _logger.Info(
                                "[PlaylistManagementPageView] Unprotect executed for '{0}'",
                                capturedName);
                        },
                        rebuildParentContent: () =>
                        {
                            ContentData = BuildOptions();
                        },
                        _jsonSerializer,
                        _logger);
                }

                // ── Write Protect events AFTER ReconcileGroundTruth so we have member data ──
                _logger.Info(
                    "[PlaylistManagementPageView] Saving {0} protected playlist(s)",
                    incomingProtectedIds.Count);

                _store.Save(incomingProtectedIds);

                // Capture GT first — Protect event payload uses the captured members
                ReconcileGroundTruth(incomingProtectedIds);

                // Now write Protect events with member payload
                var freshGt = _groundTruthStore.Load();
                foreach (var newId in beingProtected)
                {
                    var nameForEvent = ui.PlaylistRows.FirstOrDefault(r => r.Id == newId)?.Name ?? "(unnamed)";
                    freshGt.TryGetValue(newId, out var gtEntry);
                    var members = gtEntry?.Members;

                    string payload;
                    if (members == null || members.Count == 0)
                    {
                        payload = string.Empty;
                    }
                    else
                    {
                        // One line per member: "Name | Path"
                        var lines = new List<string>(members.Count);
                        foreach (var m in members)
                            lines.Add((m.Name ?? "(unnamed)") + " | " + (m.Path ?? string.Empty));
                        payload = string.Join("\n", lines);
                    }

                    WriteEvent("Protect", newId, nameForEvent, payload);
                }
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
            if (dialogView is RepairDialogView || dialogView is GroundTruthDialogView || dialogView is EventHistoryDialogView)
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

            // Collect all playlist IDs that have any event history — History button is
            // shown for these even if the playlist is no longer protected.
            var allEvents = ListProtectionPlugin.Instance.EventStore.Load();
            var idsWithHistory = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ev in allEvents)
                if (!string.IsNullOrEmpty(ev.PlaylistId))
                    idsWithHistory.Add(ev.PlaylistId);

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
                    "[PlaylistManagementPageView] Protected playlist | GuidN={0} | Name={1} | LiveInEmby={2} | GroundTruthExists={3} | MemberCount={4}",
                    protectedId,
                    gtEntry?.PlaylistName ?? "(no ground truth)",
                    isLive,
                    gtEntry != null,
                    gtEntry?.Members?.Count ?? 0);

                if (!isLive)
                    _logger.Warn(
                        "[PlaylistManagementPageView] GHOST DETECTED — protected GuidN={0} ({1}) has no matching playlist in Emby library",
                        protectedId,
                        gtEntry?.PlaylistName ?? "(unknown)");
            }

            if (items == null || items.Length == 0)
                return Array.Empty<PlaylistRow>();

            // Batch live member count for unprotected playlists in a single query.
            var unprotectedInternalIds = new List<long>();
            foreach (var item in items)
            {
                var id = item.Id.ToString("N");
                if (!protectedIds.Contains(id) || !groundTruth.ContainsKey(id))
                    unprotectedInternalIds.Add(item.InternalId);
            }

            var liveCountByInternalId = new Dictionary<long, int>();
            if (unprotectedInternalIds.Count > 0)
            {
                var allLiveMembers = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    ListIds = unprotectedInternalIds.ToArray(),
                    Recursive = true
                });

                if (allLiveMembers != null)
                {
                    if (unprotectedInternalIds.Count == 1)
                    {
                        liveCountByInternalId[unprotectedInternalIds[0]] = allLiveMembers.Length;
                    }
                    else
                    {
                        foreach (var internalId in unprotectedInternalIds)
                        {
                            var members = _libraryManager.GetItemList(new InternalItemsQuery
                            {
                                ListIds = new[] { internalId },
                                Recursive = true
                            });
                            liveCountByInternalId[internalId] = members?.Length ?? 0;
                        }
                    }
                }
            }

            var rows = new List<PlaylistRow>(items.Length);

            foreach (var item in items)
            {
                var idString = item.Id.ToString("N");
                var isProtected = protectedIds.Contains(idString);
                var gtEntry = groundTruth.TryGetValue(idString, out var gt) ? gt : null;
                var hasHistory = idsWithHistory.Contains(idString);

                int memberCount;
                if (isProtected && gtEntry != null)
                {
                    memberCount = gtEntry.Members?.Count ?? 0;
                }
                else
                {
                    liveCountByInternalId.TryGetValue(item.InternalId, out memberCount);
                }

                var playlistMissing = missingRecords
                    .Where(r => string.Equals(r.PlaylistId, idString, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var missingCount = playlistMissing.Count;
                var allCandidates = ListProtectionPlugin.Instance.CandidateStore.Load();
                var candidateCoveredCount = 0;
                foreach (var mr in playlistMissing)
                {
                    var hasCandidate = allCandidates.Any(c =>
                        string.Equals(c.PlaylistId, idString, StringComparison.OrdinalIgnoreCase) &&
                        mr.Member != null &&
                        c.MissingMember?.InternalId == mr.Member.InternalId);
                    if (hasCandidate) candidateCoveredCount++;
                }
                var status = memberCount + "/" + missingCount + "/" + candidateCoveredCount;

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
                    // History button visible for any playlist with recorded events,
                    // regardless of current protection status
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

                foreach (var playlistId in protectedIds)
                {
                    if (entries.ContainsKey(playlistId))
                        continue;

                    var capture = CaptureMembers(playlistId);
                    if (capture == null) continue;

                    entries[playlistId] = new GroundTruthEntry
                    {
                        PlaylistName = capture.PlaylistName,
                        CapturedAt = DateTime.UtcNow,
                        Members = capture.Members
                    };

                    _logger.Info(
                        "[GroundTruthStore] Captured {0} member(s) for playlist {1} ({2})",
                        capture.Members.Count, playlistId, capture.PlaylistName);
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

                // PROVEN: Playlist.GetItemList returns members in correct playlist order (ListItemOrder).
                // ILibraryManager.GetItemList with ListIds returns DB insertion order — do not use.
                // Proven by CaptureOrderProbeTask 2026-07-18.
                var playlistEntity = playlist as MediaBrowser.Controller.Playlists.Playlist;
                if (playlistEntity == null)
                {
                    _logger.Warn("[GroundTruthStore] CaptureMembers — could not cast to Playlist: {0}", playlistIdN);
                    return null;
                }

                var members = playlistEntity.GetItemList(new InternalItemsQuery());

                _logger.Info(
                    "[GroundTruthStore] CaptureMembers — captured {0} member(s) for '{1}'",
                    members.Length, playlist.Name ?? "(unnamed)");

                var result = new List<GroundTruthMember>(members.Length);
                foreach (var m in members)
                    result.Add(GroundTruthMemberFactory.FromItem(m));

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