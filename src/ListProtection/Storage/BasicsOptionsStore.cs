
using MediaBrowser.Common;
using MediaBrowser.Model.Logging;
using ListProtection.UI.Config;
using ListProtection.UIBaseClasses.Store;

namespace ListProtection.Storage
{
    public class BasicsOptionsStore : SimpleFileStore<ConfigUI>
    {
        public BasicsOptionsStore(IApplicationHost applicationHost, ILogger logger, string pluginFullName)
        : base(applicationHost, logger, pluginFullName)
        {
        }
    }
}
