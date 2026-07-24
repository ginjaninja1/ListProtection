using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using System;
using System.Collections.Generic;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Evidence collector for episode-specific combination signals.
    /// Chains on top of BaseItemEvidenceCollector.
    /// Only runs when GroundTruthMember.MediaType == "Episode".
    ///
    /// Identity for episodes is positional (Series + Season + Episode number),
    /// not by title — titles are unreliable (TBA placeholders, regional variants).
    ///
    /// Provider IDs are checked at the Series level — episode-level TVDB/IMDB IDs
    /// are not reliably populated by Emby. SeriesTvdbId/SeriesImdbId are captured
    /// in the GT snapshot at protect time from the parent Series item.
    ///
    ///   SeriesSeasonEpTitle    170  Series + Season + EpisodeNumber + Title
    ///   SeriesSeasonEpDuration 160  Series + Season + EpisodeNumber + Duration
    ///   SeriesSeasonEp         150  Series + Season + EpisodeNumber
    ///   SeriesEpDuration       120  Series + EpisodeNumber + Duration (no season)
    ///   SeriesTitleDuration    110  Series + Title + Duration
    ///   SeriesSeasonTitle      100  Series + Season + Title
    ///   SeriesTitle             70  Series + Title
    ///   SeriesDuration          50  Series + Duration
    ///   SeriesOnly              20  Series alone
    ///
    /// Duration tolerance injected at construction from
    /// PluginConfiguration.EpisodeDurationToleranceSeconds (default 5s).
    ///
    /// SeriesTvdbId/ImdbId match are additive bonus signals (not in the combination
    /// hierarchy) since they confirm series identity without pinning the episode.
    /// </summary>
    public sealed class EpisodeEvidenceCollector : IEvidenceCollector
    {
        private readonly long _durationToleranceTicks;

        public EpisodeEvidenceCollector(int durationToleranceSeconds = 5)
        {
            _durationToleranceTicks = (long)durationToleranceSeconds * 10_000_000L;
        }

        public string MediaType => "Episode";

        public IEnumerable<EvidenceFact> Collect(GroundTruthMember gt, BaseItem candidate)
        {
            var facts = new List<EvidenceFact>();

            if (gt == null || candidate == null) return facts;
            if (!(candidate is Episode episode)) return facts;

            // ── Primitive signals ──────────────────────────────────────────

            var candidateSeriesName = episode.FindSeriesName();

            var seriesNameMatch = !string.IsNullOrEmpty(gt.SeriesName) &&
                                  !string.IsNullOrEmpty(candidateSeriesName) &&
                                  string.Equals(gt.SeriesName, candidateSeriesName, StringComparison.OrdinalIgnoreCase);

            // Series provider ID — confirms series identity even if name has minor variant
            var seriesProviderMatch = false;
            if (!string.IsNullOrEmpty(gt.SeriesTvdbId) || !string.IsNullOrEmpty(gt.SeriesImdbId))
            {
                var series = episode.GetSeries(null);
                if (series != null)
                {
                    var ids = series.ProviderIds;
                    if (ids != null)
                    {
                        if (!string.IsNullOrEmpty(gt.SeriesTvdbId) &&
                            ids.TryGetValue(MetadataProviders.Tvdb.ToString(), out var tvdbId) &&
                            string.Equals(gt.SeriesTvdbId, tvdbId, StringComparison.OrdinalIgnoreCase))
                            seriesProviderMatch = true;

                        if (!string.IsNullOrEmpty(gt.SeriesImdbId) &&
                            ids.TryGetValue(MetadataProviders.Imdb.ToString(), out var imdbId) &&
                            string.Equals(gt.SeriesImdbId, imdbId, StringComparison.OrdinalIgnoreCase))
                            seriesProviderMatch = true;
                    }
                }
            }

            // Use either name match or provider match to establish series identity
            var seriesMatch = seriesNameMatch || seriesProviderMatch;

            if (!seriesMatch) return facts; // no series match = no useful signals

            var seasonMatch = gt.SeasonNumber.HasValue &&
                              episode.ParentIndexNumber.HasValue &&
                              gt.SeasonNumber.Value == episode.ParentIndexNumber.Value;

            var episodeMatch = gt.IndexNumber.HasValue &&
                               episode.IndexNumber.HasValue &&
                               gt.IndexNumber.Value == episode.IndexNumber.Value;

            var titleMatch = !string.IsNullOrEmpty(gt.Name) &&
                             !string.IsNullOrEmpty(episode.Name) &&
                             string.Equals(gt.Name, episode.Name, StringComparison.OrdinalIgnoreCase);

            var durationMatch = gt.RunTimeTicks.HasValue && gt.RunTimeTicks.Value > 0 &&
                                episode.RunTimeTicks.HasValue && episode.RunTimeTicks.Value > 0 &&
                                Math.Abs(gt.RunTimeTicks.Value - episode.RunTimeTicks.Value) <= _durationToleranceTicks;

            // ── Combination signals (highest applicable fires) ─────────────

            if (seriesMatch && seasonMatch && episodeMatch && titleMatch)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.SeriesSeasonEpTitle)));
            else if (seriesMatch && seasonMatch && episodeMatch && durationMatch)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.SeriesSeasonEpDuration)));
            else if (seriesMatch && seasonMatch && episodeMatch)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.SeriesSeasonEp)));
            else if (seriesMatch && episodeMatch && durationMatch)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.SeriesEpDuration)));
            else if (seriesMatch && titleMatch && durationMatch)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.SeriesTitleDuration)));
            else if (seriesMatch && seasonMatch && titleMatch)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.SeriesSeasonTitle)));
            else if (seriesMatch && titleMatch)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.SeriesTitle)));
            else if (seriesMatch && durationMatch)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.SeriesDuration)));
            else
                facts.Add(new EvidenceFact(nameof(ScoringWeights.SeriesOnly)));

            return facts;
        }
    }
}