using ListProtection.EntryPoints;
using ListProtection.Services;
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
    /// <summary>
    /// Full pipeline consistency check — detect → discover → auto-repair.
    ///
    /// Runs as ILibraryPostScanTask (after every library scan) and as a daily
    /// scheduled task at 03:00. Also manually triggerable from the Emby dashboard.
    ///
    /// Steps:
    ///   1. MissingMemberDetector.RunDetection  — compares GT against live playlists,
    ///      records any newly absent members.
    ///   2. CandidateDiscoverer.RunDiscovery    — scores library items against all
    ///      missing members, updates candidate store.
    ///   3. AutoRepairer.RunAutoRepair          — applies auto-repair for any missing
    ///      member whose best candidate passes the eligibility gate.
    ///
    /// Replaces: DetectMissingMembersTask, CandidateDiscoveryTask,
    ///           PostScanDetectTask, PostScanCandidateTask.
    /// </summary>
    public class ConsistencyCheckTask : ILibraryPostScanTask, IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IPlaylistManager _playlistManager;
        private readonly IUserManager _userManager;
        private readonly ILogger _logger;

        public string Name => "List Protection — Consistency Check";
        public string Key => "ListProtectionConsistencyCheck";
        public string Description => "Detects missing playlist members, discovers replacement candidates, and auto-repairs where eligible. Runs after every library scan and daily at 03:00.";
        public string Category => "GinjaNinja Tools";

        public ConsistencyCheckTask(
            ILibraryManager libraryManager,
            IPlaylistManager playlistManager,
            IUserManager userManager,
            ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _playlistManager = playlistManager;
            _userManager = userManager;
            _logger = logManager.GetLogger(nameof(ConsistencyCheckTask));
        }

        // ── ILibraryPostScanTask ───────────────────────────────────────────

        public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.Info("[ConsistencyCheckTask] Post-scan trigger");
            return RunPipeline(progress, cancellationToken);
        }

        // ── IScheduledTask ─────────────────────────────────────────────────

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("[ConsistencyCheckTask] Scheduled/manual trigger");
            return RunPipeline(progress, cancellationToken);
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type            = TaskTriggerInfo.TriggerDaily,
                    TimeOfDayTicks  = TimeSpan.FromHours(3).Ticks
                }
            };
        }

        // ── Pipeline ───────────────────────────────────────────────────────

        private async Task RunPipeline(IProgress<double> progress, CancellationToken cancellationToken)
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
                    _userManager,
                    _logger);

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