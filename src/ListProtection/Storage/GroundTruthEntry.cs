using System;
using System.Collections.Generic;

namespace ListProtection.Storage
{
    public class GroundTruthEntry
    {
        /// <summary>
        /// "Playlist" or "Collection". Null for legacy entries — treated as "Playlist".
        /// </summary>
        public string ListType { get; set; }

        /// <summary>
        /// Display name at capture time. Not used for logic — Id is the durable key.
        /// </summary>
        public string PlaylistName { get; set; }

        /// <summary>
        /// Playlist only: IsPublic value at capture time, for reinstatement.
        /// Null for collections (globally visible, no ownership concept).
        /// </summary>
        public bool? IsPublic { get; set; }

        /// <summary>
        /// When this snapshot was captured.
        /// </summary>
        public DateTime CapturedAt { get; set; }

        public List<GroundTruthMember> Members { get; set; } = new List<GroundTruthMember>();

        // ── Helpers ────────────────────────────────────────────────────────

        public bool IsCollection =>
            string.Equals(ListType, "Collection", StringComparison.OrdinalIgnoreCase);

        public bool IsPlaylist =>
            ListType == null ||
            string.Equals(ListType, "Playlist", StringComparison.OrdinalIgnoreCase);
    }
}