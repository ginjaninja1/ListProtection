using System.ComponentModel;

namespace ListProtection.UI.PlaylistManagement
{
    /// <summary>
    /// Plain row class — NOT EditableOptionsBase.
    /// Bound to DxDataGrid via [GridDataSource] on PlaylistManagementUI.
    /// Id must be Guid "N" format string to match Emby's item.Id.ToString("N").
    ///
    /// Probe result (Task 9):
    ///   commandId is always the single grid-level value ("PlaylistAction").
    ///   Action disambiguation is done by inspecting which field changed in the
    ///   round-trip data — same pattern as Tab 2 (Forget vs Repair).
    ///   itemId is always null.
    ///
    /// Actions:
    ///   IsProtected — toggle protection; always processed
    ///   RepairAll   — when true on a row, apply best candidate per missing member
    ///                 for that playlist; takes priority over IsProtected toggle
    ///
    /// Members — child grid data source (isSecondaryGridDataSource = true).
    ///   Populated from GroundTruthStore snapshot. Read-only.
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

        [DisplayName("Member Count")]
        public int MemberCount { get; set; }

        [DisplayName("Captured")]
        public string CapturedAt { get; set; }

        [DisplayName("Repair All")]
        public bool RepairAll { get; set; }

        [DisplayName("Repair…")]
        public bool OpenRepair { get; set; }

        [DisplayName("Members…")]
        public bool OpenGroundTruth { get; set; }

        [DisplayName("Members")]
        public MemberRow[] Members { get; set; } = new MemberRow[0];
    }
}