using ListProtection.Storage;
using System.Collections.Generic;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Stateless scoring engine.
    ///
    /// Produces a ScoringResult with three independent scores:
    ///   ContentScore  — from Tier 1 rule table (first matching rule wins)
    ///   LocationScore — from Tier 2 folder depth facts (cumulative)
    ///   FallbackScore — from Tier 3 fallback facts (first matching rule wins,
    ///                   only when ContentScore == 0)
    ///
    /// LocationScore only contributes to composite Score when ContentScore > 0
    /// or FallbackScore > 0 — folder depth alone cannot surface a candidate.
    /// </summary>
    public static class CandidateScorer
    {
        public static ScoringResult Score(
            IEnumerable<EvidenceFact> tier1Facts,
            IEnumerable<EvidenceFact> tier2Facts,
            IEnumerable<EvidenceFact> tier3Facts,
            string mediaType)
        {
            var result = new ScoringResult();

            // ── Tier 1 — Content (first matching rule wins) ────────────────
            var t1Set = ToHashSet(tier1Facts, result.MatchedSignals);
            var contentRules = ScoringWeights.RulesFor(mediaType);
            foreach (var rule in contentRules)
            {
                if (rule.Matches(t1Set))
                {
                    result.ContentScore = rule.Score;
                    result.ContentRule = FormatRule(rule);
                    break;
                }
            }

            // ── Tier 3 — Fallback (only when no content score) ────────────
            if (result.ContentScore == 0)
            {
                var t3Set = ToHashSet(tier3Facts, result.MatchedSignals);
                foreach (var rule in ScoringWeights.FallbackRules)
                {
                    if (rule.Matches(t3Set))
                    {
                        result.FallbackScore = rule.Score;
                        result.FallbackRule = FormatRule(rule);
                        break;
                    }
                }
            }

            // ── Tier 2 — Location (stacks only when anchor exists) ─────────
            var hasAnchor = result.ContentScore > 0 || result.FallbackScore > 0;
            if (hasAnchor)
            {
                foreach (var fact in tier2Facts ?? System.Linq.Enumerable.Empty<EvidenceFact>())
                {
                    if (!fact.SignalName.StartsWith(FolderFacts.Prefix)) continue;
                    if (!int.TryParse(fact.SignalName.Substring(FolderFacts.Prefix.Length), out var depth)) continue;

                    var marginal = FolderFacts.WeightForDepth(depth);
                    result.LocationScore += marginal;
                    result.MatchedSignals.Add(fact.SignalName + ":" + marginal);
                }
            }

            result.CompositeScore = result.ContentScore + result.FallbackScore + result.LocationScore;
            return result;
        }

        private static HashSet<string> ToHashSet(IEnumerable<EvidenceFact> facts, List<string> log)
        {
            var set = new HashSet<string>();
            if (facts == null) return set;
            foreach (var f in facts)
            {
                set.Add(f.SignalName);
                log.Add(f.SignalName);
            }
            return set;
        }

        private static string FormatRule(ScoringRule rule)
            => string.Join("+", rule.RequiredFacts) + ":" + rule.Score;
    }
}