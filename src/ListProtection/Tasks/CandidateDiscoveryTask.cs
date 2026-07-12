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
    /// IScheduledTask — surfaces a manual Run button in the Emby dashboard
    /// under Scheduled Tasks. Delegates to CandidateDiscoverer.RunDiscovery.
    ///
    /// No default triggers are registered — run manually.
    ///
    /// Stores are accessed via ListProtectionPlugin.Instance (same pattern as
    /// IServerEntryPoint implementations — DI cannot inject them).
    /// </summary>
    public class CandidateDiscoveryTask : IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        public string Name => "Discover Missing Member Candidates";
        public string Key => "ListProtectionDiscoverCandidates";
        public string Description => "Scores library Audio items against missing playlist members and writes ranked candidates to the candidate store.";
        public string Category => "List Protection";

        public CandidateDiscoveryTask(
            ILibraryManager libraryManager,
            ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _logger = logManager.GetLogger(nameof(CandidateDiscoveryTask));
        }

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            progress?.Report(0);

            _logger.Info("[CandidateDiscoveryTask] Manual run triggered");

            CandidateDiscoverer.RunDiscovery(null, _libraryManager, _logger);

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