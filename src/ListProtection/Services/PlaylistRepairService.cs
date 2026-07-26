using ListProtection.Storage;
using ListProtection.UI.MissingMembers;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ListProtection.Services
{
    /// <summary>
    /// Shared repair logic for both Playlists and Collections.
    ///
    /// Playlist repair: atomic remove-all → add-in-GT-order via IPlaylistManager.
    ///   Uses ListItemEntryId for removal. skipDuplicates=false (GT is authority).
    ///
    /// Collection repair: targeted remove-missing → add-candidate via ICollectionManager.
    ///   Collections have no user-defined order. RemoveFromCollection and AddToCollection
    ///   both take InternalIds — no ListItemEntryId involved.
    ///
    /// Branch determined by GroundTruthEntry.IsCollection.
    /// </summary>
    public class PlaylistRepairService
    {
        private readonly MissingMembersStore _missingMembersStore;
        private readonly GroundTruthStore _groundTruthStore;
        private readonly PlaylistManagementStore _playlistStore;
        private readonly ILibraryManager _libraryManager;
        private readonly IPlaylistManager _playlistManager;
        private readonly ICollectionManager _collectionManager;
        private readonly IUserManager _userManager;
        private readonly ILogger _logger;

        private struct RepairResult
        {
            public bool MissingChanged;
            public bool CandidatesChanged;
            public bool GroundTruthChanged;
        }

        public PlaylistRepairService(
            MissingMembersStore missingMembersStore,
            GroundTruthStore groundTruthStore,
            PlaylistManagementStore playlistStore,
            ILibraryManager libraryManager,
            IPlaylistManager playlistManager,
            ICollectionManager collectionManager,
            IUserManager userManager,
            ILogger logger)
        {
            _missingMembersStore = missingMembersStore;
            _groundTruthStore = groundTruthStore;
            _playlistStore = playlistStore;
            _libraryManager = libraryManager;
            _playlistManager = playlistManager;
            _collectionManager = collectionManager;
            _userManager = userManager;
            _logger = logger;
        }

        public async Task ExecuteRepairs(MissingMemberRow[] rows)
        {
            var user = _userManager.GetUserList(new UserQuery())[0];
            _logger.Info("[PlaylistRepairService] ExecuteRepairs — user={0}", user.Name);

            var plugin = ListProtectionPlugin.Instance;

            List<MissingMemberEntry> missingRecords;
            List<CandidateEntry> candidateRecords;
            Dictionary<string, GroundTruthEntry> groundTruth;
            HashSet<string> protectedIds;

            plugin.WriterLock.Wait();
            try
            {
                missingRecords = _missingMembersStore.Load();
                candidateRecords = plugin.CandidateStore.Load();
                groundTruth = _groundTruthStore.Load();
                protectedIds = _playlistStore.Load();
            }
            finally
            {
                plugin.WriterLock.Release();
            }

            var repairsByList = new Dictionary<string, List<(long missingInternalId, long candidateInternalId)>>(StringComparer.OrdinalIgnoreCase);

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
                        _logger.Warn("[PlaylistRepairService] Unexpected candidate Key format: {0}", candidateRow.Key);
                        continue;
                    }

                    var listId = parts[0];
                    if (!long.TryParse(parts[1], out var missingInternalId) ||
                        !long.TryParse(parts[2], out var candidateInternalId))
                    {
                        _logger.Warn("[PlaylistRepairService] Could not parse candidate Key: {0}", candidateRow.Key);
                        continue;
                    }

                    var exists = candidateRecords.Any(c =>
                        c.PlaylistId == listId &&
                        c.MissingMember?.InternalId == missingInternalId &&
                        c.CandidateInternalId == candidateInternalId);

                    if (!exists)
                    {
                        _logger.Warn("[PlaylistRepairService] CandidateEntry not found in store, skipping Key: {0}", candidateRow.Key);
                        continue;
                    }

                    if (!repairsByList.ContainsKey(listId))
                        repairsByList[listId] = new List<(long, long)>();

                    repairsByList[listId].Add((missingInternalId, candidateInternalId));
                }
            }

            if (repairsByList.Count == 0)
            {
                _logger.Info("[PlaylistRepairService] No repair candidates selected");
                return;
            }

            _logger.Info("[PlaylistRepairService] {0} list(s) to repair", repairsByList.Count);

            var missingChanged = false;
            var candidatesChanged = false;
            var groundTruthChanged = false;

            foreach (var kvp in repairsByList)
            {
                var oldListId = kvp.Key;
                var repairs = kvp.Value;
                var repairedMissingIds = new HashSet<long>(repairs.Select(r => r.missingInternalId));

                groundTruth.TryGetValue(oldListId, out var oldGtEntry);
                var listName = oldGtEntry?.PlaylistName ?? "(unknown)";
                var isCollection = oldGtEntry?.IsCollection ?? false;

                _logger.Info(
                    "[PlaylistRepairService] List='{0}' | Type={1} | repairing {2} member(s) | oldId={3}",
                    listName, isCollection ? "Collection" : "Playlist", repairs.Count, oldListId);

                if (!Guid.TryParseExact(oldListId, "N", out var oldGuid))
                {
                    _logger.Warn("[PlaylistRepairService] Could not parse oldListId as Guid: {0}", oldListId);
                    continue;
                }

                RepairResult result;
                if (isCollection)
                    result = await ExecuteCollectionRepair(
                        oldListId, oldGuid, oldGtEntry, listName, repairs, repairedMissingIds,
                        missingRecords, candidateRecords, groundTruth, protectedIds);
                else
                    result = await ExecutePlaylistRepair(
                        oldListId, oldGuid, oldGtEntry, listName, repairs, repairedMissingIds,
                        candidateRecords, missingRecords, groundTruth, protectedIds, user);

                if (result.MissingChanged) missingChanged = true;
                if (result.CandidatesChanged) candidatesChanged = true;
                if (result.GroundTruthChanged) groundTruthChanged = true;
            }

            plugin.WriterLock.Wait();
            try
            {
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
                    plugin.CandidateStore.Save(candidateRecords);
                    _logger.Info("[PlaylistRepairService] CandidateStore saved");
                }
            }
            finally
            {
                plugin.WriterLock.Release();
            }

            _logger.Info("[PlaylistRepairService] ExecuteRepairs complete");
        }

        // ── Collection repair ──────────────────────────────────────────────

        private async Task<RepairResult> ExecuteCollectionRepair(
            string oldListId,
            Guid oldGuid,
            GroundTruthEntry oldGtEntry,
            string listName,
            List<(long missingInternalId, long candidateInternalId)> repairs,
            HashSet<long> repairedMissingIds,
            List<MissingMemberEntry> missingRecords,
            List<CandidateEntry> candidateRecords,
            Dictionary<string, GroundTruthEntry> groundTruth,
            HashSet<string> protectedIds)
        {
            var result = default(RepairResult);

            var allCollections = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "BoxSet" },
                Recursive = true
            });

            var existingCollection = allCollections?.FirstOrDefault(c => c.Id == oldGuid) as BoxSet;

            string activeListId;

            if (existingCollection != null)
            {
                activeListId = oldListId;

                var missingToCandidate = new Dictionary<long, long>();
                foreach (var (missingId, candidateId) in repairs)
                    missingToCandidate[missingId] = candidateId;

                var removeIds = repairs.Select(r => r.missingInternalId).ToArray();
                _logger.Info("[PlaylistRepairService] Collection: removing {0} member(s) from '{1}'", removeIds.Length, listName);
                try
                {
                    _collectionManager.RemoveFromCollection(existingCollection, removeIds);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("[PlaylistRepairService] RemoveFromCollection failed for '{0}'", ex, listName);
                    return result;
                }

                var addIds = repairs.Select(r => r.candidateInternalId).ToArray();
                _logger.Info("[PlaylistRepairService] Collection: adding {0} candidate(s) to '{1}'", addIds.Length, listName);
                try
                {
                    await _collectionManager.AddToCollection(existingCollection.InternalId, addIds);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("[PlaylistRepairService] AddToCollection failed for '{0}'", ex, listName);
                    return result;
                }

                // Update GT — substitute candidates for repaired slots
                var updatedMembers = new List<GroundTruthMember>();
                if (oldGtEntry?.Members != null)
                {
                    foreach (var gtMember in oldGtEntry.Members)
                    {
                        if (missingToCandidate.TryGetValue(gtMember.InternalId, out var candidateId))
                        {
                            var liveCandidate = _libraryManager.GetItemById(candidateId);
                            updatedMembers.Add(liveCandidate != null
                                ? GroundTruthMemberFactory.FromItem(liveCandidate)
                                : new GroundTruthMember { InternalId = candidateId, Name = gtMember.Name, Path = gtMember.Path, MediaType = gtMember.MediaType });
                        }
                        else
                        {
                            updatedMembers.Add(gtMember);
                        }
                    }
                }

                groundTruth[activeListId] = new GroundTruthEntry
                {
                    ListType = "Collection",
                    PlaylistName = listName,
                    CapturedAt = DateTime.UtcNow,
                    Members = updatedMembers
                };
                result.GroundTruthChanged = true;
            }
            else
            {
                // Collection gone — recreate
                _logger.Info("[PlaylistRepairService] Collection GuidN={0} not found — calling CreateCollection", oldListId);

                var missingToCandidate = new Dictionary<long, long>();
                foreach (var (missingId, candidateId) in repairs)
                    missingToCandidate[missingId] = candidateId;

                var allMemberIds = new List<long>();
                if (oldGtEntry?.Members != null)
                {
                    foreach (var m in oldGtEntry.Members)
                        allMemberIds.Add(missingToCandidate.TryGetValue(m.InternalId, out var cid) ? cid : m.InternalId);
                }

                BoxSet newCollection;
                try
                {
                    newCollection = await _collectionManager.CreateCollection(new CollectionCreationOptions
                    {
                        Name = listName,
                        ItemIdList = allMemberIds.ToArray()
                    });
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("[PlaylistRepairService] CreateCollection failed for '{0}'", ex, listName);
                    return result;
                }

                if (newCollection == null)
                {
                    _logger.Error("[PlaylistRepairService] CreateCollection returned null for '{0}'", listName);
                    return result;
                }

                var newGuidN = newCollection.Id.ToString("N");
                activeListId = newGuidN;

                _logger.Info("[PlaylistRepairService] New collection | GuidN={0} | InternalId={1}", newGuidN, newCollection.InternalId);

                protectedIds.Remove(oldListId);
                protectedIds.Add(newGuidN);
                SaveProtectedIds(protectedIds);

                var migrateResult = MigrateStoreIds(missingRecords, candidateRecords, oldListId, newGuidN, listName, repairedMissingIds);
                result.MissingChanged |= migrateResult.missingChanged;
                result.CandidatesChanged |= migrateResult.candidatesChanged;

                var capturedMembers = newCollection.GetItemList(new InternalItemsQuery());
                var newMembers = capturedMembers.Select(m => GroundTruthMemberFactory.FromItem(m)).ToList();

                groundTruth[newGuidN] = new GroundTruthEntry
                {
                    ListType = "Collection",
                    PlaylistName = listName,
                    CapturedAt = DateTime.UtcNow,
                    Members = newMembers
                };
                if (groundTruth.ContainsKey(oldListId))
                    groundTruth.Remove(oldListId);
                result.GroundTruthChanged = true;
            }

            WriteRepairEvent(activeListId, listName, repairs, repairedMissingIds, missingRecords, candidateRecords, oldGtEntry);

            var removeResult = RemoveRepairedRecords(missingRecords, candidateRecords, oldListId, activeListId, repairedMissingIds);
            result.MissingChanged |= removeResult.missingChanged;
            result.CandidatesChanged |= removeResult.candidatesChanged;

            _logger.Info("[PlaylistRepairService] Collection repair complete | '{0}' | activeId={1}", listName, activeListId);
            return result;
        }

        // ── Playlist repair ────────────────────────────────────────────────

        private async Task<RepairResult> ExecutePlaylistRepair(
            string oldListId,
            Guid oldGuid,
            GroundTruthEntry oldGtEntry,
            string listName,
            List<(long missingInternalId, long candidateInternalId)> repairs,
            HashSet<long> repairedMissingIds,
            List<CandidateEntry> candidateRecords,
            List<MissingMemberEntry> missingRecords,
            Dictionary<string, GroundTruthEntry> groundTruth,
            HashSet<string> protectedIds,
            User user)
        {
            var result = default(RepairResult);
            var plugin = ListProtectionPlugin.Instance;

            var allPlaylists = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Playlist" },
                Recursive = true
            });

            var existingPlaylist = allPlaylists?.FirstOrDefault(p => p.Id == oldGuid);

            string activeListId;

            if (existingPlaylist != null)
            {
                activeListId = oldListId;
                var activeInternalId = existingPlaylist.InternalId;
                var activePlaylist = existingPlaylist as Playlist;

                var missingToCandidate = new Dictionary<long, long>();
                foreach (var (missingId, candidateId) in repairs)
                    missingToCandidate[missingId] = candidateId;

                // PROVEN: Playlist.GetItemList returns members in ListItemOrder (correct playlist position).
                var currentMembers = activePlaylist.GetItemList(new InternalItemsQuery());
                var currentEntryIds = currentMembers.Select(m => m.ListItemEntryId).ToArray();

                plugin.RepairSuppressedPlaylists.TryAdd(activeInternalId, 0);
                try
                {
                    if (currentEntryIds.Length > 0)
                    {
                        try
                        {
                            await _playlistManager.RemoveFromPlaylist(activePlaylist, currentEntryIds);
                            _logger.Info("[PlaylistRepairService] RemoveFromPlaylist succeeded");
                        }
                        catch (Exception ex)
                        {
                            _logger.ErrorException("[PlaylistRepairService] RemoveFromPlaylist failed for '{0}'", ex, listName);
                            return result;
                        }
                    }

                    var desiredInternalIds = new List<long>();
                    if (oldGtEntry?.Members != null)
                    {
                        foreach (var gtMember in oldGtEntry.Members)
                        {
                            if (missingToCandidate.TryGetValue(gtMember.InternalId, out var candidateId))
                                desiredInternalIds.Add(candidateId);
                            else if (!repairedMissingIds.Contains(gtMember.InternalId))
                                desiredInternalIds.Add(gtMember.InternalId);
                        }
                    }

                    try
                    {
                        await _playlistManager.AddToPlaylist(
                            activePlaylist,
                            desiredInternalIds.ToArray(),
                            skipDuplicates: false,
                            user: user,
                            cancellationToken: CancellationToken.None);
                        _logger.Info("[PlaylistRepairService] AddToPlaylist succeeded | {0} item(s)", desiredInternalIds.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException("[PlaylistRepairService] AddToPlaylist failed for '{0}'", ex, listName);
                        return result;
                    }
                }
                finally
                {
                    plugin.RepairSuppressedPlaylists.TryRemove(activeInternalId, out _);
                }

                // Update GT
                var updatedMembers = new List<GroundTruthMember>();
                if (oldGtEntry?.Members != null)
                {
                    foreach (var gtMember in oldGtEntry.Members)
                    {
                        if (missingToCandidate.TryGetValue(gtMember.InternalId, out var candidateId))
                        {
                            var liveCandidate = _libraryManager.GetItemById(candidateId);
                            updatedMembers.Add(liveCandidate != null
                                ? GroundTruthMemberFactory.FromItem(liveCandidate)
                                : new GroundTruthMember { InternalId = candidateId, Name = gtMember.Name, Path = gtMember.Path, MediaType = gtMember.MediaType });
                        }
                        else
                        {
                            updatedMembers.Add(gtMember);
                        }
                    }
                }

                groundTruth[activeListId] = new GroundTruthEntry
                {
                    ListType = "Playlist",
                    PlaylistName = listName,
                    IsPublic = oldGtEntry?.IsPublic,
                    CapturedAt = DateTime.UtcNow,
                    Members = updatedMembers
                };
                result.GroundTruthChanged = true;

                _logger.Info("[PlaylistRepairService] Ground truth updated | GuidN={0} | members={1}", activeListId, updatedMembers.Count);
            }
            else
            {
                // Playlist gone — recreate
                _logger.Info("[PlaylistRepairService] Playlist GuidN={0} not found — calling CreatePlaylist", oldListId);

                var missingToCandidate = new Dictionary<long, long>();
                foreach (var (missingId, candidateId) in repairs)
                    missingToCandidate[missingId] = candidateId;

                var candidateItemIds = BuildDesiredOrder(oldGtEntry, missingToCandidate, repairedMissingIds);

                PlaylistCreationResult createResult;
                try
                {
                    createResult = await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
                    {
                        Name = listName,
                        ItemIdList = candidateItemIds,
                        MediaType = "Audio",
                        User = user,
                        IsPublic = oldGtEntry?.IsPublic ?? true
                    });
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("[PlaylistRepairService] CreatePlaylist failed for '{0}'", ex, listName);
                    return result;
                }

                if (createResult == null || string.IsNullOrEmpty(createResult.Id))
                {
                    _logger.Error("[PlaylistRepairService] Null result for '{0}'", listName);
                    return result;
                }

                if (!long.TryParse(createResult.Id, out var newInternalId))
                {
                    _logger.Error("[PlaylistRepairService] Could not parse result.Id: {0}", createResult.Id);
                    return result;
                }

                var resolvedItems = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    ItemIds = new[] { newInternalId },
                    IncludeItemTypes = new[] { "Playlist" }
                });

                if (resolvedItems.Length == 0)
                {
                    _logger.Error("[PlaylistRepairService] Could not resolve Guid for InternalId={0}", newInternalId);
                    return result;
                }

                var newGuidN = resolvedItems[0].Id.ToString("N");
                activeListId = newGuidN;

                _logger.Info("[PlaylistRepairService] New playlist | GuidN={0} | InternalId={1}", newGuidN, newInternalId);

                protectedIds.Remove(oldListId);
                protectedIds.Add(newGuidN);
                SaveProtectedIds(protectedIds);

                var migrateResult = MigrateStoreIds(missingRecords, candidateRecords, oldListId, newGuidN, listName, repairedMissingIds);
                result.MissingChanged |= migrateResult.missingChanged;
                result.CandidatesChanged |= migrateResult.candidatesChanged;

                var newPlaylistEntity = resolvedItems[0] as Playlist;
                var capturedMembers = newPlaylistEntity != null
                    ? newPlaylistEntity.GetItemList(new InternalItemsQuery())
                    : Array.Empty<BaseItem>();

                var newMembers = capturedMembers.Select(m => GroundTruthMemberFactory.FromItem(m)).ToList();

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
                    ListType = "Playlist",
                    PlaylistName = listName,
                    IsPublic = oldGtEntry?.IsPublic,
                    CapturedAt = DateTime.UtcNow,
                    Members = newMembers
                };
                if (groundTruth.ContainsKey(oldListId))
                    groundTruth.Remove(oldListId);
                result.GroundTruthChanged = true;

                _logger.Info("[PlaylistRepairService] GroundTruthStore entry written | GuidN={0} | members={1}", newGuidN, newMembers.Count);
            }

            WriteRepairEvent(activeListId, listName, repairs, repairedMissingIds, missingRecords, candidateRecords, oldGtEntry);

            var removeResult = RemoveRepairedRecords(missingRecords, candidateRecords, oldListId, activeListId, repairedMissingIds);
            result.MissingChanged |= removeResult.missingChanged;
            result.CandidatesChanged |= removeResult.candidatesChanged;

            _logger.Info("[PlaylistRepairService] Playlist repair complete | '{0}' | activeId={1}", listName, activeListId);
            return result;
        }

        // ── Shared helpers ─────────────────────────────────────────────────

        private long[] BuildDesiredOrder(
            GroundTruthEntry oldGtEntry,
            Dictionary<long, long> missingToCandidate,
            HashSet<long> repairedMissingIds)
        {
            var ids = new List<long>();
            if (oldGtEntry?.Members == null) return ids.ToArray();
            foreach (var m in oldGtEntry.Members)
            {
                if (missingToCandidate.TryGetValue(m.InternalId, out var candidateId))
                    ids.Add(candidateId);
                else if (!repairedMissingIds.Contains(m.InternalId))
                    ids.Add(m.InternalId);
            }
            return ids.ToArray();
        }

        private (bool missingChanged, bool candidatesChanged) MigrateStoreIds(
            List<MissingMemberEntry> missingRecords,
            List<CandidateEntry> candidateRecords,
            string oldId,
            string newId,
            string listName,
            HashSet<long> repairedMissingIds)
        {
            var missingChanged = false;
            var candidatesChanged = false;

            foreach (var record in missingRecords)
            {
                if (record.PlaylistId != oldId) continue;
                if (repairedMissingIds.Contains(record.Member?.InternalId ?? -1)) continue;
                record.PlaylistId = newId;
                record.PlaylistName = listName;
                missingChanged = true;
            }
            foreach (var c in candidateRecords)
            {
                if (c.PlaylistId != oldId) continue;
                if (repairedMissingIds.Contains(c.MissingMember?.InternalId ?? -1)) continue;
                c.PlaylistId = newId;
                c.PlaylistName = listName;
                candidatesChanged = true;
            }

            return (missingChanged, candidatesChanged);
        }

        private (bool missingChanged, bool candidatesChanged) RemoveRepairedRecords(
            List<MissingMemberEntry> missingRecords,
            List<CandidateEntry> candidateRecords,
            string oldListId,
            string activeListId,
            HashSet<long> repairedMissingIds)
        {
            var missingChanged = false;
            var candidatesChanged = false;

            for (var i = missingRecords.Count - 1; i >= 0; i--)
            {
                var r = missingRecords[i];
                if ((r.PlaylistId == oldListId || r.PlaylistId == activeListId) &&
                    repairedMissingIds.Contains(r.Member?.InternalId ?? -1))
                {
                    missingRecords.RemoveAt(i);
                    missingChanged = true;
                }
            }
            for (var i = candidateRecords.Count - 1; i >= 0; i--)
            {
                var c = candidateRecords[i];
                if ((c.PlaylistId == oldListId || c.PlaylistId == activeListId) &&
                    repairedMissingIds.Contains(c.MissingMember?.InternalId ?? -1))
                {
                    candidateRecords.RemoveAt(i);
                    candidatesChanged = true;
                }
            }

            return (missingChanged, candidatesChanged);
        }

        private void WriteRepairEvent(
            string activeListId,
            string listName,
            List<(long missingInternalId, long candidateInternalId)> repairs,
            HashSet<long> repairedMissingIds,
            List<MissingMemberEntry> missingRecords,
            List<CandidateEntry> candidateRecords,
            GroundTruthEntry oldGtEntry)
        {
            try
            {
                var payloadLines = new List<string>();
                foreach (var (missingId, candidateId) in repairs)
                {
                    var candidate = candidateRecords.Find(c =>
                        c.CandidateInternalId == candidateId && c.MissingMember?.InternalId == missingId);
                    var missingRecord = missingRecords.Find(r => r.Member?.InternalId == missingId);
                    var missingName = missingRecord?.Member?.Name ?? "(unknown)";
                    var candidateName = candidate?.CandidateName ?? "(unknown)";
                    var candidatePath = candidate?.CandidatePath ?? string.Empty;
                    var pos = GetGroundTruthPosition(missingId, oldGtEntry);
                    var posPrefix = pos >= 0 ? "[POS " + (pos + 1) + "] " : string.Empty;
                    payloadLines.Add(posPrefix + missingName + " → " + candidateName + " | " + candidatePath);
                }

                ListProtectionPlugin.Instance.EventStore.Append(new EventEntry
                {
                    EventType = "Repair",
                    PlaylistId = activeListId,
                    PlaylistName = listName,
                    OccurredAt = DateTime.UtcNow,
                    Payload = string.Join("\n", payloadLines)
                });
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[PlaylistRepairService] Failed to write Repair event", ex);
            }
        }

        private void SaveProtectedIds(HashSet<string> protectedIds)
        {
            var plugin = ListProtectionPlugin.Instance;
            plugin.WriterLock.Wait();
            try { _playlistStore.Save(protectedIds); }
            finally { plugin.WriterLock.Release(); }
        }

        private static int GetGroundTruthPosition(long internalId, GroundTruthEntry gtEntry)
        {
            if (gtEntry?.Members == null || internalId <= 0) return -1;
            for (var i = 0; i < gtEntry.Members.Count; i++)
                if (gtEntry.Members[i].InternalId == internalId)
                    return i;
            return -1;
        }
    }
}