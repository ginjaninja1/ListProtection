using ListProtection.Storage;
using ListProtection.UIBaseClasses.Views;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ListProtection.UI.GroundTruthDialog
{
    /// <summary>
    /// Full-screen read-only dialog showing the ground truth member snapshot
    /// for a single protected playlist. Launched from Tab 1 OpenGroundTruth column.
    ///
    /// No editing — no onChangeCommand. Any commandId (including "DialogCancel")
    /// is unhandled and delegates to base.RunCommand which returns null,
    /// causing the framework to close the dialog.
    ///
    /// Pattern: PluginDialogView (UIBaseClasses/Views/PluginDialogueView.cs)
    ///   ShowDialogFullScreen = true
    ///   AllowOk = false
    ///   AllowCancel = true
    ///   Caption: "Members: {playlistName}"
    /// </summary>
    internal sealed class GroundTruthDialogView : PluginDialogView
    {
        private readonly string _playlistName;
        private readonly ILogger _logger;

        public GroundTruthDialogView(
            PluginInfo pluginInfo,
            string playlistId,
            string playlistName,
            GroundTruthStore groundTruthStore,
            ILogger logger)
            : base(pluginInfo.Id)
        {
            _playlistName = playlistName;
            _logger = logger;

            ShowDialogFullScreen = true;
            AllowOk = false;
            AllowCancel = true;

            ContentData = Build(playlistId, playlistName, groundTruthStore);
        }

        public override string Caption => "Members: " + _playlistName;
        public override bool ShowDialogFullScreen { get; }

        public override Task OnCancelCommand() => Task.CompletedTask;

        public override Task OnOkCommand(string providerId, string commandId, string data)
            => Task.CompletedTask;

        /// <summary>
        /// Read-only dialog — no interactive commands expected.
        /// All commandIds (including "DialogCancel") delegate to base which
        /// returns null, causing the framework to close the dialog.
        /// </summary>
        public override Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
        {
            _logger.Info(
                "[GroundTruthDialogView] RunCommand | commandId={0} — delegating to base (closes dialog)",
                commandId ?? "(null)");

            return base.RunCommand(itemId, commandId, data);
        }

        // ── Build ──────────────────────────────────────────────────────────

        private static GroundTruthDialogUI Build(
            string playlistId,
            string playlistName,
            GroundTruthStore groundTruthStore)
        {
            List<GroundTruthMember> members = null;

            if (groundTruthStore.Load().TryGetValue(playlistId, out var entry))
                members = entry.Members;

            if (members == null || members.Count == 0)
            {
                return GroundTruthDialogUI.Build(new[]
                {
                    new GroundTruthMemberRow
                    {
                        Position = 0,
                        Name = "No members captured",
                        Path = string.Empty
                    }
                });
            }

            var rows = new GroundTruthMemberRow[members.Count];
            for (var i = 0; i < members.Count; i++)
            {
                rows[i] = new GroundTruthMemberRow
                {
                    Position = i + 1,
                    Name = members[i].Name ?? "(unnamed)",
                    Path = members[i].Path ?? string.Empty
                };
            }

            return GroundTruthDialogUI.Build(rows);
        }
    }
}