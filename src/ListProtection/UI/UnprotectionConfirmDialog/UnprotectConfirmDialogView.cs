using Emby.Web.GenericEdit.Elements;
using ListProtection.Storage;
using ListProtection.UIBaseClasses.Views;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ListProtection.UI.UnprotectConfirmDialog
{
    /// <summary>
    /// Confirm dialog for unprotecting a playlist.
    ///
    /// ONE-BUTTON CLOSE — proven via decompile of PluginDialogViewHost + DialogViewBase:
    ///
    ///   PluginDialogViewHost.RunCommand calls our RunCommand first.
    ///   If we return non-null, CreateUIViewHost wraps it.
    ///   If we return OriginalUIView.PluginUIView (the parent page view),
    ///   CreateUIViewHost matches it and returns OriginalUIView — navigating
    ///   straight back to Tab 1. No OK button, no OnDialogResult needed.
    ///
    ///   DialogViewBase.IsCommandAllowed only passes "DialogCancel"/"DialogOk".
    ///   All other commandIds are blocked at that layer — BUT only reached
    ///   when our RunCommand returns null. Returning non-null bypasses it entirely.
    ///
    /// Flow:
    ///   1. User types name, presses "Check" (commandId="ConfirmUnprotect").
    ///   2. Name matches → execute unprotect immediately → rebuild parent ContentData
    ///      → return parentPageView. Framework navigates to Tab 1 in one step.
    ///   3. Name doesn't match → show error, return this (stay open).
    ///   4. Cancel → base.RunCommand("DialogCancel") → DialogViewBase handles it.
    /// </summary>
    internal sealed class UnprotectConfirmDialogView : PluginDialogView
    {
        private readonly string _playlistId;
        private readonly string _playlistName;
        private readonly IPluginUIView _parentPageView;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly ILogger _logger;

        // Callbacks executed on confirmed unprotect — avoids circular dependency
        // on PlaylistManagementPageView. The parent passes lambdas at construction.
        private readonly Action _executeUnprotect;
        private readonly Action _rebuildParentContent;

        public UnprotectConfirmDialogView(
            PluginInfo pluginInfo,
            string playlistId,
            string playlistName,
            IPluginUIView parentPageView,
            Action executeUnprotect,
            Action rebuildParentContent,
            IJsonSerializer jsonSerializer,
            ILogger logger)
            : base(pluginInfo.Id)
        {
            _playlistId = playlistId;
            _playlistName = playlistName;
            _parentPageView = parentPageView;
            _executeUnprotect = executeUnprotect;
            _rebuildParentContent = rebuildParentContent;
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
                "[UnprotectConfirmDialogView] RunCommand | commandId={0}",
                commandId ?? "(null)");

            if (string.Equals(commandId, "ConfirmUnprotect", StringComparison.OrdinalIgnoreCase))
            {
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
                        _logger.Info(
                            "[UnprotectConfirmDialogView] Name matched — executing unprotect and returning to Tab 1");

                        // Execute unprotect directly here — no OnDialogResult needed
                        _executeUnprotect();

                        // Rebuild parent Tab 1 content so it reflects the change immediately
                        _rebuildParentContent();

                        // Return the parent page view. PluginDialogViewHost.CreateUIViewHost
                        // matches this against OriginalUIView.PluginUIView and returns
                        // OriginalUIView — navigating straight back to Tab 1.
                        return Task.FromResult(_parentPageView);
                    }
                    else
                    {
                        _logger.Info("[UnprotectConfirmDialogView] Name did not match — staying open");
                        ContentData = new UnprotectConfirmDialogUI
                        {
                            ExpectedName = _playlistName,
                            ConfirmName = string.Empty,
                            ValidationStatus = new CaptionItem("✗ Name did not match — try again")
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

            // DialogCancel — rebuild parent content then return it directly,
            // same as the confirm path. base.RunCommand("DialogCancel") returns
            // OriginalUIView but the client fails to re-render without fresh ContentData.
            if (string.Equals(commandId, "DialogCancel", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info("[UnprotectConfirmDialogView] DialogCancel — returning to Tab 1");
                _rebuildParentContent();
                return Task.FromResult(_parentPageView);
            }

            // All other commandIds — delegate to base
            return base.RunCommand(itemId, commandId, data);
        }

        public override Task Cancel() => Task.CompletedTask;
    }
}