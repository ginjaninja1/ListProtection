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
    /// Auto-repair is governed by a hard semantic gate (name + artist + album).
    /// AutoRepairThreshold and AutoRepairMaxPerRun have been removed.
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
            "candidate discovery if the candidate passes the eligibility gate: " +
            "track name, artist, and album must all match exactly. " +
            "Leave disabled until you are confident in scoring results for your library.")]
        [AutoPostBack("updateconfig", nameof(AutoRepairEnabled))]
        public bool AutoRepairEnabled { get; set; } = false;

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