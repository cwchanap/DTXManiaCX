# Config: Persist-on-Edit Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make `ConfigManager.Config` the single source of truth so every config edit applies immediately (live + deferred-to-disk) instead of being staged in per-stage working copies committed on exit.

**Architecture:** Extend the existing `SetScrollSpeed` dirty-flag + deferred-flush pattern to all settings. Stages read `ConfigManager.Config` for display and call typed setters on edit; no private working copies. Binding edits fire change events that `InputManager` subscribes to, rebuilding its live maps. The Comment-1 staging bug becomes structurally impossible.

**Tech Stack:** .NET 8, MonoGame, xUnit + Moq, Microsoft.Extensions.Logging.

**Design doc:** `docs/superpowers/specs/2026-06-18-config-persist-on-edit-design.md`

---

## Scope refinements from research (read before starting)

- **Volume is OUT of scope.** `ConfigData.MasterVolume/BGMVolume/SEVolume` are never read at runtime and have no edit UI in `ConfigStage.SetupConfigItems`. Do not add volume setters or events.
- **Only these setters need change events** (live subscribers): `SetKeyBindings`, `SetSystemKeyBindings` (new) and `SetScrollSpeed` (exists). `SetAutoPlay`/`SetNoFail`/`SetAudioLatency`/`SetResolution`/`SetFullscreen`/`SetVSync` are dirty+flush only (no event) — they take effect at song-start/device-exit by reading `Config`.
- **`ConfigData.KeyBindings`** is `Dictionary<string,int>` (`ConfigData.cs:29`). **`ConfigData.SystemKeyBindings`** is `Dictionary<string,string>` keyed `"SystemKey.<Command>"` (`ConfigData.cs:32`). The existing `SaveKeyBindings(KeyBindings)` (`ConfigManager.cs:134`) and `SaveSystemKeyBindings(IReadOnlyDictionary<Keys,InputCommandType>)` (`ConfigManager.cs:254`) already do the type translation — they are refitted into the firing setters.
- **No single "rebuild system map from config" call exists.** Use `ConfigManager.LoadSystemKeyBindings(InputManager)` (`ConfigManager.cs:166`) as the subscriber's rebuild path; it parses `Config.SystemKeyBindings` into `InputManager._keyMapping`.
- **Reverse auto-save must be removed.** Today `ModularInputManager.OnKeyBindingsChanged` (`ModularInputManager.cs:107,427-432`) calls `SaveKeyBindings` back into `Config` whenever the runtime `KeyBindings` object changes — that bypasses the dirty flag and opposes the new single-direction flow (Config → runtime). Audit and remove.

**Verification gate (after every phase):** `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj` (0 errors) and `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj` (all green).

---

## Phase 1 — ConfigManager foundation (dirty flag + setters + events)

### Task 1.1: Capture loaded config path for dirty-marking

**Files:** Modify `DTXMania.Game/Lib/Config/ConfigManager.cs`

**Step 1:** Add a field to remember the path passed to `LoadConfig`, and a private `MarkDirty` helper. Set the field in `LoadConfig`.

```csharp
private string? _loadedConfigPath; // captured in LoadConfig so setters can mark dirty without a path arg

private void MarkDirty(string? path = null)
{
    _pendingSavePath = path ?? _pendingConfigPath() ?? _pendingSavePath;
}
private string? _pendingConfigPath() => _loadedConfigPath;
```

In `LoadConfig(string filePath)`, add `_loadedConfigPath = filePath;` near the top.

**Step 2:** Refit `SetScrollSpeed` to use `MarkDirty` (behavior identical):

```csharp
public void SetScrollSpeed(string configFilePath, int percent)
{
    var snapped = ScrollSpeedRange.SnapAndClamp(percent);
    var old = Config.ScrollSpeed;
    if (snapped == old) return;
    Config.ScrollSpeed = snapped;
    MarkDirty(configFilePath);
    ScrollSpeedChanged?.Invoke(this, new ScrollSpeedChangedEventArgs(old, snapped));
}
```

**Step 3:** Build + run existing scroll-speed tests. Expected: green (no behavior change).

**Step 4:** Commit: `refactor: centralize ConfigManager dirty-flag marking`

---

### Task 1.2: `SetKeyBindings` setter (fires event + dirty) — TDD

**Files:**
- Test: `DTXMania.Test/Config/ConfigManagerTests.cs` (locate existing; add a `SetKeyBindings_*` group)

**Step 1: Write failing tests**

```csharp
[Fact]
public void SetKeyBindings_MutatesConfig_MarksDirty_FiresEvent()
{
    var cm = new ConfigManager();
    var raised = false;
    cm.KeyBindingsChanged += (_, _) => raised = true;

    var kb = new KeyBindings();
    kb.BindButton("Key.X", 2);
    cm.SetKeyBindings(kb);

    Assert.Equal(2, cm.Config.KeyBindings["Key.X"]);
    Assert.True(raised);
}

[Fact]
public void SetKeyBindings_NoEvent_WhenNoSubscriber_DoesNotThrow() =>
    Assert.Null(Record.Exception(() => new ConfigManager().SetKeyBindings(new KeyBindings())));
```

**Step 2:** Run → FAIL (`SetKeyBindings` / `KeyBindingsChanged` not defined).

**Step 3: Implement** in `ConfigManager.cs`:

```csharp
public event EventHandler<EventArgs>? KeyBindingsChanged;

public void SetKeyBindings(KeyBindings keyBindings)
{
    SaveKeyBindings(keyBindings);      // existing in-memory translation (ConfigManager.cs:134)
    MarkDirty();
    KeyBindingsChanged?.Invoke(this, EventArgs.Empty);
}
```

**Step 4:** Run → PASS.

**Step 5:** Commit: `feat: ConfigManager.SetKeyBindings applies immediately (dirty + event)`

---

### Task 1.3: `SetSystemKeyBindings` setter (fires event + dirty) — TDD

**Files:** Test `DTXMania.Test/Config/ConfigManagerTests.cs`; modify `ConfigManager.cs`.

**Step 1: Failing test**

```csharp
[Fact]
public void SetSystemKeyBindings_MutatesConfig_MarksDirty_FiresEvent()
{
    var cm = new ConfigManager();
    var raised = false;
    cm.SystemKeyBindingsChanged += (_, _) => raised = true;

    cm.SetSystemKeyBindings(new Dictionary<Keys, InputCommandType> { [Keys.Z] = InputCommandType.MoveUp });

    Assert.True(raised);
    Assert.Contains("SystemKey.MoveUp", cm.Config.SystemKeyBindings.Keys);
}
```

**Step 2:** Run → FAIL.

**Step 3: Implement.** Rename the existing `SaveSystemKeyBindings(IReadOnlyDictionary<Keys,InputCommandType>)` body into a private `ApplySystemKeyBindings(...)`, and expose the firing setter keeping the old public name as the setter:

```csharp
public event EventHandler<EventArgs>? SystemKeyBindingsChanged;

public void SetSystemKeyBindings(IReadOnlyDictionary<Keys, InputCommandType> workingBindings)
{
    ApplySystemKeyBindings(workingBindings); // former SaveSystemKeyBindings body (ConfigManager.cs:254-289)
    MarkDirty();
    SystemKeyBindingsChanged?.Invoke(this, EventArgs.Empty);
}
```

Keep the `SaveSystemKeyBindings(InputManager)` overload delegating to the private apply (it's used elsewhere) — but audit callers (see Phase 6). Update any internal callers of the old `SaveSystemKeyBindings(dict)` to `SetSystemKeyBindings(dict)`.

**Step 4:** Run → PASS. **Step 5:** Commit: `feat: ConfigManager.SetSystemKeyBindings applies immediately`

---

### Task 1.4: Scalar setters (dirty+flush, no event) — TDD

**Files:** Test `DTXMania.Test/Config/ConfigManagerTests.cs`; modify `ConfigManager.cs`.

Add setters that mutate + `MarkDirty()` and **do not** fire events: `SetAutoPlay(bool)`, `SetNoFail(bool)`, `SetAudioLatency(int)`, `SetResolution(int w,int h)`, `SetFullscreen(bool)`, `SetVSync(bool)`.

**Step 1: Failing test** (one representative + a flush test):

```csharp
[Fact]
public void SetAutoPlay_Mutates_AndMarksDirty()
{
    var cm = new ConfigManager();
    cm.SetAutoPlay(true);
    Assert.True(cm.Config.AutoPlay);
    // dirty flagged => FlushPendingSave writes; verify via a temp file
}

[Fact]
public void FlushPendingSave_AfterScalarEdits_WritesFile()
{
    var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var prev = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
    Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", dir);
    try
    {
        var cm = new ConfigManager();
        cm.LoadConfig(AppPaths.GetConfigFilePath());
        cm.SetNoFail(true);
        cm.FlushPendingSave();
        Assert.True(File.Exists(AppPaths.GetConfigFilePath()));
    }
    finally { Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", prev); Directory.Delete(dir, true); }
}
```

**Step 2:** Run → FAIL. **Step 3: Implement** the six setters (each: `Config.X = v; MarkDirty();`). **Step 4:** Run → PASS. **Step 5:** Commit: `feat: ConfigManager scalar setters persist on edit (dirty+flush)`

---

### Task 1.5: Flush-failure keeps dirty + retries — TDD

**Files:** Test `ConfigManagerTests.cs` (mirror the pattern; point `DTXMANIA_APPDATA_ROOT` at a file not a dir so `SaveConfig` throws).

**Step:** Assert that after a failed `FlushPendingSave`, a subsequent successful flush (env restored) still writes. `FlushPendingSave` already logs + keeps `_pendingSavePath` (`ConfigManager.cs:501-516`) — pin it with a test so the behavior is locked. Commit: `test: pin FlushPendingSave retry-on-failure semantics`

---

## Phase 2 — Interface + live-apply subscription (Config → runtime)

### Task 2.1: Expose new members on `IConfigManager`

**Files:** `DTXMania.Game/Lib/Config/IConfigManager.cs`

Add: `SetKeyBindings`, `SetSystemKeyBindings(IReadOnlyDictionary<Keys,InputCommandType>)`, `SetAutoPlay`, `SetNoFail`, `SetAudioLatency`, `SetResolution`, `SetFullscreen`, `SetVSync`, and events `KeyBindingsChanged`, `SystemKeyBindingsChanged`. (Keep `SetScrollSpeed`/`ScrollSpeedChanged` as-is.) Build. Commit: `feat: IConfigManager persist-on-edit surface`

> Note: the `SaveKeyBindings(KeyBindings)` and `SaveSystemKeyBindings(InputManager)` methods are **not** on the interface and stay concrete-only (used internally). Do not widen the interface for them.

---

### Task 2.2: `InputManager` subscribes to binding events → rebuilds live maps

**Files:** `DTXMania.Game/Lib/Input/InputManager.cs`, `ModularInputManager.cs`, wiring in `Game1.cs` (where the `ConfigManager` + `InputManagerCompat` are constructed).

**Goal:** When `ConfigManager.KeyBindingsChanged` fires, `ModularInputManager.ReloadKeyBindings()` runs (pulls drum bindings from `Config`). When `SystemKeyBindingsChanged` fires, the system/navigate map rebuilds from `Config` via `LoadSystemKeyBindings(this)`.

**Step 1: Failing test** (`DTXMania.Test/Input/...`): construct `ConfigManager` + `InputManagerCompat`, subscribe, call `cm.SetSystemKeyBindings({...})`, assert `inputManager.GetKeyMappingSnapshot()` reflects it without any manual `ApplySystemBindings`.

**Step 2: Implement** the subscription in the composition root (Game1) — subscribe once after both are constructed; unsubscribe on dispose. Reuse `ModularInputManager.ReloadKeyBindings()` (drum) and a new `InputManager.RebuildSystemKeyMapping()` that calls `configManager.LoadSystemKeyBindings(this)`.

**Step 3:** Run → PASS. **Step 4:** Commit: `feat: InputManager live-rebuilds binding maps on ConfigManager events`

---

### Task 2.3: Remove the reverse auto-save in `ModularInputManager`

**Files:** `DTXMania.Game/Lib/Input/ModularInputManager.cs` (`:107,427-432`)

Today `OnKeyBindingsChanged` → `_configManager.SaveKeyBindings(_keyBindings)`. Under the new model `Config` is the sole writer and the runtime object is reloaded from `Config` via the event from Task 2.2. Remove the auto-save handler (or make it a no-op) so edits can't bypass the dirty flag. Audit `ModularInputManager.SaveKeyBindings()` (`:257`) callers — if only the removed handler used it, delete it; otherwise leave for explicit callers.

**Step:** Build + run full suite; fix any test that depended on the reverse flow. Commit: `refactor: remove reverse KeyBindings auto-save; ConfigManager is sole writer`

---

## Phase 3 — `DrumCapturePopup` becomes provider-driven, intent-only

### Task 3.1: Redesign popup API — TDD (rewrite tests first)

**Files:** Test `DTXMania.Test/Stage/DrumConfig/DrumCapturePopupTests.cs`; source `DTXMania.Game/Lib/Stage/DrumConfig/DrumCapturePopup.cs`

**New contract:**
```csharp
public DrumCapturePopup(
    Func<IReadOnlyDictionary<string,int>> drumBindingsProvider,   // current drum map (display)
    Func<IReadOnlyDictionary<Keys,InputCommandType>> systemMappingProvider); // conflict check
// TryCapture: required-key -> Rejected+ShowingConflict; otherwise Captured. Mutates NOTHING.
public DrumCaptureOutcome TryCapture(ButtonState button);
// Display reads (fresh each call) via the drum provider:
public IReadOnlyList<string> CurrentBindings { get; }
public IReadOnlyList<DrumBindingChip> GetBindingChips(int vw, int vh);
// Remove/Clear are REMOVED — the stage carries them out via ConfigManager.
```

**Step 1:** Rewrite the popup tests to the new API: `TryCapture_RequiredKey_Rejected`, `TryCapture_FreeKey_ReturnsCapturedWithoutMutating`, `CurrentBindings_ReadsViaProvider`, `GetBindingChips_*` (provider-driven). Delete `RemoveBinding_And_ClearLane_MutateWorkingBindings` and any test asserting the popup mutates a `KeyBindings`.

**Step 2:** Run → FAIL (constructor/members changed). **Step 3:** Refactor the popup to the new contract. `CurrentBindings`/`GetBindingChips` query `_drumBindingsProvider()`. `TryCapture` keeps the `KeyConflictChecker.GetRequiredSystemConflict` check (no `BindButton`).

**Step 4:** Run → PASS. **Step 5:** Commit: `refactor: DrumCapturePopup is provider-driven and intent-only`

---

## Phase 4 — `DrumConfigStage`: read truth, edit via setters, delete staging

### Task 4.1: Capture handler applies immediately (eviction at capture) — TDD

**Files:** Test `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`; source `DrumConfigStage.cs`

**Step 1: Failing test** (replaces `Save_EvictsSystemKeysClaimedByDrumLanes`):

```csharp
[Fact]
public void Capture_NonRequiredSystemKey_EvictsImmediatelyFromConfig()
{
    // setup ConfigManager (concrete) + InputManagerCompat; system map has PageUp->IncreaseScrollSpeed
    // stage.Capture("Key.PageUp", lane:7)  -> a new stage method that wraps clone+BindButton+SetKeyBindings+evict+SetSystemKeyBindings
    Assert.False(configManager.Config.SystemKeyBindings.Contains("SystemKey.IncreaseScrollSpeed" with PageUp));
}
```

**Step 2:** Run → FAIL. **Step 3:** In `DrumConfigStage`, replace the popup-mutation flow. Introduce a private edit helper, e.g. `ApplyCapture(string buttonId, int lane)`:
```csharp
var kb = new KeyBindings(); _configManager.LoadKeyBindings(kb); kb.BindButton(buttonId, lane);
_configManager.SetKeyBindings(kb);
// eviction (Option 1, immediate):
if (KeyBindings.IsKeyboardButtonId(buttonId) && Enum.TryParse(buttonId.Substring(4), out Keys k))
{
    var snap = _configManager.GetSystemKeyMappingKeys();  // small helper: Config.SystemKeyBindings -> Keys dict
    if (snap.Remove(k)) _configManager.SetSystemKeyBindings(snap);
}
```
The popup's `TryCapture` returns the captured button; the stage, on `Captured`, calls `ApplyCapture`. Chip-remove / clear / reset similarly: clone via `LoadKeyBindings`, mutate, `SetKeyBindings` (no system restore — Decision 3).

**Step 4:** Run → PASS. **Step 5:** Commit: `feat: DrumConfigStage applies drum edits immediately with capture-time eviction`

---

### Task 4.2: Delete staging machinery + stopgap in `DrumConfigStage`

**Files:** `DrumConfigStage.cs`

Delete: `_workingBindings`, `_workingSystemBindings`, `Save()`, `CommitAndExit`→ now just `ChangeStage(Config)`, `EvictSystemKeysClaimedByDrumLanes`, `ApplySystemBindings`, `PendingSystemBindingsKey`, `ResolvePopupSystemMapping`, and the `sharedData` popup wiring from the stopgap. `OnActivate` builds the popup with providers reading `_configManager.Config` (`() => _configManager.Config.KeyBindings` and a Keys-view for system). `OnDeactivate` calls `_configManager.FlushPendingSave()`. Back = exit.

Delete the stopgap tests: `ResolvePopupSystemMapping_*`, `PendingSystemBindingsKey_*`, and the pending-map tests added in `DrumCapturePopupTests` for the stopgap (`TryCapture_PendingRequiredKey_*`, `TryCapture_PendingFreedKey_*`) — replaced by the single-source-of-truth versions (provider reads `Config`, which is always current).

Build + run. Commit: `refactor: DrumConfigStage reads ConfigManager truth; delete staging + stopgap`

---

### Task 4.3: Comment-1 regression test (the proof) — TDD

**Files:** Test `DrumConfigStageTests.cs` / a new round-trip test

**Step:** A test that mirrors the original bug scenario under the new model: set a system key in `Config` → simulate entering DrumConfig → capture a different key → return → assert the system key survives (because there's no pending state and no deferred eviction touching unrelated keys). This is the successor proof that the bug class is dead. Commit: `test: Comment-1 round-trip regression under persist-on-edit`

---

## Phase 5 — `ConfigStage`: read truth, edit via setters, delete staging

### Task 5.1: Route `SetupConfigItems` lambdas through setters

**Files:** `ConfigStage.cs` (`:351-469`)

Replace each lambda's `_workingConfig.X = ...; _hasUnsavedChanges = true;` with the matching setter call on the concrete `ConfigManager`:
- Resolution → `_cm.SetResolution(w,h)`; Fullscreen → `SetFullscreen`; VSync → `SetVSync`; NoFail → `SetNoFail`; AutoPlay → `SetAutoPlay`; AudioLatency → `SetAudioLatency`; ScrollSpeed → `_cm.SetScrollSpeed(path, value)` (this also fixes the latent bug where ConfigStage bypassed `ScrollSpeedChanged`).
- Display getters (`() => _workingConfig.X`) → `() => _configManager.Config.X`.

Keep a `private readonly ConfigManager? _cm;` cast from `_configManager` (set in `OnActivate`). Build. Commit: `refactor: ConfigStage items edit via ConfigManager setters`

---

### Task 5.2: System panel → immediate `SetSystemKeyBindings`

**Files:** `ConfigStage.cs` (`OnPanelSaved` `:513-521`, `InitializePanels` `:471-487`)

`OnPanelSaved`: replace `_workingSystemBindings = ...; _hasUnsavedChanges = true;` with `_cm!.SetSystemKeyBindings(_systemPanel.GetWorkingMappingSnapshot());`. Rewire the panel's providers to read live truth: `_workingMappingProvider = () => ToKeysDict(_configManager.Config.SystemKeyBindings)`, `_liveDrumBindingsProvider = () => _configManager.Config.KeyBindings`, `_navigationMappingProvider = () => ToKeysDict(_configManager.Config.SystemKeyBindings)`. (Add a small `ToKeysDict` helper or reuse `InputManager.GetKeyMappingSnapshot()`.) Build. Commit: `refactor: System Key Mapping panel persists immediately`

---

### Task 5.3: Delete `ConfigStage` staging machinery

**Files:** `ConfigStage.cs`

Delete: `_workingConfig`, `_workingDrumBindings`, `_workingSystemBindings`, `_navigationBindings`, `_hasUnsavedChanges`, `DiscardPendingChanges`, `LoadWorkingInputBindings`, `ReloadWorkingDrumBindings`, `LoadConfiguration` (or reduce to a no-op/removed), the `OnActivate` preservation branch (`:122-130`), `ApplyConfiguration`'s snapshot/rollback (the whole method can shrink to just graphics apply if needed — but graphics apply timing is verified in Task 6.1), `_saveError` + `DrawSaveError`, `ApplySystemBindings` helper, `IsPanelCommandPressed`'s dependence on `_workingSystemBindings` (point it at `Config`). `HandleInput` Back path → just `ChangeStage(Title)` (no discard). `OnDeactivate` → `FlushPendingSave()`. Update `OnSaveButtonClicked` — "Save & Exit" becomes just "Exit" (or keep label, semantics = exit since already saved); prefer relabel to "EXIT".

Update `ConfigStageLogicTests` / `ConfigStageTests`: remove `_hasUnsavedChanges`/discard/round-trip-pending tests; replace with tests asserting edits call setters (mirror the existing `ActivatePressedOnDrumKeyMapping_*` style, generalized). Build + run. Commit: `refactor: ConfigStage reads ConfigManager truth; delete staging`

---

## Phase 6 — Graphics apply verification, cleanup, full gate

### Task 6.1: Verify resolution/fullscreen/vsync take-effect timing

**Files:** investigate `GraphicsManager` + `ConfigStage`/`Game1`.

Determine how resolution currently takes effect (on-exit vs next-startup). Ensure the refactor preserves it: device-deferred settings are dirty+flush only; if `GraphicsManager` needs an explicit "apply on Config deactivation" hook that previously lived in `ApplyConfiguration`, add it. Do not regress graphics application. If today it's next-startup only and that's acceptable, document and move on. Commit (if changed): `fix: preserve graphics apply timing after persist-on-edit refactor`

---

### Task 6.2: Audit deleted-method callers

Grep the whole repo for callers of: `ConfigManager.SaveKeyBindings(KeyBindings)`, `SaveSystemKeyBindings(...)`, `ModularInputManager.SaveKeyBindings()`, `DrumConfigStage.Save/PendingSystemBindingsKey/ResolvePopupSystemMapping`, `ConfigStage.ApplyConfiguration/ApplySystemBindings/DiscardPendingChanges/LoadWorkingInputBindings/ReloadWorkingDrumBindings`, `ConfigData.SnapshotBindingState/RestoreBindingState` (now unused after rollback deletion — keep the helpers only if still referenced). Remove or update every caller. Build. Commit: `chore: remove dead callers after persist-on-edit refactor`

---

### Task 6.3: Full verification gate

Run:
```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```
Expected: 0 build errors; all tests green. Also run the targeted classes:
```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~ConfigManager|FullyQualifiedName~DrumConfig|FullyQualifiedName~ConfigStage|FullyQualifiedName~DrumCapturePopup|FullyQualifiedName~Input"
```
Commit the final green state: `test: persist-on-edit refactor — full suite green`

---

## Appendix A — Deletion checklist

**ConfigStage.cs:** `_workingConfig`, `_workingDrumBindings`, `_workingSystemBindings`, `_navigationBindings`, `_hasUnsavedChanges`, `_saveError`, `LoadConfiguration`, `LoadWorkingInputBindings`, `ReloadWorkingDrumBindings`, `DiscardPendingChanges`, `ApplyConfiguration` (snapshot/rollback), `ApplySystemBindings`, the `OnActivate` preservation branch, `DrawSaveError`.

**DrumConfigStage.cs:** `_workingBindings`, `_workingSystemBindings`, `Save()`, `CommitAndExit` (→ inline exit), `EvictSystemKeysClaimedByDrumLanes`, `ApplySystemBindings`, `PendingSystemBindingsKey`, `ResolvePopupSystemMapping`, `_saveError`, the `sharedData` popup wiring.

**DrumCapturePopup.cs:** owned/mutated `KeyBindings`, `RemoveBinding`, `ClearLane` (move to stage).

**ModularInputManager.cs:** reverse auto-save handler (`OnKeyBindingsChanged`→`SaveKeyBindings`) if confirmed unused after Phase 4/5.

## Appendix B — Test churn map

- **Delete:** stopgap tests (`ResolvePopupSystemMapping_*`, `PendingSystemBindingsKey_*`, `TryCapture_PendingRequiredKey_*`, `TryCapture_PendingFreedKey_*`), `Save_EvictsSystemKeysClaimedByDrumLanes`, `Save_PreservesSystemKeyWhenDrumBindingRemovedBeforeCommit`, `Save_WhenSaveConfigThrows*` (DrumConfigStage), `_hasUnsavedChanges`/`DiscardPendingChanges`/round-trip-pending tests in ConfigStage suites.
- **Rewrite:** `DrumCapturePopupTests` to provider/ intent API; `DrumConfigStageTests` capture tests to eviction-at-capture.
- **Add:** `ConfigManager.SetX_*` setter tests; `FlushPendingSave` retry test; `InputManager` subscription test; Comment-1 round-trip regression.

## Appendix C — Open risk: bidirectional binding sync

The trickiest invariant: `Config.KeyBindings` (truth) ↔ `ModularInputManager._keyBindings` (runtime). Ensure edits flow **only** Config → runtime (via the Phase 2 event) and never runtime → Config (the removed reverse auto-save). If any gameplay/path mutates the runtime `KeyBindings` object directly, it will be silently lost. Audit `KeyBindings.BindButton/UnbindButton/ClearAllBindings/LoadDefaultBindings` callers beyond the stage edit paths.
