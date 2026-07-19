using System;
using System.Collections.Generic;

namespace ListProtection.Storage
{
    public class GroundTruthEntry
    {
        /// <summary>
        /// Playlist name at time of capture. For troubleshooting only — not used for logic.
        /// Name may have changed since capture. Id is the durable key.
        /// </summary>
        public string PlaylistName { get; set; }

        /// <summary>
        /// When this snapshot was captured.
        /// </summary>
        public DateTime CapturedAt { get; set; }

        public List<GroundTruthMember> Members { get; set; } = new List<GroundTruthMember>();
    }
}