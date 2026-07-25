using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.DxGrid;
using MediaBrowser.Model.Attributes;
using System;

namespace ListProtection.UI.Config
{
    /// <summary>
    /// Read-only full-height dialog grid showing signal weights for all media types.
    /// Extends EditableObjectBase — matches the dialog UI pattern (GroundTruthDialogUI).
    /// No editable columns, no onChangeCommand, no search or filter rows.
    /// </summary>
    public class ScoringReferenceDialogUI : EditableObjectBase
    {
        public override string EditorTitle => null;

        [GridDataSource(nameof(Rows))]
        public DxDataGrid ScoringGrid { get; set; }

        public ScoringReferenceRow[] Rows { get; set; } = Array.Empty<ScoringReferenceRow>();

        public static ScoringReferenceDialogUI Build(ScoringReferenceRow[] rows)
        {
            var options = new DxGridOptions(
                new ScoringReferenceRow(),
                "Score",
                false,  // allowEdit
                true,   // allowSelect
                false,  // search
                false)  // filter
            {
                heightMode = DxGridOptions.GridHeightMode.fullHeight,
                columnAutoWidth = true
            };

            if (options.columns != null)
            {
                foreach (var col in options.columns)
                {
                    col.allowEditing = false;

                    if (col.dataField == "Score")
                    {
                        col.width = 70;
                        col.sortIndex = 0;
                        col.sortOrder = "desc";
                    }

                    if (col.dataField == "MediaType")
                    {
                        col.width = 130;
                        col.groupIndex = 0;
                        col.showWhenGrouped = false;
                        col.autoExpandGroup = true;
                        col.allowHeaderFiltering = false;
                    }

                    if (col.dataField == "Signal")
                        col.width = 220;
                }
            }

            return new ScoringReferenceDialogUI
            {
                ScoringGrid = new DxDataGrid(options),
                Rows = rows
            };
        }
    }
}