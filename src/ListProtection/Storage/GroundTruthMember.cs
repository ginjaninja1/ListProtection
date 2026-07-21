namespace ListProtection.Storage
{
    public class GroundTruthMember
    {
        /// <summary>
        /// Emby internal ID (long). Fast for in-process lookup.
        /// </summary>
        public long InternalId { get; set; }

        /// <summary>
        /// Guid "N" format string. Durable identifier across restarts.
        /// </summary>
        public string Id { get; set; }

        public string Name { get; set; }

        public string Path { get; set; }

        /// <summary>
        /// Populated at capture time via GetItemList readback.
        /// ASSUMED to be correct when read outside of event context.
        /// Proven correct when read after ItemUpdated fires (Task 1).
        /// Used to correlate PlaylistItemsRemoved events.
        /// </summary>
        public long ListItemEntryId { get; set; }

        // ── Media type discriminator ───────────────────────────────────────

        /// <summary>
        /// Emby type name at capture time: "Audio", "Episode", "Movie", etc.
        /// Null for legacy entries captured before this field was added —
        /// evidence collectors treat null as unknown and apply base signals only.
        /// </summary>
        public string MediaType { get; set; }

        // ── Audio-specific fields (null for non-audio items) ───────────────
        // Captured once at snapshot time. The live item may be gone at discovery
        // time so these are the only source of audio metadata for scoring.

        /// <summary>
        /// item.Album at capture time. Null if not an Audio item or tag absent.
        /// </summary>
        public string Album { get; set; }

        /// <summary>
        /// item.AlbumArtists[0] at capture time. Null if not an Audio item or absent.
        /// </summary>
        public string AlbumArtist { get; set; }

        /// <summary>
        /// item.IndexNumber (track number) at capture time. Null if absent.
        /// </summary>
        public int? IndexNumber { get; set; }

        /// <summary>
        /// item.RunTimeTicks at capture time. Null if absent.
        /// Used for duration-tolerance matching (±3 seconds).
        /// </summary>
        public long? RunTimeTicks { get; set; }
    }
}