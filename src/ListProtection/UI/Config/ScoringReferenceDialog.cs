using ListProtection.Scoring;
using ListProtection.UIBaseClasses.Views;
using MediaBrowser.Model.Plugins.UI.Views;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ListProtection.UI.Config
{
    /// <summary>
    /// Read-only full-screen dialog showing signal weights for all media types.
    /// Launched from the "View Scoring Reference" button on the config page.
    ///
    /// AllowOk = false, AllowCancel = true — Close button only.
    /// All RunCommand calls delegate to base (returns null = framework closes dialog).
    /// </summary>
    internal sealed class ScoringReferenceDialog : PluginDialogView
    {
        public ScoringReferenceDialog(string pluginId)
            : base(pluginId)
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

        // ── Build ──────────────────────────────────────────────────────────

        private static ScoringReferenceDialogUI Build()
        {
            var reference = ScoringWeights.GetScoringReference();
            var rows = new List<ScoringReferenceRow>();

            foreach (var group in reference)
            {
                var mediaType = group.Key;
                foreach (var (signal, weight, description) in group.Value)
                {
                    rows.Add(new ScoringReferenceRow
                    {
                        MediaType = mediaType,
                        Score = weight,
                        Signal = signal,
                        Description = description
                    });
                }
            }

            return ScoringReferenceDialogUI.Build(rows.ToArray());
        }
    }
}