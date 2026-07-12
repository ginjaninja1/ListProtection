using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.DxGrid;
using MediaBrowser.Model.Attributes;
using System;

namespace ListProtection.UI.MissingMembers
{
    /// <summary>
    /// UI definition for Tab 2 — Missing Members.
    /// Mirrors PlaylistManagementUI pattern exactly.
    ///
    /// Grid features enabled:
    ///   showHeaderFilter = true  — multi-select filter popup on PlaylistName and DetectedAt columns.
    ///                              Provides the per-playlist filter requirement without custom controls.
    ///   showFilterRow = true     — per-column text filter row for MemberName / Path.
    ///
    /// Grouping: PlaylistName column post-processed with groupIndex=0.
    ///   Groups are collapsed by default (DxGridGrouping.autoExpandAll = false, wired in DxGridOptions ctor).
    ///   Group header shows count badge automatically (DxGridSummary.groupItems wired in DxGridOptions ctor).
    ///
    /// Column post-processing is applied after DxColumnBuilder.CreateColumns runs.
    /// dataField names are ASSUMED to match property names — probe log on first run will confirm.
    /// </summary>
    public class MissingMembersUI : EditableOptionsBase
    {
        public override string EditorTitle => "Missing Members";
        public override string EditorDescription => "Members that are no longer present in their protected playlist. Use Forget to stop tracking a member.";

        [GridDataSource(nameof(MissingMemberRows))]
        public DxDataGrid MissingMembersGrid { get; set; }

        public MissingMemberRow[] MissingMemberRows { get; set; } = Array.Empty<MissingMemberRow>();

        /// <summary>
        /// Factory — always construct from rows, never mutate an existing instance.
        /// </summary>
        public static MissingMembersUI Build(MissingMemberRow[] rows)
        {
            var options = new DxGridOptions(
                new MissingMemberRow(),
                "Key",
                false,
                true,
                true,    // showFilterRow — text filter per column
                true)    // showHeaderFilter — multi-select popup per column header
            {
                editing = new DxGridEditing
                {
                    mode = DxGridEditing.GridEditMode.cell,
                    allowUpdating = true
                },
                onChangeCommand = new DxGridOnChangeCommand { commandId = "ForgetMember" }
            };

            // Post-process columns after DxColumnBuilder has built them from MissingMemberRow.
            // PROBE: on first run, log dataField names to confirm they match property names.
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
                            // Only editable column — leave allowEditing inherited (true from editing.allowUpdating)
                            col.allowHeaderFiltering = false;
                            break;

                        default:
                            col.allowEditing = false;
                            break;
                    }
                }
            }

            return new MissingMembersUI
            {
                MissingMembersGrid = new DxDataGrid(options),
                MissingMemberRows = rows
            };
        }
    }
}