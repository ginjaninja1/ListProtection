using ListProtection.Scoring;
using ListProtection.UIBaseClasses.Views;
using MediaBrowser.Model.Plugins.UI.Views;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ListProtection.UI.Config
{
    internal sealed class ScoringReferenceDialog : PluginDialogView
    {
        private const string IdentityLabel = "Identity — best match wins";
        private const string CorroboratingLabel = "Corroborating — all matching stack";

        public ScoringReferenceDialog(string pluginId) : base(pluginId)
        {
            ShowDialogFullScreen = true;
            AllowOk = false;
            AllowCancel = true;
            ContentData = Build();
        }

        public override string Caption => "Scoring Reference";
        public override bool ShowDialogFullScreen { get; }

        public override Task OnCancelCommand() => Task.CompletedTask;

        public override Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
            => base.RunCommand(itemId, commandId, data);

        private static ScoringReferenceDialogUI Build()
        {
            var rows = new List<ScoringReferenceRow>();

            // ── Tier 1 — media-type content rules (identity, best match wins) ──
            AddRules(rows, "Audio", ScoringWeights.AudioRules, IdentityLabel);
            AddRules(rows, "Episode", ScoringWeights.EpisodeRules, IdentityLabel);
            AddRules(rows, "Movie", ScoringWeights.MovieRules, IdentityLabel);

            // ── Tier 3 — fallback rules injected into every media type ─────────
            foreach (var mediaType in new[] { "Audio", "Episode", "Movie" })
                AddRules(rows, mediaType, ScoringWeights.FallbackRules, CorroboratingLabel);

            // ── Tier 2 — folder depth (corroborating, always stacks) ───────────
            for (var depth = 1; depth <= FolderEvidenceCollector.MaxDepth; depth++)
            {
                var cumulative = FolderFacts.CumulativeWeightForDepth(depth);
                var marginal = FolderFacts.WeightForDepth(depth);

                foreach (var mediaType in new[] { "Audio", "Episode", "Movie" })
                {
                    rows.Add(new ScoringReferenceRow
                    {
                        MediaType = mediaType,
                        SignalType = CorroboratingLabel,
                        Score = marginal,
                        Signal = FolderFacts.Depth(depth),
                        Description = "Folder depth " + depth +
                                      " — " + depth + " consecutive ancestor name(s) match GT path" +
                                      " (cumulative total: " + cumulative + ")"
                    });
                }
            }

            return ScoringReferenceDialogUI.Build(rows.ToArray());
        }

        private static void AddRules(
            List<ScoringReferenceRow> rows,
            string mediaType,
            List<ScoringRule> rules,
            string signalType)
        {
            foreach (var rule in rules)
            {
                rows.Add(new ScoringReferenceRow
                {
                    MediaType = mediaType,
                    SignalType = signalType,
                    Score = rule.Score,
                    Signal = string.Join(" + ", rule.RequiredFacts),
                    Description = signalType == IdentityLabel
                        ? "First matching rule fires — higher rules take precedence"
                        : "Fires when ContentScore == 0 (no Tier 1 rule matched)"
                });
            }
        }
    }
}