using ListProtection.Storage;
using MediaBrowser.Controller.Entities.Movies;
using System;
using System.Collections.Generic;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Auto-repair eligibility gate for Movie items.
    ///
    /// A candidate passes if ALL of the following hold:
    ///
    ///   1. Score threshold  — top candidate score >= threshold (from config)
    ///   2. Min distance     — score gap between top and second >= minDistance
    ///   3. Semantic gate    — Title (exact) + ProductionYear both match.
    ///
    /// Year is required in the semantic gate because title alone is not sufficient
    /// (remakes, same-name films). If ProductionYear was not captured in GT, year
    /// check is skipped — but this is a degraded state and should be rare since
    /// Emby populates ProductionYear reliably for movies.
    ///
    /// IMDB/TMDB ID match at score 200 will always clear threshold and distance,
    /// but the semantic gate still applies as a safety floor.
    /// </summary>
    public sealed class MovieAutoRepairEligibility : IAutoRepairEligibility
    {
        public string MediaType => "Movie";

        public bool IsEligible(
            GroundTruthMember gt,
            IReadOnlyList<ScoredCandidate> rankedCandidates,
            int threshold,
            int minDistance)
        {
            if (gt == null || rankedCandidates == null || rankedCandidates.Count == 0)
                return false;

            var best = rankedCandidates[0];

            // 1. Score threshold
            if (best.Score < threshold)
                return false;

            // 2. Min distance
            if (rankedCandidates.Count > 1)
            {
                if (best.Score - rankedCandidates[1].Score < minDistance)
                    return false;
            }

            // 3. Semantic gate — Title + Year
            if (!(best.LiveItem is Movie movie))
                return false;

            if (string.IsNullOrEmpty(gt.Name))
                return false;

            if (!string.Equals(gt.Name, movie.Name, StringComparison.OrdinalIgnoreCase))
                return false;

            // Year — required if captured
            if (gt.ProductionYear.HasValue)
            {
                if (!movie.ProductionYear.HasValue ||
                    gt.ProductionYear.Value != movie.ProductionYear.Value)
                    return false;
            }

            return true;
        }
    }
}