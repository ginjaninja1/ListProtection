using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using MediaBrowser.Model.Attributes;
using System.ComponentModel;

namespace ListProtection.UI.UnprotectConfirmDialog
{
    /// <summary>
    /// Confirm dialog UI for unprotecting a playlist.
    ///
    /// Flow:
    ///   1. User types the playlist name into ConfirmName and presses the Validate button
    ///      (commandId = "ConfirmUnprotect"). RunCommand validates the name.
    ///      - Match: sets _nameMatched on the view, re-renders with OK enabled and a
    ///        success label. User then clicks the framework OK button to close.
    ///      - No match: stays open, field cleared.
    ///   2. Framework OK fires OnOkCommand on the view → sets Confirmed = true.
    ///   3. OnDialogResult on the host page checks completedOk && Confirmed.
    ///
    /// AllowOk starts false. The view sets AllowOk = true after name is validated,
    /// then calls RaiseUIViewInfoChanged() to refresh the dialog.
    /// </summary>
    public class UnprotectConfirmDialogUI : EditableObjectBase
    {
        public override string EditorTitle => null;
        public override string EditorDescription => null;

        [DisplayName("Type the playlist name to confirm")]
        public string ConfirmName { get; set; } = string.Empty;

        public ButtonItem ValidateButton { get; set; } = new ButtonItem("Unprotect")
        {
            StandardIcon = StandardIcons.Remove,
            CommandId = "ConfirmUnprotect"
        };

        /// <summary>
        /// Status label — shown after validation attempt.
        /// Displayed as a CaptionItem (read-only heading element) so it is
        /// always visible and never editable. Empty string = not yet attempted.
        /// </summary>
        public CaptionItem ValidationStatus { get; set; } = new CaptionItem(string.Empty);

        [Browsable(false)]
        public string ExpectedName { get; set; } = string.Empty;
    }
}