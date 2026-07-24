using System.ComponentModel;
using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using MediaBrowser.Model.Attributes;

namespace ListProtection.UI.Config
{
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
            "When enabled, missing playlist members are automatically repaired when a " +
            "candidate clears the score threshold, the minimum candidate distance, and the " +
            "semantic gate for its media type. Leave disabled until you are satisfied with " +
            "scoring results for your library.")]
        [AutoPostBack("updateconfig", nameof(AutoRepairEnabled))]
        public bool AutoRepairEnabled { get; set; } = false;

        [DisplayName("Score Threshold")]
        [Description(
            "Minimum score a candidate must achieve to be eligible for auto-repair. " +
            "Candidates below this score are surfaced for manual review only. Default: 150.")]
        [AutoPostBack("updateconfig", nameof(AutoRepairScoreThreshold))]
        public int AutoRepairScoreThreshold { get; set; } = 150;

        [DisplayName("Minimum Candidate Distance")]
        [Description(
            "Minimum score gap required between the top candidate and the second-best candidate. " +
            "If the gap is smaller, the repair is considered ambiguous and queued for manual review. " +
            "Set to 0 to disable the gap check. Default: 50.")]
        [AutoPostBack("updateconfig", nameof(AutoRepairMinCandidateDistance))]
        public int AutoRepairMinCandidateDistance { get; set; } = 50;

        [DisplayName("Scoring Reference")]
        [Description("Open the signal weight reference for all media types.")]
        [AutoPostBack("viewscoring", nameof(ViewScoringReference))]
        public bool ViewScoringReference { get; set; } = false;

        // ── Candidate Discovery ────────────────────────────────────────────

        public CaptionItem DiscoveryHeading { get; set; } = new CaptionItem("Candidate Discovery");

        [DisplayName("Auto-Discover Candidates on Detection")]
        [Description(
            "When enabled, candidate discovery runs automatically after missing members " +
            "are detected. When disabled, discovery only runs on the scheduled daily sweep " +
            "or a manual task trigger from the Emby dashboard.")]
        [AutoPostBack("updateconfig", nameof(AutoDiscoverCandidates))]
        public bool AutoDiscoverCandidates { get; set; } = true;

        // ── Duration Tolerances ────────────────────────────────────────────

        public CaptionItem DurationHeading { get; set; } = new CaptionItem("Duration Tolerances");

        [DisplayName("Audio Duration Tolerance (seconds)")]
        [Description(
            "Maximum duration difference (in seconds) for a duration signal to fire on Audio items. " +
            "Covers re-encodes and minor trim differences. Default: 2.")]
        [AutoPostBack("updateconfig", nameof(AudioDurationToleranceSeconds))]
        public int AudioDurationToleranceSeconds { get; set; } = 2;

        [DisplayName("Episode Duration Tolerance (seconds)")]
        [Description(
            "Maximum duration difference (in seconds) for a duration signal to fire on Episode items. " +
            "Covers intro/outro cuts across sources. Default: 5.")]
        [AutoPostBack("updateconfig", nameof(EpisodeDurationToleranceSeconds))]
        public int EpisodeDurationToleranceSeconds { get; set; } = 5;

        [DisplayName("Movie Duration Tolerance (seconds)")]
        [Description(
            "Maximum duration difference (in seconds) for a duration signal to fire on Movie items. " +
            "Covers edition cuts and encode differences. Default: 10.")]
        [AutoPostBack("updateconfig", nameof(MovieDurationToleranceSeconds))]
        public int MovieDurationToleranceSeconds { get; set; } = 10;
    }
}