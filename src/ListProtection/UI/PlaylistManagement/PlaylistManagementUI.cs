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
    /// Master grid: one row per playlist in the Emby library.
    ///   Read-only columns: Name, GT (member count), MM (missing count).
    ///   Hidden columns: Id, InternalId.
    ///   Editable columns: IsProtected, RepairAll, OpenRepair, OpenGroundTruth, OpenHistory.
    ///   Editable columns are disabled (allowEditing=false) for unprotected rows at the
    ///   action level — the columns are always visible but clicks are no-ops for
    ///   unprotected playlists (framework does not support per-row conditional visibility).
    ///
    /// Detail grid: single-row PlaylistDetailRow showing PlaylistId, Path, CapturedAt.
    ///   Read-only. Bound via PlaylistRow.Detail (isSecondaryGridDataSource = true).
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
            // ── Master grid options ────────────────────────────────────────
            var options = new DxGridOptions(new PlaylistRow(), "Id", false, true, false, false)
            {
                editing = new DxGridEditing
                {
                    mode = DxGridEditing.GridEditMode.cell,
                    allowUpdating = true
                },
                onChangeCommand = new DxGridOnChangeCommand { commandId = "PlaylistAction" }
            };

            // ── Master column post-processing ──────────────────────────────
            if (options.columns != null)
            {
                foreach (var col in options.columns)
                {
                    if (col.dataField == null) continue;

                    switch (col.dataField)
                    {
                        case "Id":
                        case "InternalId":
                            col.visible = false;
                            col.allowEditing = false;
                            break;

                        case "Name":
                        case "MemberCount":
                        case "MissingCount":
                            col.allowEditing = false;
                            break;

                        case "Detail":
                            col.visible = false;
                            col.allowEditing = false;
                            col.isSecondaryGridDataSource = true;
                            break;

                        case "IsProtected":
                        case "RepairAll":
                        case "OpenRepair":
                        case "OpenGroundTruth":
                        case "OpenHistory":
                            // Intentionally editable — leave as default
                            break;
                    }
                }
            }

            // ── Detail grid options — PlaylistDetailRow ────────────────────
            var detailOptions = new DxGridOptions(
                new PlaylistDetailRow(),
                "PlaylistId",
                false,
                false,
                false,
                false);

            if (detailOptions.columns != null)
            {
                foreach (var col in detailOptions.columns)
                {
                    if (col.dataField == null) continue;
                    col.allowEditing = false;
                }
            }

            // ── Wire master-detail ─────────────────────────────────────────
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