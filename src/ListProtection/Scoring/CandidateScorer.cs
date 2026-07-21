using System.Collections.Generic;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Stateless scoring engine.
    ///
    /// Accepts a collection of EvidenceFacts and sums the weights from
    /// ScoringWeights for each recognised signal name.
    ///
    /// Returns a (score, matchedSignals) pair — the matchedSignals list
    /// matches the existing CandidateEntry.MatchedSignals format
    /// ("SignalName:Weight") so no store schema change is needed there.
    ///
    /// Facts with no matching weight are silently ignored — they do not
    /// contribute to the score but are not an error condition.
    /// </summary>
    public static class CandidateScorer
    {
        /// <summary>
        /// Scores a set of evidence facts.
        /// </summary>
        /// <param name="facts">Facts produced by one or more IEvidenceCollectors.</param>
        /// <param name="score">Total score.</param>
        /// <param name="matchedSignals">Human-readable signal list ("Name:Weight").</param>
        public static void Score(
            IEnumerable<EvidenceFact> facts,
            out int score,
            out List<string> matchedSignals)
        {
            score = 0;
            matchedSignals = new List<string>();

            if (facts == null) return;

            foreach (var fact in facts)
            {
                var weight = ScoringWeights.Get(fact.SignalName);
                if (weight <= 0) continue;

                score += weight;
                matchedSignals.Add(fact.SignalName + ":" + weight);
            }
        }
    }
}