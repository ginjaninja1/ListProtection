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

        /// <summary>
        /// True = playlist is currently protected.
        /// False = soft-deleted (playlist was unprotected but snapshot is retained).
        /// On re-tick: restore IsActive = true, update CapturedAt only if starting fresh.
        /// FUTURE: prompt user to choose between restoring old snapshot or starting fresh.
        /// See FUTURE IDEAS in handover doc.
        /// </summary>
        public bool IsActive { get; set; }

        public List<GroundTruthMember> Members { get; set; } = new List<GroundTruthMember>();
    }
}