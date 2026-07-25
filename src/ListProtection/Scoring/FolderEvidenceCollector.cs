using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using System;
using System.Collections.Generic;
using System.IO;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Tier 2 collector — always runs regardless of media type.
    /// Emits cumulative folder depth facts by walking ancestor folder names
    /// upward from the immediate parent, comparing GT path ancestors against
    /// candidate path ancestors at the same depth.
    ///
    /// Each depth fact only fires if the shallower fact fired (conditional chain).
    /// Scoring is superlinear — deeper matches are disproportionately stronger.
    /// Weights are assigned by CandidateScorer rule table, not here.
    ///
    /// Facts emitted: FolderDepth1 .. FolderDepth{MaxDepth}
    /// MaxDepth = 10 (tunable constant).
    ///
    /// Folder signals are location corroboration — they require a content anchor
    /// (Tier 1 or Tier 3 signal) to be meaningful. The scorer enforces this by
    /// only adding LocationScore when ContentScore > 0.
    /// </summary>
    public sealed class FolderEvidenceCollector : IEvidenceCollector
    {
        public const int MaxDepth = 10;

        public string MediaType => null; // applies to all

        public IEnumerable<EvidenceFact> Collect(GroundTruthMember gt, BaseItem candidate)
        {
            var facts = new List<EvidenceFact>();

            if (gt == null || candidate == null) return facts;
            if (string.IsNullOrEmpty(gt.Path) || string.IsNullOrEmpty(candidate.Path)) return facts;

            var gtAncestors = GetAncestorNames(gt.Path);
            var candidateAncestors = GetAncestorNames(candidate.Path);

            var limit = Math.Min(Math.Min(gtAncestors.Count, candidateAncestors.Count), MaxDepth);

            for (var depth = 1; depth <= limit; depth++)
            {
                if (!string.Equals(
                        gtAncestors[depth - 1],
                        candidateAncestors[depth - 1],
                        StringComparison.OrdinalIgnoreCase))
                    break; // chain broken — no deeper facts fire

                facts.Add(new EvidenceFact(FolderFacts.Depth(depth)));
            }

            return facts;
        }

        /// <summary>
        /// Returns ancestor folder names from immediate parent upward.
        /// Index 0 = immediate parent, Index 1 = grandparent, etc.
        /// </summary>
        private static List<string> GetAncestorNames(string filePath)
        {
            var ancestors = new List<string>();
            try
            {
                var dir = Path.GetDirectoryName(filePath);
                while (!string.IsNullOrEmpty(dir))
                {
                    var name = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(name)) break;
                    ancestors.Add(name);
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch { /* never throw */ }
            return ancestors;
        }
    }

    public static class FolderFacts
    {
        public const string Prefix = "Folder.Depth";

        public static string Depth(int depth) => Prefix + depth;

        /// <summary>Depth weights — superlinear, tuned to verbal calibration.
        /// Depth 1 ~ interesting but almost meaningless.
        /// Depth 5 ~ near certainty as corroboration.
        /// Depth 6-10 ~ disambiguation, diminishing returns.
        /// Index 0 unused (depths are 1-based).
        /// </summary>
        public static readonly int[] Weights = { 0, 5, 20, 50, 80, 110, 118, 126, 134, 142, 150 };

        public static int WeightForDepth(int depth)
        {
            if (depth < 1 || depth >= Weights.Length) return 0;
            return Weights[depth] - (depth > 1 ? Weights[depth - 1] : 0); // marginal weight
        }

        public static int CumulativeWeightForDepth(int depth)
        {
            if (depth < 1 || depth >= Weights.Length) return 0;
            return Weights[depth];
        }
    }
}