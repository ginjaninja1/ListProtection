using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using MediaBrowser.Model.Attributes;
using System.ComponentModel;

namespace ListProtection.UI.UnprotectConfirmDialog
{
    /// <summary>
    /// Confirm dialog UI for unprotecting a playlist.
    ///
    /// Extends EditableObjectBase — dialog pattern.
    /// Renders a string field (ConfirmName) asking the user to type the playlist name,
    /// and a ButtonItem that fires commandId "ConfirmUnprotect" when pressed.
    ///
    /// NOTE: AutoPostBack on a string field in a dialog is unproven — we use a ButtonItem
    /// with CommandId instead. The commandId fires RunCommand on the dialog view
    /// with the full serialised UI as data, allowing the view to read ConfirmName
    /// and compare against the expected name before acting.
    ///
    /// Logging is present to surface any framework behaviour deviations at runtime.
    /// </summary>
    public class UnprotectConfirmDialogUI : EditableObjectBase
    {
        public override string EditorTitle => null;
        public override string EditorDescription => null;

        [DisplayName("Type the playlist name to confirm")]
        public string ConfirmName { get; set; } = string.Empty;

        public ButtonItem ConfirmButton { get; set; } = new ButtonItem("Unprotect")
        {
            StandardIcon = StandardIcons.Remove,
            CommandId = "ConfirmUnprotect"
        };

        /// <summary>
        /// Hidden field carrying the expected playlist name for server-side comparison.
        /// Not rendered — Browsable(false).
        /// </summary>
        [Browsable(false)]
        public string ExpectedName { get; set; } = string.Empty;
    }
}