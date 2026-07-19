EMBY PLUGIN UI FRAMEWORK (PROVEN + DECOMPILED)
=====================================================

--- PLUGIN DIALOG PATTERN (PROVEN — Tasks 9-10, 2026-07-14) ---

  Host page (PluginPageView):
    Bool column on grid row OR ButtonItem with CommandId = "commandId".
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
    RunCommand: handle known commandIds, return this to stay open.
      For ALL unhandled commandIds delegate to base.RunCommand (returns null).
      Returning null from RunCommand = framework closes the dialog.
      Returning `this` keeps dialog open.
    RaiseUIViewInfoChanged() refreshes dialog content after mutations.
    ContentData must be EditableObjectBase (not EditableOptionsBase).

  ButtonItem in dialogs (PROVEN — ArtistDashboardDialogView):
    ButtonItem with CommandId fires dialog's own RunCommand.
    Data payload is the full serialised ContentData.
    Button can trigger actions and return `this` to stay open.
    Button CANNOT directly close the dialog (custom commandId is rejected
    by framework before reaching base.RunCommand). Close only via OK/Cancel.

  PROVEN behaviours:
    Returning a PluginDialogView from host RunCommand opens the dialog.
    base.RunCommand() returns null — closes dialog.
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
      autoExpandAll = false,
      childRowsFieldName = "FieldName",  // matches property name on row class
      detailGridOptions = detailOptions  // separate DxGridOptions for child
  }
  Child grid column with isSecondaryGridDataSource = true links the data.
  Detail grid needs its own editing + onChangeCommand to be interactive.
  heightMode = auto on child grid to avoid excess height.

--- RunCommand PAYLOAD (PROVEN) ---

  itemId    — always null from DxDataGrid cell edit. Ignore.
  commandId — arrives as set in DxGridOnChangeCommand.commandId.
  data      — entire ContentData serialised as JSON.
              Deserialise as full UI class and inspect rows.

--- StandardIcons ENUM (CONFIRMED FROM SOURCE) ---

  public enum StandardIcons
  {
      Loading, Add, Edit, Refresh, Remove, Delete,
      ContextMenu, Download, AddToList
  }
  No "Check" value. Use Add for confirm/validate buttons.

--- IPlaylistManager (PROVEN — Task 8) ---

  AddToPlaylist async overload:
    await _playlistManager.AddToPlaylist(
        playlist as Playlist,
        candidateItemIds,
        skipDuplicates: true,
        user: user,
        cancellationToken: CancellationToken.None);

  CreatePlaylist:
    result = await _playlistManager.CreatePlaylist(new PlaylistCreationRequest { ... });
    result.Id = InternalId string (NOT Guid). Resolve Guid via GetItemList.

  PlaylistItemsAdded fires SYNCHRONOUSLY during await — do not rely on it
  for ground truth update. Write directly after the call.

--- IUserManager (PROVEN — Task 8) ---

  var user = _userManager.GetUserList(new UserQuery())[0];
  Single server user, InternalId=1, IsAdministrator=true.

--- GHOST PLAYLIST DETECTION (PROVEN — Task 9) ---

  var allPlaylists = _libraryManager.GetItemList(...);
  var liveIds = new HashSet<string>(...);
  foreach (var p in allPlaylists) liveIds.Add(p.Id.ToString("N"));
  var isGhost = !liveIds.Contains(playlistId);



### Dialog Routing & Command Handling — Complete Evidence Summary
The Framework Stack (bottom to top)
PageControllerHostBase.RunCommand        ← HTTP entry point
  └─ CurrentUIView.RunCommand(...)       ← calls the host wrapper
       └─ PluginDialogViewHost.RunCommand    ← our dialog's host
            └─ PluginDialogView.RunCommand   ← OUR CODE

PageControllerHostBase.RunCommand (decompiled)
csharpIUIView newSetupPage = await CurrentUIView.RunCommand(...);
if (newSetupPage == null)
    throw "Command is not implemented";
Rule: Returns null = hard throw. The framework always needs a non-null IUIView back. There is no commandId routing here — it just calls the host and validates the result.

PluginDialogViewHost.RunCommand (decompiled)
csharpIPluginUIView pluginUIView = await PluginDialogView.RunCommand(itemId, commandId, data);
if (pluginUIView != null)
    return CreateUIViewHost(pluginUIView);   // our return used directly
return await base.RunCommand(itemId, commandId, data);  // falls to DialogViewBase
Rule: If our RunCommand returns non-null, PluginDialogViewHost uses it directly via CreateUIViewHost — DialogViewBase and PageControllerHostBase never see it. If we return null, it falls to DialogViewBase.RunCommand.

DialogViewBase.RunCommand (decompiled)
csharpif (!IsCommandAllowed(commandId)) throw "Command is not allowed at this stage";
if (commandId == "DialogCancel") → OnCancelCommand → OnDialogResult(completedOk=false) → return OriginalUIView
if (commandId == "DialogOk")     → OnOkCommand     → OnDialogResult(completedOk=true)  → return OriginalUIView
csharpIsCommandAllowed(commandKey):
    if (commandKey == "DialogCancel") return cmdCancel.IsEnabled;
    if (commandKey == "DialogOk")     return cmdOk.IsEnabled;
    return false;  // everything else blocked
Rule: DialogViewBase only recognises "DialogCancel" and "DialogOk". Any other commandId with a null return from us throws "Command is not allowed at this stage". This is why custom commandIds crashed — we returned null, fell to DialogViewBase, which rejected them.

PluginDialogViewHost.CreateUIViewHost (decompiled)
csharpif (PluginDialogView.Equals(pluginView))                           return this;             // stay open
if (OriginalUIView?.PluginUIView?.Equals(pluginView) == true)      return OriginalUIView;   // go to parent
if (pluginView is IPluginPageView)   → new PluginPageViewHost(...)
if (pluginView is IPluginDialogView) → new PluginDialogViewHost(...)
Rule: Return this = stay in dialog. Return the parent IPluginUIView instance = navigate back to parent. Return a different view type = open that as a new page/dialog.

The Four Return Patterns from our RunCommand
Return valueWhat happensthisStay open — same dialog re-rendered_parentPageViewDialog closes, navigates to parent — one-button closeA different IPluginUIViewOpens that view as new page or dialognull → base.RunCommand("DialogCancel")DialogViewBase closes, OnDialogResult(completedOk=false) fires on parentnull → base.RunCommand("DialogOk")DialogViewBase closes, OnDialogResult(completedOk=true) fires on parent (requires AllowOk=true)null → base.RunCommand("anything else")Throws "Command is not allowed"

Critical Proven Rules
One-button close: Return the parent page view from RunCommand on any commandId. CreateUIViewHost matches it against OriginalUIView.PluginUIView and returns the parent directly. Execute any actions before returning. OnDialogResult does not fire on this path.
OnDialogResult only fires via DialogViewBase — i.e. only when you go through base.RunCommand("DialogCancel"/"DialogOk"). If you close by returning the parent view directly, OnDialogResult is bypassed entirely.
OnOkCommand must never call base — PluginDialogView.OnOkCommand base throws NotImplementedException by design.
OnCancelCommand is not Cancel — Cancel() is a general teardown hook that fires on every dialog close regardless of how it closes. OnCancelCommand() fires only on the cancel button path through DialogViewBase.
ContentData must be fresh on return — when returning the parent view (either confirm or cancel path), call _rebuildParentContent() first. The client re-renders from whatever ContentData is set at the moment the view is returned. Stale content = blank grid + client error even with Response 200.
completedOk is unreliable for distinguishing save vs cancel — it is true even on Cancel when going through DialogViewBase. Only reliable when you control the path yourself (i.e. you know you went through "DialogOk" vs "DialogCancel").
RaiseUIViewInfoChanged() — only needed when staying in the same dialog (return this). When returning the parent view, the framework re-renders the parent from scratch.


Playlist.GetItemList vs ILibraryManager.GetItemList with ListIds
Critical proven distinction. ILibraryManager.GetItemList(new InternalItemsQuery { ListIds = ... }) returns members in ListItemEntryId ascending order — DB insertion order, not playlist position. Playlist.GetItemList(new InternalItemsQuery()) goes through Playlist.SetQueryOptions which applies ListItemOrder — the internal DB column that reflects actual playlist position. Always use the entity method for any operation where order matters: capture, detection, repair readback.
Folder rename event behaviour — proven
Emby treats folder rename as hard delete + create. ItemRemoved fires for the folder entity (Type=Folder), not individual tracks. All tracks under the folder get new InternalIds. Zero playlist events fire. The detection hook must match by path prefix against GT member paths, not by InternalId. RefreshCompleted on the parent folder signals that replacement tracks are in the DB and candidate discovery can run.
File rename event behaviour — proven
Same delete + create pattern as folder rename but at Audio level. ItemRemoved fires for the Audio entity directly. Our existing InternalId matching in MissingMemberDetectionService catches this correctly.
Atomic remove-all then add-in-order for repair
Add-then-move (AddToPlaylist followed by MoveItem) is fragile — index arithmetic breaks across sequential repairs, and PlaylistMaintenanceService races the readback between the two calls. The correct approach is: read all current ListItemEntryIds, RemoveFromPlaylist all, then AddToPlaylist the complete desired order in one call with skipDuplicates=false. GT is the authority — if GT has duplicates, the playlist should too.
IPlaylistManager interface — confirmed signatures

AddToPlaylist(Playlist, long[], bool skipDuplicates, User, CancellationToken) — appends only, no position parameter
RemoveFromPlaylist(Playlist, long[] entryIds) — takes ListItemEntryId values not InternalId
MoveItem(Playlist, long entryId, int newIndex) — single item, zero-based index
No insert-at-position API exists

RefreshCompleted scope
Fires per item refreshed (each subfolder, then parent). For the folder rename case the parent folder's RefreshCompleted is the signal that all new tracks are committed. Pending candidate discovery is keyed by removed folder path and drained when a matching RefreshCompleted fires.