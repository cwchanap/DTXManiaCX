# Song Selection UI Fixes Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix six layout discrepancies (D2–D7) between DTXManiaNX and DTXManiaCX song selection UI by correcting layout constants and two targeted rendering code changes.

**Architecture:** All constant corrections go into `SongSelectionUILayout.cs` (centralized layout system). Two rendering changes go into `SongListDisplay.cs`: artist name uses absolute NX-authentic coordinates, and the selected bar skin texture draws at Y-30. Tests go into the existing `NXLayoutAlignmentTests.cs` and `SongListDisplayLogicTests.cs` files.

**Tech Stack:** C# / MonoGame; xUnit + reflection-based private method testing

---

## Task 1: Fix DifficultyGrid and BPMSection constants (D3, D4, D5)

**Files:**
- Test: `DTXMania.Test/UI/NXLayoutAlignmentTests.cs`
- Modify: `DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs:60-103, 35-51`

### Step 1: Write failing tests

Add to `NXLayoutAlignmentTests.cs` inside the existing `#region Layout Spacing and Animation Constants` block (or create a new `#region BPM and DifficultyGrid` block):

```csharp
#region BPM and Difficulty Grid Alignment (D3, D4, D5)

[Fact]
public void DifficultyGrid_BaseX_ShouldBe130()
{
    Assert.Equal(130, SongSelectionUILayout.DifficultyGrid.BaseX);
}

[Fact]
public void DifficultyGrid_BaseY_ShouldBe400()
{
    // NX uses (391 + (4-i)*60 - 2); BaseY drives the formula, should be 391
    Assert.Equal(391, SongSelectionUILayout.DifficultyGrid.BaseY);
}

[Fact]
public void BPMSection_X_ShouldBe90()
{
    Assert.Equal(90, SongSelectionUILayout.BPMSection.X);
}

[Fact]
public void BPMSection_Y_ShouldBe275()
{
    Assert.Equal(275, SongSelectionUILayout.BPMSection.Y);
}

[Fact]
public void BPMSection_LengthTextPosition_ShouldMatchNX()
{
    // NX: nBPM位置X + 42, nBPM位置Y - 7 = (132, 268)
    var pos = SongSelectionUILayout.BPMSection.LengthTextPosition;
    Assert.Equal(132, (int)pos.X);
    Assert.Equal(268, (int)pos.Y);
}

[Fact]
public void BPMSection_BPMTextPosition_ShouldMatchNX()
{
    // NX: nBPM位置X + 45, nBPM位置Y + 23 = (135, 298)
    var pos = SongSelectionUILayout.BPMSection.BPMTextPosition;
    Assert.Equal(135, (int)pos.X);
    Assert.Equal(298, (int)pos.Y);
}

[Fact]
public void DifficultyGrid_GetCellPosition_DrumsCol_ShouldUseBaseX130()
{
    // instrument=0: nBoxX = 130 + 561 + (187 * (0 - 3)) = 130
    var pos = SongSelectionUILayout.DifficultyGrid.GetCellPosition(4, 0);
    Assert.Equal(130, (int)pos.X);
}

[Fact]
public void DifficultyGrid_GetCellPosition_HighestRow_ShouldUseBaseY391()
{
    // diffLevel=4: nBoxY = 391 + (4-4)*60 - 2 = 389
    var pos = SongSelectionUILayout.DifficultyGrid.GetCellPosition(4, 0);
    Assert.Equal(389, (int)pos.Y);
}

#endregion
```

### Step 2: Run tests to confirm they fail

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "NXLayoutAlignmentTests" -v minimal
```

Expected: 8 failures (DifficultyGrid.BaseX=150 not 130, BaseY=400 not 391, BPMSection.X=32 not 90, etc.)

### Step 3: Fix constants in `SongSelectionUILayout.cs`

In `BPMSection` (lines 35–51):

```csharp
public const int X = 90;   // NX: nBPM位置X = 90
public const int Y = 275;  // NX: nBPM位置Y = 275
// Remove LineSpacing and TextXOffset; replace text position properties:
public static Vector2 LengthTextPosition => new Vector2(X + 42, Y - 7);  // NX: (+42, -7)
public static Vector2 BPMTextPosition => new Vector2(X + 45, Y + 23);    // NX: (+45, +23)
```

Remove unused constants `LineSpacing` and `TextXOffset` from `BPMSection` (they are no longer referenced).

In `DifficultyGrid` (lines 60–103):

```csharp
public const int BaseX = 130;  // NX: nBaseX = 130
public const int BaseY = 391;  // NX: nBoxY = 391 + (4-i)*60 - 2
```

No other changes needed in `GetCellPosition`/`GetCellContentPosition` — the formulas are already correct.

### Step 4: Run tests to confirm they pass

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "NXLayoutAlignmentTests" -v minimal
```

Expected: all pass (including the 8 new ones and all previously passing ones)

### Step 5: Commit

```bash
git add DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs DTXMania.Test/UI/NXLayoutAlignmentTests.cs
git commit -m "fix: correct DifficultyGrid and BPMSection layout constants to match NX (D3, D4, D5)"
```

---

## Task 2: Fix NoteDistributionBars constants (D7)

**Files:**
- Test: `DTXMania.Test/UI/NXLayoutAlignmentTests.cs`
- Modify: `DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs:168-207`

### Step 1: Write failing tests

Add inside a new `#region Note Distribution Bars (D7)` block in `NXLayoutAlignmentTests.cs`:

```csharp
#region Note Distribution Bars (D7)

[Fact]
public void NoteDistributionBars_Drums_StartX_ShouldBe46()
{
    // NX: nGraphBaseX(15) + 31 = 46
    Assert.Equal(46, SongSelectionUILayout.NoteDistributionBars.Drums.StartX);
}

[Fact]
public void NoteDistributionBars_Drums_GetBarPosition_Lane0_ShouldStartAt46()
{
    var pos = SongSelectionUILayout.NoteDistributionBars.Drums.GetBarPosition(0);
    Assert.Equal(46, (int)pos.X);
}

[Fact]
public void NoteDistributionBars_Drums_GetBarPosition_Lane1_ShouldBeAt54()
{
    // 46 + 1 * (4 + 4) = 54
    var pos = SongSelectionUILayout.NoteDistributionBars.Drums.GetBarPosition(1);
    Assert.Equal(54, (int)pos.X);
}

[Fact]
public void NoteDistributionBars_GuitarBass_BarSpacing_ShouldBe6()
{
    // NX interval = 10; BarWidth=4; BarSpacing = 10 - 4 = 6
    Assert.Equal(6, SongSelectionUILayout.NoteDistributionBars.GuitarBass.BarSpacing);
}

[Fact]
public void NoteDistributionBars_GuitarBass_GetBarPosition_Lane1_ShouldBeAt63()
{
    // StartX=53, interval=BarWidth(4)+BarSpacing(6)=10; lane1 = 53+10 = 63
    var pos = SongSelectionUILayout.NoteDistributionBars.GuitarBass.GetBarPosition(1);
    Assert.Equal(63, (int)pos.X);
}

#endregion
```

### Step 2: Run tests to confirm they fail

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "NXLayoutAlignmentTests" -v minimal
```

Expected: 5 new failures.

### Step 3: Fix constants in `SongSelectionUILayout.cs`

In `NoteDistributionBars.Drums`:

```csharp
public const int StartX = 46;  // NX: nGraphBaseX(15) + 31 = 46
```

In `NoteDistributionBars.GuitarBass`:

```csharp
public const int BarSpacing = 6;  // NX interval=10, BarWidth=4, so spacing=6
```

### Step 4: Run tests to confirm they pass

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "NXLayoutAlignmentTests" -v minimal
```

Expected: all pass.

### Step 5: Commit

```bash
git add DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs DTXMania.Test/UI/NXLayoutAlignmentTests.cs
git commit -m "fix: correct NoteDistributionBars StartX and GuitarBass BarSpacing to match NX (D7)"
```

---

## Task 3: Add artist name absolute position constants (D2 preparation)

**Files:**
- Test: `DTXMania.Test/UI/NXLayoutAlignmentTests.cs`
- Modify: `DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs` (SongBars section, lines 217–312)

Also add `SelectedBarTextureYOffset` constant here for D6.

### Step 1: Write failing tests

Add inside a new `#region Artist Name and Selected Bar (D2, D6)` block in `NXLayoutAlignmentTests.cs`:

```csharp
#region Artist Name and Selected Bar (D2, D6)

[Fact]
public void SongBars_ArtistNameAbsoluteRightEdge_ShouldBe1235()
{
    // NX: 1260 - 25 = 1235 (right-aligned artist name edge)
    Assert.Equal(1235, SongSelectionUILayout.SongBars.ArtistNameAbsoluteRightEdge);
}

[Fact]
public void SongBars_ArtistNameAbsoluteY_ShouldBe320()
{
    // NX: y = 320 (absolute Y for artist name)
    Assert.Equal(320, SongSelectionUILayout.SongBars.ArtistNameAbsoluteY);
}

[Fact]
public void SongBars_SelectedBarTextureYOffset_ShouldBeMinus30()
{
    // NX: bar texture drawn at y - 30; title/lamp stay at y
    Assert.Equal(-30, SongSelectionUILayout.SongBars.SelectedBarTextureYOffset);
}

#endregion
```

### Step 2: Run tests to confirm they fail

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "NXLayoutAlignmentTests" -v minimal
```

Expected: 3 new failures (constants don't exist yet).

### Step 3: Add constants to `SongSelectionUILayout.cs`

In the `SongBars` class, replace the existing artist name comment block (lines 265–268):

```csharp
// Artist name display layout (absolute NX-authentic coordinates)
public const int ArtistNameAbsoluteRightEdge = 1235; // NX: 1260 - 25 (right-aligned)
public const int ArtistNameAbsoluteY = 320;           // NX: y = 320 (absolute)

// Selected bar skin texture vertical offset
public const int SelectedBarTextureYOffset = -30;     // NX: bar texture at y - 30, title stays at y
```

Remove the now-replaced old constants `ArtistNameRightMargin`, `ArtistNameLeftPadding`, `ArtistNameMaxWidth` if they are not referenced elsewhere (search with grep first).

### Step 4: Run tests to confirm they pass

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "NXLayoutAlignmentTests" -v minimal
```

Expected: all pass.

### Step 5: Commit

```bash
git add DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs DTXMania.Test/UI/NXLayoutAlignmentTests.cs
git commit -m "feat: add ArtistNameAbsolute and SelectedBarTextureYOffset layout constants (D2, D6)"
```

---

## Task 4: Fix artist name rendering in SongListDisplay.cs (D2)

**Files:**
- Test: `DTXMania.Test/UI/SongListDisplayLogicTests.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/SongListDisplay.cs:1066-1141`

### Step 1: Write a logic test for artist name position calculation

The fix requires extracting the position calculation into a pure helper so it is testable without a graphics device. Add to `SongListDisplayLogicTests.cs`:

```csharp
[Fact]
public void CalculateArtistNamePosition_ShouldUseAbsoluteNXCoordinates()
{
    var display = new SongListDisplay();
    float textWidth = 150f;

    var pos = InvokePrivate<Vector2>(display, "CalculateArtistNamePosition", textWidth);

    // NX: x = 1260 - 25 - textWidth = 1235 - textWidth
    Assert.Equal(SongSelectionUILayout.SongBars.ArtistNameAbsoluteRightEdge - textWidth, pos.X);
    Assert.Equal(SongSelectionUILayout.SongBars.ArtistNameAbsoluteY, (int)pos.Y);
}

[Fact]
public void CalculateArtistNamePosition_WhenTextWider_ShouldStillUseAbsoluteEdge()
{
    var display = new SongListDisplay();
    float textWidth = 400f;

    var pos = InvokePrivate<Vector2>(display, "CalculateArtistNamePosition", textWidth);

    Assert.Equal(SongSelectionUILayout.SongBars.ArtistNameAbsoluteRightEdge - textWidth, pos.X);
}
```

### Step 2: Run test to confirm it fails

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "SongListDisplayLogicTests" -v minimal
```

Expected: `CalculateArtistNamePosition_*` fail with "Method not found".

### Step 3: Extract helper and fix rendering in `SongListDisplay.cs`

Add a private helper method (place near the DrawArtistName methods):

```csharp
private static Vector2 CalculateArtistNamePosition(float textWidth)
{
    return new Vector2(
        SongSelectionUILayout.SongBars.ArtistNameAbsoluteRightEdge - textWidth,
        SongSelectionUILayout.SongBars.ArtistNameAbsoluteY);
}
```

In `DrawArtistName()` (around line 1066), replace the position calculation:

```csharp
// Before:
var artistX = itemBounds.Right - 10 - textWidth;
var artistY = itemBounds.Bottom + 8;

// After:
var artistPos = CalculateArtistNamePosition(textWidth);
var artistX = artistPos.X;
var artistY = artistPos.Y;
```

In `DrawArtistNameWithManagedFont()` (around line 1107), apply the same replacement using `CalculateArtistNamePosition(textWidth)`.

### Step 4: Run tests to confirm they pass

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "SongListDisplayLogicTests" -v minimal
```

Expected: all pass.

### Step 5: Commit

```bash
git add DTXMania.Game/Lib/Song/Components/SongListDisplay.cs DTXMania.Test/UI/SongListDisplayLogicTests.cs
git commit -m "fix: use absolute NX-authentic coordinates for artist name in song bars (D2)"
```

---

## Task 5: Fix selected bar texture Y offset in SongListDisplay.cs (D6)

**Files:**
- Test: `DTXMania.Test/UI/SongListDisplayLogicTests.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/SongListDisplay.cs:878-953`

### Step 1: Write a logic test

Extract and test the helper for bar texture bounds calculation. Add to `SongListDisplayLogicTests.cs`:

```csharp
[Fact]
public void CalculateBarTextureBounds_WhenSelected_ShouldApplyYOffset()
{
    var display = new SongListDisplay();
    var itemBounds = new Rectangle(665, 269, 510, 48);

    var textureBounds = InvokePrivate<Rectangle>(display, "CalculateBarTextureBounds", itemBounds, true);

    // NX: bar texture drawn at itemBounds.Y + SelectedBarTextureYOffset (-30) = 239
    Assert.Equal(itemBounds.Y + SongSelectionUILayout.SongBars.SelectedBarTextureYOffset, textureBounds.Y);
    Assert.Equal(itemBounds.X, textureBounds.X);
    Assert.Equal(itemBounds.Width, textureBounds.Width);
}

[Fact]
public void CalculateBarTextureBounds_WhenNotSelected_ShouldUseOriginalY()
{
    var display = new SongListDisplay();
    var itemBounds = new Rectangle(673, 100, 510, 48);

    var textureBounds = InvokePrivate<Rectangle>(display, "CalculateBarTextureBounds", itemBounds, false);

    Assert.Equal(itemBounds.Y, textureBounds.Y);
}
```

### Step 2: Run test to confirm it fails

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "SongListDisplayLogicTests" -v minimal
```

Expected: `CalculateBarTextureBounds_*` fail with "Method not found".

### Step 3: Extract helper and fix rendering in `SongListDisplay.cs`

Add a private helper method (place near `DrawBarInfoWithPerspective`):

```csharp
private static Rectangle CalculateBarTextureBounds(Rectangle itemBounds, bool isSelected)
{
    int yOffset = isSelected ? SongSelectionUILayout.SongBars.SelectedBarTextureYOffset : 0;
    return new Rectangle(itemBounds.X, itemBounds.Y + yOffset, itemBounds.Width, itemBounds.Height);
}
```

In `DrawBarInfoWithPerspective()` (around lines 878–953), find where the skin bar texture rectangle is constructed. It will look like:

```csharp
var barRect = new Rectangle(itemBounds.X, itemBounds.Y, itemBounds.Width, itemBounds.Height);
```

Replace it with:

```csharp
var barRect = CalculateBarTextureBounds(itemBounds, isCenter);
```

**Do not** move the title, preview image, clear lamp, or artist name positions — those stay at `itemBounds.Y`.

### Step 4: Run tests to confirm they pass

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "SongListDisplayLogicTests" -v minimal
```

Expected: all pass.

### Step 5: Run the full test suite

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj -v minimal
```

Expected: all tests pass.

### Step 6: Commit

```bash
git add DTXMania.Game/Lib/Song/Components/SongListDisplay.cs DTXMania.Test/UI/SongListDisplayLogicTests.cs
git commit -m "fix: draw selected bar skin texture 30px higher to match NX (D6)"
```

---

## Verification

After all tasks are complete, run the full Mac test suite one final time:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj -v minimal
```

All tests should pass. Do a visual inspection of the game against the NX screenshot to confirm D2–D7 are resolved.

---

**Plan complete and saved to `docs/plans/2026-02-19-song-select-ui-fixes.md`.**
