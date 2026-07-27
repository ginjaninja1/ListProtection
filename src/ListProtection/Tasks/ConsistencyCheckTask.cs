using ListProtection.EntryPoints;
using ListProtection.Services;
using ListProtection.Storage;
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
            _logger = logManager.GetLogger(nameof(ConsistencyCheckTask));
        }

        public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.Info("[ConsistencyCheckTask] Post-scan trigger");
            return RunPipeline(progress, cancellationToken, ConsistencyCheckTrigger.PostScan);
        }

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("[ConsistencyCheckTask] Scheduled/manual trigger");
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

        private async Task RunPipeline(IProgress<double> progress, CancellationToken cancellationToken, ConsistencyCheckTrigger trigger)
        {
            try
            {
                progress?.Report(0);

                _logger.Info("[ConsistencyCheckTask] Step 1/3 — Detecting missing members");
                MissingMemberDetector.RunDetection(null, _libraryManager, _logger);
                progress?.Report(33);

                cancellationToken.ThrowIfCancellationRequested();

                _logger.Info("[ConsistencyCheckTask] Step 2/3 — Discovering candidates");
                CandidateDiscoverer.RunDiscovery(null, _libraryManager, _logger);
                progress?.Report(66);

                cancellationToken.ThrowIfCancellationRequested();

                _logger.Info("[ConsistencyCheckTask] Step 3/3 — Running auto-repair");
                await AutoRepairer.RunAutoRepair(
                    null,
                    _libraryManager,
                    _playlistManager,
                    _collectionManager,
                    _userManager,
                    _logger);

                ListProtectionPlugin.Instance.ConsistencyStatusStore.Save(DateTime.UtcNow, trigger);

                progress?.Report(100);
                _logger.Info("[ConsistencyCheckTask] Complete");
            }
            catch (OperationCanceledException)
            {
                _logger.Info("[ConsistencyCheckTask] Cancelled");
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[ConsistencyCheckTask] Pipeline failed", ex);
            }
        }
    }
}