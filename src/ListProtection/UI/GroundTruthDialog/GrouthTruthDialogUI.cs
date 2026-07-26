using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.DxGrid;
using MediaBrowser.Model.Attributes;
using System;

namespace ListProtection.UI.GroundTruthDialog
{
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
                true)   // filter row
            {
                heightMode = DxGridOptions.GridHeightMode.fullHeight,
                columnAutoWidth = true
            };

            if (options.columns != null)
            {
                foreach (var col in options.columns)
                {
                    col.allowEditing = false;

                    switch (col.dataField)
                    {
                        case "Position":
                            col.width = 50;
                            col.sortIndex = 0;
                            col.sortOrder = "asc";
                            break;
                        case "MediaType":
                            col.width = 90;
                            break;
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