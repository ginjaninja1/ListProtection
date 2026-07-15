using ListProtection.Services;
using ListProtection.Storage;
using ListProtection.UI.MissingMembers;
using ListProtection.UIBaseClasses.Views;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ListProtection.UI.RepairDialog
{
    /// <summary>
    /// Full-screen dialog showing missing members and candidates for a single
    /// playlist. Launched from Tab 1 when OpenRepair is ticked on a playlist row.
    ///
    /// Extends PluginDialogView — mirrors FullScreenArtistDialogView pattern.
    ///
    /// commandId routing:
    ///   "RepairDialogMasterChanged"     — RepairMember or DismissMember ticked
    ///   "RepairDialogCandidateChanged"  — Repair ticked on a candidate row
    ///   anything else                   — delegate to base.RunCommand (closes dialog)
    ///
    /// IMPORTANT: base.RunCommand must be called for unhandled commands —
    /// that is what closes the dialog and routes back to the parent view.
    /// </summary>
    internal sealed class RepairDialogView : PluginDialogView
    {
        private readonly PluginInfo _pluginInfo;
        private readonly string _playlistId;
        private readonly string _playlistName;
        private readonly MissingMembersStore _missingMembersStore;
        private readonly GroundTruthStore _groundTruthStore;
        private readonly PlaylistManagementStore _playlistStore;
        private readonly PlaylistRepairService _repairService;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly ILogger _logger;

        // In-memory row state — mutated as repairs/dismissals happen,
        // rebuilt from stores on each command so the view stays in sync.
        private MissingMemberRow[] _rows;

        public RepairDialogView(
            PluginInfo pluginInfo,
            string playlistId,
            string playlistName,
            MissingMembersStore missingMembersStore,
            GroundTruthStore groundTruthStore,
            PlaylistManagementStore playlistStore,
            PlaylistRepairService repairService,
            IJsonSerializer jsonSerializer,
            ILogger logger)
            : base(pluginInfo.Id)
        {
            _pluginInfo = pluginInfo;
            _playlistId = playlistId;
            _playlistName = playlistName;
            _missingMembersStore = missingMembersStore;
            _groundTruthStore = groundTruthStore;
            _playlistStore = playlistStore;
            _repairService = repairService;
            _jsonSerializer = jsonSerializer;
            _logger = logger;

            ShowDialogFullScreen = true;
            AllowOk = false;
            AllowCancel = true;

            _rows = BuildRows();
            ContentData = RepairDialogUI.Build(_rows);
        }

        public override string Caption => "Repair: " + _playlistName;
        public override bool ShowDialogFullScreen { get; }

        public override Task Cancel() => Task.CompletedTask;

        public override Task OnOkCommand(string providerId, string commandId, string data)
            => Task.CompletedTask;

        // ── RunCommand ─────────────────────────────────────────────────────

        public override async Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
        {
            _logger.Info(
                "[RepairDialogView] RunCommand | commandId={0} | itemId={1}",
                commandId ?? "(null)", itemId ?? "(null)");

            try
            {
                switch (commandId)
                {
                    case "RepairDialogMasterChanged":
                        await HandleMasterChanged(data);
                        Refresh();
                        return this;

                    case "RepairDialogCandidateChanged":
                        await HandleCandidateChanged(data);
                        Refresh();
                        return this;

                    case "RepairAll":
                        await HandleRepairAll();
                        Refresh();
                        return this;
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[RepairDialogView] RunCommand failed", ex);
            }

            // Unknown command (including "DialogCancel") — delegate to base
            // so the framework can close the dialog and return to parent.
            return await base.RunCommand(itemId, commandId, data);
        }

        // ── Master changed: RepairMember or DismissMember ──────────────────

        private async Task HandleMasterChanged(string data)
        {
            if (string.IsNullOrEmpty(data)) return;

            RepairDialogUI incoming;
            try
            {
                incoming = _jsonSerializer.DeserializeFromString<RepairDialogUI>(data);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[RepairDialogView] HandleMasterChanged — failed to deserialize", ex);
                return;
            }

            if (incoming?.MissingMemberRows == null) return;

            // RepairMember: find rows where RepairMember=true, build repair rows
            // using the strongest candidate for each, delegate to PlaylistRepairService
            var toRepair = incoming.MissingMemberRows
                .Where(r => r.RepairMember && !r.IsSynthetic && !string.IsNullOrEmpty(r.Key))
                .ToArray();

            if (toRepair.Length > 0)
            {
                _logger.Info("[RepairDialogView] RepairMember triggered for {0} row(s)", toRepair.Length);
                var repairRows = BuildRepairRowsForMembers(toRepair.Select(r => r.Key).ToArray());
                if (repairRows.Length > 0)
                    await _repairService.ExecuteRepairs(repairRows);
            }

            // DismissMember: find rows where DismissMember=true, dismiss each
            var toDismiss = incoming.MissingMemberRows
                .Where(r => r.DismissMember && !r.IsSynthetic && !string.IsNullOrEmpty(r.Key))
                .ToArray();

            if (toDismiss.Length > 0)
            {
                _logger.Info("[RepairDialogView] DismissMember triggered for {0} row(s)", toDismiss.Length);
                DismissMembers(toDismiss.Select(r => r.Key).ToArray());
            }
        }

        // ── Candidate changed: Repair this specific candidate ───────────────

        private async Task HandleCandidateChanged(string data)
        {
            if (string.IsNullOrEmpty(data)) return;

            RepairDialogUI incoming;
            try
            {
                incoming = _jsonSerializer.DeserializeFromString<RepairDialogUI>(data);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[RepairDialogView] HandleCandidateChanged — failed to deserialize", ex);
                return;
            }

            if (incoming?.MissingMemberRows == null) return;

            // Build MissingMemberRow[] with only the ticked candidate marked Repair=true
            var repairRows = new List<MissingMemberRow>();

            foreach (var masterRow in incoming.MissingMemberRows)
            {
                if (masterRow.IsSynthetic || masterRow.Candidates == null) continue;

                var tickedCandidates = masterRow.Candidates.Where(c => c.Repair).ToArray();
                if (tickedCandidates.Length == 0) continue;

                _logger.Info(
                    "[RepairDialogView] Candidate repair triggered | member='{0}' | candidates={1}",
                    masterRow.MemberName ?? "(null)", tickedCandidates.Length);

                repairRows.Add(new MissingMemberRow
                {
                    Key = masterRow.Key,
                    PlaylistName = masterRow.PlaylistName,
                    MemberName = masterRow.MemberName,
                    Path = masterRow.Path,
                    DetectedAt = masterRow.DetectedAt,
                    IsSynthetic = false,
                    Candidates = tickedCandidates
                });
            }

            if (repairRows.Count > 0)
                await _repairService.ExecuteRepairs(repairRows.ToArray());
        }

        // ── Dismiss helpers ────────────────────────────────────────────────

        /// <summary>
        /// Removes the specified members from MissingMembersStore and GroundTruthStore.
        /// Keys are "{PlaylistId}_{InternalId}" format.
        /// </summary>
        private void DismissMembers(string[] keys)
        {
            var missingRecords = _missingMembersStore.Load();
            var groundTruth = _groundTruthStore.Load();
            var candidateRecords = ListProtectionPlugin.Instance.CandidateStore.Load();
            var missingChanged = false;
            var groundTruthChanged = false;
            var candidatesChanged = false;

            foreach (var key in keys)
            {
                if (key.Length < 34 || key[32] != '_')
                {
                    _logger.Warn("[RepairDialogView] DismissMembers — unexpected key format: {0}", key);
                    continue;
                }

                var playlistId = key.Substring(0, 32);
                if (!long.TryParse(key.Substring(33), out var internalId))
                {
                    _logger.Warn("[RepairDialogView] DismissMembers — could not parse InternalId from key: {0}", key);
                    continue;
                }

                _logger.Info(
                    "[RepairDialogView] Dismissing member | playlist={0} | InternalId={1}",
                    playlistId, internalId);

                // Remove from MissingMembersStore
                for (var i = missingRecords.Count - 1; i >= 0; i--)
                {
                    var r = missingRecords[i];
                    if (r.PlaylistId == playlistId && r.Member?.InternalId == internalId)
                    {
                        missingRecords.RemoveAt(i);
                        missingChanged = true;
                        _logger.Info("[RepairDialogView] Removed missing record '{0}'", r.Member?.Name ?? "(null)");
                        break;
                    }
                }

                // Remove from GroundTruthStore
                if (groundTruth.TryGetValue(playlistId, out var entry) && entry.Members != null)
                {
                    for (var i = entry.Members.Count - 1; i >= 0; i--)
                    {
                        if (entry.Members[i].InternalId == internalId)
                        {
                            _logger.Info("[RepairDialogView] Removed from ground truth '{0}'", entry.Members[i].Name ?? "(null)");
                            entry.Members.RemoveAt(i);
                            groundTruthChanged = true;
                            break;
                        }
                    }
                }

                // Remove candidates for this member
                for (var i = candidateRecords.Count - 1; i >= 0; i--)
                {
                    var c = candidateRecords[i];
                    if (c.PlaylistId == playlistId && c.MissingMember?.InternalId == internalId)
                    {
                        candidateRecords.RemoveAt(i);
                        candidatesChanged = true;
                    }
                }
            }

            if (missingChanged) _missingMembersStore.Save(missingRecords);
            if (groundTruthChanged) _groundTruthStore.Save(groundTruth);
            if (candidatesChanged) ListProtectionPlugin.Instance.CandidateStore.Save(candidateRecords);
        }

        // ── Row builders ───────────────────────────────────────────────────

        // ── Repair All ────────────────────────────────────────────────────

        /// <summary>
        /// Applies the highest-scoring candidate to every missing member that has one.
        /// Uses the current in-memory _rows as the source — no data deserialisation needed
        /// since the button fires with the full UI payload but we only need server state.
        /// </summary>
        private async Task HandleRepairAll()
        {
            _logger.Info("[RepairDialogView] RepairAll triggered");

            var allKeys = _rows
                .Where(r => !r.IsSynthetic && !string.IsNullOrEmpty(r.Key))
                .Select(r => r.Key)
                .ToArray();

            if (allKeys.Length == 0)
            {
                _logger.Info("[RepairDialogView] RepairAll — no repairable rows");
                return;
            }

            var repairRows = BuildRepairRowsForMembers(allKeys);

            if (repairRows.Length == 0)
            {
                _logger.Info("[RepairDialogView] RepairAll — no candidates available for any row");
                return;
            }

            _logger.Info("[RepairDialogView] RepairAll — executing {0} repair(s)", repairRows.Length);
            await _repairService.ExecuteRepairs(repairRows);
        }

        private void Refresh()
        {
            _rows = BuildRows();
            ContentData = RepairDialogUI.Build(_rows);
            RaiseUIViewInfoChanged();
        }

        private MissingMemberRow[] BuildRows()
        {
            var missingRecords = _missingMembersStore.Load();
            var candidateRecords = ListProtectionPlugin.Instance.CandidateStore.Load();

            var rows = new List<MissingMemberRow>();

            var playlistMissing = missingRecords
                .Where(r => r.PlaylistId == _playlistId && r.Member != null)
                .ToList();

            if (playlistMissing.Count == 0)
            {
                rows.Add(new MissingMemberRow
                {
                    Key = "synthetic_" + _playlistId,
                    PlaylistName = _playlistName,
                    MemberName = "No missing members",
                    IsSynthetic = true,
                    Candidates = new CandidateRow[0]
                });

                _logger.Info("[RepairDialogView] BuildRows — no missing members for playlist '{0}'", _playlistName);
                return rows.ToArray();
            }

            foreach (var record in playlistMissing)
            {
                var candidates = candidateRecords
                    .Where(c => c.PlaylistId == _playlistId && c.MissingMember?.InternalId == record.Member.InternalId)
                    .OrderByDescending(c => c.Score)
                    .Select(c => new CandidateRow
                    {
                        Key = _playlistId + "_" + record.Member.InternalId + "_" + c.CandidateInternalId,
                        CandidateName = c.CandidateName ?? "(unnamed)",
                        CandidatePath = c.CandidatePath ?? string.Empty,
                        Score = c.Score,
                        Signals = string.Join(", ", c.MatchedSignals ?? new List<string>()),
                        Repair = false
                    })
                    .ToArray();

                rows.Add(new MissingMemberRow
                {
                    Key = _playlistId + "_" + record.Member.InternalId,
                    PlaylistName = _playlistName,
                    MemberName = record.Member.Name ?? "(unnamed)",
                    Path = record.Member.Path ?? string.Empty,
                    DetectedAt = record.DetectedAt.ToString("yyyy-MM-dd HH:mm") + " UTC",
                    Forget = false,
                    DismissMember = false,
                    RepairMember = false,
                    IsSynthetic = false,
                    Candidates = candidates
                });
            }

            _logger.Info(
                "[RepairDialogView] BuildRows — {0} missing member(s) for playlist '{1}'",
                rows.Count, _playlistName);

            return rows.ToArray();
        }

        /// <summary>
        /// For RepairMember: builds MissingMemberRow[] with the strongest candidate
        /// marked Repair=true, ready for PlaylistRepairService.ExecuteRepairs.
        /// Keys are "{PlaylistId}_{InternalId}".
        /// </summary>
        private MissingMemberRow[] BuildRepairRowsForMembers(string[] keys)
        {
            var missingRecords = _missingMembersStore.Load();
            var candidateRecords = ListProtectionPlugin.Instance.CandidateStore.Load();
            var rows = new List<MissingMemberRow>();

            foreach (var key in keys)
            {
                if (key.Length < 34 || key[32] != '_') continue;
                var playlistId = key.Substring(0, 32);
                if (!long.TryParse(key.Substring(33), out var internalId)) continue;

                var record = missingRecords.FirstOrDefault(r =>
                    r.PlaylistId == playlistId && r.Member?.InternalId == internalId);
                if (record == null) continue;

                var best = candidateRecords
                    .Where(c => c.PlaylistId == playlistId && c.MissingMember?.InternalId == internalId)
                    .OrderByDescending(c => c.Score)
                    .FirstOrDefault();

                if (best == null)
                {
                    _logger.Info(
                        "[RepairDialogView] RepairMember — no candidates for '{0}', skipping",
                        record.Member.Name ?? "(null)");
                    continue;
                }

                _logger.Info(
                    "[RepairDialogView] RepairMember — selecting '{0}' (score={1}) for '{2}'",
                    best.CandidateName ?? "(null)", best.Score, record.Member.Name ?? "(null)");

                rows.Add(new MissingMemberRow
                {
                    Key = key,
                    PlaylistName = _playlistName,
                    MemberName = record.Member.Name ?? "(unnamed)",
                    Path = record.Member.Path ?? string.Empty,
                    DetectedAt = record.DetectedAt.ToString("yyyy-MM-dd HH:mm") + " UTC",
                    IsSynthetic = false,
                    Candidates = new[]
                    {
                        new CandidateRow
                        {
                            Key = playlistId + "_" + internalId + "_" + best.CandidateInternalId,
                            CandidateName = best.CandidateName ?? "(unnamed)",
                            CandidatePath = best.CandidatePath ?? string.Empty,
                            Score = best.Score,
                            Signals = string.Join(", ", best.MatchedSignals ?? new List<string>()),
                            Repair = true
                        }
                    }
                });
            }

            return rows.ToArray();
        }
    }
}