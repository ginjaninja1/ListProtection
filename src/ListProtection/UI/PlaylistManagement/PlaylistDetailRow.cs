using System.ComponentModel;

namespace ListProtection.UI.PlaylistManagement
{
    /// <summary>
    /// Child grid row for Tab 1 master-detail — shows playlist metadata
    /// for troubleshooting when the row is expanded.
    ///
    /// Replaces MemberRow[] — the ground truth member list is now accessible
    /// only via the Members dialog (OpenGroundTruth button).
    ///
    /// Read-only: no editable columns, no onChangeCommand.
    /// Bound via PlaylistRow.Detail (isSecondaryGridDataSource = true).
    /// </summary>
    public class PlaylistDetailRow
    {
        [DisplayName("Playlist ID")]
        public string PlaylistId { get; set; }

        [DisplayName("Path")]
        public string Path { get; set; }

        [DisplayName("GT Captured")]
        public string CapturedAt { get; set; }
    }
}