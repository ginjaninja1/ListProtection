using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.DxGrid;
using MediaBrowser.Model.Attributes;
using System;

namespace ListProtection.UI.MissingMembers
{
    public class MissingMembersUI : EditableOptionsBase
    {
        public override string EditorTitle => "Missing Members";
        public override string EditorDescription => "Members that are no longer present in their protected playlist. Use Forget to stop tracking a member, or expand a row to see and apply repair candidates.";

        [GridDataSource(nameof(MissingMemberRows))]
        public DxDataGrid MissingMembersGrid { get; set; }

        public MissingMemberRow[] MissingMemberRows { get; set; } = Array.Empty<MissingMemberRow>();

        public static MissingMembersUI Build(MissingMemberRow[] rows)
        {
            // ── Master grid options ────────────────────────────────────────
            var options = new DxGridOptions(
                new MissingMemberRow(),
                "Key",
                false,
                true,
                true,
                true)
            {
                editing = new DxGridEditing
                {
                    mode = DxGridEditing.GridEditMode.cell,
                    allowUpdating = true
                },
                onChangeCommand = new DxGridOnChangeCommand { commandId = "ForgetMember" }
            };

            // ── Master column post-processing ──────────────────────────────
            if (options.columns != null)
            {
                foreach (var col in options.columns)
                {
                    if (col.dataField == null) continue;

                    switch (col.dataField)
                    {
                        case "Key":
                        case "IsSynthetic":
                            col.visible = false;
                            col.allowEditing = false;
                            break;

                        case "Candidates":
                            col.visible = false;
                            col.allowEditing = false;
                            col.isSecondaryGridDataSource = true;
                            break;

                        case "PlaylistName":
                            col.groupIndex = 0;
                            col.showWhenGrouped = false;
                            col.autoExpandGroup = false;
                            col.allowEditing = false;
                            col.allowHeaderFiltering = true;
                            break;

                        case "DetectedAt":
                            col.allowEditing = false;
                            col.allowHeaderFiltering = true;
                            break;

                        case "Forget":
                            col.allowHeaderFiltering = false;
                            break;

                        default:
                            col.allowEditing = false;
                            break;
                    }
                }
            }

            // ── Detail (child) grid options ────────────────────────────────
            var detailOptions = new DxGridOptions(
                new CandidateRow(),
                "Key",
                false,
                false,
                false,
                false)
            {
                editing = new DxGridEditing
                {
                    mode = DxGridEditing.GridEditMode.cell,
                    allowUpdating = true
                },
                onChangeCommand = new DxGridOnChangeCommand { commandId = "RepairMember" }
            };

            if (detailOptions.columns != null)
            {
                foreach (var col in detailOptions.columns)
                {
                    if (col.dataField == null) continue;

                    switch (col.dataField)
                    {
                        case "Key":
                            col.visible = false;
                            col.allowEditing = false;
                            break;

                        case "Score":
                            col.allowEditing = false;
                            col.sortIndex = 0;
                            col.sortOrder = "desc";
                            break;

                        case "Repair":
                            // only editable column — leave allowEditing inherited
                            break;

                        default:
                            col.allowEditing = false;
                            break;
                    }
                }
            }

            // ── Wire master-detail ─────────────────────────────────────────
            options.masterDetail = new DxGridMasterDetail
            {
                enabled = true,
                autoExpandAll = false,
                childRowsFieldName = "Candidates",
                detailGridOptions = detailOptions
            };

            return new MissingMembersUI
            {
                MissingMembersGrid = new DxDataGrid(options),
                MissingMemberRows = rows
            };
        }
    }
}