using ListProtection.Storage;
using ListProtection.UIBaseClasses.Views;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ListProtection.UI.EventHistoryDialog
{
    /// <summary>
    /// Full-screen read-only dialog showing event history for a single protected playlist.
    /// Launched from Tab 1 OpenHistory column.
    ///
    /// No editing — all commandIds (including "DialogCancel") delegate to base.RunCommand
    /// which returns null, causing the framework to close the dialog.
    /// </summary>
    internal sealed class EventHistoryDialogView : PluginDialogView
    {
        private readonly string _playlistName;
        private readonly ILogger _logger;

        public EventHistoryDialogView(
            PluginInfo pluginInfo,
            string playlistId,
            string playlistName,
            EventStore eventStore,
            ILogger logger)
            : base(pluginInfo.Id)
        {
            _playlistName = playlistName;
            _logger = logger;

            ShowDialogFullScreen = true;
            AllowOk = false;
            AllowCancel = true;

            ContentData = Build(playlistId, playlistName, eventStore);
        }

        public override string Caption => "History: " + _playlistName;
        public override bool ShowDialogFullScreen { get; }

        public override Task OnCancelCommand() => Task.CompletedTask;

        public override Task OnOkCommand(string providerId, string commandId, string data)
            => Task.CompletedTask;

        public override Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
        {
            _logger.Info(
                "[EventHistoryDialogView] RunCommand | commandId={0} — delegating to base (closes dialog)",
                commandId ?? "(null)");

            return base.RunCommand(itemId, commandId, data);
        }

        // ── Build ──────────────────────────────────────────────────────────

        private static EventHistoryDialogUI Build(
            string playlistId,
            string playlistName,
            EventStore eventStore)
        {
            var events = eventStore.LoadForPlaylist(playlistId);

            if (events == null || events.Count == 0)
            {
                return EventHistoryDialogUI.Build(new[]
                {
                    new EventHistoryRow
                    {
                        Key = "synthetic_empty",
                        EventType = "—",
                        OccurredAt = string.Empty,
                        PayloadSummary = "No events recorded for this playlist.",
                        PayloadDetail = new PayloadRow[0]
                    }
                });
            }

            var rows = new List<EventHistoryRow>(events.Count);
            for (var i = 0; i < events.Count; i++)
            {
                var e = events[i];
                var rawPayload = e.Payload ?? string.Empty;

                // Split payload into lines, ignoring blank lines
                var lines = rawPayload.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                string summary;
                PayloadRow[] detail;

                if (lines.Length == 0)
                {
                    summary = "—";
                    detail = new PayloadRow[0];
                }
                else if (lines.Length == 1)
                {
                    // Single track — show inline, no expand needed
                    summary = lines[0].Trim();
                    detail = new PayloadRow[0];
                }
                else
                {
                    // Multiple tracks — summary invites expansion
                    summary = "Expand to see " + lines.Length + " tracks";
                    detail = new PayloadRow[lines.Length];
                    for (var j = 0; j < lines.Length; j++)
                    {
                        detail[j] = new PayloadRow
                        {
                            Idx = i + "_" + j,
                            Line = lines[j].Trim()
                        };
                    }
                }

                rows.Add(new EventHistoryRow
                {
                    Key = i.ToString(),
                    EventType = e.EventType ?? string.Empty,
                    OccurredAt = e.OccurredAt.ToString("yyyy-MM-dd HH:mm") + " UTC",
                    PayloadSummary = summary,
                    PayloadDetail = detail
                });
            }

            return EventHistoryDialogUI.Build(rows.ToArray());
        }
    }
}