using System.ComponentModel;

namespace ListProtection.UI.EventHistoryDialog
{
    /// <summary>
    /// Master row for the EventHistory grid.
    ///
    /// PayloadSummary — displayed in the master row:
    ///   Protect:         "X items protected"
    ///   Unprotect:       "(no detail)"
    ///   MissingDetected: "X item(s) missing" or the single item name
    ///   CandidateFound:  "X candidate(s) found" or the single item
    ///   Repair:          "X item(s) repaired" or the single item
    ///   All multi-line events: expand to show detail rows.
    ///
    /// PayloadDetail[] — child grid rows (one per payload line).
    ///   Each row has Pos (1-based) and Line (item detail).
    ///   Only populated when there are 2+ lines; suppressed for single/empty
    ///   to avoid a pointless expand on single-item events.
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

        [DisplayName("Items")]
        public PayloadRow[] PayloadDetail { get; set; } = new PayloadRow[0];
    }

    /// <summary>
    /// Child grid row — a single payload detail line.
    /// Pos is 1-based position within the event's payload lines.
    /// </summary>
    public class PayloadRow
    {
        [DisplayName("Idx")]
        public string Idx { get; set; }

        [DisplayName("Pos")]
        public int Pos { get; set; }

        [DisplayName("Item")]
        public string Line { get; set; }
    }
}