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


REALITY OVERRIDES DESIGN
  If code contradicts this document:
    Code is correct
    Document must be updated
    Assumptions must be corrected


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

FILE SEPARATION PRINCIPLE
  UI.cs files contain bare control definitions only (columns, grid options,
  button items). No business logic, no row building.
  PageView.cs files contain PluginPageView/PluginDialogView shell, command
  routing, and page/dialog lifecycle only.
  Row builders are separate classes (e.g. *RowBuilder.cs).
  Business logic is separate services (e.g. PlaylistRepairService.cs).
  This is an agreed principle; existing files have not yet been fully
  refactored to comply — apply to new work first.

READ HANDOVER.MD FIRST
  Every new session must begin by reading this document before proposing
  or writing anything. Session starts with: clone repo, read Handover.md,
  confirm understanding, then proceed.


CURRENT IMPLEMENTATION STATE (AUTHORITATIVE)
=====================================================

IMPLEMENTED AND VERIFIED

  ── TASKS 1-5 — ALL PROVEN ─────────────────────────────────────────────

  Plugin loads into Emby 4.9.1.90                                        PROVEN
  Three-tab UI (Tab 1: Managed Playlists, Tab 2: Missing Members, Tab 3: Config)
  PlaylistManagementStore, GroundTruthStore, MissingMembersStore, CandidateStore
  Protection toggle persists across navigation and restart
  Ground truth capture on protection toggle
  Ground truth maintenance (add/remove events via PlaylistMaintenanceService)
  Missing member detection — timer (60 min) + ItemRemoved fast path
  MissingMemberDetector static class (shared logic)
  DetectMissingMembersTask (manual IScheduledTask)
  Tab 2 renders missing members grouped by playlist with Dismiss action   PROVEN
  Dismiss updates MissingMembersStore and GroundTruthStore                PROVEN

  ── TASK 6 — PROVEN (2026-06-27) ───────────────────────────────────────

  CandidateEntry.cs, CandidateStore.cs
  CandidateDiscoverer.cs (static scoring — 5 signals)
  CandidateDiscoveryTask.cs (IScheduledTask)
  Scoring signals: FilenameStemExact:100, FilenameStemNormalized:80,
                   NameExact:60, NameNormalized:40, ParentFolderMatch:20
  Score >= 100 is the UI default filter (ParentFolderMatch alone = noise)
  CandidateStore written correctly                                        PROVEN

  ── TASKS 7-8 — PROVEN ─────────────────────────────────────────────────

  Tab 2 master-detail: missing members with candidate child grid
  Repair action (Tab 2): ticking Repair on a candidate row triggers
    AddToPlaylist or CreatePlaylist (if ghost), ground truth updated,
    stores cleaned up
  PlaylistRepairService extracted as shared repair logic
  Ghost playlist detection: CreatePlaylist used when GuidN not live in Emby
  PlaylistCreationResult.Id is InternalId string (not Guid)               PROVEN
  Subsequent GetItemList needed to resolve new Guid                        PROVEN
  PlaylistItemsAdded fires synchronously during await                     PROVEN
  Ground truth written directly in repair flow, not in event handler      PROVEN
  AddToPlaylist async overload with cast to Playlist                       PROVEN
  Server user retrieved via IUserManager.GetUserList(new UserQuery())[0]  PROVEN
  Repair removes only repaired member records (not all for playlist)      PROVEN
  Remaining records migrated to new GuidN on CreatePlaylist               PROVEN

  ── TASK 9 — PROVEN (2026-07-13/14) ───────────────────────────────────

  Probe: multiple buttons per DxDataGrid row                              PROVEN
    commandId is always the single grid-level value — no per-column routing
    itemId is always null from DxDataGrid cell edits
    Action disambiguation: inspect which field changed in round-trip data
    Clicked row identified by scanning for changed bool field

  Tab 1 enhancements:
    Members child grid (ground truth members expandable per row)          PROVEN
    Repair All button (picks strongest candidate per missing member)      PROVEN
    RepairAll=true triggers repair for that playlist                      PROVEN
    3 members repaired at score=160 in single pass confirmed              PROVEN

  Tab 2 improvement:
    Ghost playlist indicator "[Will be recreated]" on group header        PROVEN

  Probe task files deleted (PlaylistRecreationProbeTask,
    CandidateDiscoveryProbeTask)

  RepairDialogView — full-screen dialog launched from Tab 1              PROVEN
    Launched by ticking OpenRepair bool column on Tab 1 playlist row
    Caption: "Repair: {playlistName}"
    Missing members with RepairMember + DismissMember bool columns
    Child grid shows candidates with Repair bool column
    RepairMember: picks strongest candidate, calls PlaylistRepairService
    DismissMember: removes from MissingMembersStore, GroundTruthStore,
                   CandidateStore
    Repair (candidate): uses specific candidate, calls PlaylistRepairService
    Dialog refresh via RaiseUIViewInfoChanged() after each action
    Parent Tab 1 refresh via OnDialogResult + RaiseUIViewInfoChanged()
    Unknown commandIds delegate to base.RunCommand (returns null = close) PROVEN
    AllowCancel=true, AllowOk=false

  Tab 2 now delegates repair to PlaylistRepairService
  MissingMemberRow gains DismissMember and RepairMember bool fields
    (Forget field retained for Tab 2 backward compatibility)

  ── TASK 10 STEP 1 — PROVEN (2026-07-14) ──────────────────────────────

  GroundTruthDialogView — full-screen read-only dialog from Tab 1        PROVEN
    Launched by ticking OpenGroundTruth bool column on Tab 1 playlist row
    Caption: "Members: {playlistName}"
    Grid columns: Position (#), Track (Name), Path — all read-only
    Sorted by Position ascending
    Unprotected playlist (no ground truth) shows "No members captured"
    Close dismisses dialog, Tab 1 refreshes via OnDialogResult            PROVEN
    base.RunCommand returning null closes dialog — confirmed              PROVEN
    OnCancelCommand overridden with Task.CompletedTask (not base)        PROVEN
    OnOkCommand must NOT call base — throws NotImplementedException      PROVEN

NOT YET DONE
  Tab 1 PlaylistManagementStore schema enrichment (PlaylistName, M3uPath)
    — deferred; not blocking anything currently
  Tab 2 redesign (see CURRENT TASK below)

NOT IMPLEMENTED / FUTURE
  Tab 2 full redesign — 
  IScheduledTask post-library-scan trigger — DEFERRED
  Tab 3 Configuration UI (placeholder only)


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

  IServerEntryPoint implementations access stores via ListProtectionPlugin.Instance.
  Do not construct stores inside lambdas or per-view.

FILE LAYOUT (ACTUAL)
  Plugin.cs
  UIBaseClasses/                       <- DO NOT MODIFY
    Controllerbase.cs
    Views/
      PluginViewBase.cs
      PluginPageView.cs
      PluginDialogueView.cs            <- base for dialog views
    Store/
      SimpleFileStore.cs
      SimpleContentStore.cs
      FileSavingEventArgs.cs
      FileSavedEventArgs.cs
  EntryPoints/
    PlaylistEventProbe.cs              <- probe only, not production
    PlaylistMaintenanceService.cs
    MissingMemberDetectionService.cs
    MissingMemberDetector.cs
    CandidateDiscoverer.cs
  Tasks/
    DetectMissingMembersTask.cs
    CandidateDiscoveryTask.cs
    CandidateRefreshTask.cs            <- unknown, unreviewed
  Services/
    PlaylistRepairService.cs           <- shared repair logic
  UI/
    MainController.cs
    TabPageController.cs
    PlaylistManagement/
      PlaylistRow.cs                   <- Id, Name, Path, InternalId, IsProtected,
                                          MemberCount, CapturedAt, RepairAll,
                                          OpenRepair, OpenGroundTruth, Members[]
      MemberRow.cs                     <- Name, Path, InternalId (Tab 1 child grid)
      PlaylistManagementUI.cs          <- master grid + Members detail grid
      PlaylistManagementPageView.cs    <- RunCommand: ToggleProtection, RepairAll,
                                          OpenRepair -> RepairDialogView,
                                          OpenGroundTruth -> GroundTruthDialogView,
                                          OnDialogResult (both dialog types)
    MissingMembers/
      MissingMemberRow.cs              <- Key, PlaylistName, MemberName, Path,
                                          DetectedAt, Forget (legacy Tab 2),
                                          DismissMember, RepairMember,
                                          IsSynthetic, Candidates[]
      CandidateRow.cs (CanididateRow.cs) <- Key, CandidateName, CandidatePath,
                                          Score, Signals, Repair
      MissingMembersUI.cs
      MissingMembersPageView.cs        <- RunCommand: RepairMember (-> PlaylistRepairService),
                                          ForgetMember (dismiss, Tab 2 legacy)
    RepairDialog/
      RepairDialogUI.cs                <- EditableObjectBase, master+detail grid
      RepairDialogView.cs              <- PluginDialogView, full-screen repair dialog
    GroundTruthDialog/
      GroundTruthMemberRow.cs          <- Position, Name, Path
      GroundTruthDialogUI.cs           <- EditableObjectBase, read-only grid
      GroundTruthDialogView.cs         <- PluginDialogView, full-screen members dialog
    Config/
      ConfigUI.cs                      <- placeholder
      ConfigPageView.cs                <- placeholder
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


EMBY PLUGIN UI FRAMEWORK (PROVEN)
=====================================================

--- PLUGIN DIALOG PATTERN (PROVEN — Tasks 9-10, 2026-07-14) ---

  Reference: MetadataCheckerv1/UI/DialogueFullScreenGrid/ +
             MetadataCheckerv1/UI/Manage/

  Host page (PluginPageView):
    Bool column on grid row OR ButtonItem with Data1 = "commandId".
    RunCommand catches the trigger, constructs the dialog view, returns it.
    OnDialogResult(dialogView, completedOk, data) called when dialog closes.
      Check dialogView type (not completedOk — that is true even on Cancel).
      Call RaiseUIViewInfoChanged() to refresh parent after dialog closes.

  Dialog view (PluginDialogView):
    Extends PluginDialogView (UIBaseClasses/Views/PluginDialogueView.cs).
    DO NOT MODIFY the base class.
    Constructor sets: ShowDialogFullScreen=true, AllowOk=false, AllowCancel=true.
    Caption property override: string shown as dialog title.
    OnCancelCommand() override: return Task.CompletedTask. Do NOT call base.
    OnOkCommand() override: return Task.CompletedTask. Do NOT call base.
      base.OnOkCommand throws NotImplementedException — never call it.
    RunCommand: handle known commandIds. For ALL unknown commandIds (including
      "DialogCancel") delegate to base.RunCommand which returns null.
      Returning null from RunCommand = framework closes the dialog.
      Returning `this` keeps dialog open — wrong for close/cancel.
    RaiseUIViewInfoChanged() refreshes dialog content after mutations.
    ContentData must be EditableObjectBase (not EditableOptionsBase).

  PROVEN behaviours:
    Returning a PluginDialogView from host RunCommand opens the dialog.
    base.RunCommand() returns Task.FromResult<IPluginUIView>(null) — closes dialog.
    Parent OnDialogResult fires when dialog closes.
    Grid commandIds inside dialog fire dialog's own RunCommand.
    completedOk=true even on Cancel — check dialogView type to distinguish.
    Read-only dialog: no onChangeCommand needed, all RunCommand calls go to base.

--- MULTIPLE COMMANDIDS PER PAGE (PROVEN — Task 9, 2026-07-13) ---

  One DxDataGrid = one onChangeCommand = one commandId.
  Master grid and detail (child) grid can have DIFFERENT commandIds.
  Action type distinguished by which field changed in round-trip data.
  itemId is always null from DxDataGrid cell edits.

--- DxDataGrid MASTER-DETAIL (PROVEN — Tasks 7/8) ---

  options.masterDetail = new DxGridMasterDetail
  {
      enabled = true,
      autoExpandAll = false,      // or true for dialog views
      childRowsFieldName = "FieldName",  // matches property name on row class
      detailGridOptions = detailOptions  // separate DxGridOptions for child
  }
  Child grid column with isSecondaryGridDataSource = true links the data.
  Detail grid needs its own editing + onChangeCommand to be interactive.

--- RunCommand PAYLOAD (PROVEN) ---

  itemId    — always null from DxDataGrid cell edit. Ignore.
  commandId — arrives as set in DxGridOnChangeCommand.commandId.
  data      — entire ContentData serialised as JSON.
              Deserialise as full UI class and inspect rows.

--- IPlaylistManager (PROVEN — Task 8) ---

  AddToPlaylist async overload:
    await _playlistManager.AddToPlaylist(
        playlist as Playlist,
        candidateItemIds,
        skipDuplicates: true,
        user: user,
        cancellationToken: CancellationToken.None);

  CreatePlaylist:
    result = await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
    {
        Name = playlistName,
        ItemIdList = candidateItemIds,
        MediaType = "Audio",
        User = user,
        IsPublic = true
    });
    result.Id = InternalId string (NOT Guid). Resolve Guid via GetItemList.

  PlaylistItemsAdded fires SYNCHRONOUSLY during await — do not rely on it
  for ground truth update. Write directly after the call.

--- IUserManager (PROVEN — Task 8) ---

  var user = _userManager.GetUserList(new UserQuery())[0];
  Single server user, InternalId=1, IsAdministrator=true.

--- GHOST PLAYLIST DETECTION (PROVEN — Task 9) ---

  var allPlaylists = _libraryManager.GetItemList(new InternalItemsQuery
  {
      IncludeItemTypes = new[] { "Playlist" },
      Recursive = true
  });
  var liveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach (var p in allPlaylists) liveIds.Add(p.Id.ToString("N"));
  var isGhost = !liveIds.Contains(playlistId);

  Ghost playlists show "[Will be recreated]" in Tab 2 group header.
  Repair of ghost playlist uses CreatePlaylist (not AddToPlaylist).


STORAGE ARCHITECTURE
=====================================================

PATTERN B — Plain store (no base class) for playlist data, configuration data might be "baseplugin<t>" type.
  Constructor: (IApplicationHost applicationHost, ILogger logger, string pluginFullName)
  File path: applicationHost.Resolve<IApplicationPaths>().PluginConfigurationsPath
  Serialiser: applicationHost.Resolve<IJsonSerializer>()
  FileSystem: applicationHost.Resolve<IFileSystem>()
  Thread safety: private readonly object _lock = new object();

GROUND TRUTH ENTRY SHAPE

  GroundTruthEntry
    PlaylistName    string    — snapshot-time name, display only
    CapturedAt      DateTime  — UTC
    IsActive        bool      — false = soft-deleted
    Members         List<GroundTruthMember>

  GroundTruthMember
    InternalId      long      — fast in-process lookup
    Id              string    — Guid "N", durable
    Name            string    — at capture time
    Path            string    — at capture time
    ListItemEntryId long      — for correlating PlaylistItemsRemoved events

CANDIDATE SCORING SIGNALS
  FilenameStemExact       100
  FilenameStemNormalized   80
  NameExact                60
  NameNormalized           40
  ParentFolderMatch        20
  Score >= 100 = strong candidate (UI default filter threshold)


VERIFIED EMBY BEHAVIOURAL LEARNINGS
=====================================================

  PlaylistItemsAdded fires synchronously during await             PROVEN Task 8
  PlaylistCreationResult.Id is InternalId string, not Guid        PROVEN Task 8
  GetItemList(ListIds=[internalId]) to read playlist members      PROVEN
  ListItemEntryId populated at capture time outside event context PROVEN
  ItemRemoved does NOT fire ItemUpdated                           PROVEN
  DxDataGrid cell edit: commandId = grid-level value, itemId = null  PROVEN
  Multiple bool columns disambiguated by field values in round-trip  PROVEN
  base.RunCommand() returns null = framework closes dialog        PROVEN Task 10
  OnCancelCommand/OnOkCommand must NOT call base (base throws)    PROVEN Task 10
  OnDialogResult completedOk=true even on Cancel                  PROVEN Task 10
  Check dialogView type, not completedOk, to identify dialog      PROVEN Task 10
  RaiseUIViewInfoChanged() required to refresh dialog content     PROVEN Task 9
  Read-only dialog needs no onChangeCommand                       PROVEN Task 10


UI DESIGN (CURRENT STATE)
=====================================================

TAB 1 - MANAGED PLAYLISTS
  Grid: all playlists, one row each
  Columns: Name, Path, InternalId, Protected (editable), Member Count, Captured,
           Repair All (editable), Repair... (editable), Members... (editable)
  Child grid (expand row): ground truth members — Position, Name, Path, read-only
  Actions:
    IsProtected toggle -> ReconcileGroundTruth
    RepairAll=true -> picks strongest candidate per missing member, repairs all
    OpenRepair=true -> launches RepairDialogView full-screen
    OpenGroundTruth=true -> launches GroundTruthDialogView full-screen
  OnDialogResult -> refreshes Tab 1 (handles both dialog types)

REPAIR DIALOG (from Tab 1 OpenRepair)
  Full-screen, caption "Repair: {playlistName}"
  Master grid: missing members
    Columns: MemberName, Path, DetectedAt, Repair (RepairMember), Dismiss (DismissMember)
    autoExpandAll=true
  Detail grid: candidates per member
    Columns: Candidate, Path, Score, Signals, Repair
    Sorted by Score descending
  Actions:
    RepairMember=true -> strongest candidate -> PlaylistRepairService
    DismissMember=true -> removed from MissingMembersStore, GroundTruthStore, CandidateStore
    Repair=true (candidate) -> specific candidate -> PlaylistRepairService
  Close -> Tab 1 OnDialogResult refreshes

GROUND TRUTH DIALOG (from Tab 1 OpenGroundTruth)
  Full-screen, caption "Members: {playlistName}"
  Single read-only grid: Position (#), Track (Name), Path
  Sorted by Position ascending
  No actions — close only
  Close -> Tab 1 OnDialogResult refreshes

TAB 2 - MISSING MEMBERS (LEGACY — will be redesigned, see CURRENT TASK)
  Current state: working, repair delegates to PlaylistRepairService
  Ghost playlists show "[Will be recreated]" in group header
  Dismiss (was: Forget) removes from MissingMembersStore + GroundTruthStore

TAB 3 - CONFIGURATION
  Placeholder only


CURRENT TASK (AUTHORITATIVE)
=====================================================

TASK 11 TAB 1 and Dialog finessing

Tab 1 - Playlist Management purpose is to provide the following functionality
Admin able to protect and unprotect playlists
Admin able to see key metadata about  the playlists: Name, Groundtruth Member qty (GT), Missing Member Qty (MM)|MissingMemeberswithCandidates(above threshold) Qty (MC) (need to use shorthands to minimise screen realesate, outside a dialog dxgrids are small)
Admin able to open groundtruth and repair dialogs and event history (new) against a playlist via a buton. (Unprotected members shouldnt have these buttons)
Playlist should have a child row with additional troubleshooting info: PlaylistID, Path, Date of capture.
Unprotecting a protected playlist should be hard, need to launch a delete dialogue and say "type name of playlist" with delete button (only delete if the playlist name is correct). With name of playlist, data of protection, Groundtruth Member qty, Missing Member Qty|MissingMemeberswithCandidates(above threshold) Qty



# Event history
An event record will need wiring up, storage creating.
## Event types
Type:PlaylistProtect, Datetime:datetime of protection, Payload: "X members" (doesnt need a drill down), Playlist:Playlist name
Type:PlaylistUnprotect, Datetime:datetime of unprotection, Payload: [blank], Playlist:Playlist name
Type:MissingMemberDetected (per scan task), DateTime:scan datetime, Payload:"the track name, artists, album, groundtruthpath"s,  Playlist:Playlist name
Type:CandidateFound (per scan task), DateTime:scan datetime, Payload:"internalid, the track name, artists and album, detectedpath"s,  Playlist:Playlist name
Type:CandiateRefresh (a type of candidate found event where missing members who already have candidates, have their candidate list changed), Payload:"internalid the track name, artists and album, detectedpath"s,  Playlist:Playlist name
Type:RepairEvent, (per Task or per individual ui event repair1|repairall) Datetime:datetime of repair event, Payload:"internalid, the track name, artists and album,newpath"s, Playlist:PlaylistName 

We want event to carry playlist just in case we ever wanted to show all events for all playlists, but in terms of the event history dialog then playlist would not be surfaced.
Payloads should be expanded a child row (not 1 per track, all tracks in 1 cell to minimise screen real estate). The parent would be the individual track if singular or "Expand to see tracks" if multiple.

Hide Tab 2 for now no change.



IMPLEMENTATION ROADMAP (STATUS)
=====================================================

1.  Event observability                                  COMPLETE
2.  Playlist protection UI (Tab 1)                       COMPLETE
3.  Ground truth capture on protection                   COMPLETE
4.  Ground truth maintenance (add/remove events)         COMPLETE
5.  Missing member detection + fast path                 COMPLETE
6.  Candidate discovery                                  COMPLETE
7.  Candidate UI (Tab 2 master-detail)                   COMPLETE
8.  Repair workflow                                      COMPLETE
9.  Tab 1 enhancements + Repair dialog                   COMPLETE
10. Ground Truth dialog (Step 1)                         COMPLETE
11. Tab and Dialogue Finneshing                    NEXT
....
20. Design for Automating Missing member and candiate establishment appropriately in a timely but ultimately reliable manner against.
21. Design for automatic fix path for scores exceed threshold.
22. Implementation nascent configuration ui and surface. Autofix threshold. Enabled y/n (turn off futher automation/load behind the scenes)
23. Implement automatic fix pathway for scores exceeding threshold.




Test Cases
=====================================================




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

  PlaylistManagementStore schema enrichment (PlaylistName, M3uPath per entry)
    — useful for displaying ghost playlist info on Tab 1 without live library query
    — deferred; not blocking

  Ground truth restore prompt — when a playlist is re-ticked and a
    soft-deleted snapshot exists, prompt user to restore or start fresh.
    Requires investigation of Emby confirmation dialog support.

  Consideration on wether json storage is robust enough? neccessary to move to sqlite?

  Playlist recovery from Emby m3u files
  Advanced matching rules / confidence scoring
  Post-library-scan trigger for DetectMissingMembersTask
    (add TaskTriggerInfo for AfterLibraryScan once trigger type confirmed from DLL)

  MissingMemberDetectionService — timer interval (60 min) is a constant.
    Future: expose via ConfigStore for user tuning via Tab 3.

  PlaylistMaintenanceService — _pendingAdds never purged on server crash.
    Low risk in practice — stale entries cost nothing, next capture re-syncs.

  CandidateRefreshTask.cs — unknown content, unreviewed. Either integrate
    or delete once purpose is established.

These are explicitly deferred.

=====================================================
END OF DOCUMENT