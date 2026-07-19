using ListProtection.Services;
using ListProtection.Storage;
using ListProtection.UI.Config;
using ListProtection.UI.MissingMembers;
using ListProtection.UI.PlaylistManagement;
using ListProtection.UIBaseClasses;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI;
using MediaBrowser.Model.Plugins.UI.Views;
using MediaBrowser.Model.Serialization;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ListProtection.UI
{
    internal class MainController : ControllerBase, IHasTabbedUIPages
    {
        private readonly PluginInfo _pluginInfo;
        private readonly PlaylistManagementStore _playlistStore;
        private readonly GroundTruthStore _groundTruthStore;
        private readonly MissingMembersStore _missingMembersStore;
        private readonly ILibraryManager _libraryManager;
        private readonly IPlaylistManager _playlistManager;
        private readonly IUserManager _userManager;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly ILogger _logger;
        private readonly PlaylistRepairService _repairService;
        private readonly List<IPluginUIPageController> _tabPages = new List<IPluginUIPageController>();

        public MainController(
            PluginInfo pluginInfo,
            IServerApplicationHost applicationHost,
            PlaylistManagementStore playlistStore,
            GroundTruthStore groundTruthStore,
            MissingMembersStore missingMembersStore,
            ILibraryManager libraryManager,
            IPlaylistManager playlistManager,
            IUserManager userManager,
            ILogManager logManager)
            : base(pluginInfo.Id)
        {
            _pluginInfo = pluginInfo;
            _playlistStore = playlistStore;
            _groundTruthStore = groundTruthStore;
            _missingMembersStore = missingMembersStore;
            _libraryManager = libraryManager;
            _playlistManager = playlistManager;
            _userManager = userManager;
            _jsonSerializer = applicationHost.Resolve<IJsonSerializer>();
            _logger = logManager.GetLogger(nameof(MainController));

            _repairService = new PlaylistRepairService(
                _missingMembersStore,
                _groundTruthStore,
                _playlistStore,
                _libraryManager,
                _playlistManager,
                _userManager,
                _logger);

            PageInfo = new PluginPageInfo
            {
                Name = "ListProtection",
                EnableInMainMenu = true,
                DisplayName = "List Protector",
                MenuIcon = "shield",
                IsMainConfigPage = true
            };

            // Tab 1 — Managed Playlists
            _tabPages.Add(new TabPageController(
                pluginInfo,
                "ListProtection",
                "ListProtection",
                info => new PlaylistManagementPageView(
                    info,
                    _playlistStore,
                    _groundTruthStore,
                    _libraryManager,
                    _jsonSerializer,
                    _logger,
                    _repairService)));

            // Tab 2 — Missing Members (hidden — functionality now covered by Repair dialog)
            // Uncomment to restore the standalone missing members tab.
            //_tabPages.Add(new TabPageController(
            //    pluginInfo,
            //    "MissingMembers",
            //    "Missing Members",
            //    info => new MissingMembersPageView(
            //        info,
            //        _missingMembersStore,
            //        _groundTruthStore,
            //        _playlistStore,
            //        _jsonSerializer,
            //        _logger,
            //        _libraryManager,
            //        _repairService)));

            // Tab 3 — Configuration
            // ConfigPageView reads from Plugin.Instance.Configuration directly;
            // no store reference needed.
            _tabPages.Add(new TabPageController(
                pluginInfo,
                "Configuration",
                "Configuration",
                info => new ConfigPageView(info)));
        }

        public override PluginPageInfo PageInfo { get; }

        public override Task<IPluginUIView> CreateDefaultPageView()
        {
            IPluginUIView view = new PlaylistManagementPageView(
                _pluginInfo,
                _playlistStore,
                _groundTruthStore,
                _libraryManager,
                _jsonSerializer,
                _logger,
                _repairService);
            return Task.FromResult(view);
        }

        public IReadOnlyList<IPluginUIPageController> TabPageControllers => _tabPages.AsReadOnly();
    }
}