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
    /// Stores event log entries as a flat list, newest-first when read.
    ///
    /// Append-only by convention — records are never mutated after creation.
    /// Capped at MaxEntries to prevent unbounded growth.
    /// </summary>
    public class EventStore
    {
        private const int MaxEntries = 2000;

        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IFileSystem _fileSystem;
        private readonly string _filePath;
        private readonly object _lock = new object();

        public EventStore(IApplicationHost applicationHost, ILogger logger, string pluginFullName)
        {
            _logger = logger;
            _jsonSerializer = applicationHost.Resolve<IJsonSerializer>();
            _fileSystem = applicationHost.Resolve<IFileSystem>();

            var appPaths = applicationHost.Resolve<IApplicationPaths>();
            _filePath = Path.Combine(appPaths.PluginConfigurationsPath, pluginFullName + ".json");

            _logger.Info("[EventStore] Store file: {0}", _filePath);
        }

        /// <summary>
        /// Returns all event entries, newest-first.
        /// Returns empty list on missing file or any error — never throws.
        /// </summary>
        public List<EventEntry> Load()
        {
            lock (_lock)
            {
                try
                {
                    if (!_fileSystem.FileExists(_filePath))
                    {
                        _logger.Info("[EventStore] No store file found — returning empty list");
                        return new List<EventEntry>();
                    }

                    using (var stream = _fileSystem.OpenRead(_filePath))
                    {
                        var data = _jsonSerializer.DeserializeFromStream<StoreData>(stream);
                        var entries = data?.Entries ?? new List<EventEntry>();
                        _logger.Info("[EventStore] Loaded {0} event(s)", entries.Count);
                        return entries;
                    }
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("[EventStore] Load failed — returning empty list", ex);
                    return new List<EventEntry>();
                }
            }
        }

        /// <summary>
        /// Appends a new event entry. Caps list at MaxEntries (oldest removed first).
        /// Never throws — logs on failure.
        /// </summary>
        public void Append(EventEntry entry)
        {
            if (entry == null) return;

            lock (_lock)
            {
                try
                {
                    List<EventEntry> entries;

                    if (_fileSystem.FileExists(_filePath))
                    {
                        using (var stream = _fileSystem.OpenRead(_filePath))
                        {
                            var data = _jsonSerializer.DeserializeFromStream<StoreData>(stream);
                            entries = data?.Entries ?? new List<EventEntry>();
                        }
                    }
                    else
                    {
                        entries = new List<EventEntry>();
                    }

                    // Insert newest first
                    entries.Insert(0, entry);

                    // Cap
                    while (entries.Count > MaxEntries)
                        entries.RemoveAt(entries.Count - 1);

                    Save(entries);

                    _logger.Info(
                        "[EventStore] Appended event | type={0} | playlist={1}",
                        entry.EventType ?? "(null)", entry.PlaylistName ?? "(null)");
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("[EventStore] Append failed", ex);
                }
            }
        }

        /// <summary>
        /// Returns events for a specific playlist, newest-first.
        /// </summary>
        public List<EventEntry> LoadForPlaylist(string playlistId)
        {
            var all = Load();
            var result = new List<EventEntry>();
            foreach (var e in all)
            {
                if (string.Equals(e.PlaylistId, playlistId, StringComparison.OrdinalIgnoreCase))
                    result.Add(e);
            }
            return result;
        }

        // ── Private ────────────────────────────────────────────────────────

        private void Save(List<EventEntry> entries)
        {
            var data = new StoreData { Entries = entries };

            using (var stream = _fileSystem.GetFileStream(_filePath, FileOpenMode.Create, FileAccessMode.Write))
            {
                _jsonSerializer.SerializeToStream(data, stream, new JsonSerializerOptions { Indent = true });
            }
        }

        private class StoreData
        {
            public List<EventEntry> Entries { get; set; } = new List<EventEntry>();
        }
    }
}