using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.DxGrid;
using MediaBrowser.Model.Attributes;
using System;

namespace ListProtection.UI.EventHistoryDialog
{
    /// <summary>
    /// Full-screen read-only dialog showing event history for a single playlist.
    /// Extends EditableObjectBase — matches the dialog pattern.
    ///
    /// Single flat grid: Type, When, Detail (multiline payload string).
    /// No editing, no child grid.
    /// </summary>
    public class EventHistoryDialogUI : EditableObjectBase
    {
        public override string EditorTitle => null;

        [GridDataSource(nameof(EventRows))]
        public DxDataGrid EventGrid { get; set; }

        public EventHistoryRow[] EventRows { get; set; } = Array.Empty<EventHistoryRow>();

        public static EventHistoryDialogUI Build(EventHistoryRow[] rows)
        {
            var options = new DxGridOptions(
                new EventHistoryRow(),
                "Key",
                false,
                true,
                false,
                false)
            {
                heightMode = DxGridOptions.GridHeightMode.fullHeight,
                columnAutoWidth = true
                // No editing — read-only
            };

            if (options.columns != null)
            {
                foreach (var col in options.columns)
                {
                    if (col.dataField == null) continue;

                    col.allowEditing = false;

                    switch (col.dataField)
                    {
                        case "Key":
                            col.visible = false;
                            break;

                        case "OccurredAt":
                            col.sortIndex = 0;
                            col.sortOrder = "desc";
                            col.width = 160;
                            break;

                        case "EventType":
                            col.width = 140;
                            break;

                            // Payload column — takes remaining width, no extra config needed
                    }
                }
            }

            return new EventHistoryDialogUI
            {
                EventGrid = new DxDataGrid(options),
                EventRows = rows
            };
        }
    }
}