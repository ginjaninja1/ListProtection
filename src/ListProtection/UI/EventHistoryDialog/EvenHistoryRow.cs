using System.ComponentModel;

namespace ListProtection.UI.EventHistoryDialog
{
    /// <summary>
    /// Row for the EventHistory master grid.
    /// One row per EventEntry for the target playlist.
    ///
    /// Key — hidden, used as keyExpr.
    /// Payload rendered as a single multiline string cell (all detail lines joined with \n).
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
        public string Payload { get; set; }
    }
}