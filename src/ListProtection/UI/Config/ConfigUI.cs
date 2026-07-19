using System.ComponentModel;
using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using MediaBrowser.Model.Attributes;

namespace ListProtection.UI.Config
{
    /// <summary>
    /// View model for the configuration tab. Rendered by Emby's GenericEdit
    /// infrastructure. Never persisted — ConfigPageView maps between this and
    /// PluginConfiguration (the actual persistence model) on every AutoPostBack.
    ///
    /// CaptionItem fields are presentation-only and not serialised because this
    /// class is assigned as ContentData, not saved via any store or
    /// SaveConfiguration().
    /// </summary>
    public class ConfigUI : EditableOptionsBase
    {
        public override string EditorTitle => "List Protection — Configuration";

        public override string EditorDescription =>
            "Controls automatic repair and candidate discovery behaviour. " +
            "Changes take effect immediately.";

        // ── Auto-Repair ────────────────────────────────────────────────────

        public CaptionItem AutoRepairHeading { get; set; } = new CaptionItem("Auto-Repair");

        [DisplayName("Enable Auto-Repair")]
        [Description(
            "When enabled, missing playlist members are automatically repaired after " +
            "candidate discovery if the best candidate meets the score threshold below. " +
            "Leave disabled until you are confident in the scoring results for your library.")]
        [AutoPostBack("updateconfig", nameof(AutoRepairEnabled))]
        public bool AutoRepairEnabled { get; set; } = false;

        [DisplayName("Auto-Repair Score Threshold")]
        [Description(
            "Minimum candidate score required for an automatic repair. " +
            "100 = FilenameStemExact signal alone. " +
            "160 = FilenameStemExact + NameExact (very high confidence). " +
            "20 = ParentFolderMatch only (album sibling — not recommended for auto-repair).")]
        [AutoPostBack("updateconfig", nameof(AutoRepairThreshold))]
        public int AutoRepairThreshold { get; set; } = 100;

        [DisplayName("Max Auto-Repairs Per Run")]
        [Description(
            "Safety cap on the number of members repaired in a single discovery run. " +
            "Prevents a large folder rename from generating unbounded write load during " +
            "an active library scan. Deferred repairs are picked up in the next cycle. " +
            "Set to 0 for no limit.")]
        [AutoPostBack("updateconfig", nameof(AutoRepairMaxPerRun))]
        public int AutoRepairMaxPerRun { get; set; } = 10;

        // ── Candidate Discovery ────────────────────────────────────────────

        public CaptionItem DiscoveryHeading { get; set; } = new CaptionItem("Candidate Discovery");

        [DisplayName("Auto-Discover Candidates on Detection")]
        [Description(
            "When enabled, candidate discovery runs automatically after missing members " +
            "are detected via file rename or removal events. " +
            "When disabled, discovery only runs on the scheduled daily sweep or " +
            "a manual task trigger from the Emby dashboard.")]
        [AutoPostBack("updateconfig", nameof(AutoDiscoverCandidates))]
        public bool AutoDiscoverCandidates { get; set; } = true;
    }
}