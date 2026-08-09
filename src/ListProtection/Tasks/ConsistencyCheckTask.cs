using ListProtection.EntryPoints;
using ListProtection.Services;
using ListProtection.Storage;
using System.Linq;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ListProtection.Tasks
{
    public class ConsistencyCheckTask : ILibraryPostScanTask, IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IPlaylistManager _playlistManager;
        private readonly ICollectionManager _collectionManager;
        private readonly IUserManager _userManager;
        private readonly ILogger _logger;

        public string Name => "List Protection — Consistency Check";
        public string Key => "ListProtectionConsistencyCheck";
        public string Description => "Detects missing playlist members, discovers replacement candidates, and auto-repairs where eligible. Runs after every library scan and daily at 03:00.";
        public string Category => "GinjaNinja Tools";

        public ConsistencyCheckTask(
            ILibraryManager libraryManager,
            IPlaylistManager playlistManager,
            ICollectionManager collectionManager,
            IUserManager userManager,
            ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _playlistManager = playlistManager;
            _collectionManager = collectionManager;
            _userManager = userManager;
            _logger = logManager.GetLogger("List Protection");
        }

        public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.Debug("[ConsistencyCheckTask] Post-scan trigger");
            return RunPipeline(progress, cancellationToken, ConsistencyCheckTrigger.PostScan);
        }

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Debug("[ConsistencyCheckTask] Scheduled/manual trigger");
            return RunPipeline(progress, cancellationToken, ConsistencyCheckTrigger.ScheduledOrManual);
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type           = TaskTriggerInfo.TriggerDaily,
                    TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
                }
            };
        }

        /// <summary>
        /// DIAGNOSTIC. Logs the per-field identity breakdown (via MemberIdentityLogger)
        /// for every member of every ground truth entry, every run. Purpose: at any
        /// point in time, a scan of the log tells you exactly what the plugin knows to
        /// be true about each protected member — no guessing, no waiting for a repair
        /// scenario to find out what fields a given type actually populates.
        /// </summary>
        private void LogAllMemberIdentities()
        {
            var entries = ListProtectionPlugin.Instance.GroundTruthStore.Load();
            int memberCount = 0;

            foreach (var kvp in entries)
            {
                var entry = kvp.Value;
                var tag = string.Equals(entry.ListType, "Collection", StringComparison.OrdinalIgnoreCase)
                    ? "[ConsistencyCheckTask][MemberIdentity][Collection]"
                    : "[ConsistencyCheckTask][MemberIdentity][Playlist]";

                foreach (var member in entry.Members ?? Enumerable.Empty<GroundTruthMember>())
                {
                    MemberIdentityLogger.LogIdentity(member, _logger, $"{tag}[{entry.ListName}]");
                    memberCount++;
                }
            }

            _logger.Debug(
                "[ConsistencyCheckTask] Member identity logged for {0} list(s), {1} member(s)",
                entries.Count, memberCount);
        }

        private async Task RunPipeline(IProgress<double> progress, CancellationToken cancellationToken, ConsistencyCheckTrigger trigger)
        {
            try
            {
                progress?.Report(0);

                _logger.Debug("[ConsistencyCheckTask] Step 1/4 — Logging ground truth member identity");
                LogAllMemberIdentities();
                progress?.Report(20);

                cancellationToken.ThrowIfCancellationRequested();

                _logger.Debug("[ConsistencyCheckTask] Step 2/4 — Detecting missing members");
                MissingMemberDetector.RunDetection(null, _libraryManager, _logger);
                progress?.Report(45);

                cancellationToken.ThrowIfCancellationRequested();

                _logger.Debug("[ConsistencyCheckTask] Step 3/4 — Discovering candidates");
                CandidateDiscoverer.RunDiscovery(null, _libraryManager, _logger);
                progress?.Report(70);

                cancellationToken.ThrowIfCancellationRequested();

                _logger.Debug("[ConsistencyCheckTask] Step 4/4 — Running auto-repair");
                await AutoRepairer.RunAutoRepair(
                    null,
                    _libraryManager,
                    _playlistManager,
                    _collectionManager,
                    _userManager,
                    _logger);

                ListProtectionPlugin.Instance.ConsistencyStatusStore.Save(DateTime.UtcNow, trigger);

                progress?.Report(100);
                _logger.Info("[List Protection] Consistency check complete");
            }
            catch (OperationCanceledException)
            {
                _logger.Debug("[ConsistencyCheckTask] Cancelled");
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[ConsistencyCheckTask] Pipeline failed", ex);
            }
        }
    }
}