using System.ComponentModel;

namespace ListProtection.UI.PlaylistManagement
{
    /// <summary>
    /// Plain row class — NOT EditableOptionsBase.
    /// Bound to DxDataGrid via [GridDataSource] on PlaylistManagementUI.
    /// Id must be Guid "N" format string to match Emby's item.Id.ToString("N").
    /// </summary>
    public class PlaylistRow
    {
        [DisplayName("Id")]
        public string Id { get; set; }

        [DisplayName("Playlist")]
        public string Name { get; set; }

        [DisplayName("Protected")]
        public bool IsProtected { get; set; }
    }
}