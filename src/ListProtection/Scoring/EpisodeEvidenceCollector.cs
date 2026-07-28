using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using System;
using System.Collections.Generic;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Emits atomic facts for Episode items. No combination logic.
    ///
    /// Atomic facts emitted:
    ///   SeriesMatch       — series name or provider ID matches
    ///   SeasonMatch       — season number matches
    ///   EpisodeMatch      — episode number matches
    ///   TitleMatch        — episode title matches
    ///   DurationMatch     — duration within tolerance
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

            var candidateSeriesName = episode.FindSeriesName();

            var seriesNameMatch = !string.IsNullOrEmpty(gt.SeriesName) &&
                                  !string.IsNullOrEmpty(candidateSeriesName) &&
                                  string.Equals(gt.SeriesName, candidateSeriesName, StringComparison.OrdinalIgnoreCase);

            var gtSeriesTvdb = GetGtValue(gt.ParentSeriesProviderIds, MetadataProviders.Tvdb.ToString());
            var gtSeriesImdb = GetGtValue(gt.ParentSeriesProviderIds, MetadataProviders.Imdb.ToString());

            var seriesProviderMatch = false;
            if (!string.IsNullOrEmpty(gtSeriesTvdb) || !string.IsNullOrEmpty(gtSeriesImdb))
            {
                var series = episode.GetSeries(null);
                if (series != null)
                {
                    var ids = series.ProviderIds;
                    if (ids != null)
                    {
                        if (!string.IsNullOrEmpty(gtSeriesTvdb) &&
                            ids.TryGetValue(MetadataProviders.Tvdb.ToString(), out var tvdbId) &&
                            string.Equals(gtSeriesTvdb, tvdbId, StringComparison.OrdinalIgnoreCase))
                            seriesProviderMatch = true;

                        if (!string.IsNullOrEmpty(gtSeriesImdb) &&
                            ids.TryGetValue(MetadataProviders.Imdb.ToString(), out var imdbId) &&
                            string.Equals(gtSeriesImdb, imdbId, StringComparison.OrdinalIgnoreCase))
                            seriesProviderMatch = true;
                    }
                }
            }

            if (!seriesNameMatch && !seriesProviderMatch) return facts;

            facts.Add(new EvidenceFact(EpisodeFacts.SeriesMatch));

            if (gt.SeasonNumber.HasValue &&
                episode.ParentIndexNumber.HasValue &&
                gt.SeasonNumber.Value == episode.ParentIndexNumber.Value)
                facts.Add(new EvidenceFact(EpisodeFacts.SeasonMatch));

            if (gt.IndexNumber.HasValue &&
                episode.IndexNumber.HasValue &&
                gt.IndexNumber.Value == episode.IndexNumber.Value)
                facts.Add(new EvidenceFact(EpisodeFacts.EpisodeMatch));

            if (!string.IsNullOrEmpty(gt.Name) &&
                !string.IsNullOrEmpty(episode.Name) &&
                string.Equals(gt.Name, episode.Name, StringComparison.OrdinalIgnoreCase))
                facts.Add(new EvidenceFact(EpisodeFacts.TitleMatch));

            if (gt.RunTimeTicks.HasValue && gt.RunTimeTicks.Value > 0 &&
                episode.RunTimeTicks.HasValue && episode.RunTimeTicks.Value > 0 &&
                Math.Abs(gt.RunTimeTicks.Value - episode.RunTimeTicks.Value) <= _durationToleranceTicks)
                facts.Add(new EvidenceFact(EpisodeFacts.DurationMatch));

            return facts;
        }

        private static string GetGtValue(System.Collections.Generic.Dictionary<string, string> ids, string key)
        {
            if (ids == null) return null;
            return ids.TryGetValue(key, out var val) ? val : null;
        }
    }

    public static class EpisodeFacts
    {
        public const string SeriesMatch = "Episode.SeriesMatch";
        public const string SeasonMatch = "Episode.SeasonMatch";
        public const string EpisodeMatch = "Episode.EpisodeMatch";
        public const string TitleMatch = "Episode.TitleMatch";
        public const string DurationMatch = "Episode.DurationMatch";
    }
}