using ListProtection.Scoring;
using ListProtection.UIBaseClasses.Views;
using MediaBrowser.Model.Plugins.UI.Views;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ListProtection.UI.Config
{
    internal sealed class ScoringReferenceDialog : PluginDialogView
    {
        private static readonly HashSet<string> _corroboratingSignals = new HashSet<string>
        {
            nameof(ScoringWeights.NameExact),
            nameof(ScoringWeights.NameNormalized),
            nameof(ScoringWeights.FilenameStemExact),
            nameof(ScoringWeights.FilenameStemNormalized),
            nameof(ScoringWeights.ParentFolderMatch),
            nameof(ScoringWeights.GrandparentFolderMatch),
        };

        private const string IdentityLabel = "Identity — best match wins";
        private const string CorroboratingLabel = "Corroborating — all matching stack";

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

        private static ScoringReferenceDialogUI Build()
        {
            var reference = ScoringWeights.GetScoringReference();
            var rows = new List<ScoringReferenceRow>();

            // Collect the BaseItem (corroborating) signals once — emitted into every media type group
            var baseItemSignals = new List<(string signal, int weight, string description)>();
            if (reference.TryGetValue("All media types", out var baseItems))
                baseItemSignals.AddRange(baseItems);

            foreach (var group in reference)
            {
                var mediaType = group.Key;
                if (mediaType == "All media types") continue; // handled by injection below

                // Identity signals for this media type
                foreach (var (signal, weight, description) in group.Value)
                {
                    rows.Add(new ScoringReferenceRow
                    {
                        MediaType = mediaType,
                        SignalType = IdentityLabel,
                        Score = weight,
                        Signal = signal,
                        Description = description,
                    });
                }

                // Corroborating signals — injected into every media type group
                foreach (var (signal, weight, description) in baseItemSignals)
                {
                    rows.Add(new ScoringReferenceRow
                    {
                        MediaType = mediaType,
                        SignalType = CorroboratingLabel,
                        Score = weight,
                        Signal = signal,
                        Description = description,
                    });
                }
            }

            return ScoringReferenceDialogUI.Build(rows.ToArray());
        }
    }
}