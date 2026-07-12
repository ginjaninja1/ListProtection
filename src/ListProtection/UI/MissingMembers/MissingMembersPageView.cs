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
using System.Linq;
using System.Threading;
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
        private readonly IPlaylistManager _playlistManager;
        private readonly IUserManager _userManager;

        public MissingMembersPageView(
            PluginInfo pluginInfo,
            MissingMembersStore missingMembersStore,
            GroundTruthStore groundTruthStore,
            PlaylistManagementStore playlistStore,
            IJsonSerializer jsonSerializer,
            ILogger logger,
            ILibraryManager libraryManager,
            IPlaylistManager playlistManager,
            IUserManager userManager)
            : base(pluginInfo.Id)
        {
            _missingMembersStore = missingMembersStore;
            _groundTruthStore = groundTruthStore;
            _playlistStore = playlistStore;
            _jsonSerializer = jsonSerializer;
            _logger = logger;
            _libraryManager = libraryManager;
            _playlistManager = playlistManager;
            _userManager = userManager;

            ShowSave = false;
            ShowBack = false;

            ContentData = BuildOptions();
        }

        // ── RunCommand ─────────────────────────────────────────────────────

        public override Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
        {
            _logger.Info(
                "[MissingMembersPageView] RunCommand | commandId={0}",
                commandId ?? "(null)");

            try
            {
                if (string.IsNullOrEmpty(data))
                {
                    _logger.Warn("[MissingMembersPageView] RunCommand — data was null or empty, ignoring");
                    ContentData = BuildOptions();
                    return Task.FromResult<IPluginUIView>(this);
                }

                if (commandId == "RepairMember")
                {
                    var repairUi = _jsonSerializer.DeserializeFromString<MissingMembersUI>(data);
                    if (repairUi?.MissingMemberRows != null)
                        ProcessRepairs(repairUi.MissingMemberRows);

                    ContentData = BuildOptions();
                    return Task.FromResult<IPluginUIView>(this);
                }

                if (commandId != "ForgetMember")
                {
                    _logger.Warn("[MissingMembersPageView] RunCommand — unexpected commandId: {0}", commandId);
                    ContentData = BuildOptions();
                    return Task.FromResult<IPluginUIView>(this);
                }

                var ui = _jsonSerializer.DeserializeFromString<MissingMembersUI>(data);

                if (ui?.MissingMemberRows == null)
                {
                    _logger.Warn("[MissingMembersPageView] RunCommand — could not parse MissingMembersUI from data");
                    ContentData = BuildOptions();
                    return Task.FromResult<IPluginUIView>(this);
                }

                ProcessForgets(ui.MissingMemberRows);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[MissingMembersPageView] RunCommand failed", ex);
            }

            ContentData = BuildOptions();
            return Task.FromResult<IPluginUIView>(this);
        }

        // ── Repair ─────────────────────────────────────────────────────────

        private void ProcessRepairs(MissingMemberRow[] rows)
        {
            var user = _userManager.GetUserList(new UserQuery())[0];
            var missingRecords = _missingMembersStore.Load();
            var candidateRecords = ListProtectionPlugin.Instance.CandidateStore.Load();
            var missingChanged = false;
            var candidatesChanged = false;

            // Resolve all playlists once — cast to Playlist
            var allPlaylists = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Playlist" }
            });

            foreach (var masterRow in rows)
            {
                if (masterRow.IsSynthetic) continue;
                if (masterRow.Candidates == null) continue;

                foreach (var candidateRow in masterRow.Candidates)
                {
                    if (!candidateRow.Repair) continue;

                    var parts = candidateRow.Key.Split('_');
                    if (parts.Length < 3)
                    {
                        _logger.Warn("[MissingMembersPageView] RepairMember — unexpected Key: {0}", candidateRow.Key);
                        continue;
                    }

                    var playlistId = parts[0];
                    if (!long.TryParse(parts[1], out var missingInternalId) ||
                        !long.TryParse(parts[2], out var candidateInternalId))
                    {
                        _logger.Warn("[MissingMembersPageView] RepairMember — could not parse Key: {0}", candidateRow.Key);
                        continue;
                    }

                    // Find matching CandidateEntry to confirm the candidate exists in store
                    CandidateEntry match = null;
                    foreach (var c in candidateRecords)
                    {
                        if (c.PlaylistId == playlistId &&
                            c.MissingMember?.InternalId == missingInternalId &&
                            c.CandidateInternalId == candidateInternalId)
                        {
                            match = c;
                            break;
                        }
                    }

                    if (match == null)
                    {
                        _logger.Warn("[MissingMembersPageView] RepairMember — no CandidateEntry for Key: {0}", candidateRow.Key);
                        continue;
                    }

                    // Resolve Playlist object by Guid
                    var playlistGuid = new Guid(playlistId);
                    var playlist = allPlaylists
                        .FirstOrDefault(p => p.Id == playlistGuid) as Playlist;

                    if (playlist == null)
                    {
                        _logger.Warn(
                            "[MissingMembersPageView] RepairMember — playlist not found: {0}. Playlist recreation not yet implemented.",
                            playlistId);
                        continue;
                    }

                    _logger.Info(
                        "[MissingMembersPageView] RepairMember — adding '{0}' (InternalId={1}) to playlist '{2}'",
                        match.CandidateName,
                        candidateInternalId,
                        playlist.Name);

                    try
                    {
                        _playlistManager.AddToPlaylist(
                            playlist,
                            new long[] { candidateInternalId },
                            skipDuplicates: true,
                            user: user,
                            cancellationToken: CancellationToken.None);

                        _logger.Info(
                            "[MissingMembersPageView] RepairMember — AddToPlaylist succeeded for '{0}'",
                            match.CandidateName);

                        // Remove from MissingMembersStore
                        for (var i = missingRecords.Count - 1; i >= 0; i--)
                        {
                            var r = missingRecords[i];
                            if (r.PlaylistId == playlistId && r.Member?.InternalId == missingInternalId)
                            {
                                missingRecords.RemoveAt(i);
                                missingChanged = true;
                                break;
                            }
                        }

                        // Remove all candidates for this missing member
                        for (var i = candidateRecords.Count - 1; i >= 0; i--)
                        {
                            var c = candidateRecords[i];
                            if (c.PlaylistId == playlistId && c.MissingMember?.InternalId == missingInternalId)
                            {
                                candidateRecords.RemoveAt(i);
                                candidatesChanged = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException(
                            "[MissingMembersPageView] RepairMember — AddToPlaylist failed for '{0}'",
                            ex,
                            match.CandidateName);
                    }
                }
            }

            if (missingChanged)
            {
                _missingMembersStore.Save(missingRecords);
                _logger.Info("[MissingMembersPageView] MissingMembersStore saved after repair");
            }

            if (candidatesChanged)
            {
                ListProtectionPlugin.Instance.CandidateStore.Save(candidateRecords);
                _logger.Info("[MissingMembersPageView] CandidateStore saved after repair");
            }
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
                    playlistId,
                    internalId,
                    row.MemberName ?? "(null)");

                for (var i = missingRecords.Count - 1; i >= 0; i--)
                {
                    var r = missingRecords[i];
                    if (r.PlaylistId == playlistId && r.Member?.InternalId == internalId)
                    {
                        _logger.Info(
                            "[MissingMembersPageView] Removing missing record: '{0}' | playlist={1}",
                            r.Member?.Name ?? "(null)",
                            playlistId);

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
                                entry.Members[i].Name ?? "(null)",
                                playlistId);

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
                protectedIds.Count,
                missingRecords.Count,
                candidateRecords.Count);

            var rows = new List<MissingMemberRow>();

            foreach (var playlistId in protectedIds)
            {
                if (!groundTruth.TryGetValue(playlistId, out var entry) || !entry.IsActive)
                    continue;

                var playlistMissingRows = new List<MissingMemberRow>();

                foreach (var record in missingRecords)
                {
                    if (record.PlaylistId != playlistId) continue;
                    if (record.Member == null) continue;

                    playlistMissingRows.Add(new MissingMemberRow
                    {
                        Key = playlistId + "_" + record.Member.InternalId,
                        PlaylistName = record.PlaylistName ?? entry.PlaylistName ?? "(unnamed)",
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
                        PlaylistName = entry.PlaylistName ?? "(unnamed)",
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
                playlistId,
                missingInternalId,
                rows.Count);

            return rows.ToArray();
        }
    }
}