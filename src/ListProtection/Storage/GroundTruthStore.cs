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
    /// Stores ground truth membership snapshots keyed by playlist Guid "N" string.
    /// Entries are soft-deleted (IsActive = false) when a playlist is unprotected.
    /// </summary>
    public class GroundTruthStore
    {
        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IFileSystem _fileSystem;
        private readonly string _filePath;
        private readonly object _lock = new object();

        public GroundTruthStore(IApplicationHost applicationHost, ILogger logger, string pluginFullName)
        {
            _logger = logger;
            _jsonSerializer = applicationHost.Resolve<IJsonSerializer>();
            _fileSystem = applicationHost.Resolve<IFileSystem>();

            var appPaths = applicationHost.Resolve<IApplicationPaths>();
            _filePath = Path.Combine(appPaths.PluginConfigurationsPath, pluginFullName + ".json");

            _logger.Info("[GroundTruthStore] Store file: {0}", _filePath);
        }

        /// <summary>
        /// Returns all ground truth entries keyed by playlist Guid "N" string.
        /// Returns empty dictionary on missing file or any error — never throws.
        /// </summary>
        public Dictionary<string, GroundTruthEntry> Load()
        {
            lock (_lock)
            {
                try
                {
                    if (!_fileSystem.FileExists(_filePath))
                    {
                        _logger.Info("[GroundTruthStore] No store file found — returning empty dictionary");
                        return new Dictionary<string, GroundTruthEntry>(StringComparer.OrdinalIgnoreCase);
                    }

                    using (var stream = _fileSystem.OpenRead(_filePath))
                    {
                        var data = _jsonSerializer.DeserializeFromStream<StoreData>(stream);
                        var entries = data?.Entries ?? new Dictionary<string, GroundTruthEntry>();
                        _logger.Info("[GroundTruthStore] Loaded {0} entr(ies)", entries.Count);
                        return entries;
                    }
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("[GroundTruthStore] Load failed — returning empty dictionary", ex);
                    return new Dictionary<string, GroundTruthEntry>(StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        /// <summary>
        /// Persists all ground truth entries.
        /// Never throws — logs on failure.
        /// </summary>
        public void Save(Dictionary<string, GroundTruthEntry> entries)
        {
            lock (_lock)
            {
                try
                {
                    var data = new StoreData { Entries = entries };

                    using (var stream = _fileSystem.GetFileStream(_filePath, FileOpenMode.Create, FileAccessMode.Write))
                    {
                        _jsonSerializer.SerializeToStream(data, stream, new JsonSerializerOptions { Indent = true });
                    }

                    _logger.Info("[GroundTruthStore] Saved {0} entr(ies)", entries.Count);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("[GroundTruthStore] Save failed", ex);
                }
            }
        }

        // ── Private ────────────────────────────────────────────────────────

        private class StoreData
        {
            public Dictionary<string, GroundTruthEntry> Entries { get; set; }
                = new Dictionary<string, GroundTruthEntry>();
        }
    }
}