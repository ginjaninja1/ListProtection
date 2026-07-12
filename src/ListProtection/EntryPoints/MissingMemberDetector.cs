using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;

namespace ListProtection.EntryPoints
{
    /// <summary>
    /// Shared detection logic called by both the timer path
    /// (MissingMemberDetectionService) and the scheduled task
    /// (DetectMissingMembersTask).
    ///
    /// Static to avoid any temptation to hold state here — all state
    /// lives in the stores accessed via ListProtectionPlugin.Instance.
    /// </summary>
    internal static class MissingMemberDetector
    {
        /// <summary>
        /// Compares ground truth against live playlist membership.
        /// Any ground truth member absent from the live playlist is recorded as missing.
        /// Deduplicates against existing MissingMembersStore records.
        ///
        /// If targetPlaylistIdN is null, runs for all active ground truth entries.
        /// If targetPlaylistIdN is provided, runs only for that playlist (fast path).
        /// </summary>
        internal static void RunDetection(
            string targetPlaylistIdN,
            ILibraryManager libraryManager,
            ILogger logger)
        {
            logger.Info(
                "[MissingMemberDetector] RunDetection starting | target={0}",
                targetPlaylistIdN ?? "ALL");

            try
            {
                var plugin = ListProtectionPlugin.Instance;
                if (plugin == null)
                {
                    logger.Error("[MissingMemberDetector] Plugin instance is null — aborting detection");
                    return;
                }

                var groundTruth = plugin.GroundTruthStore.Load();
                var missing = plugin.MissingMembersStore.Load();
                var changed = false;

                foreach (var kvp in groundTruth)
                {
                    if (!kvp.Value.IsActive)
                        continue;

                    if (targetPlaylistIdN != null && kvp.Key != targetPlaylistIdN)
                        continue;

                    var playlistIdN = kvp.Key;
                    var entry = kvp.Value;

                    if (!Guid.TryParseExact(playlistIdN, "N", out var guid))
                    {
                        logger.Warn("[MissingMemberDetector] Could not parse playlist Guid: {0}", playlistIdN);
                        continue;
                    }

                    // Resolve playlist BaseItem to get InternalId
                    var allPlaylists = libraryManager.GetItemList(new InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { "Playlist" },
                        Recursive = true
                    });

                    BaseItem playlist = null;
                    foreach (var p in allPlaylists)
                    {
                        if (p.Id == guid)
                        {
                            playlist = p;
                            break;
                        }
                    }

                    if (playlist == null)
                    {
                        logger.Warn(
                            "[MissingMemberDetector] Playlist not found in library: {0} — skipping",
                            playlistIdN);
                        continue;
                    }

                    // Get live members via ListIds pattern (PROVEN)
                    var liveMembers = libraryManager.GetItemList(new InternalItemsQuery
                    {
                        ListIds = new[] { playlist.InternalId },
                        Recursive = true
                    });

                    logger.Info(
                        "[MissingMemberDetector] Live readback for '{0}' ({1}) — {2} live member(s)",
                        entry.PlaylistName,
                        playlistIdN,
                        liveMembers?.Length ?? 0);

                    var liveIds = new HashSet<long>();
                    if (liveMembers != null)
                    {
                        foreach (var m in liveMembers)
                            liveIds.Add(m.InternalId);
                    }

                    // Compare ground truth against live
                    foreach (var member in entry.Members)
                    {
                        if (liveIds.Contains(member.InternalId))
                            continue;

                        logger.Info(
                            "[MissingMemberDetector] Member absent from live playlist: '{0}' | InternalId={1} | playlist={2}",
                            member.Name,
                            member.InternalId,
                            playlistIdN);

                        // Dedup — skip if already recorded
                        var alreadyRecorded = false;
                        foreach (var existing in missing)
                        {
                            if (existing.PlaylistId == playlistIdN
                                && existing.Member.InternalId == member.InternalId)
                            {
                                alreadyRecorded = true;
                                break;
                            }
                        }

                        if (alreadyRecorded)
                        {
                            logger.Info(
                                "[MissingMemberDetector] Already recorded as missing — skipping: '{0}' | playlist={1}",
                                member.Name,
                                playlistIdN);
                            continue;
                        }

                        missing.Add(new MissingMemberEntry
                        {
                            PlaylistId = playlistIdN,
                            PlaylistName = entry.PlaylistName,
                            DetectedAt = DateTime.UtcNow,
                            Member = member
                        });

                        logger.Info(
                            "[MissingMemberDetector] Recorded missing member: '{0}' | InternalId={1} | playlist='{2}' ({3})",
                            member.Name,
                            member.InternalId,
                            entry.PlaylistName,
                            playlistIdN);

                        changed = true;
                    }
                }

                if (changed)
                {
                    plugin.MissingMembersStore.Save(missing);
                    logger.Info("[MissingMemberDetector] Detection complete — store updated");
                }
                else
                {
                    logger.Info("[MissingMemberDetector] Detection complete — no new missing members found");
                }
            }
            catch (Exception ex)
            {
                logger.ErrorException("[MissingMemberDetector] RunDetection failed", ex);
            }
        }
    }
}