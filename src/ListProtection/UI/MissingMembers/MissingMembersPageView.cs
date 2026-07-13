using ListProtection.Services;
using ListProtection.Storage;
using ListProtection.UIBaseClasses.Views;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ListProtection.UI.MissingMembers
{
    internal class MissingMembersPageView : PluginPageView
    {
        private readonly MissingMembersStore _missingMembersStore;
        private readonly GroundTruthStore _groundTruthStore;
        private readonly PlaylistManagementStore _playlistStore;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly PlaylistRepairService _repairService;

        public MissingMembersPageView(
            PluginInfo pluginInfo,
            MissingMembersStore missingMembersStore,
            GroundTruthStore groundTruthStore,
            PlaylistManagementStore playlistStore,
            IJsonSerializer jsonSerializer,
            ILogger logger,
            ILibraryManager libraryManager,
            PlaylistRepairService repairService)
            : base(pluginInfo.Id)
        {
            _missingMembersStore = missingMembersStore;
            _groundTruthStore = groundTruthStore;
            _playlistStore = playlistStore;
            _jsonSerializer = jsonSerializer;
            _logger = logger;
            _libraryManager = libraryManager;
            _repairService = repairService;

            ShowSave = false;
            ShowBack = false;

            ContentData = BuildOptions();
        }

        // ── RunCommand ─────────────────────────────────────────────────────

        public override async Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
        {
            _logger.Info(
                "[MissingMembersPageView] RunCommand | commandId={0} | itemId={1}",
                commandId ?? "(null)",
                itemId ?? "(null)");

            if (!string.IsNullOrEmpty(data))
                _logger.Info("[MissingMembersPageView] RunCommand payload: {0}", data);
            else
                _logger.Warn("[MissingMembersPageView] RunCommand — data was null or empty, ignoring");

            try
            {
                if (string.IsNullOrEmpty(data))
                {
                    ContentData = BuildOptions();
                    return this;
                }

                if (commandId == "RepairMember")
                {
                    var repairUi = _jsonSerializer.DeserializeFromString<MissingMembersUI>(data);
                    if (repairUi?.MissingMemberRows != null)
                        await _repairService.ExecuteRepairs(repairUi.MissingMemberRows);

                    ContentData = BuildOptions();
                    return this;
                }

                if (commandId == "ForgetMember")
                {
                    var ui = _jsonSerializer.DeserializeFromString<MissingMembersUI>(data);
                    if (ui?.MissingMemberRows != null)
                        ProcessForgets(ui.MissingMemberRows);
                    else
                        _logger.Warn("[MissingMembersPageView] RunCommand — could not parse MissingMembersUI from data");

                    ContentData = BuildOptions();
                    return this;
                }

                _logger.Warn("[MissingMembersPageView] RunCommand — unexpected commandId: {0}", commandId);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[MissingMembersPageView] RunCommand failed", ex);
            }

            ContentData = BuildOptions();
            return this;
        }

        // ── Forget ─────────────────────────────────────────────────────────

        private void ProcessForgets(MissingMemberRow[] rows)
        {
            var missingRecords = _missingMembersStore.Load();
            var groundTruth = _groundTruthStore.Load();
            var missingChanged = false;
            var groundTruthChanged = false;

            foreach (var row in rows)
            {
                if (!row.Forget) continue;
                if (row.IsSynthetic) continue;
                if (string.IsNullOrEmpty(row.Key)) continue;

                if (row.Key.Length < 34 || row.Key[32] != '_')
                {
                    _logger.Warn("[MissingMembersPageView] ForgetMember — unexpected Key format: {0}", row.Key);
                    continue;
                }

                var playlistId = row.Key.Substring(0, 32);
                var internalIdStr = row.Key.Substring(33);

                if (!long.TryParse(internalIdStr, out var internalId))
                {
                    _logger.Warn("[MissingMembersPageView] ForgetMember — could not parse InternalId from Key: {0}", row.Key);
                    continue;
                }

                _logger.Info(
                    "[MissingMembersPageView] ForgetMember — playlist={0} | InternalId={1} | member='{2}'",
                    playlistId, internalId, row.MemberName ?? "(null)");

                for (var i = missingRecords.Count - 1; i >= 0; i--)
                {
                    var r = missingRecords[i];
                    if (r.PlaylistId == playlistId && r.Member?.InternalId == internalId)
                    {
                        _logger.Info(
                            "[MissingMembersPageView] Removing missing record: '{0}' | playlist={1}",
                            r.Member?.Name ?? "(null)", playlistId);
                        missingRecords.RemoveAt(i);
                        missingChanged = true;
                        break;
                    }
                }

                if (groundTruth.TryGetValue(playlistId, out var entry) && entry.Members != null)
                {
                    for (var i = entry.Members.Count - 1; i >= 0; i--)
                    {
                        if (entry.Members[i].InternalId == internalId)
                        {
                            _logger.Info(
                                "[MissingMembersPageView] Removing from ground truth: '{0}' | playlist={1}",
                                entry.Members[i].Name ?? "(null)", playlistId);
                            entry.Members.RemoveAt(i);
                            groundTruthChanged = true;
                            break;
                        }
                    }
                }
            }

            if (missingChanged)
            {
                _missingMembersStore.Save(missingRecords);
                _logger.Info("[MissingMembersPageView] MissingMembersStore saved after forget");
            }

            if (groundTruthChanged)
            {
                _groundTruthStore.Save(groundTruth);
                _logger.Info("[MissingMembersPageView] GroundTruthStore saved after forget");
            }
        }

        // ── Build ──────────────────────────────────────────────────────────

        private MissingMembersUI BuildOptions()
        {
            try
            {
                var rows = BuildRows();
                return MissingMembersUI.Build(rows);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[MissingMembersPageView] BuildOptions failed", ex);
                return MissingMembersUI.Build(Array.Empty<MissingMemberRow>());
            }
        }

        private MissingMemberRow[] BuildRows()
        {
            var protectedIds = _playlistStore.Load();
            var groundTruth = _groundTruthStore.Load();
            var missingRecords = _missingMembersStore.Load();
            var candidateRecords = ListProtectionPlugin.Instance.CandidateStore.Load();

            _logger.Info(
                "[MissingMembersPageView] BuildRows — {0} protected playlist(s), {1} missing record(s), {2} candidate(s)",
                protectedIds.Count, missingRecords.Count, candidateRecords.Count);

            // Build set of live playlist GuidNs for ghost detection
            var allPlaylists = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Playlist" },
                Recursive = true
            });

            var liveIds = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (allPlaylists != null)
                foreach (var p in allPlaylists)
                    liveIds.Add(p.Id.ToString("N"));

            _logger.Info(
                "[MissingMembersPageView] BuildRows — {0} live playlist(s) in Emby library",
                liveIds.Count);

            var rows = new List<MissingMemberRow>();

            foreach (var playlistId in protectedIds)
            {
                if (!groundTruth.TryGetValue(playlistId, out var entry) || !entry.IsActive)
                {
                    _logger.Warn(
                        "[MissingMembersPageView] BuildRows — protected playlist {0} has no active ground truth entry, skipping",
                        playlistId);
                    continue;
                }

                var isGhost = !liveIds.Contains(playlistId);
                var basePlaylistName = entry.PlaylistName ?? "(unnamed)";
                var displayPlaylistName = isGhost
                    ? basePlaylistName + " [Will be recreated]"
                    : basePlaylistName;

                if (isGhost)
                    _logger.Info(
                        "[MissingMembersPageView] BuildRows — playlist GuidN={0} ('{1}') is not live in Emby — labelling as 'Will be recreated'",
                        playlistId, basePlaylistName);

                var playlistMissingRows = new List<MissingMemberRow>();

                foreach (var record in missingRecords)
                {
                    if (record.PlaylistId != playlistId) continue;
                    if (record.Member == null) continue;

                    playlistMissingRows.Add(new MissingMemberRow
                    {
                        Key = playlistId + "_" + record.Member.InternalId,
                        PlaylistName = displayPlaylistName,
                        MemberName = record.Member.Name ?? "(unnamed)",
                        Path = record.Member.Path ?? string.Empty,
                        DetectedAt = record.DetectedAt.ToString("yyyy-MM-dd HH:mm") + " UTC",
                        Forget = false,
                        IsSynthetic = false,
                        Candidates = BuildCandidateRows(playlistId, record.Member.InternalId, candidateRecords)
                    });
                }

                if (playlistMissingRows.Count > 0)
                {
                    rows.AddRange(playlistMissingRows);
                }
                else
                {
                    rows.Add(new MissingMemberRow
                    {
                        Key = "synthetic_" + playlistId,
                        PlaylistName = displayPlaylistName,
                        MemberName = "No missing members",
                        Path = string.Empty,
                        DetectedAt = string.Empty,
                        Forget = false,
                        IsSynthetic = true,
                        Candidates = new CandidateRow[0]
                    });
                }
            }

            _logger.Info("[MissingMembersPageView] BuildRows — emitting {0} row(s)", rows.Count);
            return rows.ToArray();
        }

        private CandidateRow[] BuildCandidateRows(string playlistId, long missingInternalId, List<CandidateEntry> allCandidates)
        {
            var rows = new List<CandidateRow>();

            foreach (var c in allCandidates)
            {
                if (c.PlaylistId != playlistId) continue;
                if (c.MissingMember?.InternalId != missingInternalId) continue;

                rows.Add(new CandidateRow
                {
                    Key = playlistId + "_" + missingInternalId + "_" + c.CandidateInternalId,
                    CandidateName = c.CandidateName ?? "(unnamed)",
                    CandidatePath = c.CandidatePath ?? string.Empty,
                    Score = c.Score,
                    Signals = string.Join(", ", c.MatchedSignals ?? new List<string>()),
                    Repair = false
                });
            }

            rows.Sort((a, b) => b.Score.CompareTo(a.Score));

            _logger.Info(
                "[MissingMembersPageView] BuildCandidateRows — playlist={0} | missingId={1} | candidates={2}",
                playlistId, missingInternalId, rows.Count);

            return rows.ToArray();
        }
    }
}