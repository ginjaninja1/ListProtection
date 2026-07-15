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
    /// If the name matches (case-insensitive), Confirmed is set and the dialog closes
    /// by returning Task.FromResult(null) — NOT via base.RunCommand, which throws
    /// "Command is not implemented" when called with an unrecognised commandId.
    ///
    /// OnDialogResult on the parent page view checks Confirmed and completes the action.
    /// </summary>
    internal sealed class UnprotectConfirmDialogView : PluginDialogView
    {
        private readonly string _playlistId;
        private readonly string _playlistName;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly ILogger _logger;

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
                        _logger.Info("[UnprotectConfirmDialogView] Name matched — confirmed, closing dialog");
                        Confirmed = true;
                        // Return null directly — closes the dialog and triggers OnDialogResult.
                        // Do NOT call base.RunCommand here; it throws "Command is not implemented"
                        // for commandIds the framework does not own (e.g. "ConfirmUnprotect").
                        return Task.FromResult<IPluginUIView>(null);
                    }
                    else
                    {
                        _logger.Info("[UnprotectConfirmDialogView] Name did not match — staying open");

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
            }

            // Cancel and any other framework-owned commandIds — delegate to base (safe for these)
            _logger.Info("[UnprotectConfirmDialogView] commandId='{0}' — delegating to base", commandId ?? "(null)");
            return base.RunCommand(itemId, commandId, data);
        }
    }
}