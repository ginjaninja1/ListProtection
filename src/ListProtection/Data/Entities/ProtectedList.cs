using System.Collections.Generic;

namespace PlaylistProtection.Data.Entities
{
    /// <summary>
    /// Represents a playlist or library collection that is protected from accidental removal or modification.
    /// Acts as the source container from which MissingMembers are derived.
    /// </summary>
    public class ProtectedList
    {
        public string Id { get; }
        public string Name { get; }

        /// <summary>
        /// Raw item identifiers originally present in Emby playlist state.
        /// These are evaluated during resolution to detect missing entries.
        /// </summary>
        public List<string> ItemIds { get; }

        public ProtectedList(string id, string name, List<string> itemIds)
        {
            Id = id;
            Name = name;
            ItemIds = itemIds;
        }
    }
}