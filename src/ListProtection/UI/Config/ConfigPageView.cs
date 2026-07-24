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

        public override Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
        {
            if (commandId == "updateconfig")
            {
                var plugin = ListProtectionPlugin.Instance;
                var config = plugin.Configuration;

                config.AutoRepairEnabled = Config.AutoRepairEnabled;
                config.AutoDiscoverCandidates = Config.AutoDiscoverCandidates;

                plugin.SaveConfiguration();
            }

            return Task.FromResult((IPluginUIView)this);
        }

        private static ConfigUI BuildUI(PluginConfiguration config) => new ConfigUI
        {
            AutoRepairEnabled = config.AutoRepairEnabled,
            AutoDiscoverCandidates = config.AutoDiscoverCandidates
        };
    }
}