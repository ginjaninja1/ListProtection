using ListProtection.EntryPoints;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ListProtection.Tasks
{
    /// <summary>
    /// Runs missing member detection after every library scan and on a daily schedule.
    ///
    /// ILibraryPostScanTask — Emby calls Run() automatically after each library scan.
    ///   PROVEN: ILibraryPostScanTask.Run() is called post-scan (Task 12 probe).
    ///
    /// IScheduledTask — surfaces as "Detect Missing Members (Post-Scan)" in the Emby
    ///   dashboard under Scheduled Tasks. Default trigger: daily at 03:00.
    ///   Also manually triggerable from the dashboard.
    ///
    /// Delegates to MissingMemberDetector.RunDetection — same implementation used
    /// by the ItemRemoved fast path in MissingMemberDetectionService.
    ///
    /// Replaces the 60-minute timer that previously lived in MissingMemberDetectionService.
    /// </summary>
    public class PostScanDetectionTask : ILibraryPostScanTask, IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        // ── IScheduledTask identity ────────────────────────────────────────

        public string Name => "Detect Missing Playlist Members (Post-Scan)";
        public string Key => "ListProtectionPostScanDetection";
        public string Description => "Runs after every library scan and on a daily schedule. Compares protected playlist ground truth against the live library and records any missing members.";
        public string Category => "List Protection";

        public PostScanDetectionTask(
            ILibraryManager libraryManager,
            ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _logger = logManager.GetLogger(nameof(PostScanDetectionTask));
        }

        // ── ILibraryPostScanTask ───────────────────────────────────────────

        /// <summary>
        /// Called by Emby automatically after each library scan completes.
        /// </summary>
        public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.Info("[PostScanDetectionTask] Post-scan trigger — running missing member detection");
            MissingMemberDetector.RunDetection(null, _libraryManager, _logger);
            _logger.Info("[PostScanDetectionTask] Post-scan detection complete");
            return Task.CompletedTask;
        }

        // ── IScheduledTask ─────────────────────────────────────────────────

        /// <summary>
        /// Called by Emby when run manually from the dashboard or on the daily trigger.
        /// </summary>
        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            progress?.Report(0);
            _logger.Info("[PostScanDetectionTask] Scheduled/manual run — running missing member detection");
            MissingMemberDetector.RunDetection(null, _libraryManager, _logger);
            progress?.Report(100);
            _logger.Info("[PostScanDetectionTask] Scheduled detection complete");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Default trigger: daily at 03:00.
        /// Belt-and-braces sweep in case any events were missed.
        /// </summary>
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerDaily,
                    TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
                }
            };
        }
    }
}