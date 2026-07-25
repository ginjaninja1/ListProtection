using System.Collections.Generic;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Output of CandidateScorer.Score().
    /// Three independent scores allow threshold gates to reason about each tier separately.
    /// </summary>
    public sealed class ScoringResult
    {
        /// <summary>Score from Tier 1 media-type rule table. 0 if no rule matched.</summary>
        public int ContentScore { get; set; }

        /// <summary>Score from Tier 3 fallback rule table. 0 if ContentScore > 0 or no rule matched.</summary>
        public int FallbackScore { get; set; }

        /// <summary>Cumulative folder depth score. 0 if no content anchor present.</summary>
        public int LocationScore { get; set; }

        /// <summary>ContentScore + FallbackScore + LocationScore.</summary>
        public int CompositeScore { get; set; }

        /// <summary>Human-readable description of the content rule that fired.</summary>
        public string ContentRule { get; set; }

        /// <summary>Human-readable description of the fallback rule that fired.</summary>
        public string FallbackRule { get; set; }

        /// <summary>All atomic facts observed and folder facts that contributed.</summary>
        public List<string> MatchedSignals { get; } = new List<string>();
    }
}