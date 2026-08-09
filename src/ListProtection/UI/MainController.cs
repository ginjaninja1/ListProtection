using ListProtection.Services;
using ListProtection.Storage;
using ListProtection.UI.Config;
using ListProtection.UI.MissingMembers;
using ListProtection.UI.ListManagement;
using ListProtection.UIBaseClasses;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Collections;
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
        private readonly ListManagementStore _listStore;
        private readonly GroundTruthStore _groundTruthStore;
        private readonly MissingMembersStore _missingMembersStore;
        private readonly ILibraryManager _libraryManager;
        private readonly IPlaylistManager _playlistManager;
        private readonly ICollectionManager _collectionManager;
        private readonly IUserManager _userManager;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly ILogger _logger;
        private readonly ListRepairService _repairService;
        private readonly List<IPluginUIPageController> _tabPages = new List<IPluginUIPageController>();

        public MainController(
            PluginInfo pluginInfo,
            IServerApplicationHost applicationHost,
            ListManagementStore listStore,
            GroundTruthStore groundTruthStore,
            MissingMembersStore missingMembersStore,
            ILibraryManager libraryManager,
            IPlaylistManager playlistManager,
            ICollectionManager collectionManager,
            IUserManager userManager,
            ILogManager logManager)
            : base(pluginInfo.Id)
        {
            _pluginInfo = pluginInfo;
            _listStore = listStore;
            _groundTruthStore = groundTruthStore;
            _missingMembersStore = missingMembersStore;
            _libraryManager = libraryManager;
            _playlistManager = playlistManager;
            _collectionManager = collectionManager;
            _userManager = userManager;
            _jsonSerializer = applicationHost.Resolve<IJsonSerializer>();
            _logger = logManager.GetLogger("List Protection");

            _repairService = new ListRepairService(
                _missingMembersStore,
                _groundTruthStore,
                _listStore,
                _libraryManager,
                _playlistManager,
                _collectionManager,
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

            _tabPages.Add(new TabPageController(
                pluginInfo,
                "ListProtection",
                "ListProtection",
                info => new ListManagementPageView(
                    info,
                    _listStore,
                    _groundTruthStore,
                    _libraryManager,
                    _collectionManager,
                    _jsonSerializer,
                    _logger,
                    _repairService)));

            _tabPages.Add(new TabPageController(
                pluginInfo,
                "Configuration",
                "Configuration",
                info => new ConfigPageView(info)));
        }

        public override PluginPageInfo PageInfo { get; }

        public override Task<IPluginUIView> CreateDefaultPageView()
        {
            IPluginUIView view = new ListManagementPageView(
                _pluginInfo,
                _listStore,
                _groundTruthStore,
                _libraryManager,
                _collectionManager,
                _jsonSerializer,
                _logger,
                _repairService);
            return Task.FromResult(view);
        }

        public IReadOnlyList<IPluginUIPageController> TabPageControllers => _tabPages.AsReadOnly();
    }
}