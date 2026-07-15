using System.ComponentModel;

namespace ListProtection.UI.EventHistoryDialog
{
    /// <summary>
    /// Master row for the EventHistory grid.
    ///
    /// PayloadSummary — displayed in the master row:
    ///   If payload has exactly 1 line: that line directly.
    ///   If payload has multiple lines: "Expand to see N tracks"
    ///   If payload is empty: "—"
    ///
    /// PayloadDetail[] — child grid rows (one per payload line).
    ///   Only populated when there are 2+ lines; suppressed for single/empty
    ///   to avoid a pointless expand on single-track events.
    /// </summary>
    public class EventHistoryRow
    {
        [DisplayName("Key")]
        public string Key { get; set; }

        [DisplayName("Type")]
        public string EventType { get; set; }

        [DisplayName("When")]
        public string OccurredAt { get; set; }

        [DisplayName("Detail")]
        public string PayloadSummary { get; set; }

        [DisplayName("Tracks")]
        public PayloadRow[] PayloadDetail { get; set; } = new PayloadRow[0];
    }

    /// <summary>
    /// Child grid row — a single payload detail line.
    /// </summary>
    public class PayloadRow
    {
        [DisplayName("Idx")]
        public string Idx { get; set; }

        [DisplayName("Track")]
        public string Line { get; set; }
    }
}