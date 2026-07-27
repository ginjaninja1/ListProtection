using ListProtection.Services;
using ListProtection.Storage;
using ListProtection.UI.EventHistoryDialog;
using ListProtection.UI.GroundTruthDialog;
using ListProtection.UI.MissingMembers;
using ListProtection.UI.RepairDialog;
using ListProtection.UI.UnprotectConfirmDialog;
using ListProtection.UIBaseClasses.Views;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
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
        private readonly ICollectionManager _collectionManager;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly ILogger _logger;
        private readonly PlaylistRepairService _repairService;

        public PlaylistManagementPageView(
            PluginInfo pluginInfo,
            PlaylistManagementStore store,
            GroundTruthStore groundTruthStore,
            ILibraryManager libraryManager,
            ICollectionManager collectionManager,
            IJsonSerializer jsonSerializer,
            ILogger logger,
            PlaylistRepairService repairService)
            : base(pluginInfo.Id)
        {
            _pluginInfo = pluginInfo;
            _store = store;
            _groundTruthStore = groundTruthStore;
            _libraryManager = libraryManager;
            _collectionManager = collectionManager;
            _jsonSerializer = jsonSerializer;
            _logger = logger;
            _repairService = repairService;

            ShowSave = false;
            ShowBack = false;

            ContentData = BuildOptions();
        }

        public override async Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
        {
            _logger.Info("[PlaylistManagementPageView] RunCommand | commandId={0}", commandId ?? "(null)");

            try
            {
                if (string.IsNullOrEmpty(data))
                {
                    ContentData = BuildOptions();
                    return this;
                }

                var ui = _jsonSerializer.DeserializeFromString<PlaylistManagementUI>(data);

                if (ui?.PlaylistRows == null)
                {
                    ContentData = BuildOptions();
                    return this;
                }

                // ── Action: Open History ───────────────────────────────────
                var openHistoryRow = ui.PlaylistRows.FirstOrDefault(r => r.OpenHistory && !string.IsNullOrEmpty(r.Id));
                if (openHistoryRow != null)
                {
                    var historyCheck = ListProtectionPlugin.Instance.EventStore.LoadForPlaylist(openHistoryRow.Id);
                    if (historyCheck.Count == 0)
                    {
                        ContentData = BuildOptions();
                        return this;
                    }

                    var gtEntry = _groundTruthStore.Load().TryGetValue(openHistoryRow.Id, out var gt) ? gt : null;
                    var listName = gtEntry?.PlaylistName ?? openHistoryRow.Name ?? "(unnamed)";

                    return new EventHistoryDialogView(
                        _pluginInfo, openHistoryRow.Id, listName,
                        ListProtectionPlugin.Instance.EventStore, _logger);
                }

                // ── Action: Open Ground Truth ──────────────────────────────
                var openGtRow = ui.PlaylistRows.FirstOrDefault(r => r.OpenGroundTruth && !string.IsNullOrEmpty(r.Id));
                if (openGtRow != null)
                {
                    var protectedIds = _store.Load();
                    if (!protectedIds.Contains(openGtRow.Id))
                    {
                        ContentData = BuildOptions();
                        return this;
                    }

                    var gtEntry = _groundTruthStore.Load().TryGetValue(openGtRow.Id, out var gt) ? gt : null;
                    var listName = gtEntry?.PlaylistName ?? openGtRow.Name ?? "(unnamed)";

                    return new GroundTruthDialogView(
                        _pluginInfo, openGtRow.Id, listName, _groundTruthStore, _logger);
                }

                // ── Action: Open Repair ────────────────────────────────────
                var openRepairRow = ui.PlaylistRows.FirstOrDefault(r => r.OpenRepair && !string.IsNullOrEmpty(r.Id));
                if (openRepairRow != null)
                {
                    var protectedIds = _store.Load();
                    if (!protectedIds.Contains(openRepairRow.Id))
                    {
                        ContentData = BuildOptions();
                        return this;
                    }

                    var gtEntry = _groundTruthStore.Load().TryGetValue(openRepairRow.Id, out var gt) ? gt : null;
                    var listName = gtEntry?.PlaylistName ?? openRepairRow.Name ?? "(unnamed)";

                    return new RepairDialogView(
                        _pluginInfo,
                        openRepairRow.Id,
                        listName,
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
                    var syntheticRows = BuildRepairAllRows(repairAllRows.Select(r => r.Id).ToArray());
                    if (syntheticRows.Length > 0)
                        await _repairService.ExecuteRepairs(syntheticRows);

                    ContentData = BuildOptions();
                    return this;
                }

                // ── Action: Toggle Protection ──────────────────────────────
                var currentProtectedIds = _store.Load();
                var incomingProtectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in ui.PlaylistRows)
                    if (row.IsProtected && !string.IsNullOrEmpty(row.Id))
                        incomingProtectedIds.Add(row.Id);

                var beingUnprotected = currentProtectedIds
                    .Where(id => !incomingProtectedIds.Contains(id)).ToArray();
                var beingProtected = incomingProtectedIds
                    .Where(id => !currentProtectedIds.Contains(id)).ToArray();

                if (beingUnprotected.Length == 1 && beingProtected.Length == 0)
                {
                    var unprotectId = beingUnprotected[0];
                    var gtEntry = _groundTruthStore.Load().TryGetValue(unprotectId, out var gt) ? gt : null;
                    var unprotectName = gtEntry?.PlaylistName
                        ?? ui.PlaylistRows.FirstOrDefault(r => r.Id == unprotectId)?.Name
                        ?? "(unnamed)";

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

                            var entries = _groundTruthStore.Load();
                            if (entries.Remove(capturedId))
                                _groundTruthStore.Save(entries);

                            PurgeStaleDetectionData(new[] { capturedId });
                            ReconcileGroundTruth(currentIds);
                        },
                        rebuildParentContent: () => { ContentData = BuildOptions(); },
                        _jsonSerializer,
                        _logger);
                }

                _store.Save(incomingProtectedIds);

                if (beingProtected.Length > 0)
                    PurgeStaleDetectionData(beingProtected);

                ReconcileGroundTruth(incomingProtectedIds);

                var freshGt = _groundTruthStore.Load();
                foreach (var newId in beingProtected)
                {
                    var nameForEvent = ui.PlaylistRows.FirstOrDefault(r => r.Id == newId)?.Name ?? "(unnamed)";
                    freshGt.TryGetValue(newId, out var gtEntry);
                    var members = gtEntry?.Members;
                    string payload;
                    if (members == null || members.Count == 0)
                        payload = string.Empty;
                    else
                    {
                        var lines = members.Select(m => (m.Name ?? "(unnamed)") + " | " + (m.Path ?? string.Empty));
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

        public override void OnDialogResult(IPluginUIView dialogView, bool completedOk, object data)
        {
            if (dialogView is RepairDialogView || dialogView is GroundTruthDialogView || dialogView is EventHistoryDialogView)
            {
                ContentData = BuildOptions();
                RaiseUIViewInfoChanged();
            }
            base.OnDialogResult(dialogView, completedOk, data);
        }

        private PlaylistManagementUI BuildOptions()
        {
            try
            {
                var protectedIds = _store.Load();
                var rows = BuildRows(protectedIds);
                return PlaylistManagementUI.Build(rows, BuildConvergenceStatusText());
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[PlaylistManagementPageView] BuildOptions failed", ex);
                return PlaylistManagementUI.Build(Array.Empty<PlaylistRow>());
            }
        }

        private string BuildConvergenceStatusText()
        {
            var status = ListProtectionPlugin.Instance.ConsistencyStatusStore.Load();
            if (status == null)
                return "⚠️ Consistency check has not run yet — data below may not reflect current library state.";

            var age = DateTime.UtcNow - status.CompletedAtUtc;
            var sourceText = status.Trigger == ConsistencyCheckTrigger.PostScan
                ? "after a library scan"
                : "on schedule/manual run";

            var ageText = age.TotalMinutes < 1
                ? "just now"
                : age.TotalHours < 1
                    ? $"{(int)age.TotalMinutes} min ago"
                    : age.TotalHours < 48
                        ? $"{(int)age.TotalHours}h ago"
                        : $"{(int)age.TotalDays}d ago";

            return $"✅ Last converged {ageText} ({sourceText}). Run a library scan for the most current view.";
        }

        private PlaylistRow[] BuildRows(HashSet<string> protectedIds)
        {
            var groundTruth = _groundTruthStore.Load();
            var missingRecords = ListProtectionPlugin.Instance.MissingMembersStore.Load();
            var allCandidates = ListProtectionPlugin.Instance.CandidateStore.Load();

            var allEvents = ListProtectionPlugin.Instance.EventStore.Load();
            var idsWithHistory = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ev in allEvents)
                if (!string.IsNullOrEmpty(ev.PlaylistId))
                    idsWithHistory.Add(ev.PlaylistId);

            var playlists = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Playlist" },
                Recursive = true
            }) ?? Array.Empty<BaseItem>();

            var collections = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "BoxSet" },
                Recursive = true
            }) ?? Array.Empty<BaseItem>();

            var rows = new List<PlaylistRow>(playlists.Length + collections.Length);

            foreach (var item in playlists)
                rows.Add(BuildRow(item, "Playlist", protectedIds, groundTruth, missingRecords, allCandidates, idsWithHistory));

            foreach (var item in collections)
                rows.Add(BuildRow(item, "Collection", protectedIds, groundTruth, missingRecords, allCandidates, idsWithHistory));

            return rows.ToArray();
        }

        private PlaylistRow BuildRow(
            BaseItem item,
            string listType,
            HashSet<string> protectedIds,
            Dictionary<string, GroundTruthEntry> groundTruth,
            List<MissingMemberEntry> missingRecords,
            List<CandidateEntry> allCandidates,
            HashSet<string> idsWithHistory)
        {
            var idString = item.Id.ToString("N");
            var isProtected = protectedIds.Contains(idString);
            var gtEntry = groundTruth.TryGetValue(idString, out var gt) ? gt : null;

            int memberCount;
            if (isProtected && gtEntry != null)
                memberCount = gtEntry.Members?.Count ?? 0;
            else
            {
                if (listType == "Collection")
                {
                    var col = item as BoxSet;
                    memberCount = col?.GetItemList(new InternalItemsQuery())?.Length ?? 0;
                }
                else
                {
                    var members = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        ListIds = new[] { item.InternalId },
                        Recursive = true
                    });
                    memberCount = members?.Length ?? 0;
                }
            }

            int missingCount = 0;
            int candidateCoveredCount = 0;
            if (isProtected)
            {
                var playlistMissing = missingRecords
                    .Where(r => string.Equals(r.PlaylistId, idString, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                missingCount = playlistMissing.Count;
                foreach (var mr in playlistMissing)
                {
                    var hasCandidate = allCandidates.Any(c =>
                        string.Equals(c.PlaylistId, idString, StringComparison.OrdinalIgnoreCase) &&
                        mr.Member != null &&
                        c.MissingMember?.InternalId == mr.Member.InternalId);
                    if (hasCandidate) candidateCoveredCount++;
                }
            }

            return new PlaylistRow
            {
                Id = idString,
                InternalId = item.InternalId,
                ListType = listType,
                Name = item.Name ?? "(unnamed)",
                Status = memberCount + "/" + missingCount + "/" + candidateCoveredCount,
                IsProtected = isProtected,
                RepairAll = false,
                OpenRepair = false,
                OpenGroundTruth = false,
                OpenHistory = false,
                Detail = new[]
                {
                    new PlaylistDetailRow
                    {
                        PlaylistId = idString,
                        Path = item.Path ?? string.Empty,
                        CapturedAt = gtEntry != null
                            ? gtEntry.CapturedAt.ToString("yyyy-MM-dd HH:mm") + " UTC"
                            : string.Empty
                    }
                }
            };
        }

        private void ReconcileGroundTruth(HashSet<string> protectedIds)
        {
            try
            {
                var entries = _groundTruthStore.Load();

                foreach (var listId in protectedIds)
                {
                    if (entries.ContainsKey(listId)) continue;

                    var capture = CaptureMembers(listId);
                    if (capture == null) continue;

                    entries[listId] = new GroundTruthEntry
                    {
                        ListType = capture.ListType,
                        PlaylistName = capture.ListName,
                        IsPublic = capture.IsPublic,
                        CapturedAt = DateTime.UtcNow,
                        Members = capture.Members
                    };

                    _logger.Info(
                        "[PlaylistManagementPageView] Captured {0} member(s) for {1} '{2}' ({3})",
                        capture.Members.Count, capture.ListType, capture.ListName, listId);
                }

                _groundTruthStore.Save(entries);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[PlaylistManagementPageView] ReconcileGroundTruth failed", ex);
            }
        }

        private CaptureResult CaptureMembers(string listIdN)
        {
            try
            {
                if (!Guid.TryParseExact(listIdN, "N", out var guid))
                {
                    _logger.Warn("[PlaylistManagementPageView] CaptureMembers — invalid Guid: {0}", listIdN);
                    return null;
                }

                var playlists = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { "Playlist" },
                    Recursive = true
                });

                foreach (var p in playlists ?? Array.Empty<BaseItem>())
                {
                    if (p.Id != guid) continue;
                    var playlistEntity = p as MediaBrowser.Controller.Playlists.Playlist;
                    if (playlistEntity == null) return null;

                    var members = playlistEntity.GetItemList(new InternalItemsQuery());
                    return new CaptureResult
                    {
                        ListType = "Playlist",
                        ListName = p.Name ?? "(unnamed)",
                        IsPublic = p.IsPublic,
                        Members = members.Select(m => GroundTruthMemberFactory.FromItem(m)).ToList()
                    };
                }

                var collections = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { "BoxSet" },
                    Recursive = true
                });

                foreach (var c in collections ?? Array.Empty<BaseItem>())
                {
                    if (c.Id != guid) continue;
                    var collectionEntity = c as BoxSet;
                    if (collectionEntity == null) return null;

                    var members = collectionEntity.GetItemList(new InternalItemsQuery());
                    return new CaptureResult
                    {
                        ListType = "Collection",
                        ListName = c.Name ?? "(unnamed)",
                        IsPublic = null,
                        Members = members.Select(m => GroundTruthMemberFactory.FromItem(m)).ToList()
                    };
                }

                _logger.Warn("[PlaylistManagementPageView] CaptureMembers — not found as playlist or collection: {0}", listIdN);
                return null;
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[PlaylistManagementPageView] CaptureMembers failed for " + listIdN, ex);
                return null;
            }
        }

        private MissingMemberRow[] BuildRepairAllRows(string[] listIds)
        {
            var missingRecords = ListProtectionPlugin.Instance.MissingMembersStore.Load();
            var candidateRecords = ListProtectionPlugin.Instance.CandidateStore.Load();
            var groundTruth = _groundTruthStore.Load();
            var rows = new List<MissingMemberRow>();

            foreach (var listId in listIds)
            {
                var listMissing = missingRecords
                    .Where(r => r.PlaylistId == listId && r.Member != null)
                    .ToList();

                if (listMissing.Count == 0) continue;

                groundTruth.TryGetValue(listId, out var gtEntry);
                var listName = gtEntry?.PlaylistName ?? "(unnamed)";

                foreach (var missing in listMissing)
                {
                    var candidates = candidateRecords
                        .Where(c => c.PlaylistId == listId && c.MissingMember?.InternalId == missing.Member.InternalId)
                        .OrderByDescending(c => c.Score)
                        .ToList();

                    if (candidates.Count == 0) continue;

                    var best = candidates[0];
                    var candidateRows = candidates.Select(c => new CandidateRow
                    {
                        Key = listId + "_" + missing.Member.InternalId + "_" + c.CandidateInternalId,
                        CandidateName = c.CandidateName ?? "(unnamed)",
                        CandidatePath = c.CandidatePath ?? string.Empty,
                        Score = c.Score,
                        Signals = string.Join(", ", c.MatchedSignals ?? new List<string>()),
                        Repair = c.CandidateInternalId == best.CandidateInternalId
                    }).ToArray();

                    rows.Add(new MissingMemberRow
                    {
                        Key = listId + "_" + missing.Member.InternalId,
                        PlaylistName = listName,
                        MemberName = missing.Member.Name ?? "(unnamed)",
                        Path = missing.Member.Path ?? string.Empty,
                        DetectedAt = missing.DetectedAt.ToString("yyyy-MM-dd HH:mm") + " UTC",
                        Forget = false,
                        IsSynthetic = false,
                        Candidates = candidateRows
                    });
                }
            }

            return rows.ToArray();
        }

        private void PurgeStaleDetectionData(string[] listIds)
        {
            try
            {
                var purgeSet = new HashSet<string>(listIds, StringComparer.OrdinalIgnoreCase);

                var missing = ListProtectionPlugin.Instance.MissingMembersStore.Load();
                var beforeMissing = missing.Count;
                missing.RemoveAll(r => purgeSet.Contains(r.PlaylistId));
                if (missing.Count != beforeMissing)
                    ListProtectionPlugin.Instance.MissingMembersStore.Save(missing);

                var candidates = ListProtectionPlugin.Instance.CandidateStore.Load();
                var beforeCandidates = candidates.Count;
                candidates.RemoveAll(c => purgeSet.Contains(c.PlaylistId));
                if (candidates.Count != beforeCandidates)
                    ListProtectionPlugin.Instance.CandidateStore.Save(candidates);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[PlaylistManagementPageView] PurgeStaleDetectionData failed", ex);
            }
        }

        private void WriteEvent(string eventType, string listId, string listName, string payload)
        {
            try
            {
                ListProtectionPlugin.Instance.EventStore.Append(new EventEntry
                {
                    EventType = eventType,
                    PlaylistId = listId,
                    PlaylistName = listName,
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
            public string ListType { get; set; }
            public string ListName { get; set; }
            public bool? IsPublic { get; set; }
            public List<GroundTruthMember> Members { get; set; }
        }
    }
}