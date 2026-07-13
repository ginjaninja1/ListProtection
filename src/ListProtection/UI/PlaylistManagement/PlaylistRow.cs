using System.ComponentModel;

namespace ListProtection.UI.PlaylistManagement
{
    /// <summary>
    /// Plain row class — NOT EditableOptionsBase.
    /// Bound to DxDataGrid via [GridDataSource] on PlaylistManagementUI.
    /// Id must be Guid "N" format string to match Emby's item.Id.ToString("N").
    ///
    /// Extended fields (read-only, for diagnostics):
    ///   Path        — m3u backing file path as reported by Emby
    ///   InternalId  — Emby database InternalId (long)
    ///   MemberCount — number of members in GroundTruthStore snapshot
    ///   CapturedAt  — when the ground truth snapshot was taken
    /// </summary>
    public class PlaylistRow
    {
        [DisplayName("Id")]
        public string Id { get; set; }

        [DisplayName("Playlist")]
        public string Name { get; set; }

        [DisplayName("Path")]
        public string Path { get; set; }

        [DisplayName("InternalId")]
        public long InternalId { get; set; }

        [DisplayName("Protected")]
        public bool IsProtected { get; set; }

        [DisplayName("Members")]
        public int MemberCount { get; set; }

        [DisplayName("Captured")]
        public string CapturedAt { get; set; }
    }
}