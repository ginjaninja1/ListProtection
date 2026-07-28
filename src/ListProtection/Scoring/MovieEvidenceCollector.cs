using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Entities;
using System;
using System.Collections.Generic;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Emits atomic facts for Movie items. No combination logic.
    ///
    /// Atomic facts emitted:
    ///   ImdbIdMatch    — IMDB ID exact match
    ///   TmdbIdMatch    — TMDB ID exact match
    ///   TitleMatch     — movie title exact match
    ///   YearMatch      — production year matches
    ///   DurationMatch  — duration within tolerance
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

            var gtImdb = GetGtProviderId(gt, MetadataProviders.Imdb.ToString());
            if (!string.IsNullOrEmpty(gtImdb))
            {
                var candidateImdb = GetProviderId(movie, MetadataProviders.Imdb);
                if (!string.IsNullOrEmpty(candidateImdb) &&
                    string.Equals(gtImdb, candidateImdb, StringComparison.OrdinalIgnoreCase))
                {
                    facts.Add(new EvidenceFact(MovieFacts.ImdbIdMatch));
                    return facts;
                }
            }

            var gtTmdb = GetGtProviderId(gt, MetadataProviders.Tmdb.ToString());
            if (!string.IsNullOrEmpty(gtTmdb))
            {
                var candidateTmdb = GetProviderId(movie, MetadataProviders.Tmdb);
                if (!string.IsNullOrEmpty(candidateTmdb) &&
                    string.Equals(gtTmdb, candidateTmdb, StringComparison.OrdinalIgnoreCase))
                {
                    facts.Add(new EvidenceFact(MovieFacts.TmdbIdMatch));
                    return facts;
                }
            }

            if (!string.IsNullOrEmpty(gt.Name) &&
                !string.IsNullOrEmpty(movie.Name) &&
                string.Equals(gt.Name, movie.Name, StringComparison.OrdinalIgnoreCase))
                facts.Add(new EvidenceFact(MovieFacts.TitleMatch));

            if (gt.ProductionYear.HasValue &&
                movie.ProductionYear.HasValue &&
                gt.ProductionYear.Value == movie.ProductionYear.Value)
                facts.Add(new EvidenceFact(MovieFacts.YearMatch));

            if (gt.RunTimeTicks.HasValue && gt.RunTimeTicks.Value > 0 &&
                movie.RunTimeTicks.HasValue && movie.RunTimeTicks.Value > 0 &&
                Math.Abs(gt.RunTimeTicks.Value - movie.RunTimeTicks.Value) <= _durationToleranceTicks)
                facts.Add(new EvidenceFact(MovieFacts.DurationMatch));

            return facts;
        }

        private static string GetGtProviderId(GroundTruthMember gt, string providerKey)
        {
            if (gt?.ProviderIds == null) return null;
            return gt.ProviderIds.TryGetValue(providerKey, out var val) ? val : null;
        }

        private static string GetProviderId(Movie movie, MetadataProviders provider)
        {
            try
            {
                var ids = movie.ProviderIds;
                if (ids == null) return null;
                return ids.TryGetValue(provider.ToString(), out var val) ? val : null;
            }
            catch { return null; }
        }
    }

    public static class MovieFacts
    {
        public const string ImdbIdMatch = "Movie.ImdbIdMatch";
        public const string TmdbIdMatch = "Movie.TmdbIdMatch";
        public const string TitleMatch = "Movie.TitleMatch";
        public const string YearMatch = "Movie.YearMatch";
        public const string DurationMatch = "Movie.DurationMatch";
    }
}