using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.DxGrid;
using MediaBrowser.Model.Attributes;
using System;

namespace ListProtection.UI.EventHistoryDialog
{
    /// <summary>
    /// Full-screen read-only dialog showing event history for a single playlist.
    ///
    /// Master grid: Type | When | Detail (summary cell)
    ///   Detail shows the payload line directly if 1 track,
    ///   or "Expand to see N tracks" if multiple.
    ///
    /// Child grid (PayloadDetail): expands to show one row per payload line.
    ///   Only populated for multi-line payloads — single-track events have
    ///   no expand arrow (empty PayloadDetail array).
    /// </summary>
    public class EventHistoryDialogUI : EditableObjectBase
    {
        public override string EditorTitle => null;

        [GridDataSource(nameof(EventRows))]
        public DxDataGrid EventGrid { get; set; }

        public EventHistoryRow[] EventRows { get; set; } = Array.Empty<EventHistoryRow>();

        public static EventHistoryDialogUI Build(EventHistoryRow[] rows)
        {
            // ── Master grid ────────────────────────────────────────────────
            var options = new DxGridOptions(
                new EventHistoryRow(),
                "Key",
                false,
                true,
                true,   // search
                true)   // filter
            {
                heightMode = DxGridOptions.GridHeightMode.fullHeight,
                columnAutoWidth = false
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

                        case "EventType":
                            col.width = 130;
                            break;

                        case "OccurredAt":
                            col.width = 160;
                            col.sortIndex = 0;
                            col.sortOrder = "desc";
                            break;

                        case "PayloadSummary":
                            // Takes remaining width
                            break;

                        case "PayloadDetail":
                            // Used only as child grid source — not a visible column
                            col.visible = false;
                            col.isSecondaryGridDataSource = true;
                            break;
                    }
                }
            }

            // ── Child grid — PayloadRow ────────────────────────────────────
            var detailOptions = new DxGridOptions(
                new PayloadRow(),
                "Idx",
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

                    if (col.dataField == "Idx")
                        col.visible = false;
                }
            }

            options.masterDetail = new DxGridMasterDetail
            {
                enabled = true,
                autoExpandAll = false,
                childRowsFieldName = "PayloadDetail",
                detailGridOptions = detailOptions
            };

            return new EventHistoryDialogUI
            {
                EventGrid = new DxDataGrid(options),
                EventRows = rows
            };
        }
    }
}