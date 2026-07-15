using System.ComponentModel;

namespace ListProtection.UI.PlaylistManagement
{
    /// <summary>
    /// Plain row class — NOT EditableOptionsBase.
    /// Bound to DxDataGrid via [GridDataSource] on PlaylistManagementUI.
    ///
    /// Status column: amalgamates GT member count / missing count / candidate count
    /// as "GT/MM/MC" string. For unprotected: "XX/0/0".
    ///
    /// Action bool columns (R, M, H) are always editable by the framework — per-row
    /// conditional editing is not supported. Server-side handlers ignore clicks on
    /// unprotected playlists. RepairAll removed from grid (hidden).
    /// </summary>
    public class PlaylistRow
    {
        // ── Hidden identity ────────────────────────────────────────────────

        [DisplayName("Id")]
        public string Id { get; set; }

        [DisplayName("InternalId")]
        public long InternalId { get; set; }

        // ── Visible read-only columns ──────────────────────────────────────

        [DisplayName("Playlist")]
        public string Name { get; set; }

        /// <summary>
        /// GT/MM/MC summary — e.g. "12/2/4" or "8/0/0" for unprotected.
        /// </summary>
        [DisplayName("Status")]
        public string Status { get; set; }

        // ── Editable action columns ────────────────────────────────────────

        [DisplayName("Protected")]
        public bool IsProtected { get; set; }

        [DisplayName("R")]
        public bool OpenRepair { get; set; }

        [DisplayName("M")]
        public bool OpenGroundTruth { get; set; }

        [DisplayName("H")]
        public bool OpenHistory { get; set; }

        // ── RepairAll kept as hidden field for server logic only ───────────
        // Not shown in grid — triggered via a future mechanism if needed.
        [DisplayName("RepairAll")]
        public bool RepairAll { get; set; }

        // ── Child grid data source ─────────────────────────────────────────

        [DisplayName("Detail")]
        public PlaylistDetailRow[] Detail { get; set; } = new PlaylistDetailRow[0];
    }
}