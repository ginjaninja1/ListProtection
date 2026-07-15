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

  ── TASK 11 — IN PROGRESS (2026-07-15) ────────────────────────────────

Add dismiss all button to Repair Dialog (mirror button behaviour on Unprotect dialog for hoe to ensure button lands on host page/cancel continues to work)
Fix Event histry for repair (all emmebers listed as "(unknown) → (unknown)")
make status field on unprotect read only
make child row on tab 1 start with only as much height as needed.

NOT IMPLEMENTED / FUTURE
  IScheduledTask post-library-scan trigger — DEFERRED
  Tab 3 Configuration UI (placeholder only)
  Tab 2 redesign — hidden for now, commented out in MainController


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
  EventStore               — file: List Protection.Events.json

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
    MainController.cs                  <- Tab 2 commented out
    TabPageController.cs
    PlaylistManagement/
      PlaylistRow.cs                   <- Id, Name, Status, IsProtected,
                                          RepairAll (hidden), OpenRepair,
                                          OpenGroundTruth, OpenHistory,
                                          Detail[] (PlaylistDetailRow)
      PlaylistDetailRow.cs             <- PlaylistId, Path, CapturedAt
      MemberRow.cs                     <- dead code, safe to delete
      PlaylistManagementUI.cs          <- master grid + Detail child grid
      PlaylistManagementPageView.cs    <- RunCommand: ToggleProtection,
                                          OpenRepair, OpenGroundTruth,
                                          OpenHistory, UnprotectConfirm,
                                          OnDialogResult (all dialog types),
                                          BuildRows with GT/MM/MC status
    MissingMembers/
      MissingMemberRow.cs              <- Key, PlaylistName, MemberName, Path,
                                          DetectedAt, Forget (legacy Tab 2),
                                          DismissMember, RepairMember,
                                          IsSynthetic, Candidates[]
      CandidateRow.cs                  <- Key, CandidateName, CandidatePath,
                                          Score, Signals, Repair
      MissingMembersUI.cs
      MissingMembersPageView.cs        <- Tab 2 (hidden, not deleted)
    RepairDialog/
      RepairDialogUI.cs                <- RepairAll ButtonItem above grid,
                                          autoExpandAll=false,
                                          child heightMode=auto
      RepairDialogView.cs              <- HandleRepairAll added
    GroundTruthDialog/
      GroundTruthMemberRow.cs          <- Position, Name, Path
      GrouthTruthDialogUI.cs           <- search+filter enabled
      GroundTruthDialogView.cs
    EventHistoryDialog/
      EvenHistoryRow.cs                <- Key, EventType, OccurredAt,
                                          PayloadSummary, PayloadDetail[]
      PayloadRow (in same file)        <- Idx, Line
      EvenHistoryDialogUI.cs           <- master-detail, search+filter
      EventHistoryDialogView.cs        <- builds summary/detail from payload
    UnprotectionConfirmDialog/
      UnprotectConfirmDialogUI.cs      <- ConfirmName string, Check button,
                                          ValidationStatus label
      UnprotectConfirmDialogView.cs    <- validates name, closes via DialogOk
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
    EventEntry.cs                      <- EventType, PlaylistId, PlaylistName,
                                          OccurredAt, Payload (newline-delimited)
    EventStore.cs                      <- append-only, cap 2000, newest-first





STORAGE ARCHITECTURE
=====================================================

PATTERN B — Plain store (no base class).
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

EVENT ENTRY SHAPE

  EventEntry
    EventType       string    — Protect | Unprotect | MissingDetected |
                               CandidateFound | CandidateRefresh | Repair
    PlaylistId      string    — Guid "N"
    PlaylistName    string    — at event time
    OccurredAt      DateTime  — UTC
    Payload         string    — newline-delimited detail lines
                               empty string for events with no item detail

  EventStore: append-only, cap 2000 entries, newest-first.
  LoadForPlaylist(playlistId) returns filtered list for a single playlist.

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
  base.RunCommand() returns null for ANY commandId unconditionally PROVEN (decompile)
  OnCancelCommand/OnOkCommand must NOT call base (base throws)    PROVEN Task 10
  OnDialogResult completedOk=true even on Cancel                  PROVEN Task 10
  Check dialogView type, not completedOk, to identify dialog      PROVEN Task 10
  RaiseUIViewInfoChanged() required to refresh dialog content     PROVEN Task 9
  Read-only dialog needs no onChangeCommand                       PROVEN Task 10
  Custom commandId ("ConfirmUnprotect") crashes framework before  PROVEN Task 11
    reaching base.RunCommand — PageControllerHostBase rejects it
  ButtonItem CommandId fires dialog RunCommand with full payload   PROVEN Task 11
    (confirmed via ArtistDashboardDialogView BatchApprove pattern)
  StandardIcons.Check does not exist — use StandardIcons.Add      PROVEN (source)


UI DESIGN (CURRENT STATE)
=====================================================

TAB 1 - MANAGED PLAYLISTS
  Grid columns: Playlist (flex), Status "GT/MM/MC" (75px),
                Prot (75px), Repr (75px), Memb (75px), Hist (75px)
  Hidden: Id, InternalId, RepairAll, Detail
  Child row (expand): PlaylistDetailRow — PlaylistId, Path, CapturedAt
  Actions:
    IsProtected untick -> launches UnprotectConfirmDialogView
    IsProtected tick   -> ReconcileGroundTruth, writes Protect event
    Repr (OpenRepair)  -> launches RepairDialogView (protected only)
    Memb (OpenGroundTruth) -> launches GroundTruthDialogView (protected only)
    Hist (OpenHistory) -> launches EventHistoryDialogView (protected only)
  OnDialogResult -> refreshes Tab 1 (handles all dialog types)
  Action columns always visible; server-side guard ignores clicks
    on unprotected rows (per-row col visibility not supported)

REPAIR DIALOG (from Tab 1 Repr)
  Full-screen, caption "Repair: {playlistName}"
  RepairAll ButtonItem above grid (CommandId="RepairAll")
  Master grid: missing members
    Columns: MemberName, Path, DetectedAt, Repr (RepairMember), Dismiss (DismissMember)
    autoExpandAll=false
  Detail grid: candidates per member
    Columns: Candidate, Path, Score, Signals, Repair
    heightMode=auto, sorted by Score descending
  Actions:
    RepairAll button  -> repairs all missing members with best candidate
    RepairMember=true -> strongest candidate -> PlaylistRepairService
    DismissMember=true -> removed from MissingMembersStore, GroundTruthStore, CandidateStore
    Repair=true (candidate) -> specific candidate -> PlaylistRepairService
  Close -> Tab 1 OnDialogResult refreshes

GROUND TRUTH DIALOG (from Tab 1 Memb)
  Full-screen, caption "Members: {playlistName}"
  Single read-only grid: Position (#), Track (Name), Path
  Search + filter enabled
  Sorted by Position ascending — close only

EVENT HISTORY DIALOG (from Tab 1 Hist)
  Full-screen, caption "History: {playlistName}"
  Master grid: Type | When | Detail (summary)
    Search + filter enabled
    Detail cell: single track inline, or "Expand to see N tracks"
  Child grid: one Line per payload entry (expand arrow on multi-track events)
  Read-only — close only

UNPROTECT CONFIRM DIALOG (from Prot untick)
  Small dialog, caption "Unprotect: {playlistName}"
  ConfirmName string field + Check button (CommandId="ConfirmUnprotect")
  ValidationStatus label for feedback
  Name match: Confirmed=true → base.RunCommand(itemId,"DialogOk",data) [ASSUMED close]
  Name mismatch: field cleared, error status shown, stays open
  OnDialogResult: completedOk && Confirmed → executes unprotect + writes event
  Fallback if DialogOk fails: AllowOk enabled after match, user presses OK

TAB 2 - MISSING MEMBERS (HIDDEN)
  Commented out in MainController — not deleted, can be restored.

TAB 3 - CONFIGURATION
  Placeholder only


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
11. Tab and Dialog Finessing                             COMPLETE
12
...
20. Design for automating missing member and candidate establishment.
21. Design for automatic fix path for scores exceeding threshold.
22. Implement nascent configuration UI. Autofix threshold. Enabled y/n.
23. Implement automatic fix pathway for scores exceeding threshold.


OPEN QUESTIONS / DEFERRED INVESTIGATIONS
=====================================================

  PageControllerHostBase commandId routing — NOT decompiled.
    The DLL is at Tim's Emby install:
      C:\Users\Nicholas Bird\AppData\Roaming\Emby-Server\system\Emby.Web.GenericUI.dll
    Decompiling this would definitively answer:
      - Which commandIds are recognised as valid for dialog routing
      - Whether "DialogOk" as a synthetic close works
      - Whether there is any path to close a dialog from a custom button commandId
    Without this, the "ConfirmUnprotect crash" behaviour is proven but the
    underlying mechanism is assumed.


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

  Consideration on whether JSON storage is robust enough — SQLite?

  Playlist recovery from Emby m3u files
  Advanced matching rules / confidence scoring
  Post-library-scan trigger for DetectMissingMembersTask

  MissingMemberDetectionService — timer interval (60 min) is a constant.
    Future: expose via ConfigStore for user tuning via Tab 3.

  PlaylistMaintenanceService — _pendingAdds never purged on server crash.
    Low risk in practice.

  CandidateRefreshTask.cs — unknown content, unreviewed.

  MemberRow.cs — dead code (replaced by PlaylistDetailRow). Safe to delete.

These are explicitly deferred.

=====================================================
END OF DOCUMENT