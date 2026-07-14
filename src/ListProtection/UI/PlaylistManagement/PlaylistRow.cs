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
    ///   IsProtected  — toggle protection; requires confirm dialog when unticking
    ///   RepairAll    — when true on a row, apply best candidate per missing member
    ///   OpenRepair   — launch RepairDialogView for this playlist (protected only)
    ///   OpenGroundTruth — launch GroundTruthDialogView (protected only)
    ///   OpenHistory  — launch EventHistoryDialogView (protected only)
    ///
    /// Detail — child grid data source (isSecondaryGridDataSource = true).
    ///   Single row showing playlist metadata for troubleshooting. Read-only.
    /// </summary>
    public class PlaylistRow
    {
        // ── Hidden identity fields ─────────────────────────────────────────

        [DisplayName("Id")]
        public string Id { get; set; }

        [DisplayName("InternalId")]
        public long InternalId { get; set; }

        // ── Visible read-only columns ──────────────────────────────────────

        [DisplayName("Playlist")]
        public string Name { get; set; }

        [DisplayName("GT")]
        public int MemberCount { get; set; }

        [DisplayName("MM")]
        public int MissingCount { get; set; }

        // ── Editable action columns ────────────────────────────────────────

        [DisplayName("Protected")]
        public bool IsProtected { get; set; }

        [DisplayName("Repair All")]
        public bool RepairAll { get; set; }

        [DisplayName("Repair…")]
        public bool OpenRepair { get; set; }

        [DisplayName("Members…")]
        public bool OpenGroundTruth { get; set; }

        [DisplayName("History…")]
        public bool OpenHistory { get; set; }

        // ── Child grid data source ─────────────────────────────────────────

        [DisplayName("Detail")]
        public PlaylistDetailRow[] Detail { get; set; } = new PlaylistDetailRow[0];
    }
}