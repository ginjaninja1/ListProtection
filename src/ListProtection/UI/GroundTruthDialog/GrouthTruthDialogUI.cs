using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.DxGrid;
using MediaBrowser.Model.Attributes;
using System;

namespace ListProtection.UI.GroundTruthDialog
{
    /// <summary>
    /// Full-screen dialog UI showing ground truth members for a single playlist.
    /// Read-only — no editable columns, no onChangeCommand.
    /// Extends EditableObjectBase (not EditableOptionsBase) — matches dialog pattern.
    /// </summary>
    public class GroundTruthDialogUI : EditableObjectBase
    {
        public override string EditorTitle => null;

        [GridDataSource(nameof(MemberRows))]
        public DxDataGrid MembersGrid { get; set; }

        public GroundTruthMemberRow[] MemberRows { get; set; } = Array.Empty<GroundTruthMemberRow>();

        public static GroundTruthDialogUI Build(GroundTruthMemberRow[] rows)
        {
            var options = new DxGridOptions(
                new GroundTruthMemberRow(),
                "Position",
                false,
                true,
                true,   // search
                true)   // filter
            {
                heightMode = DxGridOptions.GridHeightMode.fullHeight,
                columnAutoWidth = true
                // No editing, no onChangeCommand — read-only
            };

            if (options.columns != null)
            {
                foreach (var col in options.columns)
                {
                    col.allowEditing = false;

                    if (col.dataField == "Position")
                    {
                        col.width = 50;
                        col.sortIndex = 0;
                        col.sortOrder = "asc";
                    }
                }
            }

            return new GroundTruthDialogUI
            {
                MembersGrid = new DxDataGrid(options),
                MemberRows = rows
            };
        }
    }
}