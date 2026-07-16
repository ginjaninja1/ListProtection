using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.DxGrid;
using ListProtection.UI.MissingMembers;
using MediaBrowser.Model.Attributes;
using System;

namespace ListProtection.UI.RepairDialog
{
    /// <summary>
    /// Full-screen dialog UI for repairing a single playlist's missing members.
    /// Extends EditableObjectBase (not EditableOptionsBase) — matches the
    /// MetadataChecker dialog pattern (ShowPlaylistStreamsUi).
    ///
    /// Master grid: one row per missing member.
    ///   Editable columns: RepairMember (repair via strongest candidate),
    ///                     DismissMember (stop tracking this member)
    ///   Read-only: MemberName, Path, DetectedAt
    ///   Hidden:    Key, IsSynthetic, Forget, PlaylistName, Candidates (data source)
    ///
    /// Detail grid: candidate rows for the expanded missing member.
    ///   Editable columns: Repair (use this specific candidate)
    ///   Read-only: CandidateName, CandidatePath, Score, Signals
    ///   Hidden:    Key
    ///
    /// commandIds:
    ///   Master edits  → "RepairDialogMasterChanged"
    ///   Candidate edits → "RepairDialogCandidateChanged"
    /// </summary>
    public class RepairDialogUI : EditableObjectBase
    {
        public override string EditorTitle => null;

        /// <summary>
        /// "Repair All" button — applies the highest-scoring candidate to every
        /// missing member in one action. Fires commandId "RepairAll".
        /// Declared before the grid so it renders above it.
        /// </summary>
        public ButtonItem RepairAllButton { get; set; } = new ButtonItem("Repair All")
        {
            StandardIcon = StandardIcons.Add,
            CommandId = "RepairAll"
        };

        /// <summary>
        /// "Dismiss All" button — removes all missing members from tracking in one action.
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