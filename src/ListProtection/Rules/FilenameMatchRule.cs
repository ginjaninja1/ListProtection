using System;
using PlaylistProtection.Core.Confidence;
using PlaylistProtection.Core.Models;

namespace PlaylistProtection.Core.Rules
{
    /// <summary>
    /// Confidence rule that evaluates similarity based on filename match.
    /// Strong signal for movies/TV, weaker for audio.
    /// </summary>
    public class FilenameMatchRule : IConfidenceRule
    {
        public string Name => "FilenameMatch";

        public int Evaluate(MissingItem missing, CandidateItem candidate)
        {
            if (missing == null || candidate == null)
                return 0;

            if (string.IsNullOrWhiteSpace(missing.Name) ||
                string.IsNullOrWhiteSpace(candidate.Path))
                return 0;

            var missingName = missing.Name.Trim();
            var candidatePath = candidate.Path.Trim();

            // Exact filename match (strong signal)
            if (candidatePath.EndsWith(missingName, StringComparison.OrdinalIgnoreCase))
            {
                return 90;
            }

            // Loose containment match (weaker signal)
            if (candidatePath.IndexOf(missingName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 60;
            }

            // Partial filename token match (very weak signal)
            var missingTokens = missingName
    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var token in missingTokens)
            {
                if (candidatePath.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return 30;
                }
            }

            return 0;
        }
    }
}