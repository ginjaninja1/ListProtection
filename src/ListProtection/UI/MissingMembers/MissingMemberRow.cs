using System.ComponentModel;

namespace ListProtection.UI.MissingMembers
{
    /// <summary>
    /// Grid row for Tab 2 — Missing Members.
    /// Bound to DxDataGrid via [GridDataSource] on MissingMembersUI.
    ///
    /// Key format (real rows):    "{PlaylistId}_{Member.InternalId}"
    ///   PlaylistId is always 32 hex chars (Guid "N"), so parsing is:
    ///   PlaylistId   = Key.Substring(0, 32)
    ///   InternalId   = long.Parse(Key.Substring(33))
    ///
    /// Key format (synthetic):    "synthetic_{PlaylistId}"
    ///   RunCommand identifies these by IsSynthetic == true and skips them.
    ///
    /// Column post-processing in MissingMembersUI.Build:
    ///   Key, IsSynthetic, Candidates → visible = false
    ///   PlaylistName                 → groupIndex = 0, showWhenGrouped = false, allowEditing = false
    ///   Forget                       → allowEditing = true (only editable column)
    ///   All others                   → allowEditing = false
    ///
    /// Candidates is marked isSecondaryGridDataSource = true in MissingMembersUI.Build
    /// and provides child rows for the master-detail expand.
    /// </summary>
    public class MissingMemberRow
    {
        [DisplayName("Key")]
        public string Key { get; set; }

        [DisplayName("Playlist")]
        public string PlaylistName { get; set; }

        [DisplayName("Member")]
        public string MemberName { get; set; }

        [DisplayName("Path")]
        public string Path { get; set; }

        [DisplayName("Detected")]
        public string DetectedAt { get; set; }

        /// <summary>Tab 2 dismiss checkbox — kept for Tab 2 compatibility.</summary>
        [DisplayName("Dismiss")]
        public bool Forget { get; set; }

        /// <summary>Dialog: dismiss this missing member (remove from tracking).</summary>
        [DisplayName("Dismiss")]
        public bool DismissMember { get; set; }

        /// <summary>Dialog: repair this member using its strongest candidate.</summary>
        [DisplayName("Repair")]
        public bool RepairMember { get; set; }

        [DisplayName("IsSynthetic")]
        public bool IsSynthetic { get; set; }

        [DisplayName("Candidates")]
        public CandidateRow[] Candidates { get; set; } = new CandidateRow[0];
    }
}