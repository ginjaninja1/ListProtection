using PlaylistProtection.Core.Models;
using PlaylistProtection.Core.Rules;

using System;
using System.Collections.Generic;
using System.Linq;

namespace PlaylistProtection.Core.Confidence
{
    /// <summary>
    /// Deterministic scoring engine that evaluates candidate matches
    /// for a missing playlist item using rule-based scoring.
    /// </summary>
    public class ConfidenceEngine
    {
        private readonly List<IConfidenceRule> _rules;

        public ConfidenceEngine()
        {
            _rules = new List<IConfidenceRule>
            {
                new FilenameMatchRule(),
                new PathMatchRule()
            };
        }

        /// <summary>
        /// Evaluates all candidates against a missing item and returns ranked results.
        /// </summary>
        public List<CandidateItem> Evaluate(MissingItem missing, List<CandidateItem> candidates)
        {
            if (missing == null)
                throw new ArgumentNullException(nameof(missing));

            if (candidates == null || candidates.Count == 0)
                return new List<CandidateItem>();

            foreach (var candidate in candidates)
            {
                int totalScore = 0;

                candidate.RuleScores.Clear();

                foreach (var rule in _rules)
                {
                    int score = rule.Evaluate(missing, candidate);

                    candidate.RuleScores[rule.Name] = score;
                    totalScore += score;
                }

                candidate.Confidence = Clamp(totalScore);
                candidate.LastEvaluatedUtc = DateTime.UtcNow;
            }

            return candidates
                .OrderByDescending(c => c.Confidence)
                .ToList();
        }

        /// <summary>
        /// Returns the single best match (if any).
        /// </summary>
        public CandidateItem BestMatch(MissingItem missing, List<CandidateItem> candidates)
        {
            return Evaluate(missing, candidates)
                .FirstOrDefault();
        }

        /// <summary>
        /// Ensures confidence stays within valid bounds.
        /// </summary>
        private int Clamp(int value)
        {
            if (value < 0) return 0;
            if (value > 100) return 100;
            return value;
        }
    }
}