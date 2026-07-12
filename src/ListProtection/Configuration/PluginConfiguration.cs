using MediaBrowser.Model.Plugins;
using System.Collections.Generic;

namespace PlaylistProtection
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public int ConfidenceThreshold { get; set; } = 70;
        public bool EnableAutoRepair { get; set; } = true;

        /// <summary>
        /// Global multiplier applied to all rule scores.
        /// Used to scale overall confidence sensitivity.
        /// </summary>
        public double GlobalWeightMultiplier { get; set; } = 1.0;

        /// <summary>
        /// Minimum confidence threshold required for a candidate to be considered valid.
        /// Candidates below this score should be ignored for repair selection.
        /// </summary>
        public int MinimumConfidenceThreshold { get; set; } = 50;

        /// <summary>
        /// Enables verbose rule scoring output for debugging and diagnostics.
        /// </summary>
        public bool EnableDebugScoring { get; set; } = false;

        /// <summary>
        /// Optional per-rule weighting overrides.
        /// Key = rule name, Value = weight multiplier.
        /// </summary>
        public Dictionary<string, double> RuleWeights { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Maximum number of candidates to evaluate per MissingItem.
        /// Prevents excessive library scanning cost.
        /// </summary>
        public int MaxCandidatesPerMissingItem { get; set; } = 50;

        /// <summary>
        /// When enabled, engine will log full per-rule breakdown per candidate.
        /// </summary>
        public bool LogRuleBreakdown { get; set; } = false;
    }
}