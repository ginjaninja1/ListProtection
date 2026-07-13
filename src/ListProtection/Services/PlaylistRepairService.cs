using ListProtection.Storage;
using ListProtection.UI.MissingMembers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ListProtection.Services
{
    /// <summary>
    /// Shared repair logic extracted from MissingMembersPageView so that
    /// Tab 1 (Repair All) and Tab 2 (per-member Repair) can both call it.
    ///
    /// Single entry point: ExecuteRepairs(MissingMemberRow[]).
    /// Callers assemble MissingMemberRow[] with Repair=true on the candidates
    /// they want applied. This class handles AddToPlaylist / CreatePlaylist,
    /// store updates, and ground truth maintenance — exactly as Task 8 proved.
    /// </summary>
    public class PlaylistRepairService
    {
        private readonly MissingMembersStore _missingMembersStore;
        private readonly GroundTruthStore _groundTruthStore;
        private readonly PlaylistManagementStore _playlistStore;
        private readonly ILibraryManager _libraryManager;
        private readonly IPlaylistManager _playlistManager;
        private readonly IUserManager _userManager;
        private readonly ILogger _logger;

        public PlaylistRepairService(
            MissingMembersStore missingMembersStore,
            GroundTruthStore groundTruthStore,
            PlaylistManagementStore playlistStore,
            ILibraryManager libraryManager,
            IPlaylistManager playlistManager,
            IUserManager userManager,
            ILogger logger)
        {
            _missingMembersStore = missingMembersStore;
            _groundTruthStore = groundTruthStore;
            _playlistStore = playlistStore;
            _libraryManager = libraryManager;
            _playlistManager = playlistManager;
            _userManager = userManager;
            _logger = logger;
        }

        /// <summary>
        /// Executes repairs for all MissingMemberRows that have at least one
        /// CandidateRow with Repair=true.
        /// Synthetic rows (IsSynthetic=true) are skipped.
        /// </summary>
        public async Task ExecuteRepairs(MissingMemberRow[] rows)
        {
            var user = _userManager.GetUserList(new UserQuery())[0];
            _logger.Info("[PlaylistRepairService] ExecuteRepairs — user={0}", user.Name);

            var missingRecords = _missingMembersStore.Load();
            var candidateRecords = ListProtectionPlugin.Instance.CandidateStore.Load();
            var groundTruth = _groundTruthStore.Load();
            var protectedIds = _playlistStore.Load();

            // Collect repaired candidates grouped by PlaylistId
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
                        _logger.Warn("[PlaylistRepairService] Unexpected candidate Key format: {0}", candidateRow.Key);
                        continue;
                    }

                    var playlistId = parts[0];
                    if (!long.TryParse(parts[1], out var missingInternalId) ||
                        !long.TryParse(parts[2], out var candidateInternalId))
                    {
                        _logger.Warn("[PlaylistRepairService] Could not parse candidate Key: {0}", candidateRow.Key);
                        continue;
                    }

                    var exists = candidateRecords.Any(c =>
                        c.PlaylistId == playlistId &&
                        c.MissingMember?.InternalId == missingInternalId &&
                        c.CandidateInternalId == candidateInternalId);

                    if (!exists)
                    {
                        _logger.Warn("[PlaylistRepairService] CandidateEntry not found in store, skipping Key: {0}", candidateRow.Key);
                        continue;
                    }

                    if (!repairsByPlaylist.ContainsKey(playlistId))
                        repairsByPlaylist[playlistId] = new List<(long, long)>();

                    repairsByPlaylist[playlistId].Add((missingInternalId, candidateInternalId));
                }
            }

            if (repairsByPlaylist.Count == 0)
            {
                _logger.Info("[PlaylistRepairService] No repair candidates selected");
                return;
            }

            _logger.Info("[PlaylistRepairService] {0} playlist(s) to repair", repairsByPlaylist.Count);

            var missingChanged = false;
            var candidatesChanged = false;
            var groundTruthChanged = false;

            foreach (var kvp in repairsByPlaylist)
            {
                var oldPlaylistId = kvp.Key;
                var repairs = kvp.Value;
                var candidateItemIds = repairs.Select(r => r.candidateInternalId).ToArray();
                var repairedMissingIds = new HashSet<long>(repairs.Select(r => r.missingInternalId));

                var playlistName = "(unknown)";
                groundTruth.TryGetValue(oldPlaylistId, out var oldGtEntry);
                if (oldGtEntry != null)
                    playlistName = oldGtEntry.PlaylistName ?? "(unknown)";

                _logger.Info(
                    "[PlaylistRepairService] Playlist='{0}' | repairing {1} member(s) | oldId={2}",
                    playlistName, repairs.Count, oldPlaylistId);

                if (!Guid.TryParseExact(oldPlaylistId, "N", out var oldGuid))
                {
                    _logger.Warn("[PlaylistRepairService] Could not parse oldPlaylistId as Guid: {0}", oldPlaylistId);
                    continue;
                }

                var allPlaylists = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { "Playlist" },
                    Recursive = true
                });

                string activePlaylistId;
                long activeInternalId;

                var existingPlaylist = allPlaylists?.FirstOrDefault(p => p.Id == oldGuid);

                if (existingPlaylist != null)
                {
                    // ── Playlist exists — AddToPlaylist ────────────────────
                    activePlaylistId = oldPlaylistId;
                    activeInternalId = existingPlaylist.InternalId;

                    _logger.Info(
                        "[PlaylistRepairService] Playlist exists | Name='{0}' | InternalId={1} | using AddToPlaylist",
                        existingPlaylist.Name ?? "(unnamed)", activeInternalId);

                    try
                    {
                        await _playlistManager.AddToPlaylist(
                            existingPlaylist as MediaBrowser.Controller.Playlists.Playlist,
                            candidateItemIds,
                            skipDuplicates: true,
                            user: user,
                            cancellationToken: System.Threading.CancellationToken.None);

                        _logger.Info("[PlaylistRepairService] AddToPlaylist succeeded | {0} item(s) added", candidateItemIds.Length);
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException("[PlaylistRepairService] AddToPlaylist failed for '{0}'", ex, playlistName);
                        continue;
                    }

                    var liveMembers = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        ListIds = new[] { activeInternalId },
                        Recursive = true
                    });

                    var updatedMembers = new List<GroundTruthMember>(liveMembers.Length);
                    foreach (var m in liveMembers)
                    {
                        updatedMembers.Add(new GroundTruthMember
                        {
                            InternalId = m.InternalId,
                            Id = m.Id.ToString("N"),
                            Name = m.Name ?? string.Empty,
                            Path = m.Path ?? string.Empty,
                            ListItemEntryId = m.ListItemEntryId
                        });
                    }

                    if (oldGtEntry?.Members != null)
                    {
                        foreach (var oldMember in oldGtEntry.Members)
                        {
                            if (repairedMissingIds.Contains(oldMember.InternalId)) continue;
                            if (updatedMembers.Any(m => m.InternalId == oldMember.InternalId)) continue;
                            updatedMembers.Add(oldMember);
                        }
                    }

                    groundTruth[activePlaylistId] = new GroundTruthEntry
                    {
                        PlaylistName = playlistName,
                        CapturedAt = DateTime.UtcNow,
                        IsActive = true,
                        Members = updatedMembers
                    };
                    groundTruthChanged = true;

                    _logger.Info(
                        "[PlaylistRepairService] Ground truth updated | GuidN={0} | members={1}",
                        activePlaylistId, updatedMembers.Count);
                }
                else
                {
                    // ── Playlist is gone — CreatePlaylist ──────────────────
                    _logger.Info(
                        "[PlaylistRepairService] Playlist GuidN={0} not found | calling CreatePlaylist with {1} item(s)",
                        oldPlaylistId, candidateItemIds.Length);

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
                        _logger.ErrorException("[PlaylistRepairService] CreatePlaylist failed for '{0}'", ex, playlistName);
                        continue;
                    }

                    _logger.Info(
                        "[PlaylistRepairService] CreatePlaylist result | Id={0} | Name={1} | ItemAddedCount={2}",
                        result?.Id ?? "(null)", result?.Name ?? "(null)", result?.ItemAddedCount ?? -1);

                    if (result == null || string.IsNullOrEmpty(result.Id))
                    {
                        _logger.Error("[PlaylistRepairService] Null result for '{0}', skipping", playlistName);
                        continue;
                    }

                    if (!long.TryParse(result.Id, out var newInternalId))
                    {
                        _logger.Error("[PlaylistRepairService] Could not parse result.Id: {0}", result.Id);
                        continue;
                    }

                    var resolvedItems = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        ItemIds = new[] { newInternalId },
                        IncludeItemTypes = new[] { "Playlist" }
                    });

                    if (resolvedItems.Length == 0)
                    {
                        _logger.Error("[PlaylistRepairService] Could not resolve Guid for InternalId={0}", newInternalId);
                        continue;
                    }

                    var newGuidN = resolvedItems[0].Id.ToString("N");
                    activePlaylistId = newGuidN;
                    activeInternalId = newInternalId;

                    _logger.Info(
                        "[PlaylistRepairService] New playlist | GuidN={0} | InternalId={1}",
                        newGuidN, newInternalId);

                    protectedIds.Remove(oldPlaylistId);
                    protectedIds.Add(newGuidN);
                    _playlistStore.Save(protectedIds);

                    var migrated = 0;
                    foreach (var record in missingRecords)
                    {
                        if (record.PlaylistId != oldPlaylistId) continue;
                        if (repairedMissingIds.Contains(record.Member?.InternalId ?? -1)) continue;
                        record.PlaylistId = newGuidN;
                        record.PlaylistName = playlistName;
                        migrated++;
                    }
                    if (migrated > 0) missingChanged = true;

                    var migratedCandidates = 0;
                    foreach (var c in candidateRecords)
                    {
                        if (c.PlaylistId != oldPlaylistId) continue;
                        if (repairedMissingIds.Contains(c.MissingMember?.InternalId ?? -1)) continue;
                        c.PlaylistId = newGuidN;
                        c.PlaylistName = playlistName;
                        migratedCandidates++;
                    }
                    if (migratedCandidates > 0) candidatesChanged = true;

                    var capturedMembers = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        ListIds = new[] { activeInternalId },
                        Recursive = true
                    });

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
                    }

                    if (oldGtEntry?.Members != null)
                    {
                        foreach (var oldMember in oldGtEntry.Members)
                        {
                            if (repairedMissingIds.Contains(oldMember.InternalId)) continue;
                            if (newMembers.Any(m => m.InternalId == oldMember.InternalId)) continue;
                            newMembers.Add(oldMember);
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

                    groundTruthChanged = true;

                    _logger.Info(
                        "[PlaylistRepairService] GroundTruthStore entry written | GuidN={0} | members={1}",
                        newGuidN, newMembers.Count);
                }

                // Remove repaired missing records
                for (var i = missingRecords.Count - 1; i >= 0; i--)
                {
                    var r = missingRecords[i];
                    var matchesPlaylist = r.PlaylistId == oldPlaylistId || r.PlaylistId == activePlaylistId;
                    if (matchesPlaylist && repairedMissingIds.Contains(r.Member?.InternalId ?? -1))
                    {
                        missingRecords.RemoveAt(i);
                        missingChanged = true;
                    }
                }

                // Remove candidate entries for repaired members
                for (var i = candidateRecords.Count - 1; i >= 0; i--)
                {
                    var c = candidateRecords[i];
                    var matchesPlaylist = c.PlaylistId == oldPlaylistId || c.PlaylistId == activePlaylistId;
                    if (matchesPlaylist && repairedMissingIds.Contains(c.MissingMember?.InternalId ?? -1))
                    {
                        candidateRecords.RemoveAt(i);
                        candidatesChanged = true;
                    }
                }

                _logger.Info("[PlaylistRepairService] Repair complete | playlist='{0}' | activeId={1}", playlistName, activePlaylistId);
            }

            // Persist
            if (groundTruthChanged)
            {
                _groundTruthStore.Save(groundTruth);
                _logger.Info("[PlaylistRepairService] GroundTruthStore saved");
            }
            if (missingChanged)
            {
                _missingMembersStore.Save(missingRecords);
                _logger.Info("[PlaylistRepairService] MissingMembersStore saved");
            }
            if (candidatesChanged)
            {
                ListProtectionPlugin.Instance.CandidateStore.Save(candidateRecords);
                _logger.Info("[PlaylistRepairService] CandidateStore saved");
            }

            _logger.Info("[PlaylistRepairService] ExecuteRepairs complete");
        }
    }
}