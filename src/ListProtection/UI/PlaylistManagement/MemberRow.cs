using System.ComponentModel;

namespace ListProtection.UI.PlaylistManagement
{
    /// <summary>
    /// Child grid row for Tab 1 master-detail — shows ground truth members
    /// of a protected playlist when the row is expanded.
    ///
    /// Read-only: no editable columns, no onChangeCommand.
    /// Bound via PlaylistRow.Members (isSecondaryGridDataSource = true).
    /// </summary>
    public class MemberRow
    {
        [DisplayName("Name")]
        public string Name { get; set; }

        [DisplayName("Path")]
        public string Path { get; set; }

        [DisplayName("InternalId")]
        public long InternalId { get; set; }
    }
}