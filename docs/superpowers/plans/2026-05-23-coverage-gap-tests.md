# Coverage Gap Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raise patch coverage for the recent font/stage refactor by adding targeted unit tests for the uncovered branches called out by Codecov.

**Architecture:** Extend the existing xUnit coverage suites instead of introducing new harnesses. Reuse the repository’s reflection helpers, uninitialized MonoGame objects, and Moq-backed interfaces to drive isolated branch coverage without changing production code unless a failing test proves a tiny seam is required.

**Tech Stack:** C# 12, .NET 8, xUnit, Moq, MonoGame test doubles, `ReflectionHelpers`

---

## File map

- `DTXMania.Test/Config/KeyAssignPanelAdditionalCoverageTests.cs` — key-assign browsing/capture branches that are currently only partially covered.
- `DTXMania.Test/Stage/ConfigStageCoverageTests.cs` — drawing fallbacks and non-happy-path stage behavior that does not require a live graphics device.
- `DTXMania.Test/Stage/ResultStageCoverageTests.cs` — result text rendering and activation null-guard branches.
- `DTXMania.Test/Stage/Performance/PerformanceStageAdditionalCoverageTests.cs` — centered text fallback/rendering and `StartSong` guard branches.
- `DTXMania.Test/Stage/SongSelectionStageInputCoverageTests.cs` — filtered activation, status-panel entry, BGM cleanup, and breadcrumb formatting branches.
- `DTXMania.Test/Resources/ManagedFontLogicTests.cs` — low-cost font guard/sanitization branches.
- `DTXMania.Test/Stage/StartupStageLogicTests.cs` — startup exception/progress branches with existing controllable test subclasses.

No production files should change in the first pass. If any new test exposes a real bug, keep the production fix minimal and local to the file under test.

### Task 1: Cover ConfigStage and key-assign panel navigation branches

**Files:**
- Modify: `DTXMania.Test/Config/KeyAssignPanelAdditionalCoverageTests.cs`
- Modify: `DTXMania.Test/Stage/ConfigStageCoverageTests.cs`
- Test: `DTXMania.Test/Config/KeyAssignPanelAdditionalCoverageTests.cs`
- Test: `DTXMania.Test/Stage/ConfigStageCoverageTests.cs`

- [ ] **Step 1: Write the failing tests**

Add these tests to `DTXMania.Test/Config/KeyAssignPanelAdditionalCoverageTests.cs`:

```csharp
[Fact]
public void DrumPanel_Update_WhenMoveLeftCommandPressed_ShouldClearBinding()
{
    var liveBindings = new KeyBindings();
    liveBindings.BindButton("Key.A", 0);
    var panel = new DrumKeyAssignPanel(CreateUnusedModularInputManager(liveBindings));
    panel._liveSystemMappingProvider = () => new Dictionary<Keys, InputCommandType>();
    panel._navigationMappingProvider = () => new Dictionary<Keys, InputCommandType>
    {
        [Keys.Q] = InputCommandType.MoveLeft
    };

    panel.Activate();
    Assert.True(panel.GetWorkingBindingsSnapshot().ButtonToLane.ContainsKey("Key.A"));

    panel.Update(0.0, new KeyboardState(Keys.Q), new KeyboardState());

    Assert.Equal(-1, panel.GetWorkingBindingsSnapshot().GetLane("Key.A"));
}

[Fact]
public void DrumPanel_Update_WhenBackCommandPressedWhileAwaitingKey_ShouldReturnToBrowsing()
{
    var panel = CreateDrumPanel();
    panel._navigationMappingProvider = () => new Dictionary<Keys, InputCommandType>
    {
        [Keys.Q] = InputCommandType.Back
    };

    panel.Activate();
    PressKey(panel, Keys.Enter);
    Assert.Equal("AwaitingKey", GetStateName(panel));

    panel.Update(0.0, new KeyboardState(Keys.Q), new KeyboardState());

    Assert.Equal("Browsing", GetStateName(panel));
}

[Fact]
public void SystemPanel_Update_WhenMoveLeftCommandPressed_ShouldUnbindOptionalAction()
{
    using var inputManager = new InputManager();
    var panel = new SystemKeyAssignPanel(inputManager);
    panel._liveDrumBindingsProvider = () => new Dictionary<string, int>();
    panel._navigationMappingProvider = () => new Dictionary<Keys, InputCommandType>
    {
        [Keys.Q] = InputCommandType.MoveLeft
    };

    panel.Activate();

    for (int i = 0; i < 6; i++)
        PressKey(panel, Keys.Down);

    var before = panel.GetWorkingMappingSnapshot();
    Assert.True(before.ContainsKey(Keys.PageUp));

    panel.Update(0.0, new KeyboardState(Keys.Q), new KeyboardState());

    var after = panel.GetWorkingMappingSnapshot();
    Assert.False(after.ContainsKey(Keys.PageUp));
}
```

Add these tests to `DTXMania.Test/Stage/ConfigStageCoverageTests.cs`:

```csharp
[Fact]
public void DrawConfigItems_WhenFontIsNull_ShouldUseRectangleFallbackWithoutThrowing()
{
    var (stage, _, inputManager) = CreateStage();
    using (inputManager)
    {
        InitializeStageMenu(stage, includePanels: false);
        ReflectionHelpers.SetPrivateField(stage, "_font", null);
        ReflectionHelpers.SetPrivateField(stage, "_boldFont", null);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", CreateUninitializedSpriteBatch());
        ReflectionHelpers.SetPrivateField(stage, "_whitePixel", (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D)));

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawConfigItems"));

        Assert.Null(exception);
    }
}

[Fact]
public void DrawButtons_WhenFontIsNull_ShouldUseRectangleFallbackWithoutThrowing()
{
    var (stage, _, inputManager) = CreateStage();
    using (inputManager)
    {
        InitializeStageMenu(stage, includePanels: false);
        ReflectionHelpers.SetPrivateField(stage, "_font", null);
        ReflectionHelpers.SetPrivateField(stage, "_boldFont", null);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", CreateUninitializedSpriteBatch());
        ReflectionHelpers.SetPrivateField(stage, "_whitePixel", (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D)));

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawButtons"));

        Assert.Null(exception);
    }
}

[Fact]
public void DrawInstructions_WhenFontIsNull_ShouldUseRectangleFallbackWithoutThrowing()
{
    var (stage, _, inputManager) = CreateStage();
    using (inputManager)
    {
        ReflectionHelpers.SetPrivateField(stage, "_font", null);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", CreateUninitializedSpriteBatch());
        ReflectionHelpers.SetPrivateField(stage, "_whitePixel", (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D)));

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawInstructions"));

        Assert.Null(exception);
    }
}
```

- [ ] **Step 2: Run the new tests to see the red/green state**

Run:

```bash
dotnet test /Users/chanwaichan/workspace/DTXmaniaCX/DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~KeyAssignPanelAdditionalCoverageTests|FullyQualifiedName~ConfigStageCoverageTests"
```

Expected: if any uncovered branch is still buggy, the new tests fail; if the implementation already satisfies them, they pass immediately and no production change is required for this task.

- [ ] **Step 3: Make the minimal code change only if a test exposes a real defect**

If a test fails because the branch is genuinely wrong, limit the fix to the production file under test. The intended code paths are:

```csharp
// DrumKeyAssignPanel.cs
else if (IsNavigationCommandPressed(current, previous, InputCommandType.Back))
    CancelAndClose();
else if (IsClearBindingPressed(current, previous) && _selectedIndex < LaneCount)
    _workingBindings.UnbindKeyboardButtonsForLane(_selectedIndex);

// SystemKeyAssignPanel.cs
else if (IsNavigationCommandPressed(current, previous, InputCommandType.Back))
    CancelAndClose();
else if (IsUnbindPressed(current, previous) && _selectedIndex < ActionCount)
    TryUnbindSelectedAction();

// ConfigStage.cs
if (_font != null)
{
    var textColor = isSelected ? Color.Yellow : Color.White;
    var font = isSelected ? _boldFont : _font;
    font.DrawString(_spriteBatch, displayText, new Vector2(x, y + 10), textColor);
}
else
{
    DrawTextRect(x, y + 10, displayText.Length * 8, 16, isSelected ? Color.Yellow : Color.White);
}
```

- [ ] **Step 4: Re-run the task tests until green**

Run:

```bash
dotnet test /Users/chanwaichan/workspace/DTXmaniaCX/DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~KeyAssignPanelAdditionalCoverageTests|FullyQualifiedName~ConfigStageCoverageTests"
```

Expected: PASS for all tests added in this task.

- [ ] **Step 5: Commit**

```bash
git -C /Users/chanwaichan/workspace/DTXmaniaCX add DTXMania.Test/Config/KeyAssignPanelAdditionalCoverageTests.cs DTXMania.Test/Stage/ConfigStageCoverageTests.cs
git -C /Users/chanwaichan/workspace/DTXmaniaCX commit -m "test: cover config stage and key assign branches"
```

### Task 2: Cover ResultStage and PerformanceStage render/fallback branches

**Files:**
- Modify: `DTXMania.Test/Stage/ResultStageCoverageTests.cs`
- Modify: `DTXMania.Test/Stage/ResultStageAdditionalCoverageTests.cs`
- Modify: `DTXMania.Test/Stage/Performance/PerformanceStageAdditionalCoverageTests.cs`
- Test: `DTXMania.Test/Stage/ResultStageCoverageTests.cs`
- Test: `DTXMania.Test/Stage/ResultStageAdditionalCoverageTests.cs`
- Test: `DTXMania.Test/Stage/Performance/PerformanceStageAdditionalCoverageTests.cs`

- [ ] **Step 1: Write the failing tests**

Add these tests to `DTXMania.Test/Stage/ResultStageCoverageTests.cs`:

```csharp
[Fact]
public void DrawResultLine_WithFont_ShouldMeasureAndDrawCenteredText()
{
#pragma warning disable SYSLIB0050
    var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
    var spriteBatch = CreateFakeSpriteBatch(800, 600);
    var font = new Mock<IFont>();
    font.Setup(f => f.MeasureString("CLEARED")).Returns(new Vector2(120, 20));

    SetPrivateField(stage, "_spriteBatch", spriteBatch);
    SetPrivateField(stage, "_resultFont", font.Object);

    object[] args = ["CLEARED", 400, 100, Color.Green, 40];
    InvokePrivateMethod(stage, "DrawResultLine", args);

    font.Verify(f => f.MeasureString("CLEARED"), Times.Once);
    font.Verify(f => f.DrawString(spriteBatch, "CLEARED", new Vector2(340, 100), Color.Green), Times.Once);
    Assert.Equal(140, args[2]);
}
```

Add this test to `DTXMania.Test/Stage/ResultStageAdditionalCoverageTests.cs`:

```csharp
[Fact]
public void OnActivate_WhenInputManagerIsNull_ShouldInitializeComponentsWithoutThrowing()
{
#pragma warning disable SYSLIB0050
    var stage = (InspectableResultStage)FormatterServices.GetUninitializedObject(typeof(InspectableResultStage));
#pragma warning restore SYSLIB0050
    SetPrivateField(stage, "_inputManager", null);
    SetPrivateField(stage, "_sharedData", new Dictionary<string, object>
    {
        ["performanceSummary"] = new PerformanceSummary { Score = 123456 }
    });

    var exception = Record.Exception(() => InvokePrivateMethod(stage, "OnActivate"));

    Assert.Null(exception);
    Assert.True(stage.WhitePixelRequested);
    Assert.True(stage.ResultFontRequested);
}

private sealed class InspectableResultStage : ResultStage
{
    public InspectableResultStage(BaseGame game) : base(game) { }

    public bool WhitePixelRequested { get; private set; }
    public bool ResultFontRequested { get; private set; }

    internal override Texture2D CreateWhitePixel()
    {
        WhitePixelRequested = true;
        return null!;
    }

    internal override IFont CreateResultFont()
    {
        ResultFontRequested = true;
        return null!;
    }
}
```

Add these tests to `DTXMania.Test/Stage/Performance/PerformanceStageAdditionalCoverageTests.cs`:

```csharp
[Fact]
public void DrawCenteredText_WithReadyFont_ShouldMeasureAndDrawAtCenteredPosition()
{
    var stage = CreateStage();
    var spriteBatch = (SpriteBatch)FormatterServices.GetUninitializedObject(typeof(SpriteBatch));
    GC.SuppressFinalize(spriteBatch);
    var font = new Mock<IFont>();
    font.Setup(f => f.MeasureString("READY")).Returns(new Vector2(100, 20));

    ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", spriteBatch);
    ReflectionHelpers.SetPrivateField(stage, "_readyFont", font.Object);

    ReflectionHelpers.InvokePrivateMethod(stage, "DrawCenteredText", "READY", Color.White);

    font.Verify(f => f.DrawString(
        spriteBatch,
        "READY",
        new Vector2((PerformanceUILayout.ScreenWidth / 2) - 50, (PerformanceUILayout.ScreenHeight / 2) - 10),
        Color.White,
        0f,
        Vector2.Zero,
        Vector2.One,
        SpriteEffects.None,
        0.1f), Times.Once);
}

[Fact]
public void DrawCenteredText_WithoutReadyFont_ShouldDrawFallbackRectangle()
{
    var stage = CreateStage();
    Rectangle? capturedRect = null;
    Color? capturedColor = null;
    float? capturedDepth = null;

    ReflectionHelpers.SetPrivateField(stage, "_readyFont", null);
    ReflectionHelpers.SetPrivateField(stage, "_fallbackRectangleDrawer",
        (Action<Rectangle, Color, float>)((rect, color, depth) =>
        {
            capturedRect = rect;
            capturedColor = color;
            capturedDepth = depth;
        }));

    ReflectionHelpers.InvokePrivateMethod(stage, "DrawCenteredText", "READY", Color.Cyan);

    Assert.NotNull(capturedRect);
    Assert.Equal(Color.Cyan, capturedColor);
    Assert.Equal(0.1f, capturedDepth);
}

[Fact]
public void StartSong_WhenCurrentGameTimeIsNull_ShouldLeaveReadyStateUnchanged()
{
    var stage = CreateStage();
    var timer = (SongTimer)FormatterServices.GetUninitializedObject(typeof(SongTimer));

    ReflectionHelpers.SetPrivateField(stage, "_songTimer", timer);
    ReflectionHelpers.SetPrivateField(stage, "_currentGameTime", null);
    ReflectionHelpers.SetPrivateField(stage, "_isReady", true);

    ReflectionHelpers.InvokePrivateMethod(stage, "StartSong");

    Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_isReady"));
}
```

- [ ] **Step 2: Run the new tests to see the red/green state**

Run:

```bash
dotnet test /Users/chanwaichan/workspace/DTXmaniaCX/DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ResultStageCoverageTests|FullyQualifiedName~ResultStageAdditionalCoverageTests|FullyQualifiedName~PerformanceStageAdditionalCoverageTests"
```

Expected: new tests fail only if a branch is actually broken; otherwise they go green immediately and validate existing behavior.

- [ ] **Step 3: Make the minimal production fix only if a test reveals a defect**

If any test fails, keep the implementation aligned with these paths:

```csharp
// ResultStage.cs
if (_resultFont != null)
{
    var textSize = _resultFont.MeasureString(text);
    var textPosition = new Vector2(centerX - textSize.X / 2, currentY);
    _resultFont.DrawString(_spriteBatch, text, textPosition, color);
}

// PerformanceStage.cs
if (_readyFont != null)
{
    var textSize = _readyFont.MeasureString(text);
    var textX = (int)(screenCenter.X - textSize.X / 2);
    var textY = (int)(screenCenter.Y - textSize.Y / 2);
    _readyFont.DrawString(_spriteBatch, text, new Vector2(textX, textY), color,
        rotation: 0f, origin: Vector2.Zero, scale: Vector2.One,
        effects: SpriteEffects.None, layerDepth: 0.1f);
}
else
{
    DrawFallbackRectangle(rectPosition, color, 0.1f);
}
```

- [ ] **Step 4: Re-run the task tests until green**

Run:

```bash
dotnet test /Users/chanwaichan/workspace/DTXmaniaCX/DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ResultStageCoverageTests|FullyQualifiedName~ResultStageAdditionalCoverageTests|FullyQualifiedName~PerformanceStageAdditionalCoverageTests"
```

Expected: PASS for all tests added in this task.

- [ ] **Step 5: Commit**

```bash
git -C /Users/chanwaichan/workspace/DTXmaniaCX add DTXMania.Test/Stage/ResultStageCoverageTests.cs DTXMania.Test/Stage/ResultStageAdditionalCoverageTests.cs DTXMania.Test/Stage/Performance/PerformanceStageAdditionalCoverageTests.cs
git -C /Users/chanwaichan/workspace/DTXmaniaCX commit -m "test: cover result and performance fallback branches"
```

### Task 3: Cover SongSelectionStage activation, cleanup, and breadcrumb formatting

**Files:**
- Modify: `DTXMania.Test/Stage/SongSelectionStageInputCoverageTests.cs`
- Modify: `DTXMania.Test/Stage/SongSelectionStageBasicTests.cs` (only if existing `SetBackgroundMusic` helpers are needed)
- Test: `DTXMania.Test/Stage/SongSelectionStageInputCoverageTests.cs`

- [ ] **Step 1: Write the failing tests**

Add these tests to `DTXMania.Test/Stage/SongSelectionStageInputCoverageTests.cs`:

```csharp
[Fact]
public void HandleActivateInput_WhenFilteredViewContainsNonScoreSelection_ShouldReturnWithoutEnteringStatusPanel()
{
    var stage = CreateStage();
    var display = new SongListDisplay();
    var selectedNode = new SongListNode { Title = "Folder", Type = NodeType.Box };
    SetProperty(display, nameof(SongListDisplay.SelectedSong), selectedNode);

    AttachCoreUi(stage, display: display);
    SetPrivateField(stage, "_selectedSong", selectedNode);
    SetPrivateField(stage, "_filteredView", new List<FilteredSongResult> { new(selectedNode, "box") });
    SetPrivateField(stage, "_isInStatusPanel", false);

    InvokePrivateMethod(stage, "HandleActivateInput");

    Assert.False(GetPrivateField<bool>(stage, "_isInStatusPanel"));
}

[Fact]
public void HandleActivateInput_WhenSelectedSongIsScore_ShouldEnterStatusPanel()
{
    var stage = CreateStage();
    var display = new SongListDisplay();
    var statusPanel = new SongStatusPanel { Visible = false };
    var selectedSong = CreateScoreNode("Song");

    AttachCoreUi(stage, display: display, statusPanel: statusPanel);
    SetPrivateField(stage, "_selectedSong", selectedSong);
    SetPrivateField(stage, "_filteredView", null);
    SetPrivateField(stage, "_isInStatusPanel", false);

    InvokePrivateMethod(stage, "HandleActivateInput");

    Assert.True(GetPrivateField<bool>(stage, "_isInStatusPanel"));
    Assert.True(statusPanel.Visible);
}

[Fact]
public void SetBackgroundMusic_WhenPreviousCleanupThrows_ShouldReplaceReferencesWithoutThrowing()
{
    var stage = CreateStage();
    var previousSound = new Mock<ISound>();
    previousSound.Setup(s => s.RemoveReference()).Throws(new InvalidOperationException("remove"));
    var previousInstance = new Mock<ISoundInstance>();
    previousInstance.Setup(i => i.Stop()).Throws(new InvalidOperationException("stop"));
    previousInstance.Setup(i => i.Dispose()).Throws(new InvalidOperationException("dispose"));

    var nextSound = new Mock<ISound>();
    var nextInstance = new Mock<ISoundInstance>();

    SetPrivateField(stage, "_backgroundMusic", previousSound.Object);
    SetPrivateField(stage, "_backgroundMusicInstance", previousInstance.Object);

    var exception = Record.Exception(() => stage.SetBackgroundMusic(nextSound.Object, nextInstance.Object));

    Assert.Null(exception);
    Assert.Same(nextSound.Object, GetPrivateField<ISound>(stage, "_backgroundMusic"));
    Assert.Same(nextInstance.Object, GetPrivateField<ISoundInstance>(stage, "_backgroundMusicInstance"));
}

[Fact]
public void LoadNavigationSound_WhenNowLoadingFails_ShouldFallbackToDecide()
{
    var cursorSound = new Mock<ISound>().Object;
    var decideSound = new Mock<ISound>().Object;
    var resourceManager = new Mock<IResourceManager>();
    resourceManager.Setup(r => r.LoadSound("Sounds/Move.ogg")).Returns(cursorSound);
    resourceManager.Setup(r => r.LoadSound("Sounds/Now loading.ogg")).Throws(new InvalidOperationException("missing"));
    resourceManager.Setup(r => r.LoadSound("Sounds/Decide.ogg")).Returns(decideSound);

    var stage = CreateStage();
    SetPrivateField(stage, "_resourceManager", resourceManager.Object);

    InvokePrivateMethod(stage, "LoadNavigationSound");

    Assert.Same(cursorSound, GetPrivateField<ISound>(stage, "_cursorMoveSound"));
    Assert.Same(decideSound, GetPrivateField<ISound>(stage, "_gameStartSound"));
}

[Theory]
[InlineData(20, null, "Lv 20+")]
[InlineData(null, 80, "Lv <=80")]
public void FormatLevel_WithSingleBound_ShouldFormatSummary(int? min, int? max, string expected)
{
    var method = typeof(SongSelectionStage).GetMethod(
        "FormatLevel",
        BindingFlags.Static | BindingFlags.NonPublic);
    Assert.NotNull(method);

    var result = (string?)method!.Invoke(null, new object?[] { min, max });

    Assert.Equal(expected, result);
}
```

- [ ] **Step 2: Run the new tests to see the red/green state**

Run:

```bash
dotnet test /Users/chanwaichan/workspace/DTXmaniaCX/DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStageInputCoverageTests"
```

Expected: the new tests either expose a real branch bug or pass immediately and lock in the current behavior.

- [ ] **Step 3: Make the minimal production fix only if a test reveals a defect**

If a failure appears, keep the production change aligned with these paths:

```csharp
// SongSelectionStage.cs
if (_filteredView != null && _songListDisplay?.SelectedSong?.Type != NodeType.Score)
{
    return;
}

if (_selectedSong.Type == NodeType.Score)
{
    _isInStatusPanel = true;
    if (_statusPanel != null)
    {
        _statusPanel.Visible = true;
    }
}

if (_backgroundMusic != null)
{
    try
    {
        _backgroundMusic.RemoveReference();
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine(
            $"SongSelectionStage.SetBackgroundMusic: Error releasing previous BGM reference: {ex.Message}");
    }
}
```

- [ ] **Step 4: Re-run the task tests until green**

Run:

```bash
dotnet test /Users/chanwaichan/workspace/DTXmaniaCX/DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStageInputCoverageTests"
```

Expected: PASS for all tests added in this task.

- [ ] **Step 5: Commit**

```bash
git -C /Users/chanwaichan/workspace/DTXmaniaCX add DTXMania.Test/Stage/SongSelectionStageInputCoverageTests.cs
git -C /Users/chanwaichan/workspace/DTXmaniaCX commit -m "test: cover song selection activation branches"
```

### Task 4: Add a second-pass safety net for remaining ManagedFont and StartupStage gaps

**Files:**
- Modify: `DTXMania.Test/Resources/ManagedFontLogicTests.cs`
- Modify: `DTXMania.Test/Stage/StartupStageLogicTests.cs`
- Test: `DTXMania.Test/Resources/ManagedFontLogicTests.cs`
- Test: `DTXMania.Test/Stage/StartupStageLogicTests.cs`

- [ ] **Step 1: Write the failing tests**

Add these tests to `DTXMania.Test/Resources/ManagedFontLogicTests.cs`:

```csharp
[Fact]
public void MeasureStringWrapped_WhenDisposed_ShouldReturnZero()
{
    var spriteFont = CreateSpriteFont([('A', 10), (' ', 8)], lineSpacing: 16);
    var font = new ManagedFont(spriteFont, "DisposedFont", 16);
    ReflectionHelpers.SetPrivateField(font, "_disposed", true);

    var result = font.MeasureStringWrapped("A A", 30f);

    Assert.Equal(Vector2.Zero, result);
}

[Fact]
public void GetCharacterBounds_WhenDisposed_ShouldReturnEmptyArray()
{
    var spriteFont = CreateSpriteFont([('A', 10)], lineSpacing: 16);
    var font = new ManagedFont(spriteFont, "DisposedFont", 16);
    ReflectionHelpers.SetPrivateField(font, "_disposed", true);

    var bounds = font.GetCharacterBounds("AAA");

    Assert.Empty(bounds);
}

[Fact]
public void MeasureString_WhenUnsupportedCharacterRequiresSanitization_ShouldUseFallbackCharacter()
{
    var spriteFont = CreateSpriteFont([('A', 10), ('?', 8)], defaultCharacter: '?');
    var font = new ManagedFont(spriteFont, "SanitizeFont", 16);

    var result = font.MeasureString("A\u2603");

    Assert.Equal(new Vector2(18, 16), result);
}
```

Add these tests to `DTXMania.Test/Stage/StartupStageLogicTests.cs`:

```csharp
[Fact]
public async Task CheckFilesystemChangesAsync_WhenNeedsEnumerationThrows_ShouldMarkEnumerationNeeded()
{
    var stage = new ThrowingNeedsEnumerationStartupStage(ReflectionHelpers.CreateGame());
    ReflectionHelpers.SetPrivateField(stage, "_songPaths", new[] { "SongsRoot" });
    ReflectionHelpers.SetPrivateField(stage, "_cancellationTokenSource", new CancellationTokenSource());

    var task = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "CheckFilesystemChangesAsync")!;
    await task;

    Assert.True(task.IsCompletedSuccessfully);
    Assert.True(ReflectionHelpers.GetPrivateField<bool?>(stage, "_needsEnumeration"));
}

[Fact]
public async Task EnumerateSongsAsync_WhenProgressOmitsFileAndDirectory_ShouldShowProcessedSummary()
{
    var stage = CreateControlledStage(songPaths: new[] { "SongsRoot" });
    stage.ReportedEnumerationProgress = new EnumerationProgress
    {
        ProcessedCount = 4,
        DiscoveredSongs = 3
    };
    ReflectionHelpers.SetPrivateField(stage, "_needsEnumeration", true);

    using var synchronizationContextScope = new SynchronizationContextScope(new ImmediateSynchronizationContext());

    var task = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "EnumerateSongsAsync")!;
    await task;

    Assert.Contains("4 processed", ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage"));
    Assert.Contains("3 songs found", ReflectionHelpers.GetPrivateField<string>(stage, "_currentProgressMessage"));
}

private sealed class ThrowingNeedsEnumerationStartupStage : StartupStage
{
    public ThrowingNeedsEnumerationStartupStage(BaseGame game) : base(game) { }

    protected override Task<bool> NeedsEnumerationCoreAsync(string[] songPaths, bool forceEnumeration)
    {
        throw new InvalidOperationException("boom");
    }
}
```

- [ ] **Step 2: Run the new tests to see the red/green state**

Run:

```bash
dotnet test /Users/chanwaichan/workspace/DTXmaniaCX/DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ManagedFontLogicTests|FullyQualifiedName~StartupStageLogicTests"
```

Expected: the new tests fail only if a real edge-case branch is wrong; otherwise they pass and add low-cost coverage.

- [ ] **Step 3: Make the minimal production fix only if a test reveals a defect**

If any failure appears, keep the implementation aligned with these branches:

```csharp
// ManagedFont.cs
if (_disposed || _spriteFont == null || string.IsNullOrEmpty(text))
    return Vector2.Zero;

if (_disposed || _spriteFont == null || string.IsNullOrEmpty(text))
    return new XnaRectangle[0];

catch (Exception ex)
{
    System.Diagnostics.Debug.WriteLine($"Error during filesystem change detection: {ex.GetType().Name}: {ex.Message}");
    _needsEnumeration = true;
}

else
{
    _currentProgressMessage = $"{phaseInfo.message} [{progress.ProcessedCount} processed, {progress.DiscoveredSongs} songs found]";
}
```

- [ ] **Step 4: Re-run the task tests until green**

Run:

```bash
dotnet test /Users/chanwaichan/workspace/DTXmaniaCX/DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ManagedFontLogicTests|FullyQualifiedName~StartupStageLogicTests"
```

Expected: PASS for all tests added in this task.

- [ ] **Step 5: Commit**

```bash
git -C /Users/chanwaichan/workspace/DTXmaniaCX add DTXMania.Test/Resources/ManagedFontLogicTests.cs DTXMania.Test/Stage/StartupStageLogicTests.cs
git -C /Users/chanwaichan/workspace/DTXmaniaCX commit -m "test: cover managed font and startup edge cases"
```

### Task 5: Verify the full Mac-safe suite and inspect coverage-sensitive files

**Files:**
- Modify: none expected
- Test: `DTXMania.Test/DTXMania.Test.Mac.csproj`

- [ ] **Step 1: Run the targeted files together**

Run:

```bash
dotnet test /Users/chanwaichan/workspace/DTXmaniaCX/DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~KeyAssignPanelAdditionalCoverageTests|FullyQualifiedName~ConfigStageCoverageTests|FullyQualifiedName~ResultStageCoverageTests|FullyQualifiedName~ResultStageAdditionalCoverageTests|FullyQualifiedName~PerformanceStageAdditionalCoverageTests|FullyQualifiedName~SongSelectionStageInputCoverageTests|FullyQualifiedName~ManagedFontLogicTests|FullyQualifiedName~StartupStageLogicTests"
```

Expected: PASS.

- [ ] **Step 2: Run the full Mac-safe test project**

Run:

```bash
dotnet test /Users/chanwaichan/workspace/DTXmaniaCX/DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: PASS.

- [ ] **Step 3: Run coverage collection for the Mac-safe suite**

Run:

```bash
dotnet test /Users/chanwaichan/workspace/DTXmaniaCX/DTXMania.Test/DTXMania.Test.Mac.csproj --collect:"XPlat Code Coverage" --settings /Users/chanwaichan/workspace/DTXmaniaCX/coverlet.runsettings --results-directory /Users/chanwaichan/workspace/DTXmaniaCX/TestResults
```

Expected: PASS and new coverage files under `TestResults/`.

- [ ] **Step 4: Review the changed test files and commit verification**

Run:

```bash
git -C /Users/chanwaichan/workspace/DTXmaniaCX diff -- DTXMania.Test/Config/KeyAssignPanelAdditionalCoverageTests.cs DTXMania.Test/Stage/ConfigStageCoverageTests.cs DTXMania.Test/Stage/ResultStageCoverageTests.cs DTXMania.Test/Stage/ResultStageAdditionalCoverageTests.cs DTXMania.Test/Stage/Performance/PerformanceStageAdditionalCoverageTests.cs DTXMania.Test/Stage/SongSelectionStageInputCoverageTests.cs DTXMania.Test/Resources/ManagedFontLogicTests.cs DTXMania.Test/Stage/StartupStageLogicTests.cs
```

Expected: only the intended test additions, unless a minimal production fix was required by a failing test.

- [ ] **Step 5: Commit the verification-complete state**

```bash
git -C /Users/chanwaichan/workspace/DTXmaniaCX add DTXMania.Test/Config/KeyAssignPanelAdditionalCoverageTests.cs DTXMania.Test/Stage/ConfigStageCoverageTests.cs DTXMania.Test/Stage/ResultStageCoverageTests.cs DTXMania.Test/Stage/ResultStageAdditionalCoverageTests.cs DTXMania.Test/Stage/Performance/PerformanceStageAdditionalCoverageTests.cs DTXMania.Test/Stage/SongSelectionStageInputCoverageTests.cs DTXMania.Test/Resources/ManagedFontLogicTests.cs DTXMania.Test/Stage/StartupStageLogicTests.cs
git -C /Users/chanwaichan/workspace/DTXmaniaCX commit -m "test: close stage and font coverage gaps"
```
