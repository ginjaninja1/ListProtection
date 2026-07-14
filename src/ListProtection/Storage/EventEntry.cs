using System;

namespace ListProtection.Storage
{
    /// <summary>
    /// A single event log record.
    ///
    /// EventType values:
    ///   Protect          — playlist was toggled to protected
    ///   Unprotect        — playlist was toggled to unprotected
    ///   MissingDetected  — one or more members detected as missing
    ///   CandidateFound   — new candidates discovered for a playlist
    ///   CandidateRefresh — candidate refresh run completed for a playlist
    ///   Repair           — one or more members repaired
    ///
    /// Payload is a newline-delimited string of detail lines.
    /// e.g. "Song A | /old/path/song_a.flac\nSong B | /old/path/song_b.flac"
    /// </summary>
    public class EventEntry
    {
        public string EventType { get; set; }

        /// <summary>Playlist Guid "N" format string.</summary>
        public string PlaylistId { get; set; }

        public string PlaylistName { get; set; }

        public DateTime OccurredAt { get; set; }

        /// <summary>
        /// Newline-delimited detail lines. May be empty string for events with no item detail.
        /// </summary>
        public string Payload { get; set; } = string.Empty;
    }
}