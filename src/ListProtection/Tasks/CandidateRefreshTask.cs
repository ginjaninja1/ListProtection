using MediaBrowser.Model.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PlaylistProtection.Tasks
{
    public class CandidateRefreshTask : IScheduledTask
    {
        public string Name => "Candidate Refresh Task";

        public string Key => "PlaylistProtection.CandidateRefresh";

        public string Description => "Refreshes candidate cache for playlist repair engine";

        public string Category => "Playlist Protection";

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            progress?.Report(10);

            // TODO: rebuild candidate index here

            progress?.Report(100);
            await Task.CompletedTask;
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new List<TaskTriggerInfo>();
        }
    }
}