using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Entities;
using System;
using System.Collections.Generic;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Evidence collector for movie-specific combination signals.
    /// Chains on top of BaseItemEvidenceCollector.
    /// Only runs when GroundTruthMember.MediaType == "Movie".
    ///
    /// Title + Year is the baseline identity for movies. Year is critical —
    /// it eliminates remakes with identical titles. IMDB/TMDB ID match is
    /// definitive and returns immediately.
    ///
    ///   ImdbMovieIdMatch       200  IMDB ID exact match — definitive
    ///   TmdbMovieIdMatch       200  TMDB ID exact match — definitive
    ///   TitleYearDuration      175  Title + Year + Duration within tolerance
    ///   TitleYear              150  Title + Year
    ///   TitleDuration          120  Title + Duration within tolerance
    ///   TitleOnly               70  Title exact, no year or duration corroboration
    ///   DurationOnly            40  Duration match alone (very weak)
    ///
    /// Duration tolerance injected at construction from
    /// PluginConfiguration.MovieDurationToleranceSeconds (default 10s).
    ///
    /// Title matching uses Emby's resolved Name (not the filename) — Emby's
    /// movie identification is authoritative. No fuzzy matching.
    /// </summary>
    public sealed class MovieEvidenceCollector : IEvidenceCollector
    {
        private readonly long _durationToleranceTicks;

        public MovieEvidenceCollector(int durationToleranceSeconds = 10)
        {
            _durationToleranceTicks = (long)durationToleranceSeconds * 10_000_000L;
        }

        public string MediaType => "Movie";

        public IEnumerable<EvidenceFact> Collect(GroundTruthMember gt, BaseItem candidate)
        {
            var facts = new List<EvidenceFact>();

            if (gt == null || candidate == null) return facts;
            if (!(candidate is Movie movie)) return facts;

            // ── Provider ID signals (definitive — return immediately) ──────

            if (!string.IsNullOrEmpty(gt.ImdbId))
            {
                var candidateImdb = GetProviderId(movie, MetadataProviders.Imdb);
                if (!string.IsNullOrEmpty(candidateImdb) &&
                    string.Equals(gt.ImdbId, candidateImdb, StringComparison.OrdinalIgnoreCase))
                {
                    facts.Add(new EvidenceFact(nameof(ScoringWeights.ImdbMovieIdMatch)));
                    return facts;
                }
            }

            if (!string.IsNullOrEmpty(gt.TmdbId))
            {
                var candidateTmdb = GetProviderId(movie, MetadataProviders.Tmdb);
                if (!string.IsNullOrEmpty(candidateTmdb) &&
                    string.Equals(gt.TmdbId, candidateTmdb, StringComparison.OrdinalIgnoreCase))
                {
                    facts.Add(new EvidenceFact(nameof(ScoringWeights.TmdbMovieIdMatch)));
                    return facts;
                }
            }

            // ── Primitive signals ──────────────────────────────────────────

            var titleMatch = !string.IsNullOrEmpty(gt.Name) &&
                             !string.IsNullOrEmpty(movie.Name) &&
                             string.Equals(gt.Name, movie.Name, StringComparison.OrdinalIgnoreCase);

            var yearMatch = gt.ProductionYear.HasValue &&
                            movie.ProductionYear.HasValue &&
                            gt.ProductionYear.Value == movie.ProductionYear.Value;

            var durationMatch = gt.RunTimeTicks.HasValue && gt.RunTimeTicks.Value > 0 &&
                                movie.RunTimeTicks.HasValue && movie.RunTimeTicks.Value > 0 &&
                                Math.Abs(gt.RunTimeTicks.Value - movie.RunTimeTicks.Value) <= _durationToleranceTicks;

            // ── Combination signals (highest applicable fires) ─────────────

            if (titleMatch && yearMatch && durationMatch)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.TitleYearDuration)));
            else if (titleMatch && yearMatch)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.TitleYear)));
            else if (titleMatch && durationMatch)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.TitleDuration)));
            else if (titleMatch)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.TitleOnly)));
            else if (durationMatch)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.DurationOnly)));

            return facts;
        }

        private static string GetProviderId(Movie movie, MetadataProviders provider)
        {
            try
            {
                var ids = movie.ProviderIds;
                if (ids == null) return null;
                var key = provider.ToString();
                return ids.TryGetValue(key, out var val) ? val : null;
            }
            catch { return null; }
        }
    }
}