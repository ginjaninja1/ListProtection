using ListProtection.Storage;
using MediaBrowser.Controller.Entities.Audio;
using System;
using System.Collections.Generic;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Auto-repair eligibility gate for Audio items.
    ///
    /// A candidate passes if ALL of the following hold:
    ///
    ///   1. Score threshold  — top candidate score >= threshold (from config)
    ///   2. Min distance     — top candidate score - second candidate score >= minDistance
    ///                         (if only one candidate exists the distance check passes)
    ///   3. Semantic gate    — Name + Artist + Album all match exactly (case-insensitive)
    ///
    /// The semantic gate is a hard floor independent of score. A high-scoring
    /// candidate that fails the name/artist/album check is never auto-repaired —
    /// it is surfaced for manual review.
    ///
    /// Absence of Album or Artist in the GT snapshot disables that part of the
    /// semantic gate (cannot enforce what was not recorded). Name is always required.
    /// </summary>
    public sealed class AudioAutoRepairEligibility : IAutoRepairEligibility
    {
        public string MediaType => "Audio";

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
                var gap = best.Score - rankedCandidates[1].Score;
                if (gap < minDistance)
                    return false;
            }

            // 3. Semantic gate — Name + Artist + Album
            if (!(best.LiveItem is Audio audio))
                return false;

            // Name — always required
            if (string.IsNullOrEmpty(gt.Name) ||
                !string.Equals(gt.Name, audio.Name, StringComparison.OrdinalIgnoreCase))
                return false;

            // Artist — required if captured
            if (!string.IsNullOrEmpty(GetPrimaryArtist(gt.Artists)))
            {
                var gtArtist = GetPrimaryArtist(gt.Artists);
                var candidateArtists = audio.Artists ?? Array.Empty<string>();
                var artistMatched = false;
                foreach (var a in candidateArtists)
                {
                    if (string.Equals(a, gtArtist, StringComparison.OrdinalIgnoreCase))
                    {
                        artistMatched = true;
                        break;
                    }
                }
                if (!artistMatched) return false;
            }

            // Album — required if captured
            if (!string.IsNullOrEmpty(gt.Album))
            {
                if (!string.Equals(gt.Album, audio.Album, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        private static string GetPrimaryArtist(List<string> artists)
        {
            if (artists == null || artists.Count == 0) return null;
            return artists[0];
        }
    }
}