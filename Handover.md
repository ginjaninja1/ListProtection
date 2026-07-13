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
  PlaylistRow.cs                (updated Task 8 — Path, InternalId, MemberCount, CapturedAt added)
  PlaylistManagementUI.cs       (DxDataGrid with cell editing + onChangeCommand)
  PlaylistManagementStore.cs    (Pattern B — HashSet<string>)
  PlaylistManagementPageView.cs (updated Task 8 — ghost detection logging, enriched BuildRows)
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

  ── TASK 8 — PROVEN (2026-07-13) ──────────────────────────────────────

  MissingMembersPageView.cs     (ProcessRepairs rewritten — full repair flow)
  PlaylistManagementPageView.cs (ghost detection logging + enriched BuildRows)
  PlaylistRow.cs                (Path, InternalId, MemberCount, CapturedAt added)

  PROBE 1 PROVEN (2026-07-13):
    result.Id from CreatePlaylist is InternalId string (e.g. "1358353")
    GetItemList(ItemIds=[long.Parse(result.Id)], IncludeItemTypes=["Playlist"])
    returns the new playlist. items[0].Id.ToString("N") = new GuidN for store key.

  PROBE 2 PROVEN (2026-07-13):
    CreatePlaylist fires PlaylistItemsAdded SYNCHRONOUSLY before the awaitable
    returns. The event fires before any post-await code runs. Therefore
    PlaylistManagementStore cannot be updated before the event fires.
    PlaylistMaintenanceService guard check always fails for new playlists.
    EVENT CHAIN CANNOT BE RELIED ON for ground truth after recreation.
    Ground truth must be written directly after CreatePlaylist, using
    GetItemList(ListIds=[newInternalId]) — the same pattern as CaptureMembers.

  Repair flow PROVEN (2026-07-13):
    Single candidate repair: playlist created, track added, stores updated.
    Subsequent candidate repairs: playlist exists, AddToPlaylist used.
    Ground truth updated correctly after both CreatePlaylist and AddToPlaylist.
    MissingMembersStore cleaned up per-member (not per-playlist) — correct.
    CandidateStore cleaned up per-member — correct.
    Tab 2 shows "No missing members" after all members repaired.

  KNOWN ISSUE (observed 2026-07-13 — LOW PRIORITY):
    First repaired member occasionally shows two ground truth entries
    (old path and new path for same track name). Likely pre-existing ground
    truth corruption from earlier test sessions, not a bug in repair logic.
    The carry-forward dedup logic (check InternalId before adding) should
    prevent this in clean state. Needs re-verification in a clean test.

NOT IMPLEMENTED
  Repair-all button on playlist row (Tab 2 — no playlist-level action yet)
  Ground truth member viewer (Tab 1 — "Members" button per playlist row)
  "Accept as missing" action (Tab 1 — acknowledge missing without replacing)
  Candidate store pruning (stale library paths)           <- Task 9
  PlaylistManagementStore schema enrichment (friendly name, m3u path)
  Ghost playlist cleanup (delete orphaned m3u on successful repair)
  IScheduledTask post-library-scan trigger                <- DEFERRED

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
  PlaylistRecreationProbeTask.cs — probe only; DELETE now Task 8 complete

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
    PlaylistRecreationProbeTask.cs  <- DELETE (Task 8 complete)
  UI/
    MainController.cs
    TabPageController.cs
    PlaylistManagement/
      PlaylistRow.cs
      PlaylistManagementUI.cs
      PlaylistManagementPageView.cs
    MissingMembers/
      MissingMemberRow.cs
      CandidateRow.cs               <- (file is CanididateRow.cs — typo in filename, do not rename)
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

  KNOWN UX LIMITATION: Full page refresh on every command round-trip.
  Grid state (expanded rows, filters, selections) resets on each action.
  The full ContentData payload is logged on every RunCommand (Task 8) so
  future work can determine whether expanded/filter state survives the
  round-trip in the payload and could be reinstated on rebuild.

--- MULTIPLE BUTTONS PER ROW (UNPROVEN — to be investigated Task 9+) ---

  The DxDataGrid framework supports multiple action buttons per row in
  principle, disambiguated by commandId in RunCommand. This needs probing
  before implementing. The proposed use case is Tab 1 playlist rows with:
    "Members" button — show current ground truth members (child grid)
    "Repair All" button — repair all missing members with top candidate
  Probe approach: add two button columns to PlaylistRow, use distinct
  commandId values, log which fires.

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
  ListIds        long[]   — filter by playlist InternalId (returns playlist members)
  ListItemIds    long[]   — filter by ListItemEntryId(s) (playlist member order)
  IncludeItemTypes string[] — e.g. new[] { "Playlist" }, new[] { "Audio" }
  Recursive      bool

  NO Guid-based filter field exists. To find a playlist by Guid:
    Load all playlists via GetItemList(IncludeItemTypes=["Playlist"], Recursive=true)
    then FirstOrDefault(p => p.Id == playlistGuid).

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
  To get the new playlist Guid after creation (PROVEN — 2026-07-13):
    long newInternalId = long.Parse(result.Id);
    var items = _libraryManager.GetItemList(new InternalItemsQuery
    {
        ItemIds = new[] { newInternalId },
        IncludeItemTypes = new[] { "Playlist" }
    });
    var newGuidN = items[0].Id.ToString("N"); // for store keys

  AddToPlaylist (PROVEN — 2026-07-13):
    Use the async overload:
      await _playlistManager.AddToPlaylist(
          existingPlaylist as Playlist,
          candidateItemIds,
          skipDuplicates: true,
          user: user,
          cancellationToken: CancellationToken.None)
    Where existingPlaylist is retrieved via:
      GetItemList(IncludeItemTypes=["Playlist"], Recursive=true)
        .FirstOrDefault(p => p.Id == oldGuid)
    The playlist item must be cast as Playlist (not BaseItem) for this overload.

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

  REPAIR STRATEGY (PROVEN — 2026-07-13):
    Check if playlist still exists in Emby before deciding which API to use:
      If playlist exists (GetItemList finds it by Guid) -> AddToPlaylist
      If playlist is gone -> CreatePlaylist
    This check happens per PlaylistId group in ProcessRepairs.

--- CREATEPLAYLIST EVENT TIMING (PROVEN — 2026-07-13) ---

  CreatePlaylist fires PlaylistItemsAdded SYNCHRONOUSLY before returning.
  The event fires during the await, before post-await code runs.
  PlaylistManagementStore cannot be updated before the event.
  PlaylistMaintenanceService guard check therefore always fails for new playlists
  created by repair — it checks IsProtected which reads PlaylistManagementStore,
  and the new GuidN is not there yet.

  CONSEQUENCE: Do NOT rely on event chain to write ground truth after CreatePlaylist.
  Write ground truth directly using GetItemList(ListIds=[newInternalId]).
  This mirrors the CaptureMembers pattern used on initial protection.

  The event chain (PlaylistMaintenanceService) correctly maintains the playlist
  going forward once PlaylistManagementStore contains the new GuidN. Only the
  initial write after recreation must be done manually.


STORAGE ARCHITECTURE
=====================================================

PATTERN A — SimpleFileStore<T> where T : EditableOptionsBase
  Used for: ConfigStore (acceptable, stays as-is)

PATTERN B — Plain store
  Constructor: (IApplicationHost, ILogger, string pluginFullName)
  Used for: all other stores

PLAYLISTMANAGEMENTSTORE SCHEMA — TODO (next coding round)
  Currently stores only GuidN strings.
  Should be extended to include per-playlist metadata:
    PlaylistName   string   — friendly name for display without cross-referencing GroundTruth
    M3uPath        string   — backing file path as reported by Emby at time of protection
  This makes the store self-describing and aids ghost diagnosis.

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

REPAIR LOGIC (PROVEN — 2026-07-13)

  ProcessRepairs in MissingMembersPageView.cs:

  1. Deserialise MissingMembersUI from data
  2. Collect candidate rows where Repair == true and IsSynthetic == false
  3. Group by PlaylistId -> list of (missingInternalId, candidateInternalId)
  4. For each PlaylistId group:
       a. Look up playlistName from GroundTruthStore
       b. Resolve User: _userManager.GetUserList(new UserQuery())[0]
       c. Build long[] candidateItemIds from candidateInternalId values
       d. Check if playlist exists in Emby:
            GetItemList(IncludeItemTypes=["Playlist"], Recursive=true)
              .FirstOrDefault(p => p.Id == oldGuid)

          IF EXISTS (AddToPlaylist path):
            Call AddToPlaylist(existingPlaylist as Playlist, candidateItemIds,
                               skipDuplicates=true, user, CancellationToken.None)
            Re-read live members via GetItemList(ListIds=[activeInternalId])
            Carry forward unrepaired members from old ground truth (by InternalId)
            Write updated GroundTruthEntry under same GuidN
            activePlaylistId = oldPlaylistId (no store key change)

          IF NOT EXISTS (CreatePlaylist path):
            Call CreatePlaylist(Name, ItemIdList=candidateItemIds, "Audio", user, IsPublic=true)
            Resolve new GuidN via GetItemList(ItemIds=[long.Parse(result.Id)])
            Update PlaylistManagementStore: remove old GuidN, add new GuidN — SAVE IMMEDIATELY
            Migrate remaining MissingMemberEntries to new GuidN (those not being repaired now)
            Migrate remaining CandidateEntries to new GuidN
            Capture ground truth directly via GetItemList(ListIds=[newInternalId])
            Carry forward unrepaired members from old ground truth (dedup by InternalId)
            Write new GroundTruthEntry under new GuidN
            Remove old GroundTruthEntry
            activePlaylistId = newGuidN

       e. Remove MissingMemberEntries where PlaylistId matches AND
          Member.InternalId is in repairedMissingIds (per-member, not per-playlist)
       f. Remove CandidateEntries where PlaylistId matches AND
          MissingMember.InternalId is in repairedMissingIds

  5. Save GroundTruthStore, MissingMembersStore, CandidateStore if changed.

  CRITICAL: Ground truth is always written directly (both paths).
    Do NOT rely on event chain for ground truth after any repair operation.
    The event chain cannot be relied on because CreatePlaylist fires
    PlaylistItemsAdded synchronously before the awaitable returns.


KNOWN DATA ISSUES
=====================================================

STALE CANDIDATE PATHS (observed 2026-07-12)
  CandidateStore may contain entries where CandidatePath points to a library
  no longer mounted. Observed when D:\ test library was removed after discovery.
  These present as repair options but point to non-existent files.
  ROOT CAUSE: CandidateDiscoverer queries all Audio items regardless of library scope.
  MITIGATION (Task 9 — DEFERRED): post-discovery prune via GetItemById.

GHOST PLAYLIST (observed 2026-07-12, not reproduced 2026-07-13)
  Original playlist e617f097c8d08fa563aa29b7118d898c lost user association
  after D:\ library was removed and Emby rescanned. The m3u file persists
  and Emby tracks it as an item but it is not user-owned and not visible in UI.
  CreatePlaylist with same name creates a new user-owned playlist alongside it.
  PlaylistManagementPageView.BuildRows now logs GHOST DETECTED warning for any
  protected GuidN not found in Emby library.
  Ghost not reproduced in 2026-07-13 test session — may have been cleared by
  earlier manual cleanup. Investigation ongoing.
  Cleanup approach: delete ghost m3u from disk or add DeleteItem call in repair flow.

DUPLICATE GROUND TRUTH MEMBERS (observed 2026-07-13 — LOW PRIORITY)
  First repaired member occasionally shows two ground truth entries for the
  same track name (old path and new path). Likely pre-existing ground truth
  corruption from earlier test sessions rather than a bug in repair logic.
  The carry-forward dedup logic checks InternalId before adding, which should
  prevent this in clean state. Needs re-verification starting from clean stores.


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

--- PLAYLIST VISIBILITY (PROVEN — 2026-07-12/13) ---

  AddToPlaylist updates the m3u correctly but does not establish user ownership.
  CreatePlaylist with User=user always produces a visible, user-owned playlist.
  PlaylistCreationResult.Id is an InternalId string, not a Guid.
  Name collision with existing ghost m3u: Emby disambiguates folder name.
  Both old and new playlists remain accessible.

--- CREATEPLAYLIST EVENT TIMING (PROVEN — 2026-07-13) ---

  PlaylistItemsAdded fires synchronously during CreatePlaylist before returning.
  Cannot update PlaylistManagementStore before the event fires.
  Do not rely on event chain for ground truth after recreation.
  Write ground truth directly after CreatePlaylist using GetItemList(ListIds=).

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
  Blow Away repair (2026-07-13):
    CreatePlaylist path — playlist created, visible, ground truth written directly
  Subsequent repairs (2026-07-13):
    AddToPlaylist path — members added to existing playlist, ground truth updated
    All missing members cleared, Tab 2 shows "No missing members"

--- ISERVICEENTRYPOINT AND STORE ACCESS (PROVEN — 2026-06-26) ---

  IServerEntryPoint cannot receive stores via constructor injection.
  Access via ListProtectionPlugin.Instance (safe from Run() onwards).

--- RunCommand PAYLOAD (PROVEN — 2026-06-26) ---

  itemId always null from DxDataGrid. commandId correct. data = full ContentData JSON.
  Full payload is now logged on every RunCommand call (Task 8) for UI state inspection.

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

TAB 1 - MANAGED PLAYLISTS (COMPLETE — PROVEN, enhancements pending)
  View all playlists with protection status
  Toggle protection on/off per playlist
  Now shows: Path, InternalId, MemberCount (from ground truth), CapturedAt
  Ghost detection logging added (GHOST DETECTED warn in server log)

  PENDING (next coding round):
    "Members" button per row — show current ground truth members as child grid
    "Repair All" button per row — repair all missing members with top candidate
    "Accept Missing" action — acknowledge members as intentionally absent,
      remove from MissingMembersStore and GroundTruthStore without replacing
    Multiple buttons per row need probing first (see DxDataGrid section)

TAB 2 - MISSING MEMBERS (COMPLETE — PROVEN)
  Grid grouped by playlist
  Forget column — immediate, no Save button
  Master-detail expand per missing member — child candidate grid
  Child grid: Candidate, Path, Score (sorted desc), Signals, Repair checkbox
  Repair fires RepairMember command — per-candidate or per-member level

  PENDING:
    Repair-all at playlist level (no button exists yet on the playlist group row)
    "Will be recreated" label against playlist group when playlist no longer
      exists in Emby (signpost to user that repair will call CreatePlaylist)

TAB 3 - CONFIGURATION (placeholder)

NOTE: Candidates implemented as child grid within Tab 2 (master-detail).
Full-screen width limitation in tab-page context — noted for future.


CURRENT TASK (AUTHORITATIVE)
=====================================================

TASK
  TASK 9 — Tab 1 enhancements + UI cleanup

STATUS
  Task 8 (ProcessRepairs) is complete and proven.
  The following items are the agreed next coding round.

WHAT TO BUILD

  1. DELETE probe files:
       Tasks/PlaylistRecreationProbeTask.cs
       Tasks/CandidateDiscoveryProbeTask.cs

  2. PROBE multiple buttons per DxDataGrid row:
       Before implementing "Members" and "Repair All" buttons on Tab 1,
       probe whether two action buttons on one row are supported and how
       commandId disambiguates them. Add two dummy button columns to
       PlaylistRow, log which commandId arrives, confirm behaviour.

  3. AFTER PROBE — implement Tab 1 enhancements:
       "Members" button per playlist row:
         Opens child grid (master-detail on Tab 1) showing current
         GroundTruthEntry members (Name, Path, InternalId, ListItemEntryId)
         Read-only — no editing in this view
       "Repair All" button per playlist row:
         Finds all MissingMemberEntries for this playlist
         Takes the highest-scoring candidate for each
         Calls ProcessRepairs logic for all in one action
       "Accept Missing" — mechanism TBD pending probe results
         Removes member from MissingMembersStore and GroundTruthStore
         Member treated as intentionally absent, will not resurface

  4. PlaylistManagementStore schema enrichment:
       Add PlaylistName and M3uPath alongside GuidN in the store
       So Tab 1 and logs are self-describing without cross-referencing GroundTruth

  5. Tab 2 UI improvements:
       Add "Will be recreated" label/indicator against playlist group row
         when the playlist GuidN is no longer found in Emby library
       Remove or relocate any "(Count: N)" display if still present

DEFINITION OF DONE — TASK 9
  Probe files deleted
  Multiple-button probe run and result recorded
  "Members" child grid working on Tab 1 (if probe confirms supported)
  "Repair All" implemented and tested
  PlaylistManagementStore schema updated
  "Will be recreated" indicator on Tab 2
  Results recorded in this document
  Task 10 defined


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
8. ProcessRepairs rewrite (CreatePlaylist strategy)   <- COMPLETE (proven 2026-07-13)
9. Tab 1 enhancements + UI cleanup                    <- CURRENT TASK
10. Candidate store pruning (stale library paths)     <- DEFERRED
11. Post-library-scan trigger                         <- DEFERRED
12. Configuration UI                                  <- DEFERRED


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
  DeleteItem on old playlist as part of repair flow (removes ghost from Emby DB)

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