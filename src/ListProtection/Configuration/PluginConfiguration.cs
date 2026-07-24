using MediaBrowser.Model.Plugins;

namespace ListProtection.Configuration
{
    /// <summary>
    /// The plugin's persisted settings. Serialised to XML by Emby's native
    /// BasePlugin&lt;T&gt; mechanism via Plugin.Instance.Configuration /
    /// SaveConfiguration(). No custom store, no hand-rolled JSON round-trip.
    ///
    /// Auto-repair eligibility for Audio is governed by a hard semantic gate
    /// (AudioAutoRepairEligibility: name + artist + album must all match).
    /// AutoRepairThreshold and AutoRepairMaxPerRun have been removed — the gate
    /// either passes or it doesn't, and there is no reason to artificially cap runs.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Master switch. When false, no automatic repairs fire.
        /// Defaults to false — enable only once you have validated scoring results.
        /// </summary>
        public bool AutoRepairEnabled { get; set; } = false;

        /// <summary>
        /// When true, discovery runs automatically after detection events.
        /// When false, only runs on the scheduled daily sweep or manual trigger.
        /// </summary>
        public bool AutoDiscoverCandidates { get; set; } = true;
    }
}