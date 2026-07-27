using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using System;
using System.IO;

namespace ListProtection.Storage
{
    /// <summary>
    /// What triggered the most recent ConsistencyCheckTask run.
    /// </summary>
    public enum ConsistencyCheckTrigger
    {
        PostScan,
        ScheduledOrManual
    }

    /// <summary>
    /// Pattern B plain store — owns serialisation, locking, and file path.
    /// Records a single fact: when ConsistencyCheckTask last completed, and what
    /// triggered it. Used purely to let the UI tell users whether they're looking
    /// at a converged view or a possibly-stale one.
    /// </summary>
    public class ConsistencyCheckStatusStore
    {
        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IFileSystem _fileSystem;
        private readonly string _filePath;
        private readonly object _lock = new object();

        public ConsistencyCheckStatusStore(IApplicationHost applicationHost, ILogger logger, string pluginFullName)
        {
            _logger = logger;
            _jsonSerializer = applicationHost.Resolve<IJsonSerializer>();
            _fileSystem = applicationHost.Resolve<IFileSystem>();

            var appPaths = applicationHost.Resolve<IApplicationPaths>();
            _filePath = Path.Combine(appPaths.PluginConfigurationsPath, pluginFullName + ".json");

            _logger.Info("[ConsistencyCheckStatusStore] Store file: {0}", _filePath);
        }

        /// <summary>
        /// Returns the last recorded run, or null if the task has never completed
        /// since this store was introduced. Never throws.
        /// </summary>
        public StatusData Load()
        {
            lock (_lock)
            {
                try
                {
                    if (!_fileSystem.FileExists(_filePath))
                        return null;

                    using (var stream = _fileSystem.OpenRead(_filePath))
                    {
                        return _jsonSerializer.DeserializeFromStream<StatusData>(stream);
                    }
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("[ConsistencyCheckStatusStore] Load failed — returning null", ex);
                    return null;
                }
            }
        }

        /// <summary>
        /// Persists the completion time and trigger source of the most recent run.
        /// Never throws — logs on failure.
        /// </summary>
        public void Save(DateTime completedAtUtc, ConsistencyCheckTrigger trigger)
        {
            lock (_lock)
            {
                try
                {
                    var data = new StatusData
                    {
                        CompletedAtUtc = completedAtUtc,
                        Trigger = trigger
                    };

                    using (var stream = _fileSystem.GetFileStream(_filePath, FileOpenMode.Create, FileAccessMode.Write))
                    {
                        _jsonSerializer.SerializeToStream(data, stream, new JsonSerializerOptions { Indent = true });
                    }

                    _logger.Info("[ConsistencyCheckStatusStore] Saved run: {0} ({1})", completedAtUtc, trigger);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("[ConsistencyCheckStatusStore] Save failed", ex);
                }
            }
        }

        public class StatusData
        {
            public DateTime CompletedAtUtc { get; set; }
            public ConsistencyCheckTrigger Trigger { get; set; }
        }
    }
}