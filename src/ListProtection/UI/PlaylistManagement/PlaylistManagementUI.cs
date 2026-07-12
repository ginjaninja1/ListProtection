using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.DxGrid;
using MediaBrowser.Model.Attributes;
using System;

namespace ListProtection.UI.PlaylistManagement
{
    /// <summary>
    /// UI definition for Tab 1.
    /// Columns are auto-derived by DxColumnBuilder from PlaylistRow's properties.
    /// keyExpr = "Id" — this is what Emby sends as itemId in RunCommand.
    /// onChangeCommand.commandId is what arrives as commandId in RunCommand.
    /// Both must be confirmed on first run — see RunCommand probe logging.
    /// </summary>
    public class PlaylistManagementUI : EditableOptionsBase
    {
        public override string EditorTitle => "Managed Playlists";
        public override string EditorDescription => "Toggle protection on a playlist to track and repair its membership.";

        [GridDataSource(nameof(PlaylistRows))]
        public DxDataGrid PlaylistGrid { get; set; }

        public PlaylistRow[] PlaylistRows { get; set; } = Array.Empty<PlaylistRow>();

        /// <summary>
        /// Factory — always construct from rows, never mutate an existing instance.
        /// </summary>
        public static PlaylistManagementUI Build(PlaylistRow[] rows)
        {
            return new PlaylistManagementUI
            {
                PlaylistGrid = new DxDataGrid(
                    new DxGridOptions(new PlaylistRow(), "Id", false, true, false, false)
                    {
                        editing = new DxGridEditing
                        {
                            mode = DxGridEditing.GridEditMode.cell,
                            allowUpdating = true
                        },
                        onChangeCommand = new DxGridOnChangeCommand { commandId = "ToggleProtection" }
                    }),
                PlaylistRows = rows
            };
        }
    }
}