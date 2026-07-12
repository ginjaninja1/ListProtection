using System.Threading.Tasks;
using ListProtection.Storage;
using ListProtection.UIBaseClasses.Views;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;

namespace ListProtection.UI.Config
{
    internal class ConfigPageView : PluginPageView
    {
        private readonly ConfigStore store;

        public ConfigPageView(PluginInfo pluginInfo, ConfigStore store)
            : base(pluginInfo.Id)
        {
            this.store = store;
            this.ContentData = store.GetOptions();

            /*
             ShowSave = true;
             ShowBack = true;
             AllowSave = true;
             AllowBack = true;
             */
            ;
        }

        public ConfigUI Config => this.ContentData as ConfigUI;

        public override Task<IPluginUIView> OnSaveCommand(string itemId, string commandId, string data)
        {
            this.store.SetOptions(this.Config);
            return base.OnSaveCommand(itemId, commandId, data);
        }
    }
}
