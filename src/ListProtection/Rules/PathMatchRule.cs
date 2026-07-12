using System;
using PlaylistProtection.Core.Confidence;
using PlaylistProtection.Core.Models;

namespace PlaylistProtection.Core.Rules
{
    /// <summary>
    /// Confidence rule that evaluates how closely the file path
    /// matches the expected structure of a missing item.
    /// </summary>
    public class PathMatchRule : IConfidenceRule
    {
        public string Name => "PathMatch";

        public int Evaluate(MissingItem missing, CandidateItem candidate)
        {
            if (missing == null || candidate == null)
                return 0;

            if (string.IsNullOrWhiteSpace(missing.Path) ||
                string.IsNullOrWhiteSpace(candidate.Path))
                return 0;

            var missingPath = missing.Path.Trim();
            var candidatePath = candidate.Path.Trim();

            // Exact path match (rare but strongest signal)
            if (candidatePath.Equals(missingPath, StringComparison.OrdinalIgnoreCase))
            {
                return 100;
            }

            // Full directory containment (strong structural match)
            if (candidatePath.StartsWith(missingPath, StringComparison.OrdinalIgnoreCase))
            {
                return 80;
            }

            // Parent folder match (medium signal)
            var missingDir = GetParentDirectory(missingPath);
            var candidateDir = GetParentDirectory(candidatePath);

            if (!string.IsNullOrEmpty(missingDir) &&
                !string.IsNullOrEmpty(candidateDir) &&
                candidateDir.Equals(missingDir, StringComparison.OrdinalIgnoreCase))
            {
                return 60;
            }

            // Loose directory overlap (weak signal)
            if (!string.IsNullOrEmpty(missingDir) &&
                candidatePath.IndexOf(missingDir, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 30;
            }

            return 0;
        }

        private string GetParentDirectory(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return null;

                var normalized = path.Replace('\\', '/');

                var lastSlash = normalized.LastIndexOf('/');
                if (lastSlash <= 0)
                    return null;

                return normalized.Substring(0, lastSlash);
            }
            catch
            {
                return null;
            }
        }
    }
}