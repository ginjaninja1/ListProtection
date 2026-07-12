PLAYLIST PROTECTION PLUGIN - AI HANDOVER DOCUMENT
=====================================================

PURPOSE

This document is the single source of truth for:

  Current implementation state
  Verified Emby behaviour
  Architectural assumptions
  Target architecture
  Development ordering
  Task completion rules

The project state must always be recoverable from this document alone.

Any discrepancy between code and this document means the document is
outdated and must be updated.


CRITICAL IMPLEMENTATION RULES
=====================================================

IMPLEMENTATION IS NOT IMPLIED BY CODE EXISTENCE

The following DO NOT mean a feature exists:

  A class exists
  A file exists
  A service is registered
  A name suggests functionality
  A partial implementation exists

A feature is ONLY considered implemented when ALL of the following are true:

  Code exists
  Code compiles
  Behaviour is tested
  Behaviour is validated
  Result is recorded in this document

Until then, it is UNIMPLEMENTED.

PROVISIONAL vs PROVEN

When recording findings, always state:
  PROVEN     — empirically confirmed in Emby runtime
  AGREED     — design decision confirmed by discussion, not yet runtime-tested
  ASSUMED    — working hypothesis, not yet tested
  DEFERRED   — intentionally left open pending more knowledge


CRITICAL AI DEVELOPMENT DIRECTIVES
=====================================================

MATCH EXISTING PATTERNS
  Before writing any constructor, store, or class — find the closest
  existing equivalent in the codebase and mirror it exactly.
  Deviation requires explicit justification and approval before writing code.
  This applies to: constructor signatures, store patterns, file path resolution,
  dependency resolution, and serialisation.

NO GUESSING
  Never assume APIs exist, events exist, payload formats exist,
  behaviour exists. Verify everything.

NO CODE AGAINST UNVERIFIED APIs
  Never write production code that calls a method or accesses a property
  that has not been confirmed from the DLL or from a proven runtime probe.
  This includes: method signatures, parameter types, property names,
  return types, and constructor shapes.
  If an API surface is needed and unverified: stop, identify exactly what
  needs peeking, ask the user to peek it, then write the code.
  The only exception is probe code itself, where the purpose is to
  discover the API surface — and even then, note what is assumed vs known.
  Guessed code wastes tokens, introduces bugs, and undermines the
  PROVEN/ASSUMED distinction that this document depends on.

SMALL STEPS ONLY
  One architectural step at a time.
  Must be tested before next step.

REALITY OVERRIDES DESIGN
  If code contradicts this document:
    Code is correct
    Document must be updated
    Assumptions must be corrected

NO PHASE SKIPPING
  Do not implement future stages early.

PROBE BEFORE PARSE
  When a new runtime value arrives (event payload, command args, etc.)
  log it raw first. Verify it matches assumptions before acting on it.

PROVEN vs PROVISIONAL
  Always distinguish empirically confirmed behaviour from agreed design
  decisions from untested assumptions. Working code is not proven design.

DO NOT MODIFY UIBaseClasses
  ControllerBase, PluginViewBase, PluginPageView, SimpleFileStore,
  SimpleContentStore, and related event args are project SDK wrappers.
  They are not to be modified.

FULL FILES ALWAYS
  Always provide complete files when making changes.
  Partial diffs cause integration errors.


CURRENT IMPLEMENTATION STATE (AUTHORITATIVE)
=====================================================

IMPLEMENTED AND VERIFIED
  Plugin loads successfully into Emby 4.9.1.90
  Plugin startup executes via IServerEntryPoint
  Event observability verified (see VERIFIED EMBY BEHAVIOURAL LEARNINGS)
  PlaylistEventProbe entry point (probe only — not production code)
  Three-tab UI structure renders in Emby                PROVEN (Task 5)
  PlaylistRow.cs
  PlaylistManagementUI.cs       (DxDataGrid with cell editing + onChangeCommand)
  PlaylistManagementStore.cs    (Pattern B — HashSet<string>)
  PlaylistManagementPageView.cs (RunCommand parses full ContentData, saves state)
  MainController.cs             (ILibraryManager, IPlaylistManager, IUserManager injected)
  Plugin.cs                     (stores constructed as singletons)
  Playlist grid renders in Emby UI                      PROVEN
  RunCommand fires on cell edit                         PROVEN
  Protection toggle persists across navigation          PROVEN
  Protection toggle persists across server restart      PROVEN
  Store file written to PluginConfigurationsPath        PROVEN
  GroundTruthStore              (Pattern B — Dictionary<string, GroundTruthEntry>)
  GroundTruthEntry              (PlaylistName, CapturedAt, IsActive, Members)
  GroundTruthMember             (InternalId, Id, Name, Path, ListItemEntryId)
  ReconcileGroundTruth          (called from RunCommand after every protected set save)
  CaptureMembers                (queries via ListIds — matches Playlist.SetQueryOptions)
  Ground truth file written on protection               PROVEN
  PlaylistName present in ground truth file             PROVEN
  ListItemEntryId populated at capture time             PROVEN
  PlaylistMaintenanceService    (IServerEntryPoint — ground truth maintenance)
  Plugin.Instance               (static singleton accessor for stores)
  Add event → member added to ground truth              PROVEN (2026-06-26)
  Remove event → member removed from ground truth       PROVEN (2026-06-26)
  ListItemEntryId round-trip confirmed (add=68, remove=68)  PROVEN (2026-06-26)

  ── TASK 5 — ALL PROVEN (2026-06-27) ──────────────────────────────────

  MissingMemberEntry.cs         (flat record: PlaylistId, PlaylistName, DetectedAt, Member)
  MissingMembersStore.cs        (Pattern B — List<MissingMemberEntry>)
  MissingMemberDetectionService.cs  (IServerEntryPoint — 60-min timer + ItemRemoved fast path)
  MissingMemberRow.cs           (grid row for Tab 2)
  MissingMembersUI.cs           (DxDataGrid — grouping, header filter, filter row)
  MissingMembersPageView.cs     (RunCommand = ForgetMember, synthetic placeholder rows)
  Plugin.cs                     (MissingMembersStore singleton added)
  MainController.cs             (Tab 2 = Missing Members, Tab 3 = Config)

  Detection timer fires at 2-minute initial delay       PROVEN (2026-06-27)
  Live readback correctly reflects externally deleted member as absent  PROVEN (2026-06-27)
  Missing member detected and written to MissingMembersStore            PROVEN (2026-06-27)
  Tab 2 renders missing member grouped under playlist                   PROVEN (2026-06-27)
  Forget updates UI, GroundTruthStore and MissingMembersStore           PROVEN (2026-06-27)
  ILibraryManager.ItemRemoved payload confirmed (see BEHAVIOURAL LEARNINGS)  PROVEN (2026-06-27)

  ── TASK 5a — ALL PROVEN (2026-06-27) ─────────────────────────────────

  MissingMemberDetector.cs      (static class — shared RunDetection logic)
  MissingMemberDetectionService.cs  (updated — fast path active, delegates to Detector)
  DetectMissingMembersTask.cs   (IScheduledTask — manual run button in Emby dashboard)

  RunDetection extracted to MissingMemberDetector static class          PROVEN (2026-06-27)
  Scheduled task appears in Emby dashboard under "List Protection"      PROVEN (2026-06-27)
  Manual Run button triggers detection via MissingMemberDetector        PROVEN (2026-06-27)
  Manual run deduplicates correctly (already-known members skipped)     PROVEN (2026-06-27)
  ItemRemoved fast path active — fires targeted detection immediately    PROVEN (2026-06-27)
  Fast path detects Swan Song (InternalId=7279) before timer fires      PROVEN (2026-06-27)
  Fast path detection correctly recorded in MissingMembersStore         PROVEN (2026-06-27)
  Subsequent manual run deduplicates fast-path result correctly         PROVEN (2026-06-27)

  ── TASK 6 — ALL PROVEN (2026-07-12) ──────────────────────────────────

  CandidateEntry.cs               (Storage/ — Pattern B model)
  CandidateStore.cs               (Storage/ — Pattern B store)
  CandidateDiscoverer.cs          (EntryPoints/ — static scoring logic)
  CandidateDiscoveryTask.cs       (Tasks/ — IScheduledTask, namespace ListProtection.Tasks)
  Plugin.cs                       (CandidateStore singleton added)

  CandidateDiscoveryTask run manually from Emby dashboard               PROVEN (2026-07-12)
  69,714 Audio items queried from library in ~0.9s                      PROVEN (2026-07-12)
  137 candidates written across 11 missing members                      PROVEN (2026-07-12)
  List Protection.Candidates.json written to PluginConfigurationsPath   PROVEN (2026-07-12)
  Deduplication correct — 2 pre-existing entries skipped on re-run      PROVEN (2026-07-12)
  Score=180 candidate always correct for each missing member            PROVEN (2026-07-12)

  SCORING BEHAVIOUR NOTES (PROVEN 2026-07-12):
    ParentFolderMatch:20 noise observed — sibling tracks from same album all score 20.
    Score < 100 candidates are noise. UI must filter to Score >= 100 by default.
    FilenameStemNormalized:70 fires on tracks with no "NN. " prefix in other libraries.

  ── TASK 7 — UI PROVEN, REPAIR STRATEGY PROVEN (2026-07-12) ───────────

  CandidateRow.cs               (UI/MissingMembers/ — child grid row)
  MissingMemberRow.cs           (updated — Candidates property added)
  MissingMembersUI.cs           (updated — master-detail wiring added)
  MissingMembersPageView.cs     (updated — BuildCandidateRows, ProcessRepairs stub)
  MainController.cs             (updated — IPlaylistManager, IUserManager added)
  Plugin.cs                     (updated — IPlaylistManager, IUserManager injected)
  PlaylistRecreationProbeTask.cs (Tasks/ — probe only, DELETE after Task 8)

  IUserManager probe confirmed                          PROVEN (2026-07-12)
    GetUserList(new UserQuery())[0] returns Cartman, InternalId=1, IsAdmin=True

  Master-detail child grid renders in Tab 2             PROVEN (2026-07-12)
  RepairMember command data shape confirmed             PROVEN (2026-07-12)
    Full MissingMembersUI ContentData arrives in data parameter
    Child row Repair state embedded in MissingMemberRows[n].Candidates[m].Repair

  AddToPlaylist probe (partial — 2026-07-12):
    AddToPlaylist called successfully — m3u file updated correctly.
    BUT: playlist not visible in Emby UI for Cartman.
    ROOT CAUSE: original playlist had lost user association (orphaned entity).
    AddToPlaylist updates the m3u but does not re-establish user ownership.

  CreatePlaylist probe PROVEN (2026-07-12):
    CreatePlaylist with User=user, ItemIdList=[long[]], IsPublic=true
    produces a user-owned playlist visible to Cartman immediately.
    10 of 11 members seeded successfully (New Heights excluded — already
    repaired and removed from CandidateStore in earlier partial test).
    Both "PLaylist 1" (name collision with ghost) and "NewPlaylistTest"
    created and visible — name collision is NOT an issue; Emby disambiguates
    folder name automatically ("PLaylist 1 [playlist] - 1").
    PlaylistCreationResult.Id returns InternalId as string (e.g. "1358178"),
    NOT a Guid. To get the Guid: resolve via GetItemList after creation
    using ItemIds=[long.Parse(result.Id)] — AGREED, not yet proven.

  CURRENT STATE OF ProcessRepairs:
    Existing implementation uses AddToPlaylist — this is the wrong strategy.
    Must be replaced with CreatePlaylist approach (see REPAIR LOGIC below).
    ProcessRepairs rewrite is Task 8.

NOT IMPLEMENTED
  ProcessRepairs rewrite using CreatePlaylist         <- Task 8
  GroundTruthStore + PlaylistManagementStore update after recreation
  Candidate store pruning (stale library paths)
  IScheduledTask post-library-scan trigger — DEFERRED

PROTOTYPE / UNVALIDATED CODE
=====================================================

Present but UNVALIDATED
  ConfidenceEngine
  SimulationService
  MissingItem model (old — superseded by MissingMemberEntry)
  CandidateItem model
  Confidence rules (FilenameMatchRule, PathMatchRule)
  CandidateDiscoveryProbeTask.cs — superseded; DELETE
  CandidateRefreshTask.cs — unknown content, unreviewed
  PlaylistRecreationProbeTask.cs — probe only; DELETE after Task 8

RULE: These components are NOT integrated, NOT verified, NOT functional
as a system. They are design scaffolding only.


PROJECT STRUCTURE (ACTUAL)
=====================================================

NAMESPACE
  ListProtection

PLUGIN CLASS
  public class ListProtectionPlugin : BasePlugin, IHasThumbImage, IHasUIPages

  BasePlugin (not generic)
  IHasUIPages — exposes UIPageControllers (IReadOnlyCollection)
  Single entry: MainController

PLUGIN DEPENDENCIES (constructor-injected)
  IServerApplicationHost
  ILogManager
  ILibraryManager
  IPlaylistManager
  IUserManager

STORES (constructed in Plugin.cs as singletons)
  PlaylistManagementStore  — file: List Protection.Playlist.json
  GroundTruthStore         — file: List Protection.GroundTruth.json
  ConfigStore              — file: List Protection.Configuration.json
  MissingMembersStore      — file: List Protection.MissingMembers.json
  CandidateStore           — file: List Protection.Candidates.json

FILE LAYOUT (ACTUAL)
  Plugin.cs
  UIBaseClasses/                    <- DO NOT MODIFY
    ControllerBase.cs
    PluginViewBase.cs
    PluginPageView.cs
    PluginDialogueView.cs
    Store/
      SimpleFileStore.cs
      SimpleContentStore.cs
      FileSavingEventArgs.cs
      FileSavedEventArgs.cs
  EntryPoints/
    PlaylistEventProbe.cs           <- probe only
    PlaylistMaintenanceService.cs
    MissingMemberDetectionService.cs
    MissingMemberDetector.cs
    CandidateDiscoverer.cs
  Tasks/
    DetectMissingMembersTask.cs
    CandidateDiscoveryTask.cs
    CandidateDiscoveryProbeTask.cs  <- DELETE
    CandidateRefreshTask.cs         <- unreviewed
    PlaylistRecreationProbeTask.cs  <- DELETE after Task 8
  UI/
    MainController.cs
    TabPageController.cs
    PlaylistManagement/
      PlaylistRow.cs
      PlaylistManagementUI.cs
      PlaylistManagementPageView.cs
    MissingMembers/
      MissingMemberRow.cs
      CandidateRow.cs
      MissingMembersUI.cs
      MissingMembersPageView.cs
    Config/
      ConfigUI.cs
      ConfigPageView.cs
  Storage/
    PlaylistManagementStore.cs
    GroundTruthStore.cs
    GroundTruthEntry.cs
    GroundTruthMember.cs
    ConfigStore.cs
    MissingMembersStore.cs
    MissingMemberEntry.cs
    CandidateStore.cs
    CandidateEntry.cs


EMBY PLUGIN UI FRAMEWORK (FULLY AUDITED)
=====================================================

--- PLUGIN SIGNATURE (ACTUAL) ---

  public class ListProtectionPlugin : BasePlugin, IHasThumbImage, IHasUIPages

  IHasUIPages exposes UIPageControllers (IReadOnlyCollection<IPluginUIPageController>)
  MainController is the single entry in UIPageControllers.
  MainController also implements IHasTabbedUIPages — this is how tabs are achieved.

--- TAB ARCHITECTURE ---

  MainController : ControllerBase, IHasTabbedUIPages
    Owns TabPageControllers (IReadOnlyList<IPluginUIPageController>)
    Each tab is a TabPageController — a generic factory taking a view factory func
    CreateDefaultPageView() on MainController returns Tab 1 view directly

  TabPageController : ControllerBase
    Constructor: (PluginInfo, name, displayName, Func<PluginInfo, IPluginUIView>)
    CreateDefaultPageView() invokes the factory func
    Stateless — view is constructed fresh on each navigation

--- UIBaseClasses (PROJECT SDK WRAPPERS — DO NOT MODIFY) ---

  RunCommand SIGNATURE (PROVEN):
    public virtual Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
    Returns Task<IPluginUIView>.
    IServerEntryPoint.Run() returns void.

--- CLIENT-SERVER INTERACTION MODEL ---

  Emby UI is round-trip stateless:
  1. CreateDefaultPageView() called -> build view from store
  2. User interacts -> command fires
  3. RunCommand(itemId, commandId, data) called on view
  4. RunCommand -> parse data -> update store -> rebuild ContentData -> return this
  5. Emby re-renders from returned view

  Store is always source of truth. View is always a pure projection.
  Never hold mutable state on the view between round-trips.

--- DxDataGrid (PROVEN FROM DLL) ---

  DxGridOptions constructor:
    DxGridOptions(object editObject, string keyExpr, bool multiSelect,
                  bool disableColumnChooser, bool showFilterRow, bool showHeaderFilter)

  DxGridOnChangeCommand properties:
    commandId  string  — arrives as commandId in RunCommand
    data2      string  — client-side only, NOT passed as RunCommand parameter

  DxGridOptions.editing — NULL by default; must set explicitly:
    editing = new DxGridEditing { mode = GridEditMode.cell, allowUpdating = true }

  DxGridColumn key properties:
    dataField, groupIndex, showWhenGrouped, autoExpandGroup, allowEditing,
    allowHeaderFiltering, visible, sortIndex, sortOrder, caption, width,
    isSecondaryGridDataSource (marks column as child row data source for master-detail)

--- DxDataGrid MASTER-DETAIL (PROVEN — 2026-07-12) ---

  DxGridMasterDetail properties:
    enabled            bool?
    autoExpandAll      bool?
    childRowsFieldName string        — property name on master row holding child rows
    detailGridOptions  DxGridOptions — full grid options for the child grid

  Wiring pattern:
  1. Master row has child collection: public CandidateRow[] Candidates { get; set; }
  2. Master column post-processing: col.isSecondaryGridDataSource = true; col.visible = false
  3. Child DxGridOptions built from CandidateRow with its own onChangeCommand
  4. options.masterDetail = new DxGridMasterDetail { enabled=true, autoExpandAll=false,
       childRowsFieldName="Candidates", detailGridOptions=detailOptions }

  LIMITATION: Child grid cannot expand to full screen width in tab-page context.
  Only possible inside a dialog view. Noted for future if column space is a problem.

--- RunCommand PAYLOAD FOR MASTER-DETAIL (PROVEN — 2026-07-12) ---

  Both master and child grid commands receive the ENTIRE ContentData as JSON in data.
  Child row state is embedded in MissingMemberRows[n].Candidates[m].
  itemId is always null. commandId distinguishes ForgetMember vs RepairMember.

--- CANDIDATE KEY FORMAT ---

  CandidateRow.Key = "{PlaylistId}_{MissingInternalId}_{CandidateInternalId}"
  All three components contain no underscores. Safe to Split('_'), check Length >= 3.
  parts[0] = PlaylistId (Guid "N", 32 chars)
  parts[1] = MissingInternalId (long)
  parts[2] = CandidateInternalId (long)

--- ILibraryManager.GetItemList (PROVEN FROM DLL) ---

  Returns: BaseItem[] (array — use .Length not .Count)

--- InternalItemsQuery KEY FIELDS (PROVEN FROM DLL) ---

  ItemIds        long[]   — filter by InternalId(s)
  ListItemIds    long[]   — filter by ListItemEntryId(s) (playlist member order)
  IncludeItemTypes string[] — e.g. new[] { "Playlist" }, new[] { "Audio" }
  Recursive      bool

  NO Guid-based filter field exists. To find a playlist by Guid:
    Load all playlists via GetItemList(IncludeItemTypes=["Playlist"])
    then FirstOrDefault(p => p.Id == playlistGuid) as Playlist.

--- IScheduledTask (PROVEN FROM DLL) ---

  string Name, Key, Description, Category
  Task Execute(CancellationToken, IProgress<double>)
  IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
  Constructor injection of ILibraryManager, IPlaylistManager, IUserManager works.
  Stores accessed via ListProtectionPlugin.Instance.
  Namespace: ListProtection.Tasks

--- IUserManager (PROVEN FROM DLL) ---

  User[] GetUserList(UserQuery query)   <- USE THIS
  UserPolicy GetUserPolicy(User user)   <- check IsAdministrator

  USER RESOLUTION (PROVEN — 2026-07-12):
    var user = _userManager.GetUserList(new UserQuery())[0];
    Returns Cartman, InternalId=1, IsAdministrator=true on this server.

--- IPlaylistManager (PROVEN FROM DLL) ---

  void AddToPlaylist(long playlistId, long[] itemIds, User user)
  Task<AddToPlaylistResult> AddToPlaylist(Playlist playlist, long[] itemIds,
      bool skipDuplicates, User user, CancellationToken cancellationToken)
  Task RemoveFromPlaylist(long playlistId, long[] entryIds)
  Task<PlaylistCreationResult> CreatePlaylist(PlaylistCreationRequest options)

  PlaylistCreationRequest (PROVEN FROM DLL):
    string   Name
    long[]   ItemIdList  — InternalIds to seed playlist (can seed all at once)
    string   MediaType   — "Audio"
    User     User        — REQUIRED for user ownership and UI visibility
    bool     IsPublic    — set true

  PlaylistCreationResult (PROVEN FROM DLL):
    string Id             — InternalId as string (NOT a Guid) e.g. "1358178"
    string Name
    int    ItemAddedCount

  CRITICAL: CreatePlaylist.Id is an InternalId string, not a Guid.
  To get the new playlist Guid after creation:
    long newInternalId = long.Parse(result.Id);
    var items = _libraryManager.GetItemList(new InternalItemsQuery
    {
        ItemIds = new[] { newInternalId },
        IncludeItemTypes = new[] { "Playlist" }
    });
    var newGuid = items[0].Id; // Guid
    var newGuidN = newGuid.ToString("N"); // for store keys
  AGREED — not yet proven. Probe in Task 8 before writing store update logic.

  PLAYLIST VISIBILITY (PROVEN — 2026-07-12):
    AddToPlaylist updates the m3u file correctly but does NOT establish
    user ownership. If the playlist entity has lost its user association
    (e.g. original playlist deleted/orphaned), AddToPlaylist will succeed
    at the file level but the playlist will not appear in the Emby UI.
    CreatePlaylist with User=user always produces a visible, user-owned playlist.

  NAME COLLISION (PROVEN — 2026-07-12):
    Creating a playlist with the same name as an existing ghost m3u is safe.
    Emby disambiguates the folder name automatically (e.g. "PLaylist 1 [playlist] - 1").
    Both old and new playlists are accessible. No data loss.

  REPAIR STRATEGY (PROVEN — 2026-07-12):
    Use CreatePlaylist (not AddToPlaylist) for repair.
    Seed all repaired member InternalIds in ItemIdList in one call.
    This guarantees user ownership and immediate UI visibility.


STORAGE ARCHITECTURE
=====================================================

PATTERN A — SimpleFileStore<T> where T : EditableOptionsBase
  Used for: ConfigStore (acceptable, stays as-is)

PATTERN B — Plain store
  Constructor: (IApplicationHost, ILogger, string pluginFullName)
  Used for: all other stores

GROUND TRUTH ENTRY SHAPE

  GroundTruthEntry
    PlaylistName    string
    CapturedAt      DateTime
    IsActive        bool
    Members         List<GroundTruthMember>

  GroundTruthMember
    InternalId      long
    Id              string    — Guid "N"
    Name            string
    Path            string
    ListItemEntryId long

MISSING MEMBER ENTRY SHAPE

  MissingMemberEntry
    PlaylistId      string            — Guid "N"
    PlaylistName    string
    DetectedAt      DateTime
    Member          GroundTruthMember

CANDIDATE ENTRY SHAPE

  CandidateEntry
    PlaylistId            string
    PlaylistName          string
    MissingMember         GroundTruthMember
    CandidateInternalId   long
    CandidateId           string            — Guid "N"
    CandidateName         string
    CandidatePath         string
    Score                 int
    MatchedSignals        List<string>
    DiscoveredAt          DateTime

CANDIDATE SCORING SIGNALS (PROVEN — 2026-07-12)

  FilenameStemExact      100
  FilenameStemNormalized  70
  NameExact               60
  NameNormalized          40
  ParentFolderMatch       20

  UI display threshold: Score >= 100
  Dedup: (PlaylistId, MissingMember.InternalId, CandidateInternalId)

GROUND TRUTH RECONCILE LOGIC

  1. For every entry not in protected set -> IsActive = false
  2. For every id in protected set:
       Active entry exists -> no action
       Soft-deleted entry -> restore IsActive = true
       No entry -> CaptureMembers
  3. Save

FORGET SEMANTICS (PROVEN 2026-06-27)

  Forget = remove from MissingMembersStore AND remove Member from GroundTruthStore.
  Member never surfaces again.

MISSING MEMBER KEY FORMAT

  Real:      "{PlaylistId}_{Member.InternalId}"   (PlaylistId = 32 hex chars)
  Synthetic: "synthetic_{PlaylistId}"

REPAIR LOGIC (Task 8 — AGREED, not yet implemented in production)

  ProcessRepairs rewrite — replaces current AddToPlaylist implementation:

  1. Deserialise MissingMembersUI from data
  2. Collect all candidate rows where Repair == true and IsSynthetic == false
  3. Group by PlaylistId
  4. For each PlaylistId group:
       a. Resolve User: _userManager.GetUserList(new UserQuery())[0]
       b. Build long[] itemIds from CandidateInternalId values in the group
          (look up each from CandidateStore to confirm entry still exists)
       c. Get PlaylistName from GroundTruthStore entry for this PlaylistId
       d. Call CreatePlaylist:
            await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
            {
                Name = playlistName,
                ItemIdList = itemIds,
                MediaType = "Audio",
                User = user,
                IsPublic = true
            })
       e. Log result.Id, result.Name, result.ItemAddedCount
       f. Resolve new Guid from new InternalId (see IPlaylistManager section above)
          PROBE THIS STEP before writing — resolution pattern is AGREED not PROVEN
       g. Update GroundTruthStore:
            Remove old entry keyed by old PlaylistId Guid "N"
            The PlaylistMaintenanceService event chain will write the new entry
            when ItemUpdated fires after CreatePlaylist — DO NOT write manually
       h. Update PlaylistManagementStore:
            Remove old PlaylistId Guid "N"
            Add new Guid "N"
       i. Remove all MissingMemberEntries for this PlaylistId from MissingMembersStore
       j. Remove all CandidateEntries for this PlaylistId from CandidateStore
  5. Save all modified stores. Rebuild view.

  CRITICAL: Do NOT manually update GroundTruthStore members after CreatePlaylist.
    PlaylistMaintenanceService handles PlaylistItemsAdded -> ItemUpdated -> readback.
    Manually writing would race with the event chain.
    Only remove the old stale entry — the new one is written by the event chain.

  UNPROVEN STEPS requiring probe before implementing:
    New Guid resolution via GetItemList(ItemIds=[long.Parse(result.Id)])
    Whether PlaylistMaintenanceService correctly handles the new PlaylistId
    (it fires on PlaylistItemsAdded for the new playlist — needs the new Guid
    to be in PlaylistManagementStore before the event fires, otherwise the
    guard check "is this playlist protected?" will fail and members won't be
    written to ground truth)

  ORDER DEPENDENCY: Update PlaylistManagementStore with new Guid BEFORE
    CreatePlaylist call returns, if possible — otherwise do it immediately
    after and before any await. The event chain may fire on a thread pool
    thread concurrently. Safe approach: update store immediately after
    getting result.Id, before any further async work.


KNOWN DATA ISSUES
=====================================================

STALE CANDIDATE PATHS (observed 2026-07-12)
  CandidateStore may contain entries where CandidatePath points to a library
  no longer mounted. Observed when D:\ test library was removed after discovery.
  These present as repair options but point to non-existent files.
  ROOT CAUSE: CandidateDiscoverer queries all Audio items regardless of library scope.
  MITIGATION (Task 9 — DEFERRED): post-discovery prune via GetItemById.

GHOST PLAYLIST (observed 2026-07-12)
  Original playlist e617f097c8d08fa563aa29b7118d898c lost user association
  after D:\ library was removed and Emby rescanned. The m3u file persists
  and Emby tracks it as an item but it is not user-owned and not visible in UI.
  CreatePlaylist with same name creates a new user-owned playlist alongside it.
  The ghost is harmless but untidy. Cleanup: delete ghost m3u from disk,
  or ignore — it does not affect repair correctness.


VERIFIED EMBY BEHAVIOURAL LEARNINGS
=====================================================

--- SDK / API NOTES ---

  BaseItem.InternalId   — long
  GetItemList()         — returns BaseItem[] (array), use .Length not .Count
  InternalItemsQuery has no Guid filter field — use ItemIds (long[]) instead
  MediaBrowser.Server.Core NuGet (4.9.1.90) contains all necessary assemblies

--- AUDIO ITEM FIELDS (PROVEN — 2026-06-27) ---

  Name, Path, FileName, FileNameWithoutExtension, Album, RunTimeTicks,
  IndexNumber, ProductionYear, Audio.Artists, Audio.AlbumArtists — all populated
  Cast: (item as Audio) — returns null if not Audio subtype

--- PLAYLIST VISIBILITY (PROVEN — 2026-07-12) ---

  AddToPlaylist updates the m3u correctly but does not establish user ownership.
  CreatePlaylist with User=user always produces a visible, user-owned playlist.
  PlaylistCreationResult.Id is an InternalId string, not a Guid.
  Name collision with existing ghost m3u: Emby disambiguates folder name.
  Both old and new playlists remain accessible.

--- EVENT OBSERVABILITY (PROVEN — Task 1) ---

  PlaylistItemsAdded, PlaylistItemsRemoved, ItemUpdated (after add only)
  ItemAdded/ItemRemoved do NOT fire for playlist membership changes.

  PlaylistItemsAdded payload: Playlist, ListItem[] (ListItemEntryId=0 at event time)
  PlaylistItemsRemoved payload: Playlist, long[] (ListItemEntryIds)
  ItemUpdated payload: ItemChangeEventArgs (Type="Playlist")
  ItemRemoved payload: ItemChangeEventArgs (Type="Audio", InternalId of media item)

--- LISTITERENTRYID ASSIGNMENT (PROVEN) ---

  ListItemEntryId is 0 at PlaylistItemsAdded event time.
  Assigned after DB write. Recovered via ItemUpdated -> readback -> match InternalId.

--- ROUND-TRIP PROOFS ---

  Blue Christmas (2026-06-24): add=7285, readback EntryId=68, remove EntryId=68
  Child In Time (2026-06-26): full maintenance cycle PROVEN
  What I Wouldn't Do (2026-06-27): full detection cycle PROVEN
  Swan Song (2026-06-27): fast path + scheduled task PROVEN
  New Heights (2026-07-12): candidate discovery PROVEN
  New Heights repair (2026-07-12):
    AddToPlaylist -> m3u updated, PlaylistMaintenanceService wrote ground truth
    BUT playlist not visible in UI (lost user association)
  CreatePlaylist probe (2026-07-12):
    10 members seeded, both test playlists visible to Cartman immediately

--- ISERVICEENTRYPOINT AND STORE ACCESS (PROVEN — 2026-06-26) ---

  IServerEntryPoint cannot receive stores via constructor injection.
  Access via ListProtectionPlugin.Instance (safe from Run() onwards).

--- RunCommand PAYLOAD (PROVEN — 2026-06-26) ---

  itemId always null from DxDataGrid. commandId correct. data = full ContentData JSON.

--- DxDataGrid EDITING (PROVEN — 2026-06-26) ---

  editing is NULL by default. Must set explicitly or cells are read-only.

--- DxDataGrid GROUPING (PROVEN — 2026-06-27) ---

  groupIndex=0 produces correct grouping. showWhenGrouped=false hides column.

--- MASTER-DETAIL GRID (PROVEN — 2026-07-12) ---

  See DxDataGrid MASTER-DETAIL section above.
  childRowsFieldName + isSecondaryGridDataSource wiring confirmed working.
  Child grid onChangeCommand fires with full master ContentData in data param.

--- REMOVE EVENT DOES NOT FIRE ItemUpdated ---

  Only PlaylistItemsRemoved fires on remove. No readback possible.

--- ListItemEntryId AT CAPTURE TIME (PROVEN — 2026-06-26) ---

  Correctly populated when reading playlist members outside of event context.


UI DESIGN
=====================================================

TAB 1 - MANAGED PLAYLISTS (COMPLETE — PROVEN)
  View all playlists with protection status
  Toggle protection on/off per playlist

TAB 2 - MISSING MEMBERS (COMPLETE — PROVEN, repair strategy updated Task 8)
  Grid grouped by playlist
  Forget column — immediate, no Save button
  Master-detail expand per missing member — child candidate grid
  Child grid: Candidate, Path, Score (sorted desc), Signals, Repair checkbox
  Repair fires RepairMember command

TAB 3 - CONFIGURATION (placeholder)

NOTE: Candidates implemented as child grid within Tab 2 (master-detail).
Full-screen width limitation in tab-page context — noted for future.


CURRENT TASK (AUTHORITATIVE)
=====================================================

TASK
  TASK 8 — ProcessRepairs rewrite + store update after CreatePlaylist

STATUS
  ProcessRepairs currently uses AddToPlaylist (wrong strategy — proven insufficient).
  Must be rewritten to use CreatePlaylist.
  Two unproven steps require probing before the rewrite is complete.

WHAT TO DO FIRST (before writing any code)

  PROBE 1 — New Guid resolution after CreatePlaylist:
    After calling CreatePlaylist, result.Id is an InternalId string (e.g. "1358178").
    Probe: call GetItemList(new InternalItemsQuery
    {
        ItemIds = new[] { long.Parse(result.Id) },
        IncludeItemTypes = new[] { "Playlist" }
    })
    Log items[0].Id (Guid) and items[0].Id.ToString("N") (Guid "N" for store key).
    Confirm this returns the new playlist and its Guid before writing store update.

  PROBE 2 — PlaylistManagementStore update timing:
    The PlaylistMaintenanceService checks PlaylistManagementStore on PlaylistItemsAdded.
    If the new Guid is not in the store when the event fires, members won't be
    written to ground truth.
    Update PlaylistManagementStore with new Guid immediately after getting result.Id,
    before any await. Then probe whether ground truth is correctly written.

WHAT TO BUILD

  Rewrite ProcessRepairs in MissingMembersPageView.cs:
    Group repaired candidates by PlaylistId.
    For each group: call CreatePlaylist with all CandidateInternalIds in ItemIdList.
    Immediately update PlaylistManagementStore with new Guid "N".
    Remove old PlaylistId from PlaylistManagementStore and old entry from
    GroundTruthStore (event chain writes new entry — do not write manually).
    Remove MissingMemberEntries and CandidateEntries for repaired PlaylistId.
    Save stores. Rebuild view.

  See REPAIR LOGIC section for full spec.

  After repair: delete PlaylistRecreationProbeTask.cs and CandidateDiscoveryProbeTask.cs.

DEFINITION OF DONE — TASK 8
  ProcessRepairs rewritten using CreatePlaylist
  New playlist visible to Cartman in Emby UI after repair
  GroundTruthStore correctly updated via event chain (not manually)
  PlaylistManagementStore updated with new Guid
  MissingMembersStore and CandidateStore cleaned up
  All 11 members repaired in a single run
  Ghost playlist handling documented
  Results recorded in this document
  Task 9 defined


IMPLEMENTATION ROADMAP (ORDERED)
=====================================================

1. Verify event observability                         <- COMPLETE
2. Playlist protection UI (Tab 1)                     <- COMPLETE
3. Ground truth capture on protection                 <- COMPLETE
4. Ground truth maintenance (add/remove events)       <- COMPLETE
5. Missing member detection                           <- COMPLETE (proven 2026-06-27)
5a. Fast path activation + scheduled task             <- COMPLETE (proven 2026-06-27)
6. Candidate discovery                                <- COMPLETE (proven 2026-07-12)
7. Candidates UI + repair workflow (UI)               <- COMPLETE (proven 2026-07-12)
8. ProcessRepairs rewrite (CreatePlaylist strategy)   <- CURRENT TASK
9. Candidate store pruning (stale library paths)      <- DEFERRED
10. Post-library-scan trigger                         <- DEFERRED
11. Configuration UI                                  <- DEFERRED


SESSION COMPLETION RULE
=====================================================

A session is NOT complete unless:
  Code is written
  Code is tested
  Behaviour verified
  Results recorded
  This document updated
  Next task defined

If any step is missing, work is incomplete.


FUTURE IDEAS (NOT MVP)
=====================================================

  Ground truth restore prompt on re-tick
  Playlist recovery from Emby m3u files
  Advanced matching rules / improved scoring
  Post-library-scan trigger for DetectMissingMembersTask
  PlaylistMaintenanceService _pendingAdds TTL cleanup
  MissingMemberDetectionService timer interval via ConfigStore
  CandidateRefreshTask.cs — review and integrate or delete
  Full-screen candidate view via dialog if column space is a problem
  Ghost playlist cleanup — delete orphaned m3u files after successful repair

These are explicitly deferred.

AI SESSION BOOTSTRAP INSTRUCTIONS
=====================================================

These instructions exist because AI assistants consistently waste time at
session start trying to read this file via web search or web fetch tools,
both of which fail. Follow this procedure exactly.

READING THIS FILE
  The only method that works is bash curl against raw.githubusercontent.com.
  The default branch is MASTER (not main) — CONFIRMED by repository owner.

  curl -sL "https://raw.githubusercontent.com/ginjaninja1/ListProtection/master/Handover.md"

  Do not attempt:
    web_search for the file
    web_fetch of github.com URLs
    web_fetch of raw.githubusercontent.com
    GitHub API (api.github.com) — rate-limited without auth token

WRITING THIS FILE
  This document cannot be pushed to GitHub from the AI session.
  Workflow:
    1. AI produces updated Handover.md as a file artifact for download
    2. User manually commits and pushes to the repository

READING SOURCE FILES
  curl -sL "https://raw.githubusercontent.com/ginjaninja1/ListProtection/master/PATH/TO/FILE"
  Branch is master.

=====================================================
END OF DOCUMENT