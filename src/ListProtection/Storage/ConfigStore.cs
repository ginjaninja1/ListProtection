using ListProtection.UIBaseClasses.Store;
using ListProtection.UI.Config;
using MediaBrowser.Common;
using MediaBrowser.Model.Logging;

namespace ListProtection.Storage
{
    public class ConfigStore : SimpleFileStore<ConfigUI>
    {
        public ConfigStore(IApplicationHost appHost, ILogger logger, string pluginFullName)
            : base(appHost, logger, pluginFullName)
        {
        }
    }
}
