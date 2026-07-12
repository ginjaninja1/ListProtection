using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
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
    /// PROBE ONLY — Task 6, Step 1.
    ///
    /// Queries raw Audio candidates for the first known missing member and logs
    /// all available fields for verification. Does NOT score, rank, or store results.
    /// This probe must be run and results verified before any scoring logic is written.
    ///
    /// Appears in Emby dashboard under Scheduled Tasks → "List Protection".
    /// Remove or disable once field availability is confirmed and scoring is implemented.
    /// </summary>
    public class CandidateDiscoveryProbeTask : IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        public CandidateDiscoveryProbeTask(ILibraryManager libraryManager, ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _logger = logManager.GetLogger("CandidateDiscoveryProbeTask");
        }

        public string Name => "Candidate Discovery Probe";
        public string Description => "PROBE: Logs raw Audio candidate fields for the first missing member. Task 6, Step 1.";
        public string Category => "List Protection";
        public string Key => "ListProtection_CandidateDiscoveryProbe";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return Array.Empty<TaskTriggerInfo>();
        }

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("[CandidateDiscoveryProbe] ===== PROBE START =====");

            try
            {
                var plugin = ListProtectionPlugin.Instance;
                if (plugin == null)
                {
                    _logger.Error("[CandidateDiscoveryProbe] Plugin instance is null — aborting");
                    return Task.CompletedTask;
                }

                // ── 1. Pick the first missing member as our probe target ──────────

                var missing = plugin.MissingMembersStore.Load();
                if (missing == null || missing.Count == 0)
                {
                    _logger.Info("[CandidateDiscoveryProbe] No missing members in store — nothing to probe");
                    return Task.CompletedTask;
                }

                var target = missing[0];
                _logger.Info(
                    "[CandidateDiscoveryProbe] Probe target: '{0}' | InternalId={1} | Path={2} | Playlist='{3}' ({4})",
                    target.Member.Name,
                    target.Member.InternalId,
                    target.Member.Path,
                    target.PlaylistName,
                    target.PlaylistId);

                // ── 2. Build exclusion set — items already in ground truth for this playlist ──

                var groundTruth = plugin.GroundTruthStore.Load();
                var excludedInternalIds = new HashSet<long>();

                if (groundTruth.TryGetValue(target.PlaylistId, out var gtEntry) && gtEntry.Members != null)
                {
                    foreach (var m in gtEntry.Members)
                        excludedInternalIds.Add(m.InternalId);

                    _logger.Info(
                        "[CandidateDiscoveryProbe] Excluding {0} ground truth member(s) for playlist '{1}'",
                        excludedInternalIds.Count,
                        target.PlaylistName);
                }
                else
                {
                    _logger.Warn(
                        "[CandidateDiscoveryProbe] No ground truth entry found for playlist {0} — exclusion set is empty",
                        target.PlaylistId);
                }

                // ── 3. Query all Audio items from the library ─────────────────────

                _logger.Info("[CandidateDiscoveryProbe] Querying library for all Audio items...");

                var allAudio = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { "Audio" },
                    Recursive = true
                });

                _logger.Info("[CandidateDiscoveryProbe] Library returned {0} Audio item(s) total", allAudio?.Length ?? 0);

                if (allAudio == null || allAudio.Length == 0)
                {
                    _logger.Warn("[CandidateDiscoveryProbe] No Audio items returned — probe cannot continue");
                    return Task.CompletedTask;
                }

                // ── 4. Log raw fields on up to 5 candidates (not in ground truth) ─

                var logged = 0;
                var excluded = 0;

                foreach (var item in allAudio)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (excludedInternalIds.Contains(item.InternalId))
                    {
                        excluded++;
                        continue;
                    }

                    if (logged >= 5)
                        break;

                    // Cast to Audio to access audio-specific fields
                    var audio = item as Audio;

                    // Log BaseItem fields — these are expected to be populated
                    _logger.Info("[CandidateDiscoveryProbe] --- Candidate {0} ---", logged + 1);
                    _logger.Info("[CandidateDiscoveryProbe]   InternalId          = {0}", item.InternalId);
                    _logger.Info("[CandidateDiscoveryProbe]   Id (Guid)           = {0}", item.Id);
                    _logger.Info("[CandidateDiscoveryProbe]   Name                = {0}", item.Name ?? "<null>");
                    _logger.Info("[CandidateDiscoveryProbe]   Path                = {0}", item.Path ?? "<null>");
                    _logger.Info("[CandidateDiscoveryProbe]   FileName            = {0}", item.FileName ?? "<null>");
                    _logger.Info("[CandidateDiscoveryProbe]   FileNameWithoutExt  = {0}", item.FileNameWithoutExtension ?? "<null>");
                    _logger.Info("[CandidateDiscoveryProbe]   Album               = {0}", item.Album ?? "<null>");
                    _logger.Info("[CandidateDiscoveryProbe]   RunTimeTicks        = {0}", item.RunTimeTicks.HasValue ? item.RunTimeTicks.Value.ToString() : "<null>");
                    _logger.Info("[CandidateDiscoveryProbe]   IndexNumber         = {0}", item.IndexNumber.HasValue ? item.IndexNumber.Value.ToString() : "<null>");
                    _logger.Info("[CandidateDiscoveryProbe]   ParentIndexNumber   = {0}", item.ParentIndexNumber.HasValue ? item.ParentIndexNumber.Value.ToString() : "<null>");
                    _logger.Info("[CandidateDiscoveryProbe]   ProductionYear      = {0}", item.ProductionYear.HasValue ? item.ProductionYear.Value.ToString() : "<null>");

                    // Log Audio-specific lazy-loaded fields — population is UNCONFIRMED
                    if (audio != null)
                    {
                        // Artists
                        try
                        {
                            var artists = audio.Artists;
                            _logger.Info("[CandidateDiscoveryProbe]   Artists             = {0}",
                                artists != null && artists.Length > 0
                                    ? string.Join(", ", artists)
                                    : "<empty or null>");
                        }
                        catch (Exception ex)
                        {
                            _logger.Info("[CandidateDiscoveryProbe]   Artists             = <EXCEPTION: {0}>", ex.Message);
                        }

                        // AlbumArtists
                        try
                        {
                            var albumArtists = audio.AlbumArtists;
                            _logger.Info("[CandidateDiscoveryProbe]   AlbumArtists        = {0}",
                                albumArtists != null && albumArtists.Length > 0
                                    ? string.Join(", ", albumArtists)
                                    : "<empty or null>");
                        }
                        catch (Exception ex)
                        {
                            _logger.Info("[CandidateDiscoveryProbe]   AlbumArtists        = <EXCEPTION: {0}>", ex.Message);
                        }
                    }
                    else
                    {
                        _logger.Info("[CandidateDiscoveryProbe]   [item is not Audio subtype — cast returned null]");
                    }

                    logged++;
                }

                _logger.Info(
                    "[CandidateDiscoveryProbe] Probe complete | candidates logged={0} | ground-truth excluded={1} | total audio={2}",
                    logged,
                    excluded,
                    allAudio.Length);

                _logger.Info("[CandidateDiscoveryProbe] ===== PROBE END =====");
            }
            catch (OperationCanceledException)
            {
                _logger.Info("[CandidateDiscoveryProbe] Probe cancelled");
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[CandidateDiscoveryProbe] Probe failed", ex);
            }

            progress?.Report(100);
            return Task.CompletedTask;
        }
    }
}