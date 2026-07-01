# NX Judgement Collision Animation Implementation Plan

> **STATUS NOTE (2026-06-30):** This plan originally specified a combined-sheet
> slicing model where `NxAttackEffectManager` would load `ChipFireCombined`
> (`Graphics/ScreenPlayDrums chip fire.png`) and slice it into per-lane animation
> frames. That approach was abandoned in commits `de9323f` and `3a9ac4c` in favor
> of porting NX's actual per-lane fire model (loading individual
> `ScreenPlayDrums chip fire_*.png` assets directly). The `ChipFireCombined`
> constant, its `GetAllTexturePaths` entry, and the 1.2 MB bundled asset were
> subsequently removed. References to combined-sheet slicing below are retained
> for historical context but no longer reflect the shipped implementation.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add bundled NX-style note collision effects and sprite-art judgement text to the gameplay stage.

**Architecture:** Keep gameplay event flow unchanged and replace only visual feedback. `PerformanceStage.OnJudgementMade` will call a new `NxAttackEffectManager` for successful hits and a new `SpriteJudgementTextPopupManager` for all judgements. Asset paths and frame geometry live in `TexturePath` and `PerformanceUILayout` so the renderers do not hardcode skin filenames or sheet dimensions.

**Tech Stack:** .NET 8, C#, MonoGame `SpriteBatch`, xUnit, Moq, existing DTXManiaCX resource manager and layout constants.

---

## File Structure

- Modify: `DTXMania.Game/Lib/Resources/TexturePath.cs`
  - Add NX judgement/effect asset constants and lane filename helpers.
  - Keep existing `JudgeStrings` and `HitFx` constants for compatibility.
- Modify: `DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs`
  - Add `NxAttackEffectAssets` and `SpriteJudgementTextAssets` constants.
- Create: `DTXMania.Game/Lib/Stage/Performance/SpriteJudgementTextPopup.cs`
  - Sprite-first judgement popup lifecycle and font fallback hook.
- Create: `DTXMania.Game/Lib/Stage/Performance/NxAttackEffectManager.cs`
  - Bundled NX-style spark, star, chip-fragment, and wave effect manager.
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
  - Replace `_effectsManager` and `_judgementTextPopupManager` usage with the new managers.
- Modify: `DTXMania.Test/Resources/TexturePathTests.cs`
  - Cover new constants and helper methods.
- Modify: `DTXMania.Test/Resources/DefaultSkinAssetsTests.cs`
  - Guard required bundled NX assets.
- Modify: `DTXMania.Test/UI/PerformanceUILayoutMoreTests.cs`
  - Cover new frame and source-rectangle constants.
- Create: `DTXMania.Test/Stage/Performance/SpriteJudgementTextPopupTests.cs`
  - Test sprite mapping, lifecycle, lane placement, and fallback.
- Create: `DTXMania.Test/Stage/Performance/NxAttackEffectManagerTests.cs`
  - Test source rectangles, spawn rules, same-lane restart, optional assets, and expiry.
- Modify: `DTXMania.Test/Stage/Performance/PerformanceStageAdditionalCoverageTests.cs`
  - Verify `OnJudgementMade` calls new visual managers without changing score/combo/gauge flow.

## Task 1: Add Texture And Layout Constants

**Files:**
- Modify: `DTXMania.Game/Lib/Resources/TexturePath.cs`
- Modify: `DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs`
- Modify: `DTXMania.Test/Resources/TexturePathTests.cs`
- Modify: `DTXMania.Test/Resources/DefaultSkinAssetsTests.cs`
- Modify: `DTXMania.Test/UI/PerformanceUILayoutMoreTests.cs`

- [ ] **Step 1: Write failing texture-path tests**

Add these tests to `DTXMania.Test/Resources/TexturePathTests.cs` inside the performance texture constants section:

```csharp
[Fact]
public void JudgeStringsXg_ShouldBeCorrectPath()
{
    Assert.Equal("Graphics/7_JudgeStrings_XG.png", TexturePath.JudgeStringsXg);
}

[Fact]
public void ChipFireCombined_ShouldBeCorrectPath()
{
    Assert.Equal("Graphics/ScreenPlayDrums chip fire.png", TexturePath.ChipFireCombined);
}

[Theory]
[InlineData(0, "Graphics/ScreenPlayDrums chip fire_LC.png")]
[InlineData(1, "Graphics/ScreenPlayDrums chip fire_HH.png")]
[InlineData(2, "Graphics/ScreenPlayDrums chip fire_LP.png")]
[InlineData(3, "Graphics/ScreenPlayDrums chip fire_SD.png")]
[InlineData(4, "Graphics/ScreenPlayDrums chip fire_HT.png")]
[InlineData(5, "Graphics/ScreenPlayDrums chip fire_BD.png")]
[InlineData(6, "Graphics/ScreenPlayDrums chip fire_LT.png")]
[InlineData(7, "Graphics/ScreenPlayDrums chip fire_FT.png")]
[InlineData(8, "Graphics/ScreenPlayDrums chip fire_CY.png")]
[InlineData(9, "Graphics/ScreenPlayDrums chip fire_RD.png")]
public void GetDrumChipFireLanePath_ShouldMapGameplayLaneToNxAssetName(int lane, string expected)
{
    Assert.Equal(expected, TexturePath.GetDrumChipFireLanePath(lane));
}

[Theory]
[InlineData(0, "Graphics/ScreenPlayDrums chip star_LC.png")]
[InlineData(3, "Graphics/ScreenPlayDrums chip star_SD.png")]
[InlineData(5, "Graphics/ScreenPlayDrums chip star_BD.png")]
[InlineData(9, "Graphics/ScreenPlayDrums chip star_RD.png")]
public void GetDrumChipStarLanePath_ShouldMapGameplayLaneToNxAssetName(int lane, string expected)
{
    Assert.Equal(expected, TexturePath.GetDrumChipStarLanePath(lane));
}

[Theory]
[InlineData(-1)]
[InlineData(10)]
public void GetDrumChipFireLanePath_WithInvalidLane_ShouldThrow(int lane)
{
    Assert.Throws<ArgumentOutOfRangeException>(() => TexturePath.GetDrumChipFireLanePath(lane));
}

[Fact]
public void GetAllTexturePaths_ShouldContainRequiredNxJudgementCollisionTextures()
{
    var paths = TexturePath.GetAllTexturePaths();

    Assert.Contains(TexturePath.JudgeStringsXg, paths);
    Assert.Contains(TexturePath.ChipFireCombined, paths);
    Assert.Contains(TexturePath.ChipWave, paths);
}
```

- [ ] **Step 2: Write failing layout tests**

Add these tests to `DTXMania.Test/UI/PerformanceUILayoutMoreTests.cs` after `HitSparks_GetSparkPosition_ShouldMatchLaneCenter`:

```csharp
[Fact]
public void NxAttackEffectAssets_CombinedSparkSheetConstants_ShouldMatchBundledAsset()
{
    Assert.Equal(150, PerformanceUILayout.NxAttackEffectAssets.CombinedSparkFrameWidth);
    Assert.Equal(150, PerformanceUILayout.NxAttackEffectAssets.CombinedSparkFrameHeight);
    Assert.Equal(12, PerformanceUILayout.NxAttackEffectAssets.CombinedSparkFrameCount);
    Assert.Equal(10, PerformanceUILayout.NxAttackEffectAssets.CombinedSparkLaneRows);
    Assert.Equal(new Vector2(128, 128), PerformanceUILayout.NxAttackEffectAssets.PrimarySparkDrawSize);
}

[Theory]
[InlineData(0, 0, 0, 150, 150)]
[InlineData(3, 4, 600, 450, 150)]
[InlineData(9, 11, 1650, 1350, 150)]
public void NxAttackEffectAssets_GetCombinedSparkSource_ShouldReturnLaneRowAndFrameColumn(
    int lane, int frame, int x, int y, int size)
{
    var source = PerformanceUILayout.NxAttackEffectAssets.GetCombinedSparkSource(lane, frame);

    Assert.Equal(new Rectangle(x, y, size, size), source);
}

[Theory]
[InlineData(JudgementType.Perfect, 3, 6, 82, 22)]
[InlineData(JudgementType.Great, 95, 6, 75, 22)]
[InlineData(JudgementType.Good, 4, 44, 80, 22)]
[InlineData(JudgementType.Poor, 114, 44, 38, 22)]
[InlineData(JudgementType.Miss, 17, 82, 52, 22)]
public void SpriteJudgementTextAssets_GetJudgementSource_ShouldReturnBundledWordBounds(
    JudgementType judgementType, int x, int y, int width, int height)
{
    var source = PerformanceUILayout.SpriteJudgementTextAssets.GetJudgementSource(judgementType);

    Assert.Equal(new Rectangle(x, y, width, height), source);
}

[Fact]
public void SpriteJudgementTextAssets_AccentBars_ShouldUseBundledBarBounds()
{
    Assert.Equal(new Rectangle(17, 111, 176, 18), PerformanceUILayout.SpriteJudgementTextAssets.YellowAccentBar);
    Assert.Equal(new Rectangle(17, 131, 176, 18), PerformanceUILayout.SpriteJudgementTextAssets.GreenAccentBar);
    Assert.Equal(new Rectangle(18, 151, 176, 18), PerformanceUILayout.SpriteJudgementTextAssets.BlueAccentBar);
}
```

Add this using to the top of `PerformanceUILayoutMoreTests.cs`:

```csharp
using DTXMania.Game.Lib.Song.Entities;
```

- [ ] **Step 3: Write failing default-skin asset guard tests**

Add this helper and test to `DTXMania.Test/Resources/DefaultSkinAssetsTests.cs`:

```csharp
[Theory]
[InlineData(TexturePath.JudgeStringsXg)]
[InlineData(TexturePath.ChipFireCombined)]
[InlineData(TexturePath.ChipWave)]
[InlineData("Graphics/ScreenPlayDrums chip fire_LC.png")]
[InlineData("Graphics/ScreenPlayDrums chip fire_SD.png")]
[InlineData("Graphics/ScreenPlayDrums chip star_LC.png")]
public void DefaultSkin_ShouldShipBundledNxJudgementCollisionAssets(string relativePath)
{
    var repoRoot = FindRepoRoot();
    var assetPath = Path.Combine(repoRoot, "System", relativePath.Replace('/', Path.DirectorySeparatorChar));

    Assert.True(File.Exists(assetPath), $"Bundled default skin must ship {relativePath}.");
    AssertPngSignature(assetPath, relativePath);
}

private static void AssertPngSignature(string filePath, string relativePath)
{
    var header = File.ReadAllBytes(filePath);
    Assert.True(header.Length >= PngSignature.Length,
        $"Bundled {relativePath} is not a valid PNG (too short).");
    for (int i = 0; i < PngSignature.Length; i++)
    {
        Assert.Equal(PngSignature[i], header[i]);
    }
}
```

Then replace the repeated PNG-signature loop in `DefaultSkin_ShouldShipHitEffectSpriteSheet_ForRequiredEffectsManagerLoad` with:

```csharp
AssertPngSignature(hitFxPath, TexturePath.HitFx);
```

- [ ] **Step 4: Run tests to verify they fail**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~TexturePathTests|FullyQualifiedName~PerformanceUILayoutMoreTests|FullyQualifiedName~DefaultSkinAssetsTests"
```

Expected: FAIL with missing `TexturePath.JudgeStringsXg`, `TexturePath.ChipFireCombined`, `TexturePath.GetDrumChipFireLanePath`, `PerformanceUILayout.NxAttackEffectAssets`, and `PerformanceUILayout.SpriteJudgementTextAssets`.

- [ ] **Step 5: Implement texture-path constants**

In `DTXMania.Game/Lib/Resources/TexturePath.cs`, add this block after `HitSparkYellow`:

```csharp
/// <summary>
/// Bundled XG-style judgement text sprite sheet for performance-stage judgement words.
/// </summary>
public const string JudgeStringsXg = "Graphics/7_JudgeStrings_XG.png";

/// <summary>
/// Combined NX-style drum chip fire sheet. Columns are animation frames; rows are lane colors.
/// </summary>
public const string ChipFireCombined = "Graphics/ScreenPlayDrums chip fire.png";

private static readonly string[] DrumNxAssetLaneCodes =
{
    "LC", "HH", "LP", "SD", "HT", "BD", "LT", "FT", "CY", "RD"
};

/// <summary>
/// Gets the bundled per-lane chip fire asset path for a gameplay lane index.
/// Gameplay lane SN maps to NX asset code SD; gameplay lane DB maps to NX asset code BD.
/// </summary>
public static string GetDrumChipFireLanePath(int laneIndex)
{
    return $"Graphics/ScreenPlayDrums chip fire_{GetDrumNxAssetLaneCode(laneIndex)}.png";
}

/// <summary>
/// Gets the bundled per-lane chip star asset path for a gameplay lane index.
/// Gameplay lane SN maps to NX asset code SD; gameplay lane DB maps to NX asset code BD.
/// </summary>
public static string GetDrumChipStarLanePath(int laneIndex)
{
    return $"Graphics/ScreenPlayDrums chip star_{GetDrumNxAssetLaneCode(laneIndex)}.png";
}

private static string GetDrumNxAssetLaneCode(int laneIndex)
{
    if (laneIndex < 0 || laneIndex >= DrumNxAssetLaneCodes.Length)
    {
        throw new ArgumentOutOfRangeException(nameof(laneIndex),
            $"Lane index must be between 0 and {DrumNxAssetLaneCodes.Length - 1}.");
    }

    return DrumNxAssetLaneCodes[laneIndex];
}
```

In `TexturePath.GetAllTexturePaths()`, add these entries immediately after `JudgeStrings`:

```csharp
JudgeStringsXg,
ChipFireCombined,
```

Do not add `ChipWave2` because the bundled System skin does not currently ship `ScreenPlayDrums chip wave2.png`.

- [ ] **Step 6: Implement layout constants**

In `DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs`, add `using DTXMania.Game.Lib.Song.Entities;` at the top.

Add this class after `HitSparks`:

```csharp
/// <summary>
/// NX-style note collision effect constants for bundled System skin assets.
/// </summary>
public static class NxAttackEffectAssets
{
    public const int CombinedSparkFrameWidth = 150;
    public const int CombinedSparkFrameHeight = 150;
    public const int CombinedSparkFrameCount = 12;
    public const int CombinedSparkLaneRows = 10;
    public const double PrimarySparkFrameDurationSeconds = 0.03;
    public static readonly Vector2 PrimarySparkDrawSize = new Vector2(128, 128);
    public static readonly Vector2 StarDrawSize = new Vector2(32, 32);
    public static readonly Vector2 WaveDrawSize = new Vector2(64, 64);
    public const int StarParticleCount = 16;
    public const int ChipFragmentCount = 2;
    public const int WaveParticleCount = 2;
    public const double StarLifetimeSeconds = 0.34;
    public const double ChipFragmentLifetimeSeconds = 0.44;
    public const double WaveLifetimeSeconds = 0.42;

    public static Rectangle GetCombinedSparkSource(int laneIndex, int frameIndex)
    {
        if (laneIndex < 0 || laneIndex >= CombinedSparkLaneRows)
        {
            throw new ArgumentOutOfRangeException(nameof(laneIndex),
                $"Lane index must be between 0 and {CombinedSparkLaneRows - 1}.");
        }
        if (frameIndex < 0 || frameIndex >= CombinedSparkFrameCount)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex),
                $"Frame index must be between 0 and {CombinedSparkFrameCount - 1}.");
        }

        return new Rectangle(
            frameIndex * CombinedSparkFrameWidth,
            laneIndex * CombinedSparkFrameHeight,
            CombinedSparkFrameWidth,
            CombinedSparkFrameHeight);
    }

    public static Vector2 GetEffectOrigin(int laneIndex)
    {
        return new Vector2(GetLaneX(laneIndex), JudgementLineY);
    }
}
```

Add this class before `TimingIndicatorAssets`:

```csharp
public static class SpriteJudgementTextAssets
{
    public const double PopDurationSeconds = 0.12;
    public const double TotalDurationSeconds = 0.45;
    public const float InitialScale = 1.25f;
    public const float SettledScale = 1.0f;
    public const int JudgementLineOffsetY = 72;

    public static readonly Rectangle PerfectSource = new Rectangle(3, 6, 82, 22);
    public static readonly Rectangle GreatSource = new Rectangle(95, 6, 75, 22);
    public static readonly Rectangle GoodSource = new Rectangle(4, 44, 80, 22);
    public static readonly Rectangle PoorSource = new Rectangle(114, 44, 38, 22);
    public static readonly Rectangle MissSource = new Rectangle(17, 82, 52, 22);
    public static readonly Rectangle YellowAccentBar = new Rectangle(17, 111, 176, 18);
    public static readonly Rectangle GreenAccentBar = new Rectangle(17, 131, 176, 18);
    public static readonly Rectangle BlueAccentBar = new Rectangle(18, 151, 176, 18);

    public static Rectangle GetJudgementSource(JudgementType judgementType)
    {
        return judgementType switch
        {
            JudgementType.Perfect => PerfectSource,
            JudgementType.Great => GreatSource,
            JudgementType.Good => GoodSource,
            JudgementType.Poor => PoorSource,
            JudgementType.Miss => MissSource,
            _ => throw new ArgumentOutOfRangeException(nameof(judgementType), judgementType, "Unknown judgement type.")
        };
    }

    public static Vector2 GetLaneTextPosition(int laneIndex, Rectangle source)
    {
        var x = GetLaneX(laneIndex) - source.Width / 2f;
        var y = JudgementLineY - JudgementLineOffsetY;
        return new Vector2(x, y);
    }
}
```

- [ ] **Step 7: Run tests to verify constants pass**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~TexturePathTests|FullyQualifiedName~PerformanceUILayoutMoreTests|FullyQualifiedName~DefaultSkinAssetsTests"
```

Expected: PASS.

- [ ] **Step 8: Commit constants**

Run:

```bash
rtk git add DTXMania.Game/Lib/Resources/TexturePath.cs DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs DTXMania.Test/Resources/TexturePathTests.cs DTXMania.Test/Resources/DefaultSkinAssetsTests.cs DTXMania.Test/UI/PerformanceUILayoutMoreTests.cs
rtk git commit -m "feat: add nx judgement collision asset constants"
```

## Task 2: Add Sprite Judgement Text Manager

**Files:**
- Create: `DTXMania.Game/Lib/Stage/Performance/SpriteJudgementTextPopup.cs`
- Create: `DTXMania.Test/Stage/Performance/SpriteJudgementTextPopupTests.cs`

- [ ] **Step 1: Write failing sprite judgement tests**

Create `DTXMania.Test/Stage/Performance/SpriteJudgementTextPopupTests.cs`:

```csharp
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance;

[Trait("Category", "Unit")]
public class SpriteJudgementTextPopupTests
{
    [Fact]
    public void Popup_Update_ShouldScaleThenFade()
    {
        var popup = new SpriteJudgementTextPopup(
            JudgementType.Perfect,
            new Rectangle(3, 6, 82, 22),
            new Vector2(100, 200));

        popup.Update(0.06);

        Assert.True(popup.Scale < PerformanceUILayout.SpriteJudgementTextAssets.InitialScale);
        Assert.True(popup.Scale >= PerformanceUILayout.SpriteJudgementTextAssets.SettledScale);
        Assert.True(popup.Alpha > 0f);
        Assert.True(popup.IsActive);
    }

    [Fact]
    public void Popup_Update_AfterTotalDuration_ShouldExpire()
    {
        var popup = new SpriteJudgementTextPopup(
            JudgementType.Great,
            new Rectangle(95, 6, 75, 22),
            Vector2.Zero);

        var active = popup.Update(0.5);

        Assert.False(active);
        Assert.False(popup.IsActive);
        Assert.Equal(0f, popup.Alpha);
    }

    [Theory]
    [InlineData(JudgementType.Perfect, 3, 6, 82, 22)]
    [InlineData(JudgementType.Great, 95, 6, 75, 22)]
    [InlineData(JudgementType.Good, 4, 44, 80, 22)]
    [InlineData(JudgementType.Poor, 114, 44, 38, 22)]
    [InlineData(JudgementType.Miss, 17, 82, 52, 22)]
    public void Manager_SpawnPopup_ShouldUseBundledSourceRect(
        JudgementType judgementType,
        int x,
        int y,
        int width,
        int height)
    {
        var manager = CreateManager(spriteTextureAvailable: true);

        manager.SpawnPopup(new JudgementEvent(10, 3, 0.0, judgementType));

        var popup = Assert.Single(manager.ActivePopupsForTesting);
        Assert.Equal(new Rectangle(x, y, width, height), popup.SourceRectangle);
        Assert.Equal(PerformanceUILayout.SpriteJudgementTextAssets.GetLaneTextPosition(3, popup.SourceRectangle), popup.Position);
    }

    [Fact]
    public void Manager_SpawnPopup_WhenSpriteTextureMissing_ShouldUseFontFallback()
    {
        var fallbackEvents = new List<JudgementEvent>();
        var manager = CreateManager(
            spriteTextureAvailable: false,
            fontFallback: e => fallbackEvents.Add(e));
        var judgement = new JudgementEvent(10, 4, 0.0, JudgementType.Good);

        manager.SpawnPopup(judgement);

        Assert.Empty(manager.ActivePopupsForTesting);
        Assert.Same(judgement, Assert.Single(fallbackEvents));
    }

    [Fact]
    public void Manager_Update_ShouldRemoveExpiredPopups()
    {
        var manager = CreateManager(spriteTextureAvailable: true);
        manager.SpawnPopup(new JudgementEvent(10, 0, 0.0, JudgementType.Perfect));

        manager.Update(0.5);

        Assert.Empty(manager.ActivePopupsForTesting);
    }

    [Fact]
    public void Manager_ClearAll_ShouldRemoveEveryPopup()
    {
        var manager = CreateManager(spriteTextureAvailable: true);
        manager.SpawnPopup(new JudgementEvent(10, 0, 0.0, JudgementType.Perfect));
        manager.SpawnPopup(new JudgementEvent(11, 1, 0.0, JudgementType.Great));

        manager.ClearAll();

        Assert.Empty(manager.ActivePopupsForTesting);
    }

    [Fact]
    public void Dispose_ShouldReleaseSpriteTextureReference()
    {
        var texture = new Mock<ITexture>();
        texture.SetupGet(x => x.Width).Returns(448);
        texture.SetupGet(x => x.Height).Returns(256);
        var manager = SpriteJudgementTextPopupManager.CreateForTesting(texture.Object);

        manager.Dispose();

        texture.Verify(x => x.RemoveReference(), Times.Once);
    }

    private static SpriteJudgementTextPopupManager CreateManager(
        bool spriteTextureAvailable,
        System.Action<JudgementEvent>? fontFallback = null)
    {
        if (!spriteTextureAvailable)
        {
            return SpriteJudgementTextPopupManager.CreateForTesting(null, fontFallback);
        }

        var texture = new Mock<ITexture>();
        texture.SetupGet(x => x.Width).Returns(448);
        texture.SetupGet(x => x.Height).Returns(256);
        return SpriteJudgementTextPopupManager.CreateForTesting(texture.Object, fontFallback);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SpriteJudgementTextPopupTests"
```

Expected: FAIL because `SpriteJudgementTextPopup` and `SpriteJudgementTextPopupManager` do not exist.

- [ ] **Step 3: Implement sprite judgement manager**

Create `DTXMania.Game/Lib/Stage/Performance/SpriteJudgementTextPopup.cs`:

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.UI.Layout;

namespace DTXMania.Game.Lib.Stage.Performance
{
    public sealed class SpriteJudgementTextPopup
    {
        private double _elapsedSeconds;

        public SpriteJudgementTextPopup(JudgementType judgementType, Rectangle sourceRectangle, Vector2 position)
        {
            JudgementType = judgementType;
            SourceRectangle = sourceRectangle;
            Position = position;
            Alpha = 1f;
            Scale = PerformanceUILayout.SpriteJudgementTextAssets.InitialScale;
            IsActive = true;
        }

        public JudgementType JudgementType { get; }
        public Rectangle SourceRectangle { get; }
        public Vector2 Position { get; }
        public float Alpha { get; private set; }
        public float Scale { get; private set; }
        public bool IsActive { get; private set; }

        public bool Update(double deltaTime)
        {
            if (!IsActive)
                return false;

            _elapsedSeconds += Math.Max(0.0, deltaTime);
            var totalDuration = PerformanceUILayout.SpriteJudgementTextAssets.TotalDurationSeconds;
            if (_elapsedSeconds >= totalDuration)
            {
                Alpha = 0f;
                Scale = PerformanceUILayout.SpriteJudgementTextAssets.SettledScale;
                IsActive = false;
                return false;
            }

            var popDuration = PerformanceUILayout.SpriteJudgementTextAssets.PopDurationSeconds;
            if (_elapsedSeconds < popDuration)
            {
                var popProgress = (float)(_elapsedSeconds / popDuration);
                Scale = MathHelper.Lerp(
                    PerformanceUILayout.SpriteJudgementTextAssets.InitialScale,
                    PerformanceUILayout.SpriteJudgementTextAssets.SettledScale,
                    popProgress);
            }
            else
            {
                Scale = PerformanceUILayout.SpriteJudgementTextAssets.SettledScale;
            }

            var fadeStart = popDuration;
            var fadeProgress = (float)((_elapsedSeconds - fadeStart) / (totalDuration - fadeStart));
            Alpha = MathHelper.Clamp(1f - fadeProgress, 0f, 1f);
            return true;
        }
    }

    public sealed class SpriteJudgementTextPopupManager : IDisposable
    {
        private readonly List<SpriteJudgementTextPopup> _activePopups;
        private readonly Action<JudgementEvent>? _fontFallback;
        private ITexture? _spriteTexture;
        private bool _disposed;

        public SpriteJudgementTextPopupManager(IResourceManager resourceManager, Action<JudgementEvent>? fontFallback = null)
            : this(LoadSpriteTexture(resourceManager), fontFallback, new List<SpriteJudgementTextPopup>())
        {
        }

        private SpriteJudgementTextPopupManager(
            ITexture? spriteTexture,
            Action<JudgementEvent>? fontFallback,
            List<SpriteJudgementTextPopup> activePopups)
        {
            _spriteTexture = spriteTexture;
            _fontFallback = fontFallback;
            _activePopups = activePopups;
        }

        internal static SpriteJudgementTextPopupManager CreateForTesting(
            ITexture? spriteTexture,
            Action<JudgementEvent>? fontFallback = null,
            List<SpriteJudgementTextPopup>? activePopups = null)
        {
            return new SpriteJudgementTextPopupManager(
                spriteTexture,
                fontFallback,
                activePopups ?? new List<SpriteJudgementTextPopup>());
        }

        internal IReadOnlyList<SpriteJudgementTextPopup> ActivePopupsForTesting => _activePopups;

        public int ActivePopupCount => _activePopups.Count;

        public void SpawnPopup(JudgementEvent judgementEvent)
        {
            if (_disposed || judgementEvent == null)
                return;

            if (_spriteTexture == null)
            {
                _fontFallback?.Invoke(judgementEvent);
                return;
            }

            Rectangle source;
            try
            {
                source = PerformanceUILayout.SpriteJudgementTextAssets.GetJudgementSource(judgementEvent.Type);
            }
            catch (ArgumentOutOfRangeException)
            {
                return;
            }

            var position = PerformanceUILayout.SpriteJudgementTextAssets.GetLaneTextPosition(judgementEvent.Lane, source);
            _activePopups.Add(new SpriteJudgementTextPopup(judgementEvent.Type, source, position));
        }

        public void Update(double deltaTime)
        {
            if (_disposed)
                return;

            for (int i = _activePopups.Count - 1; i >= 0; i--)
            {
                if (!_activePopups[i].Update(deltaTime))
                {
                    _activePopups.RemoveAt(i);
                }
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (_disposed || spriteBatch == null || _spriteTexture?.Texture == null)
                return;

            foreach (var popup in _activePopups)
            {
                if (!popup.IsActive || popup.Alpha <= 0f)
                    continue;

                var source = popup.SourceRectangle;
                var width = Math.Max(1, (int)MathF.Round(source.Width * popup.Scale));
                var height = Math.Max(1, (int)MathF.Round(source.Height * popup.Scale));
                var dest = new Rectangle(
                    (int)MathF.Round(popup.Position.X - (width - source.Width) / 2f),
                    (int)MathF.Round(popup.Position.Y - (height - source.Height) / 2f),
                    width,
                    height);

                _spriteTexture.Draw(
                    spriteBatch,
                    dest,
                    source,
                    Color.White * popup.Alpha,
                    0f,
                    Vector2.Zero,
                    SpriteEffects.None,
                    0.5f);
            }
        }

        public void ClearAll()
        {
            _activePopups.Clear();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _activePopups.Clear();
            _spriteTexture?.RemoveReference();
            _spriteTexture = null;
            _disposed = true;
        }

        private static ITexture? LoadSpriteTexture(IResourceManager resourceManager)
        {
            ArgumentNullException.ThrowIfNull(resourceManager);

            try
            {
                if (!resourceManager.ResourceExists(TexturePath.JudgeStringsXg))
                    return null;

                var texture = resourceManager.LoadTexture(TexturePath.JudgeStringsXg);
                if (texture.Width < 242 || texture.Height < 169)
                {
                    texture.RemoveReference();
                    return null;
                }

                return texture;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"SpriteJudgementTextPopupManager: {ex.GetType().Name} loading {TexturePath.JudgeStringsXg}: {ex.Message}");
                return null;
            }
        }
    }
}
```

- [ ] **Step 4: Run sprite judgement tests**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SpriteJudgementTextPopupTests"
```

Expected: PASS.

- [ ] **Step 5: Commit sprite judgement manager**

Run:

```bash
rtk git add DTXMania.Game/Lib/Stage/Performance/SpriteJudgementTextPopup.cs DTXMania.Test/Stage/Performance/SpriteJudgementTextPopupTests.cs
rtk git commit -m "feat: render judgement text from bundled sprite art"
```

## Task 3: Add NX Attack Effect Manager

**Files:**
- Create: `DTXMania.Game/Lib/Stage/Performance/NxAttackEffectManager.cs`
- Create: `DTXMania.Test/Stage/Performance/NxAttackEffectManagerTests.cs`

- [ ] **Step 1: Write failing attack-effect manager tests**

Create `DTXMania.Test/Stage/Performance/NxAttackEffectManagerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance;

[Trait("Category", "Unit")]
public class NxAttackEffectManagerTests
{
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(5, 3, 450, 750)]
    [InlineData(9, 11, 1650, 1350)]
    public void GetCombinedSparkSource_ShouldUseLaneRowsAndFrameColumns(int lane, int frame, int x, int y)
    {
        var source = NxAttackEffectManager.GetCombinedSparkSource(lane, frame);

        Assert.Equal(new Rectangle(x, y, 150, 150), source);
    }

    [Theory]
    [InlineData(JudgementType.Perfect, true)]
    [InlineData(JudgementType.Great, true)]
    [InlineData(JudgementType.Good, true)]
    [InlineData(JudgementType.Poor, true)]
    [InlineData(JudgementType.Miss, false)]
    public void Spawn_ShouldCreatePrimarySparkOnlyForHitJudgements(JudgementType judgementType, bool shouldSpawn)
    {
        var manager = CreateManager(combinedAvailable: true);

        manager.Spawn(3, judgementType);

        Assert.Equal(shouldSpawn ? 1 : 0, manager.ActivePrimarySparkCountForTesting);
    }

    [Fact]
    public void Spawn_WhenSameLaneAlreadyActive_ShouldRestartPrimarySpark()
    {
        var manager = CreateManager(combinedAvailable: true);
        manager.Spawn(2, JudgementType.Perfect);
        manager.Update(0.09);

        manager.Spawn(2, JudgementType.Great);

        var spark = Assert.Single(manager.ActivePrimarySparksForTesting.Values);
        Assert.Equal(0, spark.FrameIndex);
        Assert.Equal(JudgementType.Great, spark.JudgementType);
    }

    [Fact]
    public void Spawn_WithCombinedSheet_ShouldCreateSecondaryParticles()
    {
        var manager = CreateManager(combinedAvailable: true, starsAvailable: true, chipTextureAvailable: true, waveAvailable: true);

        manager.Spawn(0, JudgementType.Perfect);

        Assert.Equal(1, manager.ActivePrimarySparkCountForTesting);
        Assert.True(manager.ActiveParticleCountForTesting > 0);
    }

    [Fact]
    public void Constructor_WhenCombinedSheetMissing_ShouldUsePerLaneFallbackTexture()
    {
        var manager = CreateManager(combinedAvailable: false, perLaneFallbackAvailable: true);

        manager.Spawn(0, JudgementType.Perfect);

        var spark = Assert.Single(manager.ActivePrimarySparksForTesting.Values);
        Assert.False(spark.UsesCombinedSheet);
    }

    [Fact]
    public void Update_AfterPrimarySparkDuration_ShouldExpireSpark()
    {
        var manager = CreateManager(combinedAvailable: true);
        manager.Spawn(0, JudgementType.Perfect);

        manager.Update(1.0);

        Assert.Equal(0, manager.ActivePrimarySparkCountForTesting);
    }

    [Fact]
    public void Dispose_ShouldReleaseLoadedTextures()
    {
        var texture = CreateTexture(width: 1800, height: 1650);
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(It.IsAny<string>())).Returns(false);
        resourceManager.Setup(x => x.ResourceExists(TexturePath.ChipFireCombined)).Returns(true);
        resourceManager.Setup(x => x.LoadTexture(TexturePath.ChipFireCombined)).Returns(texture.Object);

        var manager = new NxAttackEffectManager(resourceManager.Object);

        manager.Dispose();

        texture.Verify(x => x.RemoveReference(), Times.Once);
    }

    private static NxAttackEffectManager CreateManager(
        bool combinedAvailable,
        bool perLaneFallbackAvailable = false,
        bool starsAvailable = false,
        bool chipTextureAvailable = false,
        bool waveAvailable = false)
    {
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.ResourceExists(It.IsAny<string>())).Returns(false);

        if (combinedAvailable)
        {
            resourceManager.Setup(x => x.ResourceExists(TexturePath.ChipFireCombined)).Returns(true);
            resourceManager.Setup(x => x.LoadTexture(TexturePath.ChipFireCombined))
                .Returns(CreateTexture(width: 1800, height: 1650).Object);
        }

        if (perLaneFallbackAvailable)
        {
            for (var lane = 0; lane < PerformanceUILayout.LaneCount; lane++)
            {
                var path = TexturePath.GetDrumChipFireLanePath(lane);
                resourceManager.Setup(x => x.ResourceExists(path)).Returns(true);
                resourceManager.Setup(x => x.LoadTexture(path)).Returns(CreateTexture(width: 128, height: 128).Object);
            }
        }

        if (starsAvailable)
        {
            for (var lane = 0; lane < PerformanceUILayout.LaneCount; lane++)
            {
                var path = TexturePath.GetDrumChipStarLanePath(lane);
                resourceManager.Setup(x => x.ResourceExists(path)).Returns(true);
                resourceManager.Setup(x => x.LoadTexture(path)).Returns(CreateTexture(width: 32, height: 32).Object);
            }
        }

        if (chipTextureAvailable)
        {
            resourceManager.Setup(x => x.ResourceExists(TexturePath.DrumChips)).Returns(true);
            resourceManager.Setup(x => x.LoadTexture(TexturePath.DrumChips)).Returns(CreateTexture(width: 718, height: 776).Object);
        }

        if (waveAvailable)
        {
            resourceManager.Setup(x => x.ResourceExists(TexturePath.ChipWave)).Returns(true);
            resourceManager.Setup(x => x.LoadTexture(TexturePath.ChipWave)).Returns(CreateTexture(width: 64, height: 64).Object);
        }

        return new NxAttackEffectManager(resourceManager.Object, random: new Random(0));
    }

    private static Mock<ITexture> CreateTexture(int width, int height)
    {
        var texture = new Mock<ITexture>();
        texture.SetupGet(x => x.Width).Returns(width);
        texture.SetupGet(x => x.Height).Returns(height);
        return texture;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~NxAttackEffectManagerTests"
```

Expected: FAIL because `NxAttackEffectManager` does not exist.

- [ ] **Step 3: Implement attack effect manager**

Create `DTXMania.Game/Lib/Stage/Performance/NxAttackEffectManager.cs`:

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.UI.Layout;

namespace DTXMania.Game.Lib.Stage.Performance
{
    public sealed class NxAttackEffectSettings
    {
        public static NxAttackEffectSettings Default { get; } = new NxAttackEffectSettings();

        public double PrimaryFrameDurationSeconds { get; init; } =
            PerformanceUILayout.NxAttackEffectAssets.PrimarySparkFrameDurationSeconds;
        public int PrimaryFrameCount { get; init; } =
            PerformanceUILayout.NxAttackEffectAssets.CombinedSparkFrameCount;
        public int StarParticleCount { get; init; } =
            PerformanceUILayout.NxAttackEffectAssets.StarParticleCount;
        public int ChipFragmentCount { get; init; } =
            PerformanceUILayout.NxAttackEffectAssets.ChipFragmentCount;
        public int WaveParticleCount { get; init; } =
            PerformanceUILayout.NxAttackEffectAssets.WaveParticleCount;
    }

    public sealed class NxAttackEffectManager : IDisposable
    {
        private readonly NxAttackEffectSettings _settings;
        private readonly Random _random;
        private readonly Dictionary<int, PrimarySparkInstance> _primarySparks = new Dictionary<int, PrimarySparkInstance>();
        private readonly List<ParticleInstance> _particles = new List<ParticleInstance>();
        private readonly ITexture?[] _laneSparkTextures = new ITexture?[PerformanceUILayout.LaneCount];
        private readonly ITexture?[] _laneStarTextures = new ITexture?[PerformanceUILayout.LaneCount];
        private ITexture? _combinedSparkTexture;
        private ITexture? _chipTexture;
        private ITexture? _waveTexture;
        private bool _disposed;

        public NxAttackEffectManager(
            IResourceManager resourceManager,
            NxAttackEffectSettings? settings = null,
            Random? random = null)
        {
            ArgumentNullException.ThrowIfNull(resourceManager);
            _settings = settings ?? NxAttackEffectSettings.Default;
            _random = random ?? new Random(0);

            _combinedSparkTexture = LoadOptionalTexture(resourceManager, TexturePath.ChipFireCombined);
            if (!CanUseCombinedSparkSheet(_combinedSparkTexture))
            {
                _combinedSparkTexture?.RemoveReference();
                _combinedSparkTexture = null;
            }

            for (var lane = 0; lane < PerformanceUILayout.LaneCount; lane++)
            {
                _laneSparkTextures[lane] = LoadOptionalTexture(resourceManager, TexturePath.GetDrumChipFireLanePath(lane));
                _laneStarTextures[lane] = LoadOptionalTexture(resourceManager, TexturePath.GetDrumChipStarLanePath(lane));
            }

            _chipTexture = LoadOptionalTexture(resourceManager, TexturePath.DrumChips);
            _waveTexture = LoadOptionalTexture(resourceManager, TexturePath.ChipWave);
        }

        internal IReadOnlyDictionary<int, PrimarySparkInstance> ActivePrimarySparksForTesting => _primarySparks;
        internal int ActivePrimarySparkCountForTesting => _primarySparks.Count;
        internal int ActiveParticleCountForTesting => _particles.Count;

        public static Rectangle GetCombinedSparkSource(int laneIndex, int frameIndex)
        {
            return PerformanceUILayout.NxAttackEffectAssets.GetCombinedSparkSource(laneIndex, frameIndex);
        }

        public void Spawn(int lane, JudgementType judgementType)
        {
            if (_disposed || lane < 0 || lane >= PerformanceUILayout.LaneCount || judgementType == JudgementType.Miss)
                return;

            var origin = PerformanceUILayout.NxAttackEffectAssets.GetEffectOrigin(lane);
            if (_combinedSparkTexture != null || _laneSparkTextures[lane] != null)
            {
                _primarySparks[lane] = new PrimarySparkInstance(
                    lane,
                    judgementType,
                    origin,
                    usesCombinedSheet: _combinedSparkTexture != null);
            }

            SpawnStars(lane, origin);
            SpawnChipFragments(lane, origin);
            SpawnWaves(lane, origin);
        }

        public void Update(double deltaTime)
        {
            if (_disposed)
                return;

            var safeDelta = Math.Max(0.0, deltaTime);
            var expiredLanes = new List<int>();
            foreach (var kvp in _primarySparks)
            {
                kvp.Value.Update(safeDelta, _settings.PrimaryFrameDurationSeconds, _settings.PrimaryFrameCount);
                if (kvp.Value.IsExpired)
                {
                    expiredLanes.Add(kvp.Key);
                }
            }

            foreach (var lane in expiredLanes)
            {
                _primarySparks.Remove(lane);
            }

            for (var i = _particles.Count - 1; i >= 0; i--)
            {
                _particles[i].Update(safeDelta);
                if (_particles[i].IsExpired)
                {
                    _particles.RemoveAt(i);
                }
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (_disposed || spriteBatch == null)
                return;

            foreach (var spark in _primarySparks.Values)
            {
                DrawPrimarySpark(spriteBatch, spark);
            }

            foreach (var particle in _particles)
            {
                DrawParticle(spriteBatch, particle);
            }
        }

        public void ClearAll()
        {
            _primarySparks.Clear();
            _particles.Clear();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            ClearAll();
            _combinedSparkTexture?.RemoveReference();
            _combinedSparkTexture = null;
            _chipTexture?.RemoveReference();
            _chipTexture = null;
            _waveTexture?.RemoveReference();
            _waveTexture = null;

            for (var i = 0; i < _laneSparkTextures.Length; i++)
            {
                _laneSparkTextures[i]?.RemoveReference();
                _laneSparkTextures[i] = null;
                _laneStarTextures[i]?.RemoveReference();
                _laneStarTextures[i] = null;
            }

            _disposed = true;
        }

        private void SpawnStars(int lane, Vector2 origin)
        {
            if (_laneStarTextures[lane] == null)
                return;

            for (var i = 0; i < _settings.StarParticleCount; i++)
            {
                var angle = NextFloat(0f, MathHelper.TwoPi);
                var speed = NextFloat(70f, 145f);
                var velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
                _particles.Add(ParticleInstance.CreateStar(
                    lane,
                    origin,
                    velocity,
                    PerformanceUILayout.NxAttackEffectAssets.StarLifetimeSeconds));
            }
        }

        private void SpawnChipFragments(int lane, Vector2 origin)
        {
            if (_chipTexture == null)
                return;

            for (var i = 0; i < _settings.ChipFragmentCount; i++)
            {
                var direction = i == 0 ? -1f : 1f;
                var velocity = new Vector2(direction * NextFloat(90f, 130f), NextFloat(-150f, -95f));
                _particles.Add(ParticleInstance.CreateChip(
                    lane,
                    origin,
                    velocity,
                    GetChipFragmentSource(lane, i),
                    PerformanceUILayout.NxAttackEffectAssets.ChipFragmentLifetimeSeconds));
            }
        }

        private void SpawnWaves(int lane, Vector2 origin)
        {
            if (_waveTexture == null)
                return;

            for (var i = 0; i < _settings.WaveParticleCount; i++)
            {
                _particles.Add(ParticleInstance.CreateWave(
                    lane,
                    origin,
                    delaySeconds: i * 0.05,
                    PerformanceUILayout.NxAttackEffectAssets.WaveLifetimeSeconds));
            }
        }

        private void DrawPrimarySpark(SpriteBatch spriteBatch, PrimarySparkInstance spark)
        {
            var drawSize = PerformanceUILayout.NxAttackEffectAssets.PrimarySparkDrawSize;
            var destination = CenteredDestination(spark.Position, drawSize, 1f);
            if (spark.UsesCombinedSheet && _combinedSparkTexture != null)
            {
                _combinedSparkTexture.Draw(
                    spriteBatch,
                    destination,
                    GetCombinedSparkSource(spark.Lane, spark.FrameIndex),
                    Color.White,
                    0f,
                    Vector2.Zero,
                    SpriteEffects.None,
                    0f);
                return;
            }

            var fallback = _laneSparkTextures[spark.Lane];
            fallback?.Draw(spriteBatch, destination, null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0f);
        }

        private void DrawParticle(SpriteBatch spriteBatch, ParticleInstance particle)
        {
            if (particle.DelaySeconds > 0)
                return;

            var color = Color.White * particle.Alpha;
            if (particle.Kind == ParticleKind.Star)
            {
                var texture = _laneStarTextures[particle.Lane];
                texture?.Draw(spriteBatch, CenteredDestination(particle.Position, PerformanceUILayout.NxAttackEffectAssets.StarDrawSize, particle.Scale),
                    null, color, particle.Rotation, Vector2.Zero, SpriteEffects.None, 0f);
            }
            else if (particle.Kind == ParticleKind.Chip)
            {
                _chipTexture?.Draw(spriteBatch, CenteredDestination(particle.Position, new Vector2(particle.SourceRectangle.Width, particle.SourceRectangle.Height), particle.Scale),
                    particle.SourceRectangle, color, particle.Rotation, Vector2.Zero, SpriteEffects.None, 0f);
            }
            else if (particle.Kind == ParticleKind.Wave)
            {
                _waveTexture?.Draw(spriteBatch, CenteredDestination(particle.Position, PerformanceUILayout.NxAttackEffectAssets.WaveDrawSize, particle.Scale),
                    null, color, particle.Rotation, Vector2.Zero, SpriteEffects.None, 0f);
            }
        }

        private static Rectangle CenteredDestination(Vector2 center, Vector2 size, float scale)
        {
            var width = Math.Max(1, (int)MathF.Round(size.X * scale));
            var height = Math.Max(1, (int)MathF.Round(size.Y * scale));
            return new Rectangle(
                (int)MathF.Round(center.X - width / 2f),
                (int)MathF.Round(center.Y - height / 2f),
                width,
                height);
        }

        private static Rectangle GetChipFragmentSource(int lane, int side)
        {
            var laneWidth = Math.Max(1, PerformanceUILayout.GetLaneWidth(lane));
            var x = PerformanceUILayout.GetLaneLeftX(lane) - PerformanceUILayout.GetLaneLeftX(0);
            var y = 640;
            var width = Math.Max(8, Math.Min(40, laneWidth / 2));
            return new Rectangle(Math.Max(0, x + side * width), y, width, 64);
        }

        private static ITexture? LoadOptionalTexture(IResourceManager resourceManager, string path)
        {
            try
            {
                if (!resourceManager.ResourceExists(path))
                    return null;
                return resourceManager.LoadTexture(path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"NxAttackEffectManager: {ex.GetType().Name} loading {path}: {ex.Message}");
                return null;
            }
        }

        private static bool CanUseCombinedSparkSheet(ITexture? texture)
        {
            return texture != null
                && texture.Width >= PerformanceUILayout.NxAttackEffectAssets.CombinedSparkFrameWidth
                    * PerformanceUILayout.NxAttackEffectAssets.CombinedSparkFrameCount
                && texture.Height >= PerformanceUILayout.NxAttackEffectAssets.CombinedSparkFrameHeight
                    * PerformanceUILayout.NxAttackEffectAssets.CombinedSparkLaneRows;
        }

        private float NextFloat(float min, float max)
        {
            return min + (float)_random.NextDouble() * (max - min);
        }

        internal sealed class PrimarySparkInstance
        {
            private double _elapsedSeconds;

            public PrimarySparkInstance(int lane, JudgementType judgementType, Vector2 position, bool usesCombinedSheet)
            {
                Lane = lane;
                JudgementType = judgementType;
                Position = position;
                UsesCombinedSheet = usesCombinedSheet;
            }

            public int Lane { get; }
            public JudgementType JudgementType { get; }
            public Vector2 Position { get; }
            public bool UsesCombinedSheet { get; }
            public int FrameIndex { get; private set; }
            public bool IsExpired { get; private set; }

            public void Update(double deltaTime, double frameDurationSeconds, int frameCount)
            {
                _elapsedSeconds += deltaTime;
                FrameIndex = (int)(_elapsedSeconds / frameDurationSeconds);
                if (FrameIndex >= frameCount)
                {
                    FrameIndex = frameCount - 1;
                    IsExpired = true;
                }
            }
        }

        internal sealed class ParticleInstance
        {
            private readonly double _durationSeconds;
            private double _elapsedSeconds;

            private ParticleInstance(
                ParticleKind kind,
                int lane,
                Vector2 position,
                Vector2 velocity,
                Rectangle sourceRectangle,
                double delaySeconds,
                double durationSeconds)
            {
                Kind = kind;
                Lane = lane;
                Position = position;
                Velocity = velocity;
                SourceRectangle = sourceRectangle;
                DelaySeconds = delaySeconds;
                _durationSeconds = durationSeconds;
                Alpha = 1f;
                Scale = kind == ParticleKind.Wave ? 0.6f : 1f;
            }

            public ParticleKind Kind { get; }
            public int Lane { get; }
            public Vector2 Position { get; private set; }
            public Vector2 Velocity { get; }
            public Rectangle SourceRectangle { get; }
            public double DelaySeconds { get; private set; }
            public float Alpha { get; private set; }
            public float Scale { get; private set; }
            public float Rotation { get; private set; }
            public bool IsExpired { get; private set; }

            public static ParticleInstance CreateStar(int lane, Vector2 position, Vector2 velocity, double durationSeconds)
            {
                return new ParticleInstance(ParticleKind.Star, lane, position, velocity, Rectangle.Empty, 0.0, durationSeconds);
            }

            public static ParticleInstance CreateChip(int lane, Vector2 position, Vector2 velocity, Rectangle sourceRectangle, double durationSeconds)
            {
                return new ParticleInstance(ParticleKind.Chip, lane, position, velocity, sourceRectangle, 0.0, durationSeconds);
            }

            public static ParticleInstance CreateWave(int lane, Vector2 position, double delaySeconds, double durationSeconds)
            {
                return new ParticleInstance(ParticleKind.Wave, lane, position, Vector2.Zero, Rectangle.Empty, delaySeconds, durationSeconds);
            }

            public void Update(double deltaTime)
            {
                if (DelaySeconds > 0)
                {
                    DelaySeconds = Math.Max(0.0, DelaySeconds - deltaTime);
                    return;
                }

                _elapsedSeconds += deltaTime;
                if (_elapsedSeconds >= _durationSeconds)
                {
                    Alpha = 0f;
                    IsExpired = true;
                    return;
                }

                var progress = (float)(_elapsedSeconds / _durationSeconds);
                Position += Velocity * (float)deltaTime;
                Rotation += (Kind == ParticleKind.Chip ? 6f : 2f) * (float)deltaTime;
                Scale = Kind == ParticleKind.Wave ? MathHelper.Lerp(0.6f, 1.7f, progress) : MathHelper.Lerp(1f, 0.75f, progress);
                Alpha = MathHelper.Clamp(1f - progress, 0f, 1f);
            }
        }

        internal enum ParticleKind
        {
            Star,
            Chip,
            Wave
        }
    }
}
```

- [ ] **Step 4: Run attack effect tests**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~NxAttackEffectManagerTests"
```

Expected: PASS.

- [ ] **Step 5: Commit attack effect manager**

Run:

```bash
rtk git add DTXMania.Game/Lib/Stage/Performance/NxAttackEffectManager.cs DTXMania.Test/Stage/Performance/NxAttackEffectManagerTests.cs
rtk git commit -m "feat: add nx attack effect manager"
```

## Task 4: Wire New Visual Managers Into PerformanceStage

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Modify: `DTXMania.Test/Stage/Performance/PerformanceStageAdditionalCoverageTests.cs`

- [ ] **Step 1: Add stage integration test**

Add this test to `DTXMania.Test/Stage/Performance/PerformanceStageAdditionalCoverageTests.cs`:

```csharp
[Fact]
public void OnJudgementMade_WithHit_ShouldSpawnNxAttackAndSpriteJudgementText()
{
    var stage = CreateStage();
    var chartManager = CreateChartManagerWithSingleNote();
    var attackManager = NxAttackEffectManagerTestFactory.CreateEmpty();
    var spriteTextManager = SpriteJudgementTextPopupManager.CreateForTesting(CreateTexture(width: 448, height: 256).Object);

    ReflectionHelpers.SetPrivateField(stage, "_scoreManager", new ScoreManager(1));
    ReflectionHelpers.SetPrivateField(stage, "_comboManager", new ComboManager());
    ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", new GaugeManager());
    ReflectionHelpers.SetPrivateField(stage, "_skillManager", new SkillManager(1, new ComboManager()));
    ReflectionHelpers.SetPrivateField(stage, "_skillPanelDisplay", null);
    ReflectionHelpers.SetPrivateField(stage, "_nxAttackEffectManager", attackManager);
    ReflectionHelpers.SetPrivateField(stage, "_spriteJudgementTextPopupManager", spriteTextManager);
    ReflectionHelpers.SetPrivateField(stage, "_padRenderer", null);

    ReflectionHelpers.InvokePrivateMethod(stage, "OnJudgementMade", null,
        new JudgementEvent(0, 0, 0.0, JudgementType.Perfect));

    Assert.Equal(1, attackManager.SpawnCallCountForTesting);
    Assert.Equal(1, spriteTextManager.ActivePopupCount);
}

[Fact]
public void OnJudgementMade_WithMiss_ShouldSkipNxAttackAndShowSpriteJudgementText()
{
    var stage = CreateStage();
    var attackManager = NxAttackEffectManagerTestFactory.CreateEmpty();
    var spriteTextManager = SpriteJudgementTextPopupManager.CreateForTesting(CreateTexture(width: 448, height: 256).Object);

    ReflectionHelpers.SetPrivateField(stage, "_scoreManager", new ScoreManager(1));
    ReflectionHelpers.SetPrivateField(stage, "_comboManager", new ComboManager());
    ReflectionHelpers.SetPrivateField(stage, "_gaugeManager", new GaugeManager());
    ReflectionHelpers.SetPrivateField(stage, "_skillManager", new SkillManager(1, new ComboManager()));
    ReflectionHelpers.SetPrivateField(stage, "_skillPanelDisplay", null);
    ReflectionHelpers.SetPrivateField(stage, "_nxAttackEffectManager", attackManager);
    ReflectionHelpers.SetPrivateField(stage, "_spriteJudgementTextPopupManager", spriteTextManager);
    ReflectionHelpers.SetPrivateField(stage, "_padRenderer", null);

    ReflectionHelpers.InvokePrivateMethod(stage, "OnJudgementMade", null,
        new JudgementEvent(0, 0, 200.0, JudgementType.Miss));

    Assert.Equal(0, attackManager.SpawnCallCountForTesting);
    Assert.Equal(1, spriteTextManager.ActivePopupCount);
}

private static Mock<ITexture> CreateTexture(int width, int height)
{
    var texture = new Mock<ITexture>();
    texture.SetupGet(x => x.Width).Returns(width);
    texture.SetupGet(x => x.Height).Returns(height);
    return texture;
}
```

Add this helper class near the bottom of the same test file:

```csharp
private sealed class CountingNxAttackEffectManager : NxAttackEffectManager
{
    public CountingNxAttackEffectManager()
        : base(new Mock<IResourceManager>().Object)
    {
    }

    public int SpawnCallCountForTesting { get; private set; }

    public override void Spawn(int lane, JudgementType judgementType)
    {
        SpawnCallCountForTesting++;
    }
}

private static class NxAttackEffectManagerTestFactory
{
    public static CountingNxAttackEffectManager CreateEmpty()
    {
        return new CountingNxAttackEffectManager();
    }
}
```

This test requires `NxAttackEffectManager` to allow test override. In `NxAttackEffectManager.cs`, change the class and `Spawn` signature:

```csharp
public class NxAttackEffectManager : IDisposable
```

```csharp
public virtual void Spawn(int lane, JudgementType judgementType)
```

- [ ] **Step 2: Run integration tests to verify they fail**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~PerformanceStageAdditionalCoverageTests"
```

Expected: FAIL because `PerformanceStage` has not been wired to `_nxAttackEffectManager` and `_spriteJudgementTextPopupManager`.

- [ ] **Step 3: Replace visual manager fields**

In `DTXMania.Game/Lib/Stage/PerformanceStage.cs`, replace:

```csharp
private EffectsManager _effectsManager = null!;
private JudgementTextPopupManager _judgementTextPopupManager = null!;
```

with:

```csharp
private NxAttackEffectManager _nxAttackEffectManager = null!;
private JudgementTextPopupManager _fontJudgementTextPopupManager = null!;
private SpriteJudgementTextPopupManager _spriteJudgementTextPopupManager = null!;
```

- [ ] **Step 4: Update initialization**

In `InitializeComponents()`, replace:

```csharp
_effectsManager = new EffectsManager(graphicsDevice, _resourceManager);
_judgementTextPopupManager = new JudgementTextPopupManager(graphicsDevice, _resourceManager);
```

with:

```csharp
_nxAttackEffectManager = new NxAttackEffectManager(_resourceManager);
_fontJudgementTextPopupManager = new JudgementTextPopupManager(graphicsDevice, _resourceManager);
_spriteJudgementTextPopupManager = new SpriteJudgementTextPopupManager(
    _resourceManager,
    judgementEvent => _fontJudgementTextPopupManager?.SpawnPopup(judgementEvent));
```

- [ ] **Step 5: Update cleanup**

In `CleanupComponents()`, replace:

```csharp
_effectsManager?.Dispose();
_effectsManager = null;
_judgementTextPopupManager?.Dispose();
_judgementTextPopupManager = null;
```

with:

```csharp
_nxAttackEffectManager?.Dispose();
_nxAttackEffectManager = null;
_spriteJudgementTextPopupManager?.Dispose();
_spriteJudgementTextPopupManager = null;
_fontJudgementTextPopupManager?.Dispose();
_fontJudgementTextPopupManager = null;
```

- [ ] **Step 6: Update gameplay updates**

In `UpdateGameplay(double deltaTime)`, replace:

```csharp
_effectsManager?.Update(deltaTime);

_judgementTextPopupManager?.Update(deltaTime);
```

with:

```csharp
_nxAttackEffectManager?.Update(deltaTime);

_spriteJudgementTextPopupManager?.Update(deltaTime);
_fontJudgementTextPopupManager?.Update(deltaTime);
```

- [ ] **Step 7: Update judgement handling**

In `OnJudgementMade`, replace:

```csharp
_effectsManager?.SpawnHitEffect(e.Lane);
```

with:

```csharp
_nxAttackEffectManager?.Spawn(e.Lane, e.Type);
```

Replace:

```csharp
_judgementTextPopupManager?.SpawnPopup(e);
```

with:

```csharp
_spriteJudgementTextPopupManager?.SpawnPopup(e);
```

- [ ] **Step 8: Update draw methods**

In `DrawHitEffects()`, replace:

```csharp
_effectsManager?.Draw(_spriteBatch);
```

with:

```csharp
_nxAttackEffectManager?.Draw(_spriteBatch);
```

In `DrawJudgementTexts()`, replace:

```csharp
_judgementTextPopupManager?.Draw(_spriteBatch);
```

with:

```csharp
_spriteJudgementTextPopupManager?.Draw(_spriteBatch);
_fontJudgementTextPopupManager?.Draw(_spriteBatch);
```

- [ ] **Step 9: Run stage integration tests**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~PerformanceStageAdditionalCoverageTests"
```

Expected: PASS.

- [ ] **Step 10: Commit stage wiring**

Run:

```bash
rtk git add DTXMania.Game/Lib/Stage/PerformanceStage.cs DTXMania.Game/Lib/Stage/Performance/NxAttackEffectManager.cs DTXMania.Test/Stage/Performance/PerformanceStageAdditionalCoverageTests.cs
rtk git commit -m "feat: wire nx judgement collision visuals"
```

## Task 5: Run Focused Regression Suite And Fix Compile Issues

**Files:**
- Modify only files touched in Tasks 1-4 if the compiler identifies signature or nullable mismatches.

- [ ] **Step 1: Run focused performance and resource tests**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~NxAttackEffectManagerTests|FullyQualifiedName~SpriteJudgementTextPopupTests|FullyQualifiedName~JudgementTextPopupTests|FullyQualifiedName~TexturePathTests|FullyQualifiedName~PerformanceUILayoutMoreTests|FullyQualifiedName~DefaultSkinAssetsTests|FullyQualifiedName~PerformanceStageAdditionalCoverageTests"
```

Expected: PASS.

- [ ] **Step 2: Run Mac test project**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: PASS.

- [ ] **Step 3: Build Mac game project**

Run:

```bash
rtk dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
```

Expected: PASS with no compile errors.

- [ ] **Step 4: Commit focused verification fixes**

If Step 1, Step 2, or Step 3 required compile or nullable fixes, commit only those fixes:

```bash
rtk git add DTXMania.Game/Lib/Stage/Performance/NxAttackEffectManager.cs DTXMania.Game/Lib/Stage/Performance/SpriteJudgementTextPopup.cs DTXMania.Game/Lib/Stage/PerformanceStage.cs DTXMania.Game/Lib/Resources/TexturePath.cs DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs DTXMania.Test/Stage/Performance/NxAttackEffectManagerTests.cs DTXMania.Test/Stage/Performance/SpriteJudgementTextPopupTests.cs DTXMania.Test/Stage/Performance/PerformanceStageAdditionalCoverageTests.cs DTXMania.Test/Resources/TexturePathTests.cs DTXMania.Test/Resources/DefaultSkinAssetsTests.cs DTXMania.Test/UI/PerformanceUILayoutMoreTests.cs
rtk git commit -m "fix: stabilize nx judgement collision tests"
```

If no fixes were required, do not create an empty commit.

## Task 6: Manual Gameplay Verification

**Files:**
- No planned source changes.

- [ ] **Step 1: Launch the Mac game**

Run:

```bash
rtk dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj
```

Expected: game launches to the title screen.

- [ ] **Step 2: Verify visual behavior with an autoplay chart**

Use a chart that reaches Gameplay stage with `AutoPlay=True`.

Expected visual results:

- Successful notes show a lane-local NX-style spark at the judgement line.
- Repeated hits on the same lane restart that lane's primary spark.
- Stars, chip fragments, and wave pulses appear briefly when their bundled assets are present.
- `Perfect`, `Great`, `Good`, `OK`, and `Miss` judgement text use sprite art.
- `Miss` shows judgement text but does not create the attack effect.
- Score, combo, gauge, progress, and skill panels remain readable.

- [ ] **Step 3: Capture any failure evidence**

If a visual issue appears, stop manual verification and report the exact visible behavior in the final response. Include whether the issue is in the attack effect, judgement text, or draw order, and point to either `DTXMania.Game/Lib/Stage/Performance/NxAttackEffectManager.cs` or `DTXMania.Game/Lib/Stage/Performance/SpriteJudgementTextPopup.cs`.

- [ ] **Step 4: Final status**

Run:

```bash
rtk git status --short
```

Expected: clean worktree after all intended commits.

## Self-Review Checklist

- Spec coverage:
  - Bundled defaults: Task 1 and Task 3.
  - Full NX-style attack effect layers: Task 3.
  - Sprite judgement text: Task 2.
  - Stage event flow unchanged: Task 4.
  - Fallback behavior: Task 2 and Task 3.
  - Resource ownership: Task 2 and Task 3 disposal tests.
  - Verification: Task 5 and Task 6.
- Placeholder scan:
  - No red-flag markers or incomplete sections.
  - Every code-changing step includes exact code or an exact replacement.
- Type consistency:
  - `NxAttackEffectManager.Spawn(int lane, JudgementType judgementType)` is used consistently.
  - `SpriteJudgementTextPopupManager.SpawnPopup(JudgementEvent judgementEvent)` is used consistently.
  - Texture constants match the bundled `System/Graphics` filenames.
