using ListProtection.Storage;
using ListProtection.UIBaseClasses.Views;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ListProtection.UI.GroundTruthDialog
{
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

            ContentData = Build(playlistId, groundTruthStore);
        }

        public override string Caption => "Members: " + _playlistName;
        public override bool ShowDialogFullScreen { get; }

        public override Task OnCancelCommand() => Task.CompletedTask;

        public override Task OnOkCommand(string providerId, string commandId, string data)
            => Task.CompletedTask;

        public override Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
        {
            _logger.Info(
                "[GroundTruthDialogView] RunCommand | commandId={0} — delegating to base (closes dialog)",
                commandId ?? "(null)");

            return base.RunCommand(itemId, commandId, data);
        }

        private static GroundTruthDialogUI Build(string playlistId, GroundTruthStore groundTruthStore)
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
                        MediaType = string.Empty,
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
                    MediaType = members[i].MediaType ?? string.Empty,
                    Name = members[i].Name ?? "(unnamed)",
                    Path = members[i].Path ?? string.Empty
                };
            }

            return GroundTruthDialogUI.Build(rows);
        }
    }
}