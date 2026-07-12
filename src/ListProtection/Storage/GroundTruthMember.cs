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
    }
}