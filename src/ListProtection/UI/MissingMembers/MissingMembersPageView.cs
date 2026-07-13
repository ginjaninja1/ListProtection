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

        public override async Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
        {
            _logger.Info(
                "[MissingMembersPageView] RunCommand | commandId={0} | itemId={1}",
                commandId ?? "(null)",
                itemId ?? "(null)");

            // Log raw payload so we can inspect UI state on every round-trip
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
                        await ProcessRepairs(repairUi.MissingMemberRows);

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

        // ── Repair ─────────────────────────────────────────────────────────

        private async Task ProcessRepairs(MissingMemberRow[] rows)
        {
            var user = _userManager.GetUserList(new UserQuery())[0];
            _logger.Info("[MissingMembersPageView] ProcessRepairs — user={0}", user.Name);

            var missingRecords = _missingMembersStore.Load();
            var candidateRecords = ListProtectionPlugin.Instance.CandidateStore.Load();
            var groundTruth = _groundTruthStore.Load();
            var protectedIds = _playlistStore.Load();

            // Collect repaired candidates grouped by PlaylistId
            // Each entry: playlistId -> list of (missingInternalId, candidateInternalId)
            var repairsByPlaylist = new Dictionary<string, List<(long missingInternalId, long candidateInternalId)>>(StringComparer.OrdinalIgnoreCase);

            foreach (var masterRow in rows)
            {
                if (masterRow.IsSynthetic) continue;
                if (masterRow.Candidates == null) continue;

                foreach (var candidateRow in masterRow.Candidates)
                {
                    if (!candidateRow.Repair) continue;

                    // Key format: "{playlistId}_{missingInternalId}_{candidateInternalId}"
                    var parts = candidateRow.Key.Split('_');
                    if (parts.Length < 3)
                    {
                        _logger.Warn("[MissingMembersPageView] ProcessRepairs — unexpected Key format: {0}", candidateRow.Key);
                        continue;
                    }

                    var playlistId = parts[0];
                    if (!long.TryParse(parts[1], out var missingInternalId) ||
                        !long.TryParse(parts[2], out var candidateInternalId))
                    {
                        _logger.Warn("[MissingMembersPageView] ProcessRepairs — could not parse Key: {0}", candidateRow.Key);
                        continue;
                    }

                    // Confirm entry still exists in CandidateStore
                    var exists = candidateRecords.Any(c =>
                        c.PlaylistId == playlistId &&
                        c.MissingMember?.InternalId == missingInternalId &&
                        c.CandidateInternalId == candidateInternalId);

                    if (!exists)
                    {
                        _logger.Warn("[MissingMembersPageView] ProcessRepairs — CandidateEntry not found in store, skipping Key: {0}", candidateRow.Key);
                        continue;
                    }

                    if (!repairsByPlaylist.ContainsKey(playlistId))
                        repairsByPlaylist[playlistId] = new List<(long, long)>();

                    repairsByPlaylist[playlistId].Add((missingInternalId, candidateInternalId));
                }
            }

            if (repairsByPlaylist.Count == 0)
            {
                _logger.Info("[MissingMembersPageView] ProcessRepairs — no repair candidates selected");
                return;
            }

            _logger.Info("[MissingMembersPageView] ProcessRepairs — {0} playlist(s) to repair", repairsByPlaylist.Count);

            var missingChanged = false;
            var candidatesChanged = false;

            foreach (var kvp in repairsByPlaylist)
            {
                var oldPlaylistId = kvp.Key;
                var repairs = kvp.Value; // list of (missingInternalId, candidateInternalId)
                var candidateItemIds = repairs.Select(r => r.candidateInternalId).ToArray();

                // Look up playlist name from GroundTruthStore
                var playlistName = "(unknown)";
                if (groundTruth.TryGetValue(oldPlaylistId, out var oldGtEntry))
                    playlistName = oldGtEntry.PlaylistName ?? "(unknown)";

                _logger.Info(
                    "[MissingMembersPageView] ProcessRepairs — playlist='{0}' | repairing {1} member(s) | oldId={2}",
                    playlistName, repairs.Count, oldPlaylistId);

                // ── Check if playlist already exists in Emby ───────────────────────────────────
                // If a previous repair already recreated it, use AddToPlaylist instead
                string activePlaylistId = null;
                long activeInternalId = 0;

                if (!Guid.TryParseExact(oldPlaylistId, "N", out var oldGuid))
                {
                    _logger.Warn("[MissingMembersPageView] ProcessRepairs — could not parse oldPlaylistId as Guid: {0}", oldPlaylistId);
                    continue;
                }

                var existingPlaylist = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { "Playlist" }
                }).FirstOrDefault(p => p.Id == oldGuid);

                if (existingPlaylist != null)
                {
                    // Playlist still exists — use AddToPlaylist
                    activePlaylistId = oldPlaylistId;
                    activeInternalId = existingPlaylist.InternalId;

                    _logger.Info(
                        "[MissingMembersPageView] ProcessRepairs — playlist exists (InternalId={0}), using AddToPlaylist",
                        activeInternalId);

                    try
                    {
                        _playlistManager.AddToPlaylist(
                            existingPlaylist as MediaBrowser.Controller.Playlists.Playlist,
                            candidateItemIds,
                            skipDuplicates: true,
                            user: user,
                            cancellationToken: System.Threading.CancellationToken.None);

                        _logger.Info(
                            "[MissingMembersPageView] ProcessRepairs — AddToPlaylist succeeded | {0} item(s) added",
                            candidateItemIds.Length);
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException(
                            "[MissingMembersPageView] ProcessRepairs — AddToPlaylist failed for '{0}'", ex, playlistName);
                        continue;
                    }
                }
                else
                {
                    // Playlist is gone — recreate with CreatePlaylist
                    _logger.Info(
                        "[MissingMembersPageView] ProcessRepairs — playlist not found in Emby, calling CreatePlaylist with {0} item(s)",
                        candidateItemIds.Length);

                    PlaylistCreationResult result;
                    try
                    {
                        result = await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
                        {
                            Name = playlistName,
                            ItemIdList = candidateItemIds,
                            MediaType = "Audio",
                            User = user,
                            IsPublic = true
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException(
                            "[MissingMembersPageView] ProcessRepairs — CreatePlaylist failed for '{0}'", ex, playlistName);
                        continue;
                    }

                    _logger.Info(
                        "[MissingMembersPageView] ProcessRepairs — CreatePlaylist result | Id={0} | Name={1} | ItemAddedCount={2}",
                        result?.Id ?? "(null)", result?.Name ?? "(null)", result?.ItemAddedCount ?? -1);

                    if (result == null || string.IsNullOrEmpty(result.Id))
                    {
                        _logger.Error("[MissingMembersPageView] ProcessRepairs — null result for '{0}', skipping", playlistName);
                        continue;
                    }

                    if (!long.TryParse(result.Id, out var newInternalId))
                    {
                        _logger.Error("[MissingMembersPageView] ProcessRepairs — could not parse result.Id: {0}", result.Id);
                        continue;
                    }

                    // Resolve new Guid
                    var resolvedItems = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        ItemIds = new[] { newInternalId },
                        IncludeItemTypes = new[] { "Playlist" }
                    });

                    if (resolvedItems.Length == 0)
                    {
                        _logger.Error("[MissingMembersPageView] ProcessRepairs — could not resolve Guid for InternalId={0}", newInternalId);
                        continue;
                    }

                    var newGuidN = resolvedItems[0].Id.ToString("N");
                    activePlaylistId = newGuidN;
                    activeInternalId = newInternalId;

                    _logger.Info(
                        "[MissingMembersPageView] ProcessRepairs — resolved new GuidN={0}", newGuidN);

                    // ── Update PlaylistManagementStore ─────────────────────────────────────────
                    protectedIds.Remove(oldPlaylistId);
                    protectedIds.Add(newGuidN);
                    _playlistStore.Save(protectedIds);
                    _logger.Info(
                        "[MissingMembersPageView] ProcessRepairs — PlaylistManagementStore updated | removed={0} | added={1}",
                        oldPlaylistId, newGuidN);

                    // ── Migrate remaining missing records to new PlaylistId ─────────────────────
                    // Any missing member not being repaired now must still surface in Tab 2
                    // under the new GuidN, not the old one which no longer exists in the store.
                    var repairedMissingIds = new HashSet<long>(repairs.Select(r => r.missingInternalId));
                    var migrated = 0;
                    foreach (var record in missingRecords)
                    {
                        if (record.PlaylistId != oldPlaylistId) continue;
                        if (repairedMissingIds.Contains(record.Member?.InternalId ?? -1)) continue;
                        record.PlaylistId = newGuidN;
                        record.PlaylistName = playlistName;
                        migrated++;
                    }
                    if (migrated > 0)
                    {
                        missingChanged = true;
                        _logger.Info(
                            "[MissingMembersPageView] ProcessRepairs — migrated {0} remaining missing record(s) to new GuidN={1}",
                            migrated, newGuidN);
                    }

                    // ── Migrate remaining CandidateEntries to new PlaylistId ───────────────────
                    var migratedCandidates = 0;
                    foreach (var c in candidateRecords)
                    {
                        if (c.PlaylistId != oldPlaylistId) continue;
                        if (repairedMissingIds.Contains(c.MissingMember?.InternalId ?? -1)) continue;
                        c.PlaylistId = newGuidN;
                        c.PlaylistName = playlistName;
                        migratedCandidates++;
                    }
                    if (migratedCandidates > 0)
                    {
                        candidatesChanged = true;
                        _logger.Info(
                            "[MissingMembersPageView] ProcessRepairs — migrated {0} candidate(s) to new GuidN={1}",
                            migratedCandidates, newGuidN);
                    }

                    // ── Build new GroundTruthEntry via direct capture ──────────────────────────
                    // Write it ourselves — event chain cannot be relied on for recreation case.
                    var capturedMembers = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        ListIds = new[] { activeInternalId },
                        Recursive = true
                    });

                    _logger.Info(
                        "[MissingMembersPageView] ProcessRepairs — captured {0} member(s) from new playlist",
                        capturedMembers.Length);

                    var newMembers = new List<GroundTruthMember>(capturedMembers.Length);
                    foreach (var m in capturedMembers)
                    {
                        newMembers.Add(new GroundTruthMember
                        {
                            InternalId = m.InternalId,
                            Id = m.Id.ToString("N"),
                            Name = m.Name ?? string.Empty,
                            Path = m.Path ?? string.Empty,
                            ListItemEntryId = m.ListItemEntryId
                        });
                        _logger.Info(
                            "[MissingMembersPageView] ProcessRepairs — ground truth member '{0}' | InternalId={1} | ListItemEntryId={2}",
                            m.Name ?? "(null)", m.InternalId, m.ListItemEntryId);
                    }

                    // Carry forward unrepaired members from old ground truth so they remain tracked
                    if (oldGtEntry?.Members != null)
                    {
                        foreach (var oldMember in oldGtEntry.Members)
                        {
                            if (repairedMissingIds.Contains(oldMember.InternalId)) continue;
                            // Don't duplicate if already captured from live playlist
                            if (newMembers.Any(m => m.InternalId == oldMember.InternalId)) continue;
                            newMembers.Add(oldMember);
                            _logger.Info(
                                "[MissingMembersPageView] ProcessRepairs — carried forward unrepaired member '{0}' to new ground truth",
                                oldMember.Name ?? "(null)");
                        }
                    }

                    groundTruth[newGuidN] = new GroundTruthEntry
                    {
                        PlaylistName = playlistName,
                        CapturedAt = DateTime.UtcNow,
                        IsActive = true,
                        Members = newMembers
                    };

                    if (groundTruth.ContainsKey(oldPlaylistId))
                        groundTruth.Remove(oldPlaylistId);

                    _groundTruthStore.Save(groundTruth);
                    _logger.Info(
                        "[MissingMembersPageView] ProcessRepairs — GroundTruthStore saved | GuidN={0} | members={1}",
                        newGuidN, newMembers.Count);
                }

                // ── Remove repaired MissingMemberEntries only ──────────────────────────────────
                var repairedIds = new HashSet<long>(repairs.Select(r => r.missingInternalId));
                for (var i = missingRecords.Count - 1; i >= 0; i--)
                {
                    var r = missingRecords[i];
                    // Match on current PlaylistId (may have been migrated above to activePlaylistId)
                    var matchesPlaylist = r.PlaylistId == oldPlaylistId || r.PlaylistId == activePlaylistId;
                    if (matchesPlaylist && repairedIds.Contains(r.Member?.InternalId ?? -1))
                    {
                        _logger.Info(
                            "[MissingMembersPageView] ProcessRepairs — removing missing record '{0}'",
                            r.Member?.Name ?? "(null)");
                        missingRecords.RemoveAt(i);
                        missingChanged = true;
                    }
                }

                // ── Remove CandidateEntries for repaired members only ──────────────────────────
                for (var i = candidateRecords.Count - 1; i >= 0; i--)
                {
                    var c = candidateRecords[i];
                    var matchesPlaylist = c.PlaylistId == oldPlaylistId || c.PlaylistId == activePlaylistId;
                    if (matchesPlaylist && repairedIds.Contains(c.MissingMember?.InternalId ?? -1))
                    {
                        candidateRecords.RemoveAt(i);
                        candidatesChanged = true;
                    }
                }

                _logger.Info(
                    "[MissingMembersPageView] ProcessRepairs — playlist '{0}' repair complete | activeId={1}",
                    playlistName, activePlaylistId);
            }

            // ── Persist ────────────────────────────────────────────────────────────────────────

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

            _logger.Info("[MissingMembersPageView] ProcessRepairs complete");
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
                playlistId, missingInternalId, rows.Count);

            return rows.ToArray();
        }
    }
}