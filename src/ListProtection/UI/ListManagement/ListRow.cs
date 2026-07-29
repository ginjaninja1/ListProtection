using System.ComponentModel;

namespace ListProtection.UI.ListManagement
{
    /// <summary>
    /// Row for the managed lists grid — covers both Playlists and Collections.
    /// ListType: "Playlist" | "Collection"
    /// Status: "GT/MM/MC" summary.
    /// </summary>
    public class ListRow
    {
        [DisplayName("Id")]
        public string Id { get; set; }

        [DisplayName("InternalId")]
        public long InternalId { get; set; }

        /// <summary>"Playlist" or "Collection"</summary>
        [DisplayName("Type")]
        public string ListType { get; set; }

        [DisplayName("Name")]
        public string Name { get; set; }

        [DisplayName("Status")]
        public string Status { get; set; }

        [DisplayName("Protected")]
        public bool IsProtected { get; set; }

        [DisplayName("R")]
        public bool OpenRepair { get; set; }

        [DisplayName("M")]
        public bool OpenGroundTruth { get; set; }

        [DisplayName("H")]
        public bool OpenHistory { get; set; }

        [DisplayName("RepairAll")]
        public bool RepairAll { get; set; }

        [DisplayName("Detail")]
        public ListDetailRow[] Detail { get; set; } = new ListDetailRow[0];
    }
}