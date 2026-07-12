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
    /// Stores discovered repair candidates as a flat list.
    /// Mirrors MissingMembersStore structure exactly.
    /// </summary>
    public class CandidateStore
    {
        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IFileSystem _fileSystem;
        private readonly string _filePath;
        private readonly object _lock = new object();

        public CandidateStore(IApplicationHost applicationHost, ILogger logger, string pluginFullName)
        {
            _logger = logger;
            _jsonSerializer = applicationHost.Resolve<IJsonSerializer>();
            _fileSystem = applicationHost.Resolve<IFileSystem>();

            var appPaths = applicationHost.Resolve<IApplicationPaths>();
            _filePath = Path.Combine(appPaths.PluginConfigurationsPath, pluginFullName + ".json");

            _logger.Info("[CandidateStore] Store file: {0}", _filePath);
        }

        /// <summary>
        /// Returns all candidate entries.
        /// Returns empty list on missing file or any error — never throws.
        /// </summary>
        public List<CandidateEntry> Load()
        {
            lock (_lock)
            {
                try
                {
                    if (!_fileSystem.FileExists(_filePath))
                    {
                        _logger.Info("[CandidateStore] No store file found — returning empty list");
                        return new List<CandidateEntry>();
                    }

                    using (var stream = _fileSystem.OpenRead(_filePath))
                    {
                        var data = _jsonSerializer.DeserializeFromStream<StoreData>(stream);
                        var entries = data?.Entries ?? new List<CandidateEntry>();
                        _logger.Info("[CandidateStore] Loaded {0} candidate entr(ies)", entries.Count);
                        return entries;
                    }
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("[CandidateStore] Load failed — returning empty list", ex);
                    return new List<CandidateEntry>();
                }
            }
        }

        /// <summary>
        /// Persists all candidate entries.
        /// Never throws — logs on failure.
        /// </summary>
        public void Save(List<CandidateEntry> entries)
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

                    _logger.Info("[CandidateStore] Saved {0} candidate entr(ies)", entries.Count);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("[CandidateStore] Save failed", ex);
                }
            }
        }

        // ── Private ────────────────────────────────────────────────────────

        private class StoreData
        {
            public List<CandidateEntry> Entries { get; set; } = new List<CandidateEntry>();
        }
    }
}