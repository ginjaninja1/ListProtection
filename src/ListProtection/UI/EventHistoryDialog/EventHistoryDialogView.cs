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
    /// Full-screen read-only dialog showing event history for a single playlist.
    /// Launched from Tab 1 OpenHistory column.
    ///
    /// Available for both currently-protected and previously-protected playlists —
    /// history is keyed by PlaylistId in the EventStore and persists after unprotect.
    ///
    /// No editing — all commandIds (including "DialogCancel") delegate to base.RunCommand
    /// which returns null, causing the framework to close the dialog.
    ///
    /// PayloadSummary wording by event type:
    ///   Protect         — "X items protected"  (capped at first 10 in child grid)
    ///   Unprotect       — "(no detail)"
    ///   MissingDetected — "X item(s) missing"  or single item name inline
    ///   CandidateFound  — "X candidate(s) found" or single item name inline
    ///   Repair          — "X item(s) repaired"  or single item name inline
    ///
    /// Child grid always shows Pos | Item columns for all event types.
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
                var eventType = e.EventType ?? string.Empty;

                // Split payload into lines, ignoring blank lines
                var lines = rawPayload.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                string summary;
                PayloadRow[] detail;

                if (lines.Length == 0)
                {
                    summary = BuildEmptySummary(eventType);
                    detail = new PayloadRow[0];
                }
                else if (lines.Length == 1)
                {
                    // Single item — show inline, no expand needed
                    summary = lines[0].Trim();
                    detail = new PayloadRow[0];
                }
                else
                {
                    summary = BuildMultiSummary(eventType, lines.Length);

                    // For Protect events cap child rows at first 10 — a full library
                    // protect could have thousands of items and is not useful to list exhaustively.
                    var detailLines = (eventType == "Protect" && lines.Length > 10)
                        ? SubArray(lines, 0, 10)
                        : lines;

                    detail = new PayloadRow[detailLines.Length];
                    for (var j = 0; j < detailLines.Length; j++)
                    {
                        detail[j] = new PayloadRow
                        {
                            Idx = i + "_" + j,
                            Pos = j + 1,
                            Line = detailLines[j].Trim()
                        };
                    }
                }

                rows.Add(new EventHistoryRow
                {
                    Key = i.ToString(),
                    EventType = eventType,
                    OccurredAt = e.OccurredAt.ToString("yyyy-MM-dd HH:mm") + " UTC",
                    PayloadSummary = summary,
                    PayloadDetail = detail
                });
            }

            return EventHistoryDialogUI.Build(rows.ToArray());
        }

        // ── Summary helpers ────────────────────────────────────────────────

        private static string BuildEmptySummary(string eventType)
        {
            switch (eventType)
            {
                case "Protect": return "(no items recorded)";
                case "Unprotect": return "(no detail)";
                default: return "—";
            }
        }

        private static string BuildMultiSummary(string eventType, int count)
        {
            switch (eventType)
            {
                case "Protect": return count + " items protected";
                case "Unprotect": return "(no detail)";
                case "MissingDetected": return count + " item(s) missing";
                case "CandidateFound": return count + " candidate(s) found";
                case "Repair": return count + " item(s) repaired";
                default: return "Expand to see " + count + " items";
            }
        }

        private static string[] SubArray(string[] source, int start, int length)
        {
            var result = new string[length];
            Array.Copy(source, start, result, 0, length);
            return result;
        }
    }
}