# ListProtection — Handover

## Session workflow
1. Read this file at session start
2. Confirm sync with Tim before touching any code
3. Ask Tim to commit latest changes before re-reading the repository
4. Update this file at session end

---


---

## Current state (2026-07-25)

### What was completed this session

**Full scoring engine redesign — committed and tested.**

The original scoring architecture was replaced with a clean three-tier model. All files listed below are in their final committed state on `NewScoring`.

#### Architecture — three tiers

**Tier 1 — Media-type content (Identity)**
- `AudioEvidenceCollector`, `EpisodeEvidenceCollector`, `MovieEvidenceCollector`
- Emit atomic boolean facts only — no combination logic, no else-if chains
- `CandidateScorer` applies `ScoringWeights` prioritised rule table → `ContentScore`
- First matching rule wins (most specific first)

**Tier 2 — Folder depth (Location)**
- `FolderEvidenceCollector` — always runs, media-type agnostic
- Emits `Folder.Depth1` through `Folder.Depth10` facts (cumulative conditional chain)
- Contributes `LocationScore` only when `ContentScore > 0` OR `FallbackScore > 0`
- Folder depth alone cannot surface a candidate — requires content anchor

**Tier 3 — Fallback**
- `FallbackEvidenceCollector` — runs only when `ContentScore == 0`
- Name pair (exact/normalized) and filename stem pair — mutually exclusive within each pair
- Name and filename can stack (different fields)

#### Key files changed/added
- `EvidenceFact.cs` — atomic signal, name only
- `IEvidenceCollector.cs` — updated interface comment
- `AudioEvidenceCollector.cs` — atomic facts only, + `AudioFacts` constants class
- `EpisodeEvidenceCollector.cs` — atomic facts only, + `EpisodeFacts` constants class
- `MovieEvidenceCollector.cs` — atomic facts only, + `MovieFacts` constants class
- `FolderEvidenceCollector.cs` — new, depth loop, `FolderFacts` constants class
- `FallbackEvidenceCollector.cs` — new, name/filename fallback, `FallbackFacts` constants class
- `ScoringWeights.cs` — rule tables for Audio/Episode/Movie/Fallback + `ScoringRule` class
- `ScoringResult.cs` — new, three independent scores + matched signals
- `CandidateScorer.cs` — stateless, three-pass scoring, enforces tier suppression
- `CandidateEntry.cs` — gains `ContentScore`, `FallbackScore`, `LocationScore` alongside `Score`
- `CandidateDiscoverer.cs` — updated to collect per-tier, call new scorer signature, log C/L/F breakdown
- `BaseItemEvidenceCollector.cs` — deleted (replaced by Folder + Fallback collectors)
- `ScoringReferenceDialog.cs` — rebuilt from rule tables, no longer references old ScoringWeights shape

#### Test result (2026-07-25)
FLAC album removed, MP3 version added in slightly different parent folder.
- Correct candidates found at `C=170, L=0, F=0` — full Name+Artist+Album+Track+Duration rule fired
- `L=0` correct — folder changed, no depth match
- `F=0` correct — Tier 3 suppressed when Tier 1 fires
- Sibling-album noise candidates at `C=25` — `ArtistMatch+AlbumMatch` only. Expected, acceptable.
- No `C=0, L>0` anywhere — anchor requirement holding

---

## Decisions made this session (do not relitigate without strong reason)

1. **Collectors are dumb fact emitters** — no combination logic, no suppression, no else-if. All weighting and combination logic lives in `ScoringWeights`. This is a hard architectural rule, not a style preference.

2. **Three-score result, single composite gate** — `ScoringResult` exposes `ContentScore`, `LocationScore`, and `FallbackScore` separately for transparency and logging. The auto-repair gate uses only `CompositeScore` (their sum) against a single `AutoRepairScoreThreshold`. This is correct — the scorer is the single source of truth and the repair layer makes a simple threshold decision. Re-combining individual scores in `AutoRepairer` would be scoring logic leaking into the wrong layer.

3. **No minimum store floor** — candidates with score > 0 enter the store. The `AutoRepairScoreThreshold` and `AutoRepairMinCandidateDistance` config values are the correct gate against low-confidence candidates causing harm. A floor suppresses real observations and is a sticking plaster.

4. **Folder depth is superlinear, piecewise** — weights defined in `FolderFacts.Weights[]` lookup array. Depth 1 = 5 (almost meaningless), Depth 5 = 110 (near certainty as corroboration), Depth 6-10 = +8/level disambiguation. These are calibrated estimates — subject to revision as real-world data accumulates.

5. **No absolute path matching** — library root can move. "Same location" is captured entirely by depth of consecutive ancestor name matches. `FolderPathExact` was considered and rejected.

---

## On the horizon — priority order

### 1. Negative signals (highest priority unimplemented improvement)
Currently the engine only accumulates positive evidence. A candidate that matches name and artist but definitively has a different album should be penalised, not just fail to gain album points.

Requires:
- `PenaltyScore` field on `ScoringResult`
- Penalty rule pass in `CandidateScorer` after content pass
- Specific rule: `AlbumMismatch` — name matches, album is present in both GT and candidate, but differs

### 2. `DiscNumberMatch` atomic signal (Audio)
Multi-disc albums are the primary disambiguation failure case. `artist\album\disc1\files` vs `artist\album\disc2\files` — two tracks at the same track number on different discs are not the same track.

Requires:
- `DiscNumber` field on `GroundTruthMember` (from `Audio.ParentIndexNumber` at capture time)
- `Audio.DiscNumberMatch` fact emitted by `AudioEvidenceCollector`
- New rules in `ScoringWeights.AudioRules` promoting combinations that include disc number

### 3. Auto-repair config gate for poorly-tagged libraries
**No implementation required — existing single threshold is correct.**

`AutoRepairer.cs` gates on `CompositeScore >= AutoRepairScoreThreshold`. This is correct under the architecture: `CandidateScorer` produces a `CompositeScore` that already correctly reflects all tiers of evidence. For well-tagged libraries, `ContentScore` dominates. For poorly-tagged libraries, `FallbackScore + LocationScore` combine to produce a meaningful composite (e.g. filename exact + depth 5 folder match = 40 + 110 = 150). The single threshold handles both cases without the repair layer needing to reason about individual score components.

An OR gate splitting `ContentScore` and `LocationScore` in `AutoRepairer` was considered and rejected — it would be scoring logic leaking into the repair layer, violating the principle that the scorer produces a single trustworthy output and the repair gate makes a simple threshold decision against it.

The only legitimate config addition for poorly-tagged library users is a lower default `AutoRepairScoreThreshold` — or user guidance to lower it — not a structural gate change.

### 4. Eligibility gates — technical debt, remove when rule table is tightened
`AudioAutoRepairEligibility`, `EpisodeAutoRepairEligibility`, `MovieAutoRepairEligibility` each re-derive metadata conditions (name, artist, album, series, year) from the live item at repair time as a semantic safety floor. This is architecturally inconsistent — those conditions were already established by the collector and evaluated by the rule table. The gates exist because the rule table weights are not yet differentiated enough to make some combinations impossible to clear the auto-repair threshold without the right signals. For example, `AlbumMatch + TrackNumber + Duration = 110` without `NameMatch` could theoretically clear the threshold — the gate blocks this case.

The correct long-term fix is tightening rule weights so no wrong combination can clear the auto-repair threshold, then removing the gates. The gates are a temporary safety net compensating for imprecise weights, not correct architecture. Do not add new gates for new media types — instead ensure the rule table scores make them unnecessary.

### 5. `ScoringReferenceDialog` grouping display
Current state: two-level grouping (MediaType outer, SignalType inner) renders correctly but Fallback rules repeated per media type looks cluttered. Scoring reference is informational only — acceptable state for now but could be improved.

### 6. Collections support
`BaseItem` subtype. Not yet supported — no collector, no GT capture. Deferred until Audio/Episode/Movie are solid.

### 7. Unit test for rule ordering
`ScoringWeights` rule tables must be ordered most-specific-first. No test currently enforces this. A future developer adding rules in the wrong position will introduce silent wrong scores.

---

## Scoring engine — hard-won principles (do not assume, verify)

### The collector/scorer boundary is a hard architectural rule
Collectors emit atomic boolean facts only. They never suppress other facts, never decide which combination is strongest. All combination logic lives exclusively in `ScoringWeights` rule tables. Violating this boundary reintroduces double-counting and suppression failures that took significant design effort to eliminate.

### Tier enforcement contract
- Tier 2 (location) only contributes when `ContentScore > 0` OR `FallbackScore > 0`
- Tier 3 (fallback) only fires when `ContentScore == 0`
- These are correctness requirements, not optimisations

### Rule table ordering is load-bearing
Rules evaluated top-to-bottom, first match wins. More specific combinations (more required facts) must always precede less specific. Ordering violation = silent wrong score. Mitigate with unit test.

### Sibling-album noise at score 25 is expected
Every missing audio track attracts sibling tracks from the same album at `ArtistMatch+AlbumMatch=25`. Correct behaviour. Config thresholds are the gate, not a store floor.

### Folder depth scoring — marginal not cumulative
`FolderFacts.Weights[]` stores cumulative totals. `WeightForDepth(n)` returns marginal increment. Scorer sums marginals. `FolderDepth3` fact alone scores 30 (marginal), not 50 (cumulative). A developer must understand this or they will double-count.

### Legitimate improvement paths for the scoring engine
1. **New atomic facts** — new dimension of evidence (e.g. `DiscNumberMatch`, `BitrateMatch`, `TotalTracksMatch`)
2. **Negative signals** — penalise definitive mismatches (e.g. `AlbumMismatch`)
3. **Rule weight calibration** — empirically derived from repair history (future state, data not yet available)
4. **Conditional weight boosting** — new combinations not yet represented in rule tables
5. **Signal reliability by media type** — weights within each table more aggressively differentiated

### What is NOT a legitimate improvement
- Minimum store floor to suppress low-confidence candidates — hides observations, use threshold gates instead
- Smuggling combination logic into collectors — reintroduces the architectural problems this design solved
- Adding folder depth to CompositeScore without a content anchor — produces false positives

---

## Vestigial files — exclude from compilation if present
- `PlaylistProtection` namespace cluster: `PluginServices.cs`, `SimulationService.cs`, `ConfidenceEngine.cs`, `IConfidenceRule.cs`, `FilenameMatchRule.cs`, `PathMatchRule.cs`, four model files, `CandidateRefreshTask.cs`
- `PlaylistEventProbe.cs` and `UserManagerProbeService.cs` — log noise in production, probe-only

---

## Key Emby framework learnings
See `Evidence.md` for the full proven/decompiled evidence log. Summary of most critical:

- `RunCommand` is the sole framework entry point — never override `OnSaveCommand`
- Two-class config pattern: `PluginConfiguration : BasePluginConfiguration` (POCO, persisted) + `ConfigUI : EditableOptionsBase` (view-only, never persisted)
- `PluginDialogView.OnOkCommand` base throws `NotImplementedException` — never call it
- `DxDataGrid` with `ListIds` returns DB insertion order — use `Playlist.GetItemList` for order-sensitive operations
- Repair ordering: atomic remove-all then add-in-order (not add-then-move)
- `PlaylistItemsAdded` fires synchronously during `await CreatePlaylist` — write GT directly after creation, not in the event handler
- Folder rename = hard delete + create in Emby — detect by path prefix, not InternalId
- `RefreshCompleted` on parent folder signals replacement tracks are committed and candidate discovery can run