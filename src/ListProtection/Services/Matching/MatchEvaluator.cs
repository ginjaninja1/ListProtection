using System;
using System.Collections.Generic;
using System.Linq;
using PlaylistProtection.Data.Entities;

namespace PlaylistProtection.Services.Matching
{
    /// <summary>
    /// Deterministic scoring engine for MissingMember → MatchCandidate evaluation.
    /// Uses PluginConfiguration for scoring behavior.
    /// </summary>
    public class MatchEvaluator
    {
        private readonly PluginConfiguration _config;

        public MatchEvaluator(PluginConfiguration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Scores and ranks candidates for a MissingMember.
        /// </summary>
        public List<MatchCandidate> Evaluate(MissingMember missing, List<MatchCandidate> candidates)
        {
            if (missing == null) throw new ArgumentNullException(nameof(missing));
            if (candidates == null || candidates.Count == 0)
                return new List<MatchCandidate>();

            var results = new List<MatchCandidate>();

            foreach (var candidate in candidates)
            {
                candidate.Confidence = 0;
                candidate.RuleScores.Clear();

                ApplyNameRules(missing, candidate);
                ApplyPathRules(missing, candidate);

                ApplyGlobalMultiplier(candidate);

                results.Add(candidate);
            }

            return results
                .Where(c => c.Confidence >= _config.MinimumConfidenceThreshold)
                .OrderByDescending(c => c.Confidence)
                .ToList();
        }

        /// <summary>
        /// Convenience helper used by AutoRepairTask.
        /// </summary>
        public MatchCandidate CreateBestCandidate(MissingMember missing, List<MatchCandidate> candidates)
        {
            return Evaluate(missing, candidates).FirstOrDefault();
        }

        // --------------------------------------------------
        // RULES
        // --------------------------------------------------

        private void ApplyNameRules(MissingMember missing, MatchCandidate candidate)
        {
            if (string.IsNullOrWhiteSpace(missing.ExpectedName) ||
                string.IsNullOrWhiteSpace(candidate.Name))
                return;

            if (string.Equals(missing.ExpectedName, candidate.Name, StringComparison.OrdinalIgnoreCase))
            {
                AddScore(candidate, "ExactNameMatch", 100);
            }
            else if (candidate.Name?.IndexOf(missing.ExpectedName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddScore(candidate, "PartialNameMatch", 50);
            }
        }

        private void ApplyPathRules(MissingMember missing, MatchCandidate candidate)
        {
            if (string.IsNullOrWhiteSpace(missing.ExpectedPath) ||
                string.IsNullOrWhiteSpace(candidate.Path))
                return;

            if (string.Equals(missing.ExpectedPath, candidate.Path, StringComparison.OrdinalIgnoreCase))
            {
                AddScore(candidate, "ExactPathMatch", 80);
            }
            else if (candidate.Path?.IndexOf(missing.ExpectedPath, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddScore(candidate, "PartialPathMatch", 30);
            }
        }

        // --------------------------------------------------
        // SCORING CORE
        // --------------------------------------------------

        private void ApplyGlobalMultiplier(MatchCandidate candidate)
        {
            if (_config.GlobalWeightMultiplier == 1.0)
                return;

            candidate.Confidence = (int)(candidate.Confidence * _config.GlobalWeightMultiplier);
        }

        private void AddScore(MatchCandidate candidate, string rule, int score)
        {
            if (_config.RuleWeights != null &&
                _config.RuleWeights.TryGetValue(rule, out var weight))
            {
                score = (int)(score * weight);
            }

            candidate.Confidence += score;

            if (!candidate.RuleScores.ContainsKey(rule))
                candidate.RuleScores[rule] = 0;

            candidate.RuleScores[rule] += score;
        }
    }
}