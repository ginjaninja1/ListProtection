using ListProtection.Storage;
using MediaBrowser.Controller.Entities.TV;
using System;
using System.Collections.Generic;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Auto-repair eligibility gate for Episode items.
    ///
    /// A candidate passes if ALL of the following hold:
    ///
    ///   1. Score threshold  — top candidate score >= threshold (from config)
    ///   2. Min distance     — score gap between top and second >= minDistance
    ///   3. Semantic gate    — SeriesName + SeasonNumber + EpisodeNumber (IndexNumber)
    ///                         all match exactly.
    ///
    /// Title is intentionally excluded from the semantic gate — episode titles are
    /// unreliable (TBA placeholders, regional variants). Position within a series is
    /// the stable identity.
    ///
    /// SeasonNumber or EpisodeNumber absent in GT disables that part of the gate.
    /// SeriesName is always required.
    /// </summary>
    public sealed class EpisodeAutoRepairEligibility : IAutoRepairEligibility
    {
        public string MediaType => "Episode";

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

            // 3. Semantic gate
            if (!(best.LiveItem is Episode episode))
                return false;

            // SeriesName — always required
            if (string.IsNullOrEmpty(gt.SeriesName))
                return false;

            var candidateSeriesName = episode.FindSeriesName();
            if (!string.Equals(gt.SeriesName, candidateSeriesName, StringComparison.OrdinalIgnoreCase))
                return false;

            // SeasonNumber (ParentIndexNumber) — required if captured
            if (gt.SeasonNumber.HasValue)
            {
                if (!episode.ParentIndexNumber.HasValue ||
                    gt.SeasonNumber.Value != episode.ParentIndexNumber.Value)
                    return false;
            }

            // EpisodeNumber (IndexNumber) — required if captured
            if (gt.IndexNumber.HasValue)
            {
                if (!episode.IndexNumber.HasValue ||
                    gt.IndexNumber.Value != episode.IndexNumber.Value)
                    return false;
            }

            return true;
        }
    }
}