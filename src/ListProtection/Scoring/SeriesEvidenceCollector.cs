using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using System;
using System.Collections.Generic;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Emits atomic facts for Series items used directly as collection members
    /// (a whole series added to a Collection, as distinct from an Episode inside
    /// a protected playlist/collection — see EpisodeEvidenceCollector for that case).
    ///
    /// Atomic facts emitted:
    ///   TvdbIdMatch  — TVDB ID exact match (definitive)
    ///   ImdbIdMatch  — IMDB ID exact match (definitive)
    ///   TitleMatch   — series name exact match
    /// </summary>
    public sealed class SeriesEvidenceCollector : IEvidenceCollector
    {
        public string MediaType => "Series";

        public IEnumerable<EvidenceFact> Collect(GroundTruthMember gt, BaseItem candidate)
        {
            var facts = new List<EvidenceFact>();

            if (gt == null || candidate == null) return facts;
            if (!(candidate is Series series)) return facts;

            var gtTvdb = GetGtProviderId(gt, MetadataProviders.Tvdb.ToString());
            if (!string.IsNullOrEmpty(gtTvdb))
            {
                var candidateTvdb = GetProviderId(series, MetadataProviders.Tvdb);
                if (!string.IsNullOrEmpty(candidateTvdb) &&
                    string.Equals(gtTvdb, candidateTvdb, StringComparison.OrdinalIgnoreCase))
                {
                    facts.Add(new EvidenceFact(SeriesFacts.TvdbIdMatch));
                    return facts;
                }
            }

            var gtImdb = GetGtProviderId(gt, MetadataProviders.Imdb.ToString());
            if (!string.IsNullOrEmpty(gtImdb))
            {
                var candidateImdb = GetProviderId(series, MetadataProviders.Imdb);
                if (!string.IsNullOrEmpty(candidateImdb) &&
                    string.Equals(gtImdb, candidateImdb, StringComparison.OrdinalIgnoreCase))
                {
                    facts.Add(new EvidenceFact(SeriesFacts.ImdbIdMatch));
                    return facts;
                }
            }

            if (!string.IsNullOrEmpty(gt.Name) &&
                !string.IsNullOrEmpty(series.Name) &&
                string.Equals(gt.Name, series.Name, StringComparison.OrdinalIgnoreCase))
                facts.Add(new EvidenceFact(SeriesFacts.TitleMatch));

            return facts;
        }

        private static string GetGtProviderId(GroundTruthMember gt, string providerKey)
        {
            if (gt?.ProviderIds == null) return null;
            return gt.ProviderIds.TryGetValue(providerKey, out var val) ? val : null;
        }

        private static string GetProviderId(Series series, MetadataProviders provider)
        {
            try
            {
                var ids = series.ProviderIds;
                if (ids == null) return null;
                return ids.TryGetValue(provider.ToString(), out var val) ? val : null;
            }
            catch { return null; }
        }
    }

    public static class SeriesFacts
    {
        public const string TvdbIdMatch = "Series.TvdbIdMatch";
        public const string ImdbIdMatch = "Series.ImdbIdMatch";
        public const string TitleMatch = "Series.TitleMatch";
    }
}