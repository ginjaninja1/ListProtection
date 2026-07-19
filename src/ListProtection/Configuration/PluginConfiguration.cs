using MediaBrowser.Model.Plugins;

namespace ListProtection.Configuration
{
    /// <summary>
    /// The plugin's persisted settings. Serialised to XML by Emby's native
    /// BasePlugin&lt;T&gt; mechanism via Plugin.Instance.Configuration /
    /// SaveConfiguration(). No custom store, no hand-rolled JSON round-trip.
    ///
    /// This class has no UI members — it is never assigned as ContentData.
    /// ConfigUI is the separate view-model rendered by GenericEdit; it reads
    /// from and writes back to this class via ConfigPageView.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Master switch. When false, no automatic repairs fire regardless of
        /// other settings. Defaults to false — enable only once scoring results
        /// have been validated in your library.
        /// </summary>
        public bool AutoRepairEnabled { get; set; } = false;

        /// <summary>
        /// Minimum candidate score required for an automatic repair.
        ///   100 = FilenameStemExact alone qualifies (strong signal)
        ///   160 = FilenameStemExact + NameExact (very high confidence)
        ///    20 = ParentFolderMatch only (album sibling — not recommended)
        /// Default: 100.
        /// </summary>
        public int AutoRepairThreshold { get; set; } = 100;

        /// <summary>
        /// Safety cap on the number of members auto-repaired in a single
        /// discovery run. Prevents a single large folder rename from
        /// generating unbounded write load during an active library scan.
        /// Repairs deferred by this cap are picked up in the next cycle.
        /// Set to 0 for no limit. Default: 10.
        /// </summary>
        public int AutoRepairMaxPerRun { get; set; } = 10;

        /// <summary>
        /// When true, candidate discovery runs automatically after detection
        /// events (file rename, ItemRemoved). When false, discovery only runs
        /// on the scheduled daily sweep or a manual dashboard task trigger.
        /// Default: true.
        /// </summary>
        public bool AutoDiscoverCandidates { get; set; } = true;
    }
}