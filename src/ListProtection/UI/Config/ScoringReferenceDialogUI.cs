using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.DxGrid;
using MediaBrowser.Model.Attributes;
using System;
using System.ComponentModel;

namespace ListProtection.UI.Config
{
    public class ScoringReferenceDialogUI : EditableObjectBase
    {
        public override string EditorTitle => null;

        // ── Introduction ───────────────────────────────────────────────────

        [DisplayName("How scoring works")]
        public LabelItem IntroLabel { get; set; } = new LabelItem(
            "When a protected playlist member goes missing, the plugin scores every library " +
            "item of the same media type as a potential replacement candidate. Scoring has three " +
            "independent tiers. Tier 1 (Content) evaluates media-type metadata — name, artist, " +
            "album, track number, duration, and external IDs. Only the highest-scoring rule that " +
            "matches fires; lower rules are ignored. Tier 2 (Location) counts how many consecutive " +
            "ancestor folder names match the original file path, working upward from the immediate " +
            "parent. Tier 3 (Fallback) compares display name and filename stem when no Tier 1 rule " +
            "fires — covering poorly-tagged files or unsupported media types. " +
            "CompositeScore = ContentScore + LocationScore + FallbackScore.");

        [DisplayName("Location scoring requires a content anchor")]
        public LabelItem LocationAnchorLabel { get; set; } = new LabelItem(
            "Tier 2 (Location) only contributes to the composite score when Tier 1 or Tier 3 has " +
            "already scored above zero. Folder depth alone cannot surface a candidate — it is " +
            "corroborating evidence, not identity evidence.");

        // ── Threshold context ──────────────────────────────────────────────

        [DisplayName("Your current thresholds")]
        public LabelItem ThresholdLabel { get; set; }

        // ── Grid ───────────────────────────────────────────────────────────

        [GridDataSource(nameof(Rows))]
        public DxDataGrid ScoringGrid { get; set; }

        public ScoringReferenceRow[] Rows { get; set; } = Array.Empty<ScoringReferenceRow>();

        // ── Auto-repair eligibility gate note ──────────────────────────────

        [DisplayName("Audio auto-repair eligibility gate")]
        public LabelItem EligibilityGateLabel { get; set; } = new LabelItem(
            "In addition to the score and candidate-distance thresholds, audio auto-repair currently " +
            "applies a secondary semantic check: the candidate must match the missing member on name, " +
            "primary artist, and album (where those fields were captured at protect time). " +
            "This gate exists as a temporary safety floor because the scoring rule table is still " +
            "being calibrated — the weights are not yet differentiated enough to guarantee that no " +
            "wrong combination of signals can reach the auto-repair threshold unaided. As real-world " +
            "scoring data accumulates and rule weights are tightened, this gate will be removed. " +
            "The long-term design intention is that the score alone is sufficient to make the repair " +
            "decision safely.");

        // ── Feedback guidance ──────────────────────────────────────────────

        [DisplayName("Feedback — what to look for")]
        public LabelItem FeedbackWhatLabel { get; set; } = new LabelItem(
            "The scoring model is under active calibration and community observations are valuable. " +
            "The most useful reports are: (1) correct candidate found but ranked below a wrong one — " +
            "include both scores and which signals fired on each; (2) auto-repair acted on a wrong " +
            "candidate — most critical, include the full C/L/F score breakdown; (3) correct candidate " +
            "found but score seems low relative to your threshold — note which signals fired and which " +
            "did not, and whether the missing signals reflect actual metadata gaps or a scoring gap. " +
            "Expected noise: every missing audio track will attract sibling tracks from the same album " +
            "at score 25 (ArtistMatch + AlbumMatch only) — this is correct behaviour and the " +
            "threshold gates are designed to suppress it.");

        [DisplayName("Feedback — what to include")]
        public LabelItem FeedbackIncludeLabel { get; set; } = new LabelItem(
            "When reporting: media type | missing member name | correct candidate score (C / L / F) | " +
            "best wrong candidate score (C / L / F) | signals fired on each | your threshold settings. " +
            "The C/L/F breakdown for every scored candidate is written to the Emby server log when a " +
            "discovery run fires. Candidate scores are also visible in the Missing Members tab.");

        // ── Factory ────────────────────────────────────────────────────────

        public static ScoringReferenceDialogUI Build(
            ScoringReferenceRow[] rows,
            int autoRepairThreshold,
            int autoRepairDistance,
            int manualRepairThreshold,
            int manualRepairDistance)
        {
            var options = new DxGridOptions(
                new ScoringReferenceRow(),
                "Score",
                false,   // allowEdit
                true,    // allowSelect
                false,   // search
                false)   // filter
            {
                heightMode = DxGridOptions.GridHeightMode.fullHeight,
                columnAutoWidth = true
            };

            if (options.columns != null)
            {
                foreach (var col in options.columns)
                {
                    col.allowEditing = false;

                    if (col.dataField == "Tier")
                    {
                        col.width = 200;
                        col.groupIndex = 0;
                        col.showWhenGrouped = false;
                        col.autoExpandGroup = true;
                        col.allowHeaderFiltering = false;
                    }

                    if (col.dataField == "Score")
                    {
                        col.width = 70;
                        col.sortIndex = 0;
                        col.sortOrder = "desc";
                    }

                    if (col.dataField == "Signal")
                        col.width = 280;
                }
            }

            var thresholdText =
                "Auto-Repair: score threshold " + autoRepairThreshold +
                ", minimum candidate distance " + autoRepairDistance + ". " +
                "Manual 'Repair All (considerate)': score threshold " + manualRepairThreshold +
                ", minimum candidate distance " + manualRepairDistance + ". " +
                "Candidates that do not reach the relevant threshold are surfaced for manual review only.";

            return new ScoringReferenceDialogUI
            {
                ThresholdLabel = new LabelItem(thresholdText),
                ScoringGrid = new DxDataGrid(options),
                Rows = rows
            };
        }
    }
}