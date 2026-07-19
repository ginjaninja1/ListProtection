using ListProtection.Configuration;
using ListProtection.UIBaseClasses.Views;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;
using System.Threading.Tasks;

namespace ListProtection.UI.Config
{
    internal class ConfigPageView : PluginPageView
    {
        public ConfigPageView(PluginInfo pluginInfo)
            : base(pluginInfo.Id)
        {
            ShowSave = false;
            ShowBack = false;

            ContentData = BuildUI(ListProtectionPlugin.Instance.Configuration);
        }

        private ConfigUI Config => ContentData as ConfigUI;

        /// <summary>
        /// Called by the Emby framework for every AutoPostBack field change
        /// (commandId = "updateconfig"). Maps the view model back to
        /// PluginConfiguration and persists via SaveConfiguration().
        /// Emby serialises PluginConfiguration to XML; ConfigUI is never persisted.
        /// </summary>
        public override Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
        {
            if (commandId == "updateconfig")
            {
                var plugin = ListProtectionPlugin.Instance;
                var config = plugin.Configuration;

                config.AutoRepairEnabled = Config.AutoRepairEnabled;
                config.AutoRepairThreshold = Config.AutoRepairThreshold;
                config.AutoRepairMaxPerRun = Config.AutoRepairMaxPerRun;
                config.AutoDiscoverCandidates = Config.AutoDiscoverCandidates;

                plugin.SaveConfiguration();
            }

            return Task.FromResult((IPluginUIView)this);
        }

        // ── Helpers ────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a fresh ConfigUI from the current PluginConfiguration values.
        /// Called at construction time; also available if the view needs refreshing
        /// after an external configuration change.
        /// </summary>
        private static ConfigUI BuildUI(PluginConfiguration config) => new ConfigUI
        {
            AutoRepairEnabled = config.AutoRepairEnabled,
            AutoRepairThreshold = config.AutoRepairThreshold,
            AutoRepairMaxPerRun = config.AutoRepairMaxPerRun,
            AutoDiscoverCandidates = config.AutoDiscoverCandidates
        };
    }
}