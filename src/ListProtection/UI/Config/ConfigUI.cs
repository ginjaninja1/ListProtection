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
            "";

        // ── Scoring Reference ──────────────────────────────────────────────

        [DisplayName("Scoring Reference")]
        [Description("View signal weights used to score replacement candidates for all media types.")]
        public ButtonItem ViewScoringReference { get; set; } = new ButtonItem
        {
            CommandId = "viewscoring",
            Caption = "View Scoring Reference"
        };

        // ── Auto-Repair ────────────────────────────────────────────────────

        public CaptionItem AutoRepairHeading { get; set; } = new CaptionItem("Auto-Repair");

        [DisplayName("Enable Auto-Repair")]
        [Description(
            "Missing playlist members are automatically repaired during event driven and scheduled analysis. Leave disabled until you are satisfied with " +
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

        // ── Manual Repair ──────────────────────────────────────────────────

        public CaptionItem ManualRepairHeading { get; set; } = new CaptionItem("Manual Repair");

        [DisplayName("Score Threshold")]
        [Description(
            "Minimum score a candidate must achieve for 'Repair All (considerate)' to act on it. " +
            "Members whose best candidate falls below this score are skipped and left for per-row review. Default: 100.")]
        [AutoPostBack("updateconfig", nameof(ManualRepairScoreThreshold))]
        public int ManualRepairScoreThreshold { get; set; } = 100;

        [DisplayName("Minimum Candidate Distance")]
        [Description(
            "Minimum score gap required between the top and second-best candidate before " +
            "'Repair All (considerate)' will proceed. Set to 0 to disable the gap check. Default: 30.")]
        [AutoPostBack("updateconfig", nameof(ManualRepairMinCandidateDistance))]
        public int ManualRepairMinCandidateDistance { get; set; } = 30;

        // ── Real-time Protection ───────────────────────────────────────────

        public CaptionItem RealTimeHeading { get; set; } = new CaptionItem("Real-time Protection");

        public LabelItem RealTimeCaption { get; set; } = new LabelItem("Optional, more responsive event drive repair. Alternatively, rely on just the scheduled task.");

        [DisplayName("Event Driven Repair")]
        [Description(
            "When enabled, missing member detection and candidate discovery run in response " +
            "to library events such as file renames and folder changes. When disabled, protection " +
            "relies solely on the scheduled post-scan and daily sweep tasks.")]
        [AutoPostBack("updateconfig", nameof(EventDrivenRepairEnabled))]
        public bool EventDrivenRepairEnabled { get; set; } = true;

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