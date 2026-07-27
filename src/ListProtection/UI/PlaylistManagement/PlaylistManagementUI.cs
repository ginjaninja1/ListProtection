using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.DxGrid;
using MediaBrowser.Model.Attributes;
using System;
using System.ComponentModel;

namespace ListProtection.UI.PlaylistManagement
{
    public class PlaylistManagementUI : EditableOptionsBase
    {
        public override string EditorTitle => "Managed Lists";
        public override string EditorDescription => "Toggle protection on a playlist or collection to track its membership.";

        public CaptionItem StatusLegend { get; set; } = new CaptionItem("ℹ️ Status: Members / Missing / With Candidates");

        public LabelItem ConvergenceStatus { get; set; } = new LabelItem(string.Empty);

        [GridDataSource(nameof(PlaylistRows))]
        public DxDataGrid PlaylistGrid { get; set; }

        [Browsable(false)]
        public PlaylistRow[] PlaylistRows { get; set; } = Array.Empty<PlaylistRow>();

        public static PlaylistManagementUI Build(PlaylistRow[] rows, string convergenceStatusText = "")
        {
            var options = new DxGridOptions(new PlaylistRow(), "Id", false, true, true, false)
            {
                editing = new DxGridEditing
                {
                    mode = DxGridEditing.GridEditMode.cell,
                    allowUpdating = true
                },
                onChangeCommand = new DxGridOnChangeCommand { commandId = "PlaylistAction" },
                columnAutoWidth = false,
                heightMode = DxGridOptions.GridHeightMode.large
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

                        case "ListType":
                            col.allowEditing = false;
                            col.caption = "Type";
                            col.width = 90;
                            break;

                        case "Name":
                            col.allowEditing = false;
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

            var detailOptions = new DxGridOptions(new PlaylistDetailRow(), "PlaylistId", false, false, false, false)
            {
                heightMode = DxGridOptions.GridHeightMode.auto
            };

            if (detailOptions.columns != null)
                foreach (var col in detailOptions.columns)
                    col.allowEditing = false;

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
                PlaylistRows = rows,
                ConvergenceStatus = new LabelItem(convergenceStatusText)
            };
        }
    }
}