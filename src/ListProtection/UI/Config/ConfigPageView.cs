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
                config.AutoRepairScoreThreshold = Config.AutoRepairScoreThreshold;
                config.AutoRepairMinCandidateDistance = Config.AutoRepairMinCandidateDistance;
                config.ManualRepairScoreThreshold = Config.ManualRepairScoreThreshold;
                config.ManualRepairMinCandidateDistance = Config.ManualRepairMinCandidateDistance;
                config.EventDrivenRepairEnabled = Config.EventDrivenRepairEnabled;
                config.AudioDurationToleranceSeconds = Config.AudioDurationToleranceSeconds;
                config.EpisodeDurationToleranceSeconds = Config.EpisodeDurationToleranceSeconds;
                config.MovieDurationToleranceSeconds = Config.MovieDurationToleranceSeconds;

                plugin.SaveConfiguration();

                return Task.FromResult((IPluginUIView)this);
            }

            if (commandId == "viewscoring")
            {
                var dialog = new ScoringReferenceDialog(PluginId, ListProtectionPlugin.Instance.Configuration);
                return Task.FromResult((IPluginUIView)dialog);
            }

            return Task.FromResult((IPluginUIView)this);
        }

        private static ConfigUI BuildUI(PluginConfiguration config) => new ConfigUI
        {
            AutoRepairEnabled = config.AutoRepairEnabled,
            AutoRepairScoreThreshold = config.AutoRepairScoreThreshold,
            AutoRepairMinCandidateDistance = config.AutoRepairMinCandidateDistance,
            ManualRepairScoreThreshold = config.ManualRepairScoreThreshold,
            ManualRepairMinCandidateDistance = config.ManualRepairMinCandidateDistance,
            EventDrivenRepairEnabled = config.EventDrivenRepairEnabled,
            AudioDurationToleranceSeconds = config.AudioDurationToleranceSeconds,
            EpisodeDurationToleranceSeconds = config.EpisodeDurationToleranceSeconds,
            MovieDurationToleranceSeconds = config.MovieDurationToleranceSeconds,
        };
    }
}