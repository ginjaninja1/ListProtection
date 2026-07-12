using ListProtection.Storage;
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
        private readonly ILogger _logger;

        public string Name => "List Protection — Playlist Recreation Probe";
        public string Key => "ListProtectionPlaylistRecreationProbe";
        public string Description => "Probe: creates two test playlists to confirm user ownership and visibility behaviour.";
        public string Category => "List Protection";

        public PlaylistRecreationProbeTask(
            IPlaylistManager playlistManager,
            IUserManager userManager,
            ILogManager logManager)
        {
            _playlistManager = playlistManager;
            _userManager = userManager;
            _logger = logManager.GetLogger(nameof(PlaylistRecreationProbeTask));
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("[PlaylistRecreationProbe] Starting");

            var user = _userManager.GetUserList(new UserQuery())[0];
            _logger.Info("[PlaylistRecreationProbe] User: {0} | InternalId={1}", user.Name, user.InternalId);

            // Get top Score=180 candidate per missing member from CandidateStore
            var candidates = ListProtectionPlugin.Instance.CandidateStore.Load();
            var topCandidates = candidates
                .Where(c => c.Score >= 180)
                .GroupBy(c => new { c.PlaylistId, MissingId = c.MissingMember?.InternalId })
                .Select(g => g.OrderByDescending(c => c.Score).First())
                .ToList();

            var itemIds = topCandidates.Select(c => c.CandidateInternalId).ToArray();

            _logger.Info("[PlaylistRecreationProbe] Seeding with {0} item(s)", itemIds.Length);

            // Playlist A — same name as the ghost
            var resultA = await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
            {
                Name = "PLaylist 1",
                ItemIdList = itemIds,
                MediaType = "Audio",
                User = user,
                IsPublic = true
            });
            _logger.Info(
                "[PlaylistRecreationProbe] Created 'PLaylist 1' | Id={0} | Name={1} | ItemAddedCount={2}",
                resultA?.Id ?? "(null)",
                resultA?.Name ?? "(null)",
                resultA?.ItemAddedCount ?? -1);

            // Playlist B — different name
            var resultB = await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
            {
                Name = "NewPlaylistTest",
                ItemIdList = itemIds,
                MediaType = "Audio",
                User = user,
                IsPublic = true
            });
            _logger.Info(
                "[PlaylistRecreationProbe] Created 'NewPlaylistTest' | Id={0} | Name={1} | ItemAddedCount={2}",
                resultB?.Id ?? "(null)",
                resultB?.Name ?? "(null)",
                resultB?.ItemAddedCount ?? -1);

            _logger.Info("[PlaylistRecreationProbe] Done — check Emby UI for both playlists under Cartman");
            progress.Report(100);
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
            => Array.Empty<TaskTriggerInfo>();
    }
}