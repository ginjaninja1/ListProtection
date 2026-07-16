using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.DxGrid;
using MediaBrowser.Model.Attributes;
using System;

namespace ListProtection.UI.PlaylistManagement
{
    /// <summary>
    /// UI definition for Tab 1 — Managed Playlists.
    ///
    /// Columns (left to right):
    ///   Playlist (Name)   — wide, read-only
    ///   Status            — "GT/MM/MC" summary string, read-only, narrow
    ///   Protected         — bool toggle
    ///   R                 — OpenRepair bool (protected only, server-side guard)
    ///   M                 — OpenGroundTruth bool (protected only)
    ///   H                 — OpenHistory bool (protected only)
    ///
    /// Hidden: Id, InternalId, RepairAll, Detail (child grid source).
    ///
    /// Detail child grid: PlaylistDetailRow — PlaylistId, Path, GT Captured.
    /// </summary>
    public class PlaylistManagementUI : EditableOptionsBase
    {
        public override string EditorTitle => "Managed Playlists";
        public override string EditorDescription => "Toggle protection on a playlist to track and repair its membership.";

        [GridDataSource(nameof(PlaylistRows))]
        public DxDataGrid PlaylistGrid { get; set; }

        public PlaylistRow[] PlaylistRows { get; set; } = Array.Empty<PlaylistRow>();

        public static PlaylistManagementUI Build(PlaylistRow[] rows)
        {
            var options = new DxGridOptions(new PlaylistRow(), "Id", false, true, false, false)
            {
                editing = new DxGridEditing
                {
                    mode = DxGridEditing.GridEditMode.cell,
                    allowUpdating = true
                },
                onChangeCommand = new DxGridOnChangeCommand { commandId = "PlaylistAction" },
                columnAutoWidth = false
            };

            if (options.columns != null)
            {
                foreach (var col in options.columns)
                {
                    if (col.dataField == null) continue;

                    switch (col.dataField)
                    {
                        case "Id":
                        case "InternalId":
                        case "RepairAll":
                            col.visible = false;
                            col.allowEditing = false;
                            break;

                        case "Detail":
                            col.visible = false;
                            col.allowEditing = false;
                            col.isSecondaryGridDataSource = true;
                            break;

                        case "Name":
                            col.allowEditing = false;
                            // No fixed width — consumes remaining space
                            break;

                        case "Status":
                            col.allowEditing = false;
                            col.caption = "Status";
                            col.width = 100;
                            break;

                        case "IsProtected":
                            col.caption = "Prot";
                            col.width = 75;
                            break;

                        case "OpenRepair":
                            col.caption = "Repr";
                            col.width = 75;
                            break;

                        case "OpenGroundTruth":
                            col.caption = "Memb";
                            col.width = 75;
                            break;

                        case "OpenHistory":
                            col.caption = "Hist";
                            col.width = 75;
                            break;
                    }
                }
            }

            // ── Detail child grid ──────────────────────────────────────────
            var detailOptions = new DxGridOptions(
                new PlaylistDetailRow(),
                "PlaylistId",
                false,
                false,
                false,
                false)
            {
                heightMode = DxGridOptions.GridHeightMode.auto
            };

            if (detailOptions.columns != null)
            {
                foreach (var col in detailOptions.columns)
                {
                    if (col.dataField == null) continue;
                    col.allowEditing = false;
                }
            }

            options.masterDetail = new DxGridMasterDetail
            {
                enabled = true,
                autoExpandAll = false,
                childRowsFieldName = "Detail",
                detailGridOptions = detailOptions
            };

            return new PlaylistManagementUI
            {
                PlaylistGrid = new DxDataGrid(options),
                PlaylistRows = rows
            };
        }
    }
}