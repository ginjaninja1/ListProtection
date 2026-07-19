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
    /// Runs candidate discovery after every library scan and on a daily schedule.
    ///
    /// ILibraryPostScanTask — Emby calls Run() automatically after each library scan.
    /// IScheduledTask — surfaces in the Emby dashboard. Default trigger: daily at 04:00.
    ///
    /// Pairs with PostScanDetectionTask (runs at 03:00) to form the full
    /// belt-and-braces sweep: detect missing members, then discover candidates.
    ///
    /// Also manually triggerable from the dashboard for testing.
    /// </summary>
    public class PostScanCandidateTask : ILibraryPostScanTask, IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        public string Name => "Discover Missing Member Candidates (Post-Scan)";
        public string Key => "ListProtectionPostScanCandidates";
        public string Description => "Runs after every library scan and on a daily schedule. Scores library Audio items against missing playlist members and writes ranked candidates to the candidate store.";
        public string Category => "List Protection";

        public PostScanCandidateTask(
            ILibraryManager libraryManager,
            ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _logger = logManager.GetLogger(nameof(PostScanCandidateTask));
        }

        // ── ILibraryPostScanTask ───────────────────────────────────────────

        public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.Info("[PostScanCandidateTask] Post-scan trigger — running candidate discovery");
            CandidateDiscoverer.RunDiscovery(null, _libraryManager, _logger);
            _logger.Info("[PostScanCandidateTask] Post-scan candidate discovery complete");
            return Task.CompletedTask;
        }

        // ── IScheduledTask ─────────────────────────────────────────────────

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            progress?.Report(0);
            _logger.Info("[PostScanCandidateTask] Scheduled/manual run — running candidate discovery");
            CandidateDiscoverer.RunDiscovery(null, _libraryManager, _logger);
            progress?.Report(100);
            _logger.Info("[PostScanCandidateTask] Scheduled candidate discovery complete");
            return Task.CompletedTask;
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerDaily,
                    TimeOfDayTicks = TimeSpan.FromHours(4).Ticks
                }
            };
        }
    }
}