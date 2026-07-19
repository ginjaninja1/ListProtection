using ListProtection.Configuration;
using ListProtection.Storage;
using ListProtection.UI;
using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
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
using System.Threading;

namespace ListProtection
{
    public class ListProtectionPlugin : BasePlugin<PluginConfiguration>, IHasThumbImage, IHasUIPages
    {
        private readonly IServerApplicationHost _applicationHost;
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IPlaylistManager _playlistManager;
        private readonly IUserManager _userManager;
        private List<IPluginUIPageController> _pages;

        public static ListProtectionPlugin Instance { get; private set; }

        /// <summary>
        /// Process-wide writer lock — all Load→mutate→Save sequences on any store
        /// must acquire this before loading and release after saving.
        /// Prevents concurrent writes from PlaylistMaintenanceService,
        /// MissingMemberDetectionService, PlaylistRepairService, and UI handlers
        /// from silently clobbering each other.
        /// Use: plugin.WriterLock.Wait() / finally plugin.WriterLock.Release()
        /// </summary>
        public SemaphoreSlim WriterLock { get; } = new SemaphoreSlim(1, 1);

        public PlaylistManagementStore PlaylistStore { get; }
        public GroundTruthStore GroundTruthStore { get; }
        public MissingMembersStore MissingMembersStore { get; }
        public CandidateStore CandidateStore { get; }
        public EventStore EventStore { get; }

        public ListProtectionPlugin(
            IApplicationPaths applicationPaths,
            IXmlSerializer xmlSerializer,
            IServerApplicationHost applicationHost,
            ILogManager logManager,
            ILibraryManager libraryManager,
            IPlaylistManager playlistManager,
            IUserManager userManager)
            : base(applicationPaths, xmlSerializer)
        {
            _applicationHost = applicationHost;
            _libraryManager = libraryManager;
            _playlistManager = playlistManager;
            _userManager = userManager;
            _logger = logManager.GetLogger(this.Name);

            PlaylistStore = new PlaylistManagementStore(applicationHost, _logger, this.Name + ".Playlist");
            GroundTruthStore = new GroundTruthStore(applicationHost, _logger, this.Name + ".GroundTruth");
            MissingMembersStore = new MissingMembersStore(applicationHost, _logger, this.Name + ".MissingMembers");
            CandidateStore = new CandidateStore(applicationHost, _logger, this.Name + ".Candidates");
            EventStore = new EventStore(applicationHost, _logger, this.Name + ".Events");

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