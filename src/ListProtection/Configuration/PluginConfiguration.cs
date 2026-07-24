using MediaBrowser.Model.Plugins;

namespace ListProtection.Configuration
{
    /// <summary>
    /// The plugin's persisted settings. Serialised to XML by Emby's native
    /// BasePlugin&lt;T&gt; mechanism via Plugin.Instance.Configuration /
    /// SaveConfiguration(). No custom store, no hand-rolled JSON round-trip.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        // ── Auto-Repair ────────────────────────────────────────────────────

        /// <summary>
        /// Master switch. When false, no automatic repairs fire.
        /// Defaults to false — enable only once you have validated scoring results.
        /// </summary>
        public bool AutoRepairEnabled { get; set; } = false;

        /// <summary>
        /// Minimum composite score a candidate must reach to be considered for
        /// auto-repair. Candidates below this threshold are surfaced for manual
        /// review regardless of eligibility gate result.
        /// Default: 150.
        /// </summary>
        public int AutoRepairScoreThreshold { get; set; } = 150;

        /// <summary>
        /// Minimum score gap between the top candidate and the second-best candidate
        /// required before auto-repair will proceed. If the gap is smaller, the repair
        /// is ambiguous and the item is queued for manual review.
        /// Default: 50. Set to 0 to disable the gap check.
        /// </summary>
        public int AutoRepairMinCandidateDistance { get; set; } = 50;

        // ── Candidate Discovery ────────────────────────────────────────────

        /// <summary>
        /// When true, discovery runs automatically after detection events.
        /// When false, only runs on the scheduled daily sweep or manual trigger.
        /// </summary>
        public bool AutoDiscoverCandidates { get; set; } = true;

        // ── Duration tolerances (per media type, in seconds) ───────────────

        /// <summary>
        /// Maximum duration delta (seconds) for a duration signal to fire on Audio items.
        /// Covers re-encodes and minor trim differences. Default: 2.
        /// </summary>
        public int AudioDurationToleranceSeconds { get; set; } = 2;

        /// <summary>
        /// Maximum duration delta (seconds) for a duration signal to fire on Episode items.
        /// Covers intro/outro cuts across sources. Default: 5.
        /// </summary>
        public int EpisodeDurationToleranceSeconds { get; set; } = 5;

        /// <summary>
        /// Maximum duration delta (seconds) for a duration signal to fire on Movie items.
        /// Covers edition cuts and encode differences. Default: 10.
        /// </summary>
        public int MovieDurationToleranceSeconds { get; set; } = 10;
    }
}