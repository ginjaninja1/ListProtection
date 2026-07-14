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
    /// Probe result (Task 9):
    ///   Single onChangeCommand fires for ALL cell edits with commandId="PlaylistAction".
    ///   itemId is always null. Action type is determined by inspecting changed fields
    ///   in the round-trip PlaylistRows data (RepairAll=true takes priority).
    ///
    /// Master grid: one row per playlist in the Emby library.
    ///   Editable columns: IsProtected, RepairAll.
    ///   keyExpr: "Id" (GuidN string).
    ///
    /// Detail grid: ground truth members of the expanded playlist row.
    ///   Read-only. Bound via PlaylistRow.Members (isSecondaryGridDataSource = true).
    /// </summary>
    public class PlaylistManagementUI : EditableOptionsBase
    {
        public override string EditorTitle => "Managed Playlists";
        public override string EditorDescription => "Toggle protection on a playlist to track and repair its membership. Use Repair All to apply the best available candidate for every missing member.";

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
                        case "Path":
                        case "InternalId":
                        case "MemberCount":
                        case "CapturedAt":
                        case "Name":
                            col.allowEditing = false;
                            break;

                        case "Members":
                            col.visible = false;
                            col.allowEditing = false;
                            col.isSecondaryGridDataSource = true;
                            break;

                        case "IsProtected":
                        case "RepairAll":
                        case "OpenRepair":
                        case "OpenGroundTruth":
                            // Intentionally editable — leave as default
                            break;
                    }
                }
            }

            // ── Detail (child) grid options — Members ──────────────────────
            var detailOptions = new DxGridOptions(
                new MemberRow(),
                "InternalId",
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
                childRowsFieldName = "Members",
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