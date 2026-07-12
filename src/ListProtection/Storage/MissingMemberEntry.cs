using System;

namespace ListProtection.Storage
{
    /// <summary>
    /// A single missing-member record.
    /// Stored flat (not keyed by playlist) to support cross-playlist identity lookup:
    /// any candidate repair valid for Member.Id on one playlist is valid on all playlists.
    /// </summary>
    public class MissingMemberEntry
    {
        /// <summary>
        /// Playlist Guid "N" format string. Matches key in GroundTruthStore.
        /// </summary>
        public string PlaylistId { get; set; }

        /// <summary>
        /// Playlist name at detection time. Display only — not used for logic.
        /// </summary>
        public string PlaylistName { get; set; }

        /// <summary>
        /// UTC timestamp when this member was first identified as missing.
        /// </summary>
        public DateTime DetectedAt { get; set; }

        /// <summary>
        /// Full snapshot of the member at the time it was captured in ground truth.
        /// Member.Id (Guid "N") is the durable cross-playlist identity key.
        /// Member.InternalId is used for live playlist lookups.
        /// </summary>
        public GroundTruthMember Member { get; set; }
    }
}