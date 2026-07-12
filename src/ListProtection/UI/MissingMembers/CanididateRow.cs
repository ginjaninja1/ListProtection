using System.ComponentModel;

namespace ListProtection.UI.MissingMembers
{
    /// <summary>
    /// Child grid row for the master-detail candidate view on Tab 2.
    /// One row per CandidateEntry for a given missing member.
    ///
    /// Key format: "{PlaylistId}_{MissingMember.InternalId}_{CandidateInternalId}"
    ///
    /// Column post-processing in MissingMembersUI.Build (detailOptions):
    ///   Key    → visible = false, allowEditing = false
    ///   Score  → allowEditing = false, sortIndex = 0, sortOrder = "desc"
    ///   Repair → only editable column
    ///   All others → allowEditing = false
    /// </summary>
    public class CandidateRow
    {
        [DisplayName("Key")]
        public string Key { get; set; }

        [DisplayName("Candidate")]
        public string CandidateName { get; set; }

        [DisplayName("Path")]
        public string CandidatePath { get; set; }

        [DisplayName("Score")]
        public int Score { get; set; }

        [DisplayName("Signals")]
        public string Signals { get; set; }

        [DisplayName("Repair")]
        public bool Repair { get; set; }
    }
}