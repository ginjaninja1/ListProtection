using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ListProtection.EntryPoints
{
    /// <summary>
    /// IScheduledTask — surfaces a manual Run button in the Emby dashboard
    /// under Scheduled Tasks. Delegates to MissingMemberDetector.RunDetection,
    /// the same implementation used by the timer path in MissingMemberDetectionService.
    ///
    /// No default triggers are registered — this task is run manually or will
    /// gain a post-library-scan trigger in a future task.
    ///
    /// Stores are accessed via ListProtectionPlugin.Instance (same pattern as
    /// IServerEntryPoint implementations — DI cannot inject them).
    /// </summary>
    public class DetectMissingMembersTask : IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        public string Name => "Detect Missing Playlist Members";
        public string Key => "ListProtectionDetectMissingMembers";
        public string Description => "Scans all protected playlists and records any members that are absent from the live library.";
        public string Category => "List Protection";

        public DetectMissingMembersTask(
            ILibraryManager libraryManager,
            ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _logger = logManager.GetLogger(nameof(DetectMissingMembersTask));
        }

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            progress?.Report(0);

            _logger.Info("[DetectMissingMembersTask] Manual run triggered");

            MissingMemberDetector.RunDetection(null, _libraryManager, _logger);

            progress?.Report(100);

            return Task.FromResult(true);
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // No automatic schedule — manual run only.
            return Array.Empty<TaskTriggerInfo>();
        }
    }
}