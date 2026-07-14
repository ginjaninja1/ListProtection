using ListProtection.Storage;
using ListProtection.UIBaseClasses.Views;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;
using MediaBrowser.Model.Serialization;
using System;
using System.Threading.Tasks;

namespace ListProtection.UI.UnprotectConfirmDialog
{
    /// <summary>
    /// Confirm dialog for unprotecting a playlist.
    /// Launched from PlaylistManagementPageView when IsProtected is unticked.
    ///
    /// User must type the playlist name and press "Unprotect" (commandId="ConfirmUnprotect").
    /// If the name matches (case-insensitive), the page view is signalled via
    /// OnDialogResult to complete the unprotect action. If it doesn't match, the
    /// dialog stays open with no action.
    ///
    /// Any other commandId (e.g. "DialogCancel") delegates to base.RunCommand → closes dialog.
    ///
    /// PROBE NOTE: ButtonItem CommandId in a dialog, and string field serialisation in
    /// EditableObjectBase dialogs, are unproven at this SDK version. Detailed logging
    /// is present to surface any deviations at runtime.
    ///
    /// Result signalling: sets Confirmed=true on close so OnDialogResult can check it.
    /// </summary>
    internal sealed class UnprotectConfirmDialogView : PluginDialogView
    {
        private readonly string _playlistId;
        private readonly string _playlistName;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly ILogger _logger;

        /// <summary>
        /// True if the user typed the name correctly and pressed Unprotect.
        /// Checked by PlaylistManagementPageView.OnDialogResult.
        /// </summary>
        public bool Confirmed { get; private set; }

        public UnprotectConfirmDialogView(
            PluginInfo pluginInfo,
            string playlistId,
            string playlistName,
            IJsonSerializer jsonSerializer,
            ILogger logger)
            : base(pluginInfo.Id)
        {
            _playlistId = playlistId;
            _playlistName = playlistName;
            _jsonSerializer = jsonSerializer;
            _logger = logger;

            ShowDialogFullScreen = false;
            AllowOk = false;
            AllowCancel = true;

            ContentData = new UnprotectConfirmDialogUI
            {
                ExpectedName = playlistName,
                ConfirmName = string.Empty
            };
        }

        public override string Caption => "Unprotect: " + _playlistName;
        public override bool ShowDialogFullScreen { get; }

        public override Task OnCancelCommand() => Task.CompletedTask;

        public override Task OnOkCommand(string providerId, string commandId, string data)
            => Task.CompletedTask;

        public override Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
        {
            _logger.Info(
                "[UnprotectConfirmDialogView] RunCommand | commandId={0} | itemId={1}",
                commandId ?? "(null)", itemId ?? "(null)");

            if (string.Equals(commandId, "ConfirmUnprotect", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info("[UnprotectConfirmDialogView] ConfirmUnprotect received — deserialising data");

                try
                {
                    var ui = _jsonSerializer.DeserializeFromString<UnprotectConfirmDialogUI>(data);

                    _logger.Info(
                        "[UnprotectConfirmDialogView] ConfirmName='{0}' | ExpectedName='{1}'",
                        ui?.ConfirmName ?? "(null)", _playlistName);

                    if (ui != null && string.Equals(
                            ui.ConfirmName?.Trim(),
                            _playlistName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Info("[UnprotectConfirmDialogView] Name matched — confirmed");
                        Confirmed = true;
                    }
                    else
                    {
                        _logger.Info("[UnprotectConfirmDialogView] Name did not match — staying open");

                        // Refresh the dialog with a hint
                        ContentData = new UnprotectConfirmDialogUI
                        {
                            ExpectedName = _playlistName,
                            ConfirmName = string.Empty
                        };
                        RaiseUIViewInfoChanged();
                        return Task.FromResult<IPluginUIView>(this);
                    }
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("[UnprotectConfirmDialogView] Failed to deserialise data", ex);
                    return Task.FromResult<IPluginUIView>(this);
                }

                // Confirmed — close the dialog (base.RunCommand returns null)
                return base.RunCommand(itemId, commandId, data);
            }

            // All other commands (DialogCancel etc.) — close dialog without confirming
            _logger.Info("[UnprotectConfirmDialogView] Unhandled commandId '{0}' — delegating to base (closes dialog)", commandId ?? "(null)");
            return base.RunCommand(itemId, commandId, data);
        }
    }
}