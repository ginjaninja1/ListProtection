using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.IO;

namespace ListProtection.Storage
{
    /// <summary>
    /// Pattern B plain store — owns serialisation, locking, and file path.
    /// No EditableOptionsBase dependency.
    /// Stores only the set of protected playlist IDs (Guid "N" format strings).
    /// All consumers call Load() to read and Save(set) to write — no caching at this layer.
    /// </summary>
    public class PlaylistManagementStore
    {
        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IFileSystem _fileSystem;
        private readonly string _filePath;
        private readonly object _lock = new object();

        public PlaylistManagementStore(IApplicationHost applicationHost, ILogger logger, string pluginFullName)
        {
            _logger = logger;
            _jsonSerializer = applicationHost.Resolve<IJsonSerializer>();
            _fileSystem = applicationHost.Resolve<IFileSystem>();

            var appPaths = applicationHost.Resolve<IApplicationPaths>();
            _filePath = Path.Combine(appPaths.PluginConfigurationsPath, pluginFullName + ".json");

            _logger.Info("[PlaylistManagementStore] Store file: {0}", _filePath);
        }

        /// <summary>
        /// Returns the set of protected playlist IDs (Guid "N" format strings).
        /// Returns empty set on missing file or any error — never throws.
        /// </summary>
        public HashSet<string> Load()
        {
            lock (_lock)
            {
                try
                {
                    if (!_fileSystem.FileExists(_filePath))
                    {
                        _logger.Info("[PlaylistManagementStore] No store file found — returning empty set");
                        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    }

                    using (var stream = _fileSystem.OpenRead(_filePath))
                    {
                        var data = _jsonSerializer.DeserializeFromStream<StoreData>(stream);
                        var ids = data?.ProtectedIds ?? new List<string>();
                        _logger.Info("[PlaylistManagementStore] Loaded {0} protected playlist ID(s)", ids.Count);
                        return new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("[PlaylistManagementStore] Load failed — returning empty set", ex);
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        /// <summary>
        /// Persists the set of protected playlist IDs.
        /// Never throws — logs on failure.
        /// </summary>
        public void Save(HashSet<string> protectedIds)
        {
            lock (_lock)
            {
                try
                {
                    var data = new StoreData { ProtectedIds = new List<string>(protectedIds) };

                    using (var stream = _fileSystem.GetFileStream(_filePath, FileOpenMode.Create, FileAccessMode.Write))
                    {
                        _jsonSerializer.SerializeToStream(data, stream, new JsonSerializerOptions { Indent = true });
                    }

                    _logger.Info("[PlaylistManagementStore] Saved {0} protected playlist ID(s)", protectedIds.Count);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("[PlaylistManagementStore] Save failed", ex);
                }
            }
        }

        // ── Private ────────────────────────────────────────────────────────

        private class StoreData
        {
            public List<string> ProtectedIds { get; set; } = new List<string>();
        }
    }
}