using ListProtection.Storage;
using ListProtection.UI;
using MediaBrowser.Common;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.IO;

namespace ListProtection
{
    public class ListProtectionPlugin : BasePlugin, IHasThumbImage, IHasUIPages
    {
        private readonly IServerApplicationHost _applicationHost;
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IPlaylistManager _playlistManager;
        private readonly IUserManager _userManager;
        private List<IPluginUIPageController> _pages;

        public static ListProtectionPlugin Instance { get; private set; }

        public PlaylistManagementStore PlaylistStore { get; }
        public GroundTruthStore GroundTruthStore { get; }
        public MissingMembersStore MissingMembersStore { get; }
        public CandidateStore CandidateStore { get; }

        private readonly ConfigStore _configStore;

        public ListProtectionPlugin(
            IServerApplicationHost applicationHost,
            ILogManager logManager,
            ILibraryManager libraryManager,
            IPlaylistManager playlistManager,
            IUserManager userManager)
        {
            _applicationHost = applicationHost;
            _libraryManager = libraryManager;
            _playlistManager = playlistManager;
            _userManager = userManager;
            _logger = logManager.GetLogger(this.Name);

            PlaylistStore = new PlaylistManagementStore(applicationHost, _logger, this.Name + ".Playlist");
            _configStore = new ConfigStore(applicationHost, _logger, this.Name + ".Configuration");
            GroundTruthStore = new GroundTruthStore(applicationHost, _logger, this.Name + ".GroundTruth");
            MissingMembersStore = new MissingMembersStore(applicationHost, _logger, this.Name + ".MissingMembers");
            CandidateStore = new CandidateStore(applicationHost, _logger, this.Name + ".Candidates");

            Instance = this;
        }

        public override string Description => "Provides self heal and heal candidates to List missing members";
        public override Guid Id => new Guid("B2C3D4E5-F6A7-4B8C-9D0E-1F2A3B4C5D6E");
        public override string Name => "List Protection";

        public ImageFormat ThumbImageFormat => ImageFormat.Png;

        public Stream GetThumbImage()
            => this.GetType().Assembly.GetManifestResourceStream(this.GetType().Namespace + ".thumb.png");

        public IReadOnlyCollection<IPluginUIPageController> UIPageControllers
        {
            get
            {
                if (_pages == null)
                {
                    _pages = new List<IPluginUIPageController>
                    {
                        new MainController(
                            this.GetPluginInfo(),
                            _applicationHost,
                            PlaylistStore,
                            GroundTruthStore,
                            MissingMembersStore,
                            _configStore,
                            _libraryManager,
                            _playlistManager,
                            _userManager,
                            _applicationHost.Resolve<ILogManager>())
                    };
                }
                return _pages.AsReadOnly();
            }
        }
    }
}