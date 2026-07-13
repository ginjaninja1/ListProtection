using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ListProtection.Tasks
{
    public class PlaylistRecreationProbeTask : IScheduledTask
    {
        private readonly IPlaylistManager _playlistManager;
        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        public string Name => "List Protection — Playlist Recreation Probe";
        public string Key => "ListProtectionPlaylistRecreationProbe";
        public string Description => "Probe: creates a test playlist then resolves its Guid and confirms store timing for Task 8.";
        public string Category => "List Protection";

        public PlaylistRecreationProbeTask(
            IPlaylistManager playlistManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            ILogManager logManager)
        {
            _playlistManager = playlistManager;
            _userManager = userManager;
            _libraryManager = libraryManager;
            _logger = logManager.GetLogger(nameof(PlaylistRecreationProbeTask));
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("[PlaylistRecreationProbe] Starting");

            var user = _userManager.GetUserList(new UserQuery())[0];
            _logger.Info("[PlaylistRecreationProbe] User: {0} | InternalId={1}", user.Name, user.InternalId);

            // Pick the top-scored candidate per missing member as seed items
            var candidates = ListProtectionPlugin.Instance.CandidateStore.Load();
            var topCandidates = candidates
                .Where(c => c.Score >= 180)
                .GroupBy(c => new { c.PlaylistId, MissingId = c.MissingMember?.InternalId })
                .Select(g => g.OrderByDescending(c => c.Score).First())
                .ToList();

            var itemIds = topCandidates.Select(c => c.CandidateInternalId).ToArray();
            _logger.Info("[PlaylistRecreationProbe] Seeding with {0} item(s)", itemIds.Length);

            // ── PROBE 2 setup — update PlaylistManagementStore BEFORE CreatePlaylist ──────────
            // We cannot know the new Guid yet, so we do this in two parts:
            //   Part A: record the pre-creation time so we can confirm store timing in logs
            //   Part B: update the store immediately after getting result.Id (see below)
            var preCreationTime = DateTime.UtcNow;
            _logger.Info("[PlaylistRecreationProbe] Pre-creation timestamp: {0:O}", preCreationTime);

            // ── CreatePlaylist ─────────────────────────────────────────────────────────────────
            var result = await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
            {
                Name = "ProbePlaylist_Task8",
                ItemIdList = itemIds,
                MediaType = "Audio",
                User = user,
                IsPublic = true
            });

            _logger.Info(
                "[PlaylistRecreationProbe] CreatePlaylist result | Id={0} | Name={1} | ItemAddedCount={2}",
                result?.Id ?? "(null)",
                result?.Name ?? "(null)",
                result?.ItemAddedCount ?? -1);

            if (result == null || string.IsNullOrEmpty(result.Id))
            {
                _logger.Error("[PlaylistRecreationProbe] result or result.Id was null — aborting probe");
                progress.Report(100);
                return;
            }

            // ── PROBE 1 — Resolve new Guid from InternalId ────────────────────────────────────
            // result.Id is an InternalId string (e.g. "1358178"), NOT a Guid.
            // Resolve via GetItemList using ItemIds (long[]).
            if (!long.TryParse(result.Id, out var newInternalId))
            {
                _logger.Error("[PlaylistRecreationProbe] Could not parse result.Id as long: {0}", result.Id);
                progress.Report(100);
                return;
            }

            var resolvedItems = _libraryManager.GetItemList(new InternalItemsQuery
            {
                ItemIds = new[] { newInternalId },
                IncludeItemTypes = new[] { "Playlist" }
            });

            _logger.Info("[PlaylistRecreationProbe] PROBE 1 — GetItemList result count: {0}", resolvedItems.Length);

            if (resolvedItems.Length == 0)
            {
                _logger.Error("[PlaylistRecreationProbe] PROBE 1 FAILED — no item resolved for InternalId={0}", newInternalId);
                progress.Report(100);
                return;
            }

            var newItem = resolvedItems[0];
            var newGuid = newItem.Id;
            var newGuidN = newGuid.ToString("N");

            _logger.Info(
                "[PlaylistRecreationProbe] PROBE 1 PROVEN | InternalId={0} | Guid={1} | GuidN={2} | Name={3}",
                newInternalId,
                newGuid.ToString(),
                newGuidN,
                newItem.Name ?? "(null)");

            // ── PROBE 2 — Update PlaylistManagementStore with new Guid immediately ─────────────
            // In production ProcessRepairs this update must happen before any further awaits
            // so the PlaylistMaintenanceService event chain sees the new playlist as protected.
            // Here we do it right after Guid resolution and log the timing delta.
            var postCreationTime = DateTime.UtcNow;
            var elapsed = (postCreationTime - preCreationTime).TotalMilliseconds;
            _logger.Info(
                "[PlaylistRecreationProbe] PROBE 2 — Updating PlaylistManagementStore with new GuidN={0} | ElapsedMs={1:F0}",
                newGuidN,
                elapsed);

            var protectedIds = ListProtectionPlugin.Instance.PlaylistStore.Load();
            protectedIds.Add(newGuidN);
            ListProtectionPlugin.Instance.PlaylistStore.Save(protectedIds);

            _logger.Info(
                "[PlaylistRecreationProbe] PROBE 2 — PlaylistManagementStore updated. Now contains {0} id(s). New GuidN present: {1}",
                protectedIds.Count,
                protectedIds.Contains(newGuidN));

            // Wait to allow the PlaylistItemsAdded -> ItemUpdated event chain to fire
            // and (if the store update was in time) write the new GroundTruthStore entry.
            _logger.Info("[PlaylistRecreationProbe] Waiting 5s for event chain to settle...");
            await Task.Delay(5000, cancellationToken);

            var groundTruth = ListProtectionPlugin.Instance.GroundTruthStore.Load();
            var groundTruthEntry = groundTruth.ContainsKey(newGuidN) ? groundTruth[newGuidN] : null;

            if (groundTruthEntry != null)
            {
                _logger.Info(
                    "[PlaylistRecreationProbe] PROBE 2 PROVEN — GroundTruthStore entry found for new GuidN={0} | PlaylistName={1} | Members={2}",
                    newGuidN,
                    groundTruthEntry.PlaylistName ?? "(null)",
                    groundTruthEntry.Members?.Count ?? 0);
            }
            else
            {
                _logger.Warn(
                    "[PlaylistRecreationProbe] PROBE 2 UNCONFIRMED — no GroundTruthStore entry for new GuidN={0} after 5s. " +
                    "Event chain may not have fired, or PlaylistMaintenanceService guard check failed. " +
                    "Check server log for PlaylistMaintenanceService entries around this time.",
                    newGuidN);
            }

            _logger.Info("[PlaylistRecreationProbe] Done — check Emby UI and server logs");
            progress.Report(100);
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
            => Array.Empty<TaskTriggerInfo>();
    }
}