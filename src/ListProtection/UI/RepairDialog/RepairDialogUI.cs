using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.DxGrid;
using ListProtection.UI.MissingMembers;
using MediaBrowser.Model.Attributes;
using System;

namespace ListProtection.UI.RepairDialog
{
    public class RepairDialogUI : EditableObjectBase
    {
        public override string EditorTitle => null;

        /// <summary>
        /// Informational label above the action buttons explaining what each one does.
        /// </summary>
        public LabelItem ActionNote { get; set; } = new LabelItem(
            "Dismiss = accept the member is removed from the playlist (take care)." +
            "Considerate = respects the configuration manual repair threshold. " +
            "Inconsiderate/Row Repair = selects the best candidate in any case.");

        /// <summary>
        /// "Repair All (considerate)" — skips members whose top candidate does not
        /// clear the manual repair score threshold and distance. Fires commandId "RepairAllConsiderate".
        /// Rendered to the left of the inconsiderate button.
        /// </summary>
        public ButtonItem RepairAllConsiderateButton { get; set; } = new ButtonItem("Repair All (considerate)")
        {
            StandardIcon = StandardIcons.Add,
            CommandId = "RepairAllConsiderate"
        };

        /// <summary>
        /// "Repair All (inconsiderate)" — applies the highest-scoring candidate to every
        /// missing member regardless of score or distance. Fires commandId "RepairAll".
        /// </summary>
        public ButtonItem RepairAllButton { get; set; } = new ButtonItem("Repair All (inconsiderate)")
        {
            StandardIcon = StandardIcons.Add,
            CommandId = "RepairAll"
        };

        /// <summary>
        /// "Dismiss All" — removes all missing members from tracking.
        /// Fires commandId "DismissAll".
        /// </summary>
        public ButtonItem DismissAllButton { get; set; } = new ButtonItem("Dismiss All")
        {
            StandardIcon = StandardIcons.Delete,
            CommandId = "DismissAll"
        };

        [GridDataSource(nameof(MissingMemberRows))]
        public DxDataGrid MissingMembersGrid { get; set; }

        public MissingMemberRow[] MissingMemberRows { get; set; } = Array.Empty<MissingMemberRow>();

        public static RepairDialogUI Build(MissingMemberRow[] rows)
        {
            // ── Master grid ────────────────────────────────────────────────
            var options = new DxGridOptions(
                new MissingMemberRow(),
                "Key",
                false,
                true,
                true,
                true)
            {
                heightMode = DxGridOptions.GridHeightMode.fullHeight,
                columnAutoWidth = true,
                editing = new DxGridEditing
                {
                    mode = DxGridEditing.GridEditMode.cell,
                    allowUpdating = true
                },
                onChangeCommand = new DxGridOnChangeCommand { commandId = "RepairDialogMasterChanged" }
            };

            if (options.columns != null)
            {
                foreach (var col in options.columns)
                {
                    if (col.dataField == null) continue;

                    switch (col.dataField)
                    {
                        case "Key":
                        case "IsSynthetic":
                        case "Forget":
                        case "PlaylistName":
                            col.visible = false;
                            col.allowEditing = false;
                            break;

                        case "Candidates":
                            col.visible = false;
                            col.allowEditing = false;
                            col.isSecondaryGridDataSource = true;
                            break;

                        case "RepairMember":
                        case "DismissMember":
                            // Intentionally editable
                            break;

                        default:
                            col.allowEditing = false;
                            break;
                    }
                }
            }

            // ── Detail (candidate) grid ────────────────────────────────────
            var detailOptions = new DxGridOptions(
                new CandidateRow(),
                "Key",
                false,
                false,
                false,
                false)
            {
                heightMode = DxGridOptions.GridHeightMode.auto,
                columnAutoWidth = true,
                editing = new DxGridEditing
                {
                    mode = DxGridEditing.GridEditMode.cell,
                    allowUpdating = true
                },
                onChangeCommand = new DxGridOnChangeCommand { commandId = "RepairDialogCandidateChanged" }
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
                            // Intentionally editable
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

            return new RepairDialogUI
            {
                MissingMembersGrid = new DxDataGrid(options),
                MissingMemberRows = rows
            };
        }
    }
}