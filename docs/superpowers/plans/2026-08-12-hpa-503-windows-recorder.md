# HPA-503 Windows Recorder Vertical Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build one Windows-first `dtx-video` tool that records an exact indexed chart from rendered Song Select through prepared preview, normal AutoPlay gameplay, and Result while leaving live CX data untouched.

**Architecture:** One plain `net8.0` recorder executable plus tests. Reuse `DTXMania.Automation`; keep one `RecordWorkflow`, four-op `IObsRecorder`, pure `ObsProtocol`, unique per-run config sandbox, compact diagnostics, and one artifact verifier.

**Tech Stack:** .NET 8, `DTXMania.Automation`, `ClientWebSocket`, `System.Text.Json`, xUnit, optional PATH `ffprobe`.

## Global Constraints

- Production recorder references `DTXMania.Automation` only; no Game/E2E/MCP project references.
- Live recording is Windows x64; recorder tests remain plain `net8.0` on Windows/macOS.
- One unique disposable app-data root per run; never copy live DB/WAL/cache/score/history.
- Source config must already be CX-normalized: preserve `SkinPath=Default`, require filesystem path values fully qualified, reject relative/legacy values.
- Enter Song Select through Title GAME START with one 50 ms Enter; no `ChangeStageAsync`.
- Wait for `SongSelect && SelectedSongTitle non-empty` before one-shot `PrepareVideoChartAsync`.
- `HttpClient.Timeout = Timeout.InfiniteTimeSpan`; use four recorder-owned workflow bounds.
- OBS interface remains Connect / Status / Start / Stop only; no scene/source screenshot/validation APIs.
- Raw-path containment belongs only to the artifact verifier.
- Keep PATH `ffprobe` optional; do not add FFMpegCore/MMTools packages.
- Diagnostics are `run.json`, `cx-stdout.log`, `cx-stderr.log`; never copy sandbox Config.ini.
- No persistent recorder DB/cache, live OBS CI, batch queue, DI/workflow framework, remux/re-encode, or platform adapter hierarchy.

---

## Files

```text
DTXMania.VideoRecorder/
  DTXMania.VideoRecorder.csproj
  Properties/AssemblyInfo.cs
  Program.cs
  RecorderCommandLine.cs
  Configuration/RecorderEnvironment.cs
  Sandbox/RecordingSandbox.cs
  Obs/IObsRecorder.cs
  Obs/ObsProtocol.cs
  Obs/ObsWebSocketRecorder.cs
  Workflow/IGameRecordingControl.cs
  Workflow/AutomationGameRecordingControl.cs
  Workflow/RecordingStep.cs
  Workflow/RecordWorkflow.cs
  Diagnostics/RecorderDiagnostics.cs
  Media/RecordingArtifactVerifier.cs

DTXMania.VideoRecorder.Tests/
  DTXMania.VideoRecorder.Tests.csproj
  Sandbox/RecordingSandboxTests.cs
  Obs/ObsProtocolTests.cs
  Workflow/RecordWorkflowTests.cs
  Diagnostics/RecorderDiagnosticsTests.cs
  Media/RecordingArtifactVerifierTests.cs

Modify: DTXMania.sln, .github/workflows/build-and-test.yml
```

Keep all five tasks in one HPA-503 implementation PR.

---

### Task 1: Recorder shell and disposable normalized-config sandbox

**Files:** project/solution files, `Program.cs`, `RecorderCommandLine.cs`, `RecorderEnvironment.cs`, `RecordingSandbox.cs`, sandbox tests.

**Produces:**

```csharp
internal sealed record RecorderEnvironment(
    Uri ObsUrl,
    string ObsPassword,
    string ObsOutputDirectory,
    string SourceAppDataRoot);

internal sealed class RecordingSandbox
{
    public string RunRoot { get; }
    public string AppDataRoot { get; }
    public string ConfigPath { get; }
    public int ApiPort { get; }
    public string ApiKey { get; }
    public static RecordingSandbox Create(string sourceAppDataRoot);
    public Task DeleteOnSuccessAsync();
}
```

- [ ] **Step 1: Scaffold projects and internal test visibility.**

Production project references only `../DTXMania.Automation/DTXMania.Automation.csproj`. Test project mirrors Automation test package versions.

```csharp
// Properties/AssemblyInfo.cs
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("DTXMania.VideoRecorder.Tests")]
```

Add both projects to `DTXMania.sln`.

- [ ] **Step 2: Add failing sandbox contract tests.**

Required cases:

```text
SkinPath=Default survives unchanged, case-insensitive
absolute DTXPath/SongRoot.N/SystemSkinRoot/custom SkinPath survive
relative values such as DTXPath=Songs, SongRoot.0=~/charts, SkinPath=Skins/X fail
missing SongRoot.* fails
LastUsedSkin + play/scroll/pitch/volume/unrelated lines survive
only API/AutoPlay/NoFail/1280x720/windowed are overridden
songs.db/WAL/Cache/CrashReports are never copied
success deletes run root; failure leaves it
```

Error for non-normalized config must include:

```text
Open CX once and exit normally, then retry dtx-video.
```

Run and expect FAIL:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --filter "FullyQualifiedName~RecordingSandboxTests"
```

- [ ] **Step 3: Implement copy/validate/patch without path compatibility logic.**

Validation helper is intentionally small:

```csharp
static bool IsDefaultSkin(string value) =>
    value.Trim().Equals("Default", StringComparison.OrdinalIgnoreCase);

static void RequireAbsolute(string key, string value)
{
    if (!Path.IsPathFullyQualified(value))
        throw new InvalidOperationException(
            $"Source Config.ini key '{key}' is not normalized. " +
            "Open CX once and exit normally, then retry dtx-video.");
}
```

Apply `RequireAbsolute` to `DTXPath`, every `SongRoot.N`, `SystemSkinRoot`, and custom `SkinPath`; require at least one `SongRoot.N`. Never interpret `LastUsedSkin` as a path.

Create `%TEMP%/DTXManiaCX-video/<run-id>/appdata`, copy Config.ini only, choose a local ephemeral API port, generate a random API key, and patch only the owned keys from Global Constraints.

- [ ] **Step 4: Implement two-verb CLI/environment validation.**

```text
doctor
record --chart <absolute path> --output <directory>
```

OBS env:

```text
DTXMANIA_VIDEO_OBS_URL default ws://127.0.0.1:4455, loopback only
DTXMANIA_VIDEO_OBS_PASSWORD optional
DTXMANIA_VIDEO_OBS_OUTPUT_DIR required for record
```

`record` rejects non-Windows, missing/non-absolute chart, unusable publish directory, missing/non-normalized source config, non-loopback OBS URL, or invalid OBS output dir before creating owned resources.

- [ ] **Step 5: Verify and commit.**

```bash
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --filter "FullyQualifiedName~RecordingSandboxTests"
git add DTXMania.VideoRecorder DTXMania.VideoRecorder.Tests DTXMania.sln
git commit -m "feat: add recorder sandbox foundation"
```

---

### Task 2: Testable OBS protocol, four-op client, and doctor gate

**Files:** `IObsRecorder.cs`, `ObsProtocol.cs`, `ObsWebSocketRecorder.cs`, `ObsProtocolTests.cs`, `Program.cs`.

**Produces:**

```csharp
internal sealed record ObsRecordStatus(bool IsRecording);

internal interface IObsRecorder : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken token);
    Task<ObsRecordStatus> GetRecordStatusAsync(CancellationToken token);
    Task StartRecordAsync(CancellationToken token);
    Task<string> StopRecordAsync(CancellationToken token);
}
```

- [ ] **Step 1: Add failing pure protocol tests.**

`ObsProtocol` exposes only narrow internal helpers for:

```text
auth = Base64(SHA256(Base64(SHA256(password + salt)) + challenge))
GetRecordStatus outputActive -> ObsRecordStatus
requestStatus result/code/comment -> success or actionable exception
StopRecord responseData.outputPath -> required string
malformed/wrong response kinds -> failure
```

Compute the auth expected value independently in the test. No sockets/fake server.

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --filter "FullyQualifiedName~ObsProtocolTests"
```

Expected: FAIL.

- [ ] **Step 2: Implement `ObsProtocol`.**

Use `SHA256`, Base64, and `System.Text.Json`. Support only Hello/Identify/Identified plus GetRecordStatus/StartRecord/StopRecord response mapping. It owns no socket/path/source/scene behavior.

- [ ] **Step 3: Implement `ObsWebSocketRecorder`.**

Use `ClientWebSocket`; responsibilities are Hello -> Identify, monotonically unique request IDs, one outstanding request at a time, matching response correlation, harmless-event skipping, and the four interface methods.

`StartRecordAsync` confirms active status before returning. `StopRecordAsync` returns OBS `outputPath` unchanged; no containment check here.

- [ ] **Step 4: Implement read-only `doctor`.**

Report Windows/repo/game-project/source-config/OBS URL/raw-output-dir/OBS auth+status/ffprobe availability.

Print the manual prerequisite:

```text
Dedicated OBS profile/collection/scene already selected
CX window/program capture configured
CX application audio configured
Hybrid MP4 configured
WebSocket enabled
raw output dir matches DTXMANIA_VIDEO_OBS_OUTPUT_DIR
```

Also warn that each HPA-503 run uses a fresh songs.db and cold enumeration may take minutes. Do not inspect/mutate OBS sources.

- [ ] **Step 5: Verify automated protocol tests, then live doctor.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --filter "FullyQualifiedName~ObsProtocolTests"
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj
```

On Windows with the dedicated OBS profile open:

```text
dtx-video doctor
```

Require Hello/Identify + GetRecordStatus success, OBS inactive, correct raw output dir, and no OBS state mutation. If that environment is unavailable, record this gate as unverified rather than substituting a fake server.

- [ ] **Step 6: Commit.**

```bash
git add DTXMania.VideoRecorder/Obs DTXMania.VideoRecorder.Tests/Obs DTXMania.VideoRecorder/Program.cs
git commit -m "feat: add minimal OBS recorder client"
```

---

### Task 3: Normal CX journey, readiness gates, and owned cleanup

**Files:** workflow files/tests plus `Program.cs`.

**Produces thin test seam:**

```csharp
internal interface IGameRecordingControl : IAsyncDisposable
{
    string StandardOutput { get; }
    string StandardError { get; }
    void Start(GameProcessStartOptions options);
    Task WaitForStartupAsync(TimeSpan timeout, CancellationToken token);
    Task<GameStateSnapshot> GetGameStateAsync(CancellationToken token);
    Task SendKeyAsync(string key, TimeSpan hold, CancellationToken token);
    Task PrepareVideoChartAsync(string chartPath, CancellationToken token);
    Task<string?> TakeScreenshotBase64Async(CancellationToken token);
    Task StartPreparedPreviewAsync(CancellationToken token);
    Task ActivatePreparedChartAsync(CancellationToken token);
}
```

No stage-wait/ChangeStage operation on this interface.

Timeouts:

```csharp
SetupTimeout       = TimeSpan.FromMinutes(5);
StageTimeout       = TimeSpan.FromMinutes(2);
PerformanceTimeout = TimeSpan.FromMinutes(20);
ExternalIoTimeout  = TimeSpan.FromSeconds(15);
```

- [ ] **Step 1: Add failing workflow ordering/ownership tests.**

Happy order:

```text
Start -> WaitStartup -> Title -> Enter(50ms)
-> SongSelect with non-empty SelectedSongTitle
-> Prepare once -> CX screenshot
-> OBS connect/status/start
-> preview Playing + elapsed>=10s
-> Activate -> SongTransition
-> PerformanceReady+AutoPlay+notes>0
-> Result SongComplete+complete judgements
-> CX screenshot -> 5s no-input hold
-> owned OBS stop -> cleanup
```

Also cover pre-existing OBS recording, Start failure, post-Start failure, unexpected stage order, AutoPlay false, zero notes, unsuccessful/incomplete Result, cancellation at preview/performance/hold, and idempotent cleanup. Assert prepare is called exactly once and only after populated Song Select.

- [ ] **Step 2: Implement `AutomationGameRecordingControl` by delegation.**

Construct `HttpClient` with `Timeout.InfiniteTimeSpan`. Delegate directly to `GameProcessDriver` and HPA-510 `JsonRpcGameClient` methods. Copy small repo-root/port policy locally; do not change Automation.

- [ ] **Step 3: Implement startup/readiness prefix with `Eventually.UntilAsync`.**

```csharp
await game.WaitForStartupAsync(SetupTimeout, token);
await Eventually.UntilAsync(game.GetGameStateAsync,
    s => s.StageType == "Title", SetupTimeout, poll, "Title", token);
await game.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), token);
await Eventually.UntilAsync(game.GetGameStateAsync,
    s => s.StageType == "SongSelect" && !string.IsNullOrWhiteSpace(s.SelectedSongTitle),
    SetupTimeout, poll, "populated Song Select", token);
await game.PrepareVideoChartAsync(chartPath, setupBoundToken);
```

One-shot prepare preserves the real permanent "chart not available" error instead of retrying it for minutes.

- [ ] **Step 4: Implement the remaining journey.**

Predicates:

```text
Preview: SongSelect && PreparedPreviewState==Playing && PreparedPreviewElapsedMs>=10000
Performance: StageType==Performance && PerformanceReady && AutoPlayEnabled && TotalNotes>0
Result: StageType==Result && StageCompleted && ClearFlag && CompletionReason==SongComplete
        && TotalNotes>0 && TotalJudgements==TotalNotes
```

Observe SongTransition explicitly before Performance. Use `StageTimeout` for preview/transition/readiness and `PerformanceTimeout` for gameplay to completed Result. After Result screenshot, use a tiny injectable delay seam for exactly 5 seconds and send no game input.

- [ ] **Step 5: Implement one `finally` ownership path and Ctrl+C cancellation.**

Track only `obsOwned`, `obsStopped`, `rawOutputPath`. Stop in finally only when the workflow successfully acquired OBS ownership and has not stopped it. Always dispose the owned CX control. Cleanup uses `ExternalIoTimeout`; secondary cleanup failures never replace the primary failure. `Program` sets `ConsoleCancelEventArgs.Cancel=true` and cancels the workflow token.

- [ ] **Step 6: Verify and commit.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --filter "FullyQualifiedName~RecordWorkflowTests"
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj
git add DTXMania.VideoRecorder/Workflow DTXMania.VideoRecorder.Tests/Workflow DTXMania.VideoRecorder/Program.cs
git commit -m "feat: add Windows recording workflow"
```

---

### Task 4: Compact diagnostics and single-owner artifact verification

**Files:** diagnostics/media files/tests; integrate into `RecordWorkflow`/`Program`.

**Output:**

```text
<output>/diagnostics/<run-id>/run.json
<output>/diagnostics/<run-id>/cx-stdout.log
<output>/diagnostics/<run-id>/cx-stderr.log
```

- [ ] **Step 1: Add failing diagnostics tests.**

Assert exact three-file output, no Config.ini, and that known API/OBS secret strings do not appear. `run.json` holds step timeline, selected telemetry, OBS outcomes, raw/published paths, verifier warning, failure/last completed step, and retained sandbox path on failure. Do not create a generic redaction service.

- [ ] **Step 2: Add failing artifact verifier tests.**

Cases: raw path outside configured OBS root, missing/empty file, destination collision, successful copy preserving raw, ffprobe absent warning, ffprobe output without both video+audio failure.

Containment lives here only:

```csharp
var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(obsRoot));
var raw = Path.GetFullPath(rawPath);
var relative = Path.GetRelativePath(root, raw);
var escapes = Path.IsPathRooted(relative)
    || relative == ".."
    || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
```

- [ ] **Step 3: Implement optional local `ffprobe`.**

Locate `ffprobe` on PATH. If absent, return warning. If present, run bounded by `ExternalIoTimeout`:

```text
ffprobe -v error -show_entries stream=codec_type -of json <raw-file>
```

Parse JSON and require at least one `video` and one `audio` codec_type. Keep this `ProcessStartInfo` private to the verifier; no generic subprocess/FFMpegCore layer.

- [ ] **Step 4: Integrate finalization.**

Normal: stop OBS -> verify/publish -> mark Completed -> write diagnostics -> delete sandbox.

Failure/cancellation: ownership cleanup -> write failure/cleanup evidence -> retain sandbox. Diagnostics failure is secondary and does not mask primary failure.

- [ ] **Step 5: Verify and commit.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj
git add DTXMania.VideoRecorder/Diagnostics DTXMania.VideoRecorder/Media \
  DTXMania.VideoRecorder.Tests/Diagnostics DTXMania.VideoRecorder.Tests/Media \
  DTXMania.VideoRecorder/Workflow/RecordWorkflow.cs DTXMania.VideoRecorder/Program.cs
git commit -m "feat: finalize recorder artifacts"
```

---

### Task 5: CI, portable verification, and HPA-513 handoff

**Files:** `.github/workflows/build-and-test.yml`.

- [ ] **Step 1: Add recorder tests after Automation tests in both normal OS jobs.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --configuration Debug --verbosity normal --logger trx \
  --results-directory ./TestResults/VideoRecorder
```

No OBS CI.

- [ ] **Step 2: Run portable verification.**

```bash
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj
```

- [ ] **Step 3: Record the native gate status honestly.**

Run `doctor`. When the dedicated Windows OBS environment is available, confirm auth/status/raw-output-dir, OBS inactive, manual profile requirements, and ffprobe status. Otherwise record the Windows/OBS gates as unverified; do not add a fake server, hosted OBS CI, scene/source introspection, or false proof artifact.

- [ ] **Step 4: Hand off native acceptance to HPA-513.**

HPA-513 owns the first native proof:

```text
dtx-video record --chart <absolute-dtx-path> --output <proof-output-directory>
```

It must retain populated Song Select before prepare, >=10s Playing preview, SongTransition, valid Performance, complete SongComplete Result, 5s hold, raw/published MP4, Completed run.json, and source Config.ini/database/WAL isolation evidence. HPA-503 does not require those live artifacts to complete.

- [ ] **Step 5: Final scope review.**

Reject any accidental persistent recorder cache, OBS scene/source screenshot API, FFMpegCore/MMTools recorder dependency, ChangeStage navigation, new Automation stage/library helper, batch/framework abstraction, or live DB copying.

- [ ] **Step 6: Commit and prepare implementation PR.**

```bash
git add .github/workflows/build-and-test.yml
git commit -m "test: validate recorder vertical slice"
```

PR description: HPA-503 link, automated results, doctor/native-gate status without secrets, and explicit HPA-513 first-proof/final-acceptance follow-up.

---

## Definition of done

- record enters Title -> 50ms GAME START -> populated Song Select before one-shot prepare;
- unique sandbox never touches/copies live DB state and accepts only current normalized config semantics;
- preview uses CX Playing + elapsed>=10s; SongTransition/Performance/Result are observed and validated in order;
- only owned CX/OBS resources are stopped, including Ctrl+C/failure paths;
- verifier alone owns raw-path containment, optional ffprobe, collision, and copy-publish behavior;
- diagnostics are one run.json plus CX logs and contain no Config.ini/secrets;
- recorder tests run on both OS CI jobs;
- portable recorder, Automation, macOS-safe game, and macOS/Windows-target build verification passes;
- HPA-513 owns the successful native Windows doctor, first real recording, MP4/diagnostics evidence, live-data isolation proof, and final visual/audio acceptance.
