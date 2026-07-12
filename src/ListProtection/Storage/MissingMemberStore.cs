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
    /// Stores missing member records as a flat list (not keyed by playlist).
    ///
    /// Flat structure rationale: cross-playlist identity requires querying by Member.Id
    /// across all playlists. A flat list makes this a simple Where clause. Per-playlist
    /// lookups are equally simple. Scale is negligible for a music library.
    ///
    /// Constructor signature matches all other Pattern B stores exactly.
    /// </summary>
    public class MissingMembersStore
    {
        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IFileSystem _fileSystem;
        private readonly string _filePath;
        private readonly object _lock = new object();

        public MissingMembersStore(IApplicationHost applicationHost, ILogger logger, string pluginFullName)
        {
            _logger = logger;
            _jsonSerializer = applicationHost.Resolve<IJsonSerializer>();
            _fileSystem = applicationHost.Resolve<IFileSystem>();

            var appPaths = applicationHost.Resolve<IApplicationPaths>();
            _filePath = Path.Combine(appPaths.PluginConfigurationsPath, pluginFullName + ".json");

            _logger.Info("[MissingMembersStore] Store file: {0}", _filePath);
        }

        /// <summary>
        /// Returns all missing member records.
        /// Returns empty list on missing file or any error — never throws.
        /// </summary>
        public List<MissingMemberEntry> Load()
        {
            lock (_lock)
            {
                try
                {
                    if (!_fileSystem.FileExists(_filePath))
                    {
                        _logger.Info("[MissingMembersStore] No store file found — returning empty list");
                        return new List<MissingMemberEntry>();
                    }

                    using (var stream = _fileSystem.OpenRead(_filePath))
                    {
                        var data = _jsonSerializer.DeserializeFromStream<StoreData>(stream);
                        var entries = data?.Entries ?? new List<MissingMemberEntry>();
                        _logger.Info("[MissingMembersStore] Loaded {0} record(s)", entries.Count);
                        return entries;
                    }
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("[MissingMembersStore] Load failed — returning empty list", ex);
                    return new List<MissingMemberEntry>();
                }
            }
        }

        /// <summary>
        /// Persists all missing member records.
        /// Never throws — logs on failure.
        /// </summary>
        public void Save(List<MissingMemberEntry> entries)
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

                    _logger.Info("[MissingMembersStore] Saved {0} record(s)", entries.Count);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("[MissingMembersStore] Save failed", ex);
                }
            }
        }

        // ── Private ────────────────────────────────────────────────────────

        private class StoreData
        {
            public List<MissingMemberEntry> Entries { get; set; } = new List<MissingMemberEntry>();
        }
    }
}