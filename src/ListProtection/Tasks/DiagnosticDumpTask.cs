using ListProtection.Services;
using ListProtection.Storage;
using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ListProtection.Tasks
{
    /// <summary>
    /// DIAGNOSTIC ONLY — not part of the detect/discover/repair pipeline.
    ///
    /// Dumps full identity + metadata state for:
    ///   1. Every Playlist and its live members (via Playlist.GetItemList)
    ///   2. Every Collection (BoxSet) and its live members (via BoxSet.GetItemList)
    ///   3. Every library Audio item matching the watch terms below, regardless of
    ///      whether it is currently a member of any list
    ///
    /// Purpose: investigate whether InternalId/Guid survive a file move/rename, and
    /// whether collection membership (item-side metadata) survives independently of
    /// playlist membership (entry-side). Run before and after moving files on disk
    /// and diff the two JSON dumps.
    ///
    /// Field extraction reuses GroundTruthMemberFactory.FromItem — same proven code
    /// path used for real ground truth capture — so nothing here is guessed.
    ///
    /// Manual trigger only (no default schedule). Run from Scheduled Tasks in the
    /// Emby dashboard under category "GinjaNinja Tools".
    /// </summary>
    public class DiagnosticDumpTask : IScheduledTask
    {
        // Case-insensitive substring watch list — any item whose Name, Path, Album,
        // AlbumArtist, or Artists contains one of these is flagged "Watch": true.
        private static readonly string[] WatchTerms =
        {
            "a fine frenzy",
            "bomb in a birdcage"
        };

        private readonly ILibraryManager _libraryManager;
        private readonly IApplicationHost _applicationHost;
        private readonly ILogger _logger;

        public string Name => "List Protection — Diagnostic Dump (manual)";
        public string Key => "ListProtectionDiagnosticDump";
        public string Description => "Dumps full id/metadata state of all Playlist and Collection members, plus any matching library Audio items, to a timestamped JSON file for manual comparison across runs.";
        public string Category => "GinjaNinja Tools";

        public DiagnosticDumpTask(
            ILibraryManager libraryManager,
            IApplicationHost applicationHost,
            ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _applicationHost = applicationHost;
            _logger = logManager.GetLogger(nameof(DiagnosticDumpTask));
        }

        // Manual trigger only — no default schedule.
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            try
            {
                progress?.Report(0);
                _logger.Info("[DiagnosticDumpTask] Starting dump");

                var dump = new DumpResult
                {
                    GeneratedAtUtc = DateTime.UtcNow,
                    WatchTerms = WatchTerms.ToList()
                };

                // ── Playlists ─────────────────────────────────────────────
                var allPlaylists = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { "Playlist" },
                    Recursive = true
                }) ?? Array.Empty<BaseItem>();

                foreach (var pItem in allPlaylists)
                {
                    if (!(pItem is Playlist playlist)) continue;

                    var liveMembers = playlist.GetItemList(new InternalItemsQuery()) ?? Array.Empty<BaseItem>();

                    dump.Playlists.Add(new ListDump
                    {
                        ListId = playlist.Id.ToString("N"),
                        ListInternalId = playlist.InternalId,
                        ListName = playlist.Name ?? string.Empty,
                        MemberCount = liveMembers.Length,
                        Members = liveMembers.Select(i => ToMemberDump(i, _logger, "[DiagnosticDumpTask][MemberIdentity][Playlist]")).ToList()
                    });
                }
                _logger.Info("[DiagnosticDumpTask] Playlists dumped: {0}", dump.Playlists.Count);

                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(33);

                // ── Collections ───────────────────────────────────────────
                var allCollections = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { "BoxSet" },
                    Recursive = true
                }) ?? Array.Empty<BaseItem>();

                foreach (var cItem in allCollections)
                {
                    if (!(cItem is BoxSet collection)) continue;

                    var liveMembers = collection.GetItemList(new InternalItemsQuery()) ?? Array.Empty<BaseItem>();

                    dump.Collections.Add(new ListDump
                    {
                        ListId = collection.Id.ToString("N"),
                        ListInternalId = collection.InternalId,
                        ListName = collection.Name ?? string.Empty,
                        MemberCount = liveMembers.Length,
                        Members = liveMembers.Select(i => ToMemberDump(i, _logger, "[DiagnosticDumpTask][MemberIdentity][Collection]")).ToList()
                    });
                }
                _logger.Info("[DiagnosticDumpTask] Collections dumped: {0}", dump.Collections.Count);

                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(66);

                // ── Library-wide Audio scan for watch terms ──────────────
                // Proven query pattern (same as CandidateDiscoverer's full-library pool query).
                var allAudio = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { "Audio" },
                    Recursive = true
                }) ?? Array.Empty<BaseItem>();

                foreach (var item in allAudio)
                {
                    var member = ToMemberDump(item, null, null);
                    if (member.Watch)
                        dump.WatchedLibraryAudio.Add(member);
                }
                _logger.Info(
                    "[DiagnosticDumpTask] Library-wide Audio scanned: {0} item(s), {1} matched watch terms",
                    allAudio.Length, dump.WatchedLibraryAudio.Count);

                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(90);

                // ── Log every watched record at Info level for quick tailing ──
                foreach (var m in dump.Playlists.SelectMany(l => l.Members).Where(m => m.Watch))
                    _logger.Info("[DiagnosticDumpTask][WATCH][Playlist] {0}", Describe(m));
                foreach (var m in dump.Collections.SelectMany(l => l.Members).Where(m => m.Watch))
                    _logger.Info("[DiagnosticDumpTask][WATCH][Collection] {0}", Describe(m));
                foreach (var m in dump.WatchedLibraryAudio)
                    _logger.Info("[DiagnosticDumpTask][WATCH][LibraryAudio] {0}", Describe(m));

                // ── Write JSON dump ───────────────────────────────────────
                var jsonSerializer = _applicationHost.Resolve<IJsonSerializer>();
                var fileSystem = _applicationHost.Resolve<IFileSystem>();
                var appPaths = _applicationHost.Resolve<IApplicationPaths>();

                var fileName = "List Protection.DiagnosticDump." + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".json";
                var filePath = Path.Combine(appPaths.PluginConfigurationsPath, fileName);

                using (var stream = fileSystem.GetFileStream(filePath, FileOpenMode.Create, FileAccessMode.Write))
                {
                    jsonSerializer.SerializeToStream(dump, stream, new JsonSerializerOptions { Indent = true });
                }

                _logger.Info("[DiagnosticDumpTask] Dump written to {0}", filePath);
                progress?.Report(100);

                return Task.CompletedTask;
            }
            catch (OperationCanceledException)
            {
                _logger.Info("[DiagnosticDumpTask] Cancelled");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[DiagnosticDumpTask] Failed", ex);
                return Task.CompletedTask;
            }
        }

        private static MemberDump ToMemberDump(BaseItem item, ILogger logger, string identityTag)
        {
            // Reuse the proven capture path — identical fields to real GT capture.
            var gt = GroundTruthMemberFactory.FromItem(item);

            if (identityTag != null)
                MemberIdentityLogger.LogIdentity(gt, logger, identityTag);

            var watch = ContainsAnyWatchTerm(gt.Name)
                || ContainsAnyWatchTerm(gt.Path)
                || ContainsAnyWatchTerm(gt.Album)
                || ContainsAnyWatchTerm(gt.AlbumArtist)
                || (gt.Artists != null && gt.Artists.Any(ContainsAnyWatchTerm));

            return new MemberDump
            {
                InternalId = gt.InternalId,
                Id = gt.Id,
                Name = gt.Name,
                Path = gt.Path,
                MediaType = gt.MediaType,
                Album = gt.Album,
                AlbumArtist = gt.AlbumArtist,
                Artists = gt.Artists,
                IndexNumber = gt.IndexNumber,
                MusicBrainzTrackId = gt.MusicBrainzTrackId,
                Watch = watch
            };
        }

        private static bool ContainsAnyWatchTerm(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            foreach (var term in WatchTerms)
                if (value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private static string Describe(MemberDump m)
            => $"Name='{m.Name}' | InternalId={m.InternalId} | Id={m.Id} | Type={m.MediaType} | Album='{m.Album}' | Path='{m.Path}'";

        // ── Dump models — plain POCOs, serialised as-is ──────────────────

        private class DumpResult
        {
            public DateTime GeneratedAtUtc { get; set; }
            public List<string> WatchTerms { get; set; } = new List<string>();
            public List<ListDump> Playlists { get; set; } = new List<ListDump>();
            public List<ListDump> Collections { get; set; } = new List<ListDump>();
            public List<MemberDump> WatchedLibraryAudio { get; set; } = new List<MemberDump>();
        }

        private class ListDump
        {
            public string ListId { get; set; }
            public long ListInternalId { get; set; }
            public string ListName { get; set; }
            public int MemberCount { get; set; }
            public List<MemberDump> Members { get; set; } = new List<MemberDump>();
        }

        private class MemberDump
        {
            public long InternalId { get; set; }
            public string Id { get; set; }
            public string Name { get; set; }
            public string Path { get; set; }
            public string MediaType { get; set; }
            public string Album { get; set; }
            public string AlbumArtist { get; set; }
            public List<string> Artists { get; set; }
            public int? IndexNumber { get; set; }
            public string MusicBrainzTrackId { get; set; }
            public bool Watch { get; set; }
        }
    }
}