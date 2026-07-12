using System;

namespace PlaylistProtection.Core.Models
{
    public class MissingItem
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public string Path { get; set; }

        public string ProviderId { get; set; }

        public string MediaType { get; set; }

        public DateTime LastSeenUtc { get; set; }

        // Optional: helps distinguish Emby vs filesystem origin
        public string Source { get; set; }

        // Optional: tracks whether this was ever confirmed present in Emby
        public bool WasConfirmedInLibrary { get; set; }
    }
}