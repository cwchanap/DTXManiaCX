# Result Stage NX Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace CX's centered result text screen with an NX-style result screen using existing CX performance data, NX structural assets, sprite-font text, reveal behavior, and optional result sounds.

**Architecture:** Keep `ResultStage` as the lifecycle/input/persistence owner. Add a testable `ResultScreenModel` for rank, result category, formatting, preview fallback, and new-record decisions; add `ResultRevealState` for frame-rate independent reveal timing; add `ResultScreenRenderer` for result-stage resource loading and drawing.

**Tech Stack:** .NET 8, MonoGame, xUnit, Moq, CX `IResourceManager`/`ITexture`/`IFont`/`ISound`, existing `SongScore` skill helpers.

---

## File Structure

- Modify: `DTXMania.Game/Lib/Resources/TexturePath.cs`
  - Add structural result-stage texture constants and include them in `GetAllTexturePaths()`.
- Modify: `DTXMania.Game/Lib/UI/Layout/ResultUILayout.cs`
  - Keep existing fallback constants; add NX virtual-screen dimensions and result element coordinates.
- Create: `DTXMania.Game/Lib/Stage/Result/ResultScreenModel.cs`
  - Pure model. Computes rank, plate category, formatted values, title/artist fallbacks, preview path, and new-record flag.
- Create: `DTXMania.Game/Lib/Stage/Result/ResultRevealState.cs`
  - Pure reveal timer. Tracks rank/panel reveal progress and completion.
- Create: `DTXMania.Game/Lib/Stage/Result/ResultScreenRenderer.cs`
  - Loads structural textures only when resources exist. Draws NX layers in virtual 1280x720 coordinates with sprite-font text.
- Modify: `DTXMania.Game/Lib/Stage/ResultStage.cs`
  - Build the model before score persistence, own reveal state, delegate drawing to renderer, play optional sounds, and update navigation behavior.
- Modify: `DTXMania.Test/Resources/TexturePathTests.cs`
  - Cover new texture constants and `GetAllTexturePaths()` entries.
- Modify: `DTXMania.Test/UI/PerformanceUILayoutTests.cs`
  - Extend `ResultUILayoutTests` for new NX coordinates and virtual viewport constants.
- Create: `DTXMania.Test/Stage/Result/ResultScreenModelTests.cs`
  - Cover rank thresholds, plate selection, formatting, preview fallback, and new-record detection.
- Create: `DTXMania.Test/Stage/Result/ResultRevealStateTests.cs`
  - Cover reveal progress, completion, and negative delta handling.
- Create: `DTXMania.Test/Stage/Result/ResultScreenRendererTests.cs`
  - Cover resource load decisions and virtual viewport transform.
- Modify: `DTXMania.Test/Stage/ResultStageTests.cs`
  - Replace old centered-text/fallback-line assertions with model/reveal/navigation assertions.
- Modify: `DTXMania.Test/Stage/ResultStageAdditionalCoverageTests.cs`
  - Update activation tests for renderer/model fields and sound no-throw paths.

## Task 1: Add Result Asset Constants And NX Layout Coordinates

**Files:**
- Modify: `DTXMania.Game/Lib/Resources/TexturePath.cs`
- Modify: `DTXMania.Game/Lib/UI/Layout/ResultUILayout.cs`
- Modify: `DTXMania.Test/Resources/TexturePathTests.cs`
- Modify: `DTXMania.Test/UI/PerformanceUILayoutTests.cs`

Correction: `ResultBackgroundRankSS/S/A/B/C/D/E` are optional skin override assets and must stay out of `TexturePath.GetAllTexturePaths()`, which is used for required preload/validation paths. Expose those seven rank background paths through `GetOptionalResultTexturePaths()` instead; keep only required structural result assets in `GetAllTexturePaths()`.

- [ ] **Step 1: Write failing texture path tests**

Add these facts to `DTXMania.Test/Resources/TexturePathTests.cs` near the existing result/background tests:

```csharp
[Fact]
public void ResultStageAssets_ShouldUseNXPaths()
{
    Assert.Equal("Graphics/8_rankSS.png", TexturePath.ResultRankSS);
    Assert.Equal("Graphics/8_rankS.png", TexturePath.ResultRankS);
    Assert.Equal("Graphics/8_rankA.png", TexturePath.ResultRankA);
    Assert.Equal("Graphics/8_rankB.png", TexturePath.ResultRankB);
    Assert.Equal("Graphics/8_rankC.png", TexturePath.ResultRankC);
    Assert.Equal("Graphics/8_rankD.png", TexturePath.ResultRankD);
    Assert.Equal("Graphics/8_rankE.png", TexturePath.ResultRankE);
    Assert.Equal("Graphics/ScreenResult StageCleared.png", TexturePath.ResultPlateStageCleared);
    Assert.Equal("Graphics/ScreenResult fullcombo.png", TexturePath.ResultPlateFullCombo);
    Assert.Equal("Graphics/ScreenResult Excellent.png", TexturePath.ResultPlateExcellent);
    Assert.Equal("Graphics/7_JacketPanel.png", TexturePath.ResultJacketPanel);
    Assert.Equal("Graphics/7_SkillPanel.png", TexturePath.ResultSkillPanel);
    Assert.Equal("Graphics/8_New Record.png", TexturePath.ResultNewRecord);
    Assert.Equal("Graphics/5_preimage default.png", TexturePath.ResultDefaultPreview);
}

[Fact]
public void ResultStageAssets_ShouldBeIncludedInAllTexturePaths()
{
    var paths = TexturePath.GetAllTexturePaths();

    Assert.Contains(TexturePath.ResultRankSS, paths);
    Assert.Contains(TexturePath.ResultRankS, paths);
    Assert.Contains(TexturePath.ResultRankA, paths);
    Assert.Contains(TexturePath.ResultRankB, paths);
    Assert.Contains(TexturePath.ResultRankC, paths);
    Assert.Contains(TexturePath.ResultRankD, paths);
    Assert.Contains(TexturePath.ResultRankE, paths);
    Assert.Contains(TexturePath.ResultPlateStageCleared, paths);
    Assert.Contains(TexturePath.ResultPlateFullCombo, paths);
    Assert.Contains(TexturePath.ResultPlateExcellent, paths);
    Assert.Contains(TexturePath.ResultJacketPanel, paths);
    Assert.Contains(TexturePath.ResultSkillPanel, paths);
    Assert.Contains(TexturePath.ResultNewRecord, paths);
    Assert.Contains(TexturePath.ResultDefaultPreview, paths);
}
```

- [ ] **Step 2: Write failing layout tests**

Add these facts to the existing `ResultUILayoutTests` class in `DTXMania.Test/UI/PerformanceUILayoutTests.cs`:

```csharp
[Fact]
public void NXViewport_ShouldMatchResultAssetResolution()
{
    Assert.Equal(1280, ResultUILayout.NXViewport.Width);
    Assert.Equal(720, ResultUILayout.NXViewport.Height);
}

[Fact]
public void NXLayout_KeyPositions_ShouldMatchDrumsResultLayout()
{
    Assert.Equal(new Vector2(480, 0), ResultUILayout.Rank.BadgePosition);
    Assert.Equal(new Vector2(315, 100), ResultUILayout.ResultPlate.Position);
    Assert.Equal(new Vector2(467, 287), ResultUILayout.Jacket.PanelPosition);
    Assert.Equal(new Rectangle(519, 338, 245, 245), ResultUILayout.Jacket.PreviewDestination);
    Assert.Equal(new Vector2(180, 260), ResultUILayout.SkillPanel.PanelPosition);
    Assert.Equal(new Vector2(30, 58), ResultUILayout.Score.Position);
    Assert.Equal(new Vector2(500, 630), ResultUILayout.SongInfo.TitlePosition);
    Assert.Equal(new Vector2(500, 665), ResultUILayout.SongInfo.ArtistPosition);
}

[Fact]
public void NXLayout_FontSizes_ShouldBePositive()
{
    Assert.True(ResultUILayout.Fonts.Small > 0);
    Assert.True(ResultUILayout.Fonts.Normal > 0);
    Assert.True(ResultUILayout.Fonts.Large > 0);
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~TexturePathTests|FullyQualifiedName~ResultUILayoutTests"
```

Expected: build fails because the new `TexturePath` and `ResultUILayout` members do not exist.

- [ ] **Step 4: Add texture constants**

In `DTXMania.Game/Lib/Resources/TexturePath.cs`, add this region after `ResultBackground`:

```csharp
        /// <summary>
        /// Result stage rank-specific background textures. These are optional skin assets.
        /// </summary>
        public const string ResultBackgroundRankSS = "Graphics/8_background rankSS.png";
        public const string ResultBackgroundRankS = "Graphics/8_background rankS.png";
        public const string ResultBackgroundRankA = "Graphics/8_background rankA.png";
        public const string ResultBackgroundRankB = "Graphics/8_background rankB.png";
        public const string ResultBackgroundRankC = "Graphics/8_background rankC.png";
        public const string ResultBackgroundRankD = "Graphics/8_background rankD.png";
        public const string ResultBackgroundRankE = "Graphics/8_background rankE.png";

        /// <summary>
        /// Result stage rank badge textures.
        /// </summary>
        public const string ResultRankSS = "Graphics/8_rankSS.png";
        public const string ResultRankS = "Graphics/8_rankS.png";
        public const string ResultRankA = "Graphics/8_rankA.png";
        public const string ResultRankB = "Graphics/8_rankB.png";
        public const string ResultRankC = "Graphics/8_rankC.png";
        public const string ResultRankD = "Graphics/8_rankD.png";
        public const string ResultRankE = "Graphics/8_rankE.png";

        /// <summary>
        /// Result stage clear category plate textures.
        /// </summary>
        public const string ResultPlateStageCleared = "Graphics/ScreenResult StageCleared.png";
        public const string ResultPlateFullCombo = "Graphics/ScreenResult fullcombo.png";
        public const string ResultPlateExcellent = "Graphics/ScreenResult Excellent.png";

        /// <summary>
        /// Result stage structural panel textures.
        /// </summary>
        public const string ResultJacketPanel = "Graphics/7_JacketPanel.png";
        public const string ResultSkillPanel = "Graphics/7_SkillPanel.png";
        public const string ResultNewRecord = "Graphics/8_New Record.png";
        public const string ResultDefaultPreview = "Graphics/5_preimage default.png";
```

In `GetAllTexturePaths()`, add the required structural result constants immediately after `ResultBackground`; do not include the optional rank-specific background overrides:

```csharp
                ResultRankSS,
                ResultRankS,
                ResultRankA,
                ResultRankB,
                ResultRankC,
                ResultRankD,
                ResultRankE,
                ResultPlateStageCleared,
                ResultPlateFullCombo,
                ResultPlateExcellent,
                ResultJacketPanel,
                ResultSkillPanel,
                ResultNewRecord,
                ResultDefaultPreview,
```

- [ ] **Step 5: Add NX layout constants while preserving old constants**

In `DTXMania.Game/Lib/UI/Layout/ResultUILayout.cs`, keep the existing `Background`, `ResultDisplay`, and `FallbackText` classes. Add these nested classes before `FallbackText`:

```csharp
        public static class NXViewport
        {
            public const int Width = 1280;
            public const int Height = 720;
        }

        public static class Fonts
        {
            public const int Small = 16;
            public const int Normal = 20;
            public const int Large = 32;
        }

        public static class Rank
        {
            public static readonly Vector2 BadgePosition = new(480, 0);
        }

        public static class ResultPlate
        {
            public static readonly Vector2 Position = new(315, 100);
            public static readonly Vector2 FailedTextPosition = new(420, 156);
        }

        public static class Jacket
        {
            public static readonly Vector2 PanelPosition = new(467, 287);
            public static readonly Rectangle PreviewDestination = new(519, 338, 245, 245);
        }

        public static class SkillPanel
        {
            public static readonly Vector2 PanelPosition = new(180, 260);
            public static readonly Vector2 LevelPosition = new(198, 550);
            public static readonly Vector2 PlayingSkillPosition = new(238, 537);
            public static readonly Vector2 GameSkillPosition = new(268, 623);
            public static readonly Vector2 PerfectCountPosition = new(260, 332);
            public static readonly Vector2 GreatCountPosition = new(260, 362);
            public static readonly Vector2 GoodCountPosition = new(260, 392);
            public static readonly Vector2 PoorCountPosition = new(260, 422);
            public static readonly Vector2 MissCountPosition = new(260, 452);
            public static readonly Vector2 MaxComboCountPosition = new(260, 482);
            public static readonly Vector2 PerfectPercentPosition = new(347, 332);
            public static readonly Vector2 GreatPercentPosition = new(347, 362);
            public static readonly Vector2 GoodPercentPosition = new(347, 392);
            public static readonly Vector2 PoorPercentPosition = new(347, 422);
            public static readonly Vector2 MissPercentPosition = new(347, 452);
            public static readonly Vector2 MaxComboPercentPosition = new(347, 482);
        }

        public static class Score
        {
            public static readonly Vector2 Position = new(30, 58);
        }

        public static class SongInfo
        {
            public static readonly Vector2 TitlePosition = new(500, 630);
            public static readonly Vector2 ArtistPosition = new(500, 665);
            public const int MaxWidth = 320;
        }

        public static class NewRecord
        {
            public static readonly Vector2 BadgePosition = new(298, 582);
        }
```

- [ ] **Step 6: Run tests to verify they pass**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~TexturePathTests|FullyQualifiedName~ResultUILayoutTests"
```

Expected: tests pass.

- [ ] **Step 7: Commit**

```bash
git add DTXMania.Game/Lib/Resources/TexturePath.cs DTXMania.Game/Lib/UI/Layout/ResultUILayout.cs DTXMania.Test/Resources/TexturePathTests.cs DTXMania.Test/UI/PerformanceUILayoutTests.cs
git commit -m "feat: add result stage NX asset constants"
```

## Task 2: Add `ResultScreenModel`

**Files:**
- Create: `DTXMania.Game/Lib/Stage/Result/ResultScreenModel.cs`
- Create: `DTXMania.Test/Stage/Result/ResultScreenModelTests.cs`

- [ ] **Step 1: Write failing model tests**

Create `DTXMania.Test/Stage/Result/ResultScreenModelTests.cs`:

```csharp
using System.IO;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Stage.Result;
using Xunit;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Stage.Result;

[Trait("Category", "Unit")]
public class ResultScreenModelTests
{
    [Theory]
    [InlineData(95.0, ResultRank.SS, "SS")]
    [InlineData(94.99, ResultRank.S, "S")]
    [InlineData(80.0, ResultRank.S, "S")]
    [InlineData(79.99, ResultRank.A, "A")]
    [InlineData(73.0, ResultRank.A, "A")]
    [InlineData(72.99, ResultRank.B, "B")]
    [InlineData(63.0, ResultRank.B, "B")]
    [InlineData(62.99, ResultRank.C, "C")]
    [InlineData(53.0, ResultRank.C, "C")]
    [InlineData(52.99, ResultRank.D, "D")]
    [InlineData(45.0, ResultRank.D, "D")]
    [InlineData(44.99, ResultRank.E, "E")]
    public void Create_ShouldComputeNXRankFromPlayingSkill(double skill, ResultRank expectedRank, string expectedLabel)
    {
        var model = ResultScreenModel.Create(
            Summary(playingSkill: skill, totalNotes: 100),
            selectedSong: null,
            selectedDifficulty: 0,
            chart: null,
            previousScore: null);

        Assert.Equal(expectedRank, model.Rank);
        Assert.Equal(expectedLabel, model.RankLabel);
    }

    [Fact]
    public void Create_AllPerfect_ShouldSelectExcellentPlate()
    {
        var model = ResultScreenModel.Create(
            Summary(perfect: 100, totalNotes: 100, poor: 0, miss: 0, clear: true),
            null,
            0,
            null,
            null);

        Assert.Equal(ResultPlateKind.Excellent, model.PlateKind);
    }

    [Fact]
    public void Create_ClearNoPoorNoMiss_ShouldSelectFullComboPlate()
    {
        var model = ResultScreenModel.Create(
            Summary(perfect: 90, great: 10, totalNotes: 100, poor: 0, miss: 0, clear: true),
            null,
            0,
            null,
            null);

        Assert.Equal(ResultPlateKind.FullCombo, model.PlateKind);
    }

    [Fact]
    public void Create_ClearWithMiss_ShouldSelectStageClearedPlate()
    {
        var model = ResultScreenModel.Create(
            Summary(perfect: 90, great: 9, miss: 1, totalNotes: 100, clear: true),
            null,
            0,
            null,
            null);

        Assert.Equal(ResultPlateKind.StageCleared, model.PlateKind);
    }

    [Fact]
    public void Create_NotClear_ShouldSelectFailedPlate()
    {
        var model = ResultScreenModel.Create(
            Summary(perfect: 20, miss: 80, totalNotes: 100, clear: false),
            null,
            0,
            null,
            null);

        Assert.Equal(ResultPlateKind.Failed, model.PlateKind);
    }

    [Fact]
    public void Create_ShouldFormatCoreValues()
    {
        var model = ResultScreenModel.Create(
            Summary(score: 123456, maxCombo: 87, perfect: 70, great: 20, good: 5, poor: 3, miss: 2, totalNotes: 100, playingSkill: 76.543, gameSkill: 133.456, level: 78, levelDec: 33),
            null,
            0,
            null,
            null);

        Assert.Equal("0123456", model.ScoreText);
        Assert.Equal("87", model.MaxComboText);
        Assert.Equal("70", model.PerfectCountText);
        Assert.Equal("70%", model.PerfectPercentText);
        Assert.Equal("2", model.MissCountText);
        Assert.Equal("2%", model.MissPercentText);
        Assert.Equal("76.54", model.PlayingSkillText);
        Assert.Equal("133.46", model.GameSkillText);
        Assert.Equal("8.13", model.ChartLevelText);
    }

    [Fact]
    public void Create_NoTotalNotes_ShouldFormatZeroPercents()
    {
        var model = ResultScreenModel.Create(
            Summary(perfect: 1, great: 1, totalNotes: 0),
            null,
            0,
            null,
            null);

        Assert.Equal("0%", model.PerfectPercentText);
        Assert.Equal("0%", model.GreatPercentText);
        Assert.Equal("0%", model.MaxComboPercentText);
    }

    [Fact]
    public void Create_WithSongAndChart_ShouldUseMetadataAndPreviewPath()
    {
        var chartDirectory = Path.Combine(Path.GetTempPath(), "dtx-result-test");
        var chart = new SongChart
        {
            FilePath = Path.Combine(chartDirectory, "chart.dtx"),
            PreviewImage = "jacket.png",
            DrumLevel = 80,
            DrumLevelDec = 50
        };
        var song = new SongListNode
        {
            Title = "Fallback Title",
            DatabaseSong = new SongEntity { Title = "Song Title", Artist = "Song Artist" },
            DatabaseChart = chart
        };

        var model = ResultScreenModel.Create(Summary(), song, 0, chart, null);

        Assert.Equal("Song Title", model.Title);
        Assert.Equal("Song Artist", model.Artist);
        Assert.Equal(Path.Combine(chartDirectory, "jacket.png"), model.PreviewImagePath);
        Assert.Equal("8.50", model.ChartLevelText);
    }

    [Fact]
    public void Create_WithoutPreviewImage_ShouldUseDefaultPreview()
    {
        var model = ResultScreenModel.Create(Summary(), null, 0, null, null);

        Assert.Equal(TexturePath.ResultDefaultPreview, model.PreviewImagePath);
    }

    [Fact]
    public void Create_WithPreviousScore_ShouldDetectNewRecord()
    {
        var previous = new SongScore
        {
            PlayCount = 4,
            BestScore = 500000,
            HighSkill = 90.0
        };

        var model = ResultScreenModel.Create(
            Summary(score: 500001, gameSkill: 89.0),
            null,
            0,
            null,
            previous);

        Assert.True(model.NewRecord);
    }

    [Fact]
    public void Create_WithPreviousBetterScoreAndSkill_ShouldNotMarkNewRecord()
    {
        var previous = new SongScore
        {
            PlayCount = 4,
            BestScore = 900000,
            HighSkill = 200.0
        };

        var model = ResultScreenModel.Create(
            Summary(score: 800000, gameSkill: 199.0),
            null,
            0,
            null,
            previous);

        Assert.False(model.NewRecord);
    }

    private static PerformanceSummary Summary(
        int score = 0,
        int maxCombo = 0,
        bool clear = true,
        int perfect = 0,
        int great = 0,
        int good = 0,
        int poor = 0,
        int miss = 0,
        int totalNotes = 0,
        double playingSkill = 0.0,
        double gameSkill = 0.0,
        int level = 0,
        int levelDec = 0)
    {
        return new PerformanceSummary
        {
            Score = score,
            MaxCombo = maxCombo,
            ClearFlag = clear,
            PerfectCount = perfect,
            GreatCount = great,
            GoodCount = good,
            PoorCount = poor,
            MissCount = miss,
            TotalNotes = totalNotes,
            PlayingSkill = playingSkill,
            GameSkill = gameSkill,
            ChartLevel = level,
            ChartLevelDec = levelDec
        };
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ResultScreenModelTests"
```

Expected: build fails because `ResultScreenModel`, `ResultRank`, and `ResultPlateKind` do not exist.

- [ ] **Step 3: Add the model implementation**

Create `DTXMania.Game/Lib/Stage/Result/ResultScreenModel.cs`:

```csharp
#nullable enable

using System;
using System.Globalization;
using System.IO;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;

namespace DTXMania.Game.Lib.Stage.Result
{
    public enum ResultRank
    {
        SS = 0,
        S = 1,
        A = 2,
        B = 3,
        C = 4,
        D = 5,
        E = 6
    }

    public enum ResultPlateKind
    {
        Excellent,
        FullCombo,
        StageCleared,
        Failed
    }

    public sealed class ResultScreenModel
    {
        private ResultScreenModel() { }

        public PerformanceSummary Summary { get; private init; } = new();
        public ResultRank Rank { get; private init; }
        public string RankLabel { get; private init; } = "E";
        public ResultPlateKind PlateKind { get; private init; }
        public string ScoreText { get; private init; } = "0000000";
        public string MaxComboText { get; private init; } = "0";
        public string PerfectCountText { get; private init; } = "0";
        public string GreatCountText { get; private init; } = "0";
        public string GoodCountText { get; private init; } = "0";
        public string PoorCountText { get; private init; } = "0";
        public string MissCountText { get; private init; } = "0";
        public string PerfectPercentText { get; private init; } = "0%";
        public string GreatPercentText { get; private init; } = "0%";
        public string GoodPercentText { get; private init; } = "0%";
        public string PoorPercentText { get; private init; } = "0%";
        public string MissPercentText { get; private init; } = "0%";
        public string MaxComboPercentText { get; private init; } = "0%";
        public string PlayingSkillText { get; private init; } = "0.00";
        public string GameSkillText { get; private init; } = "0.00";
        public string ChartLevelText { get; private init; } = "--";
        public string Title { get; private init; } = "Unknown Song";
        public string Artist { get; private init; } = "Unknown Artist";
        public string PreviewImagePath { get; private init; } = TexturePath.ResultDefaultPreview;
        public bool NewRecord { get; private init; }

        public static ResultScreenModel Create(
            PerformanceSummary? summary,
            SongListNode? selectedSong,
            int selectedDifficulty,
            SongChart? chart,
            SongScore? previousScore)
        {
            var safeSummary = summary ?? new PerformanceSummary();
            var rank = ComputeRank(safeSummary.PlayingSkill);

            return new ResultScreenModel
            {
                Summary = safeSummary,
                Rank = rank,
                RankLabel = GetRankLabel(rank),
                PlateKind = ComputePlateKind(safeSummary),
                ScoreText = FormatScore(safeSummary.Score),
                MaxComboText = FormatCount(safeSummary.MaxCombo),
                PerfectCountText = FormatCount(safeSummary.PerfectCount),
                GreatCountText = FormatCount(safeSummary.GreatCount),
                GoodCountText = FormatCount(safeSummary.GoodCount),
                PoorCountText = FormatCount(safeSummary.PoorCount),
                MissCountText = FormatCount(safeSummary.MissCount),
                PerfectPercentText = FormatPercent(safeSummary.PerfectCount, safeSummary.TotalNotes),
                GreatPercentText = FormatPercent(safeSummary.GreatCount, safeSummary.TotalNotes),
                GoodPercentText = FormatPercent(safeSummary.GoodCount, safeSummary.TotalNotes),
                PoorPercentText = FormatPercent(safeSummary.PoorCount, safeSummary.TotalNotes),
                MissPercentText = FormatPercent(safeSummary.MissCount, safeSummary.TotalNotes),
                MaxComboPercentText = FormatPercent(safeSummary.MaxCombo, safeSummary.TotalNotes),
                PlayingSkillText = FormatDecimal(safeSummary.PlayingSkill),
                GameSkillText = FormatDecimal(safeSummary.GameSkill),
                ChartLevelText = FormatLevel(chart?.DrumLevel ?? safeSummary.ChartLevel, chart?.DrumLevelDec ?? safeSummary.ChartLevelDec),
                Title = ResolveTitle(selectedSong),
                Artist = ResolveArtist(selectedSong),
                PreviewImagePath = ResolvePreviewImagePath(chart),
                NewRecord = IsNewRecord(safeSummary, previousScore)
            };
        }

        public static ResultRank ComputeRank(double playingSkill)
        {
            if (playingSkill >= 95.0) return ResultRank.SS;
            if (playingSkill >= 80.0) return ResultRank.S;
            if (playingSkill >= 73.0) return ResultRank.A;
            if (playingSkill >= 63.0) return ResultRank.B;
            if (playingSkill >= 53.0) return ResultRank.C;
            if (playingSkill >= 45.0) return ResultRank.D;
            return ResultRank.E;
        }

        public static ResultPlateKind ComputePlateKind(PerformanceSummary summary)
        {
            if (summary.TotalNotes > 0 && summary.PerfectCount == summary.TotalNotes)
                return ResultPlateKind.Excellent;

            if (summary.ClearFlag && summary.PoorCount == 0 && summary.MissCount == 0)
                return ResultPlateKind.FullCombo;

            if (summary.ClearFlag)
                return ResultPlateKind.StageCleared;

            return ResultPlateKind.Failed;
        }

        public static string GetRankLabel(ResultRank rank)
        {
            return rank switch
            {
                ResultRank.SS => "SS",
                ResultRank.S => "S",
                ResultRank.A => "A",
                ResultRank.B => "B",
                ResultRank.C => "C",
                ResultRank.D => "D",
                ResultRank.E => "E",
                _ => "E"
            };
        }

        public static string FormatScore(int score)
        {
            return Math.Clamp(score, 0, 9_999_999).ToString("0000000", CultureInfo.InvariantCulture);
        }

        public static string FormatCount(int count)
        {
            return Math.Max(0, count).ToString(CultureInfo.InvariantCulture);
        }

        public static string FormatPercent(int value, int total)
        {
            if (total <= 0)
                return "0%";

            var percent = (int)Math.Round(value * 100.0 / total, MidpointRounding.AwayFromZero);
            return $"{Math.Clamp(percent, 0, 100)}%";
        }

        public static string FormatDecimal(double value)
        {
            return Math.Max(0.0, value).ToString("0.00", CultureInfo.InvariantCulture);
        }

        public static string FormatLevel(int level, int levelDec)
        {
            if (level <= 0)
                return "--";

            double actual = level >= 100
                ? level / 100.0
                : (level / 10.0) + (levelDec / 100.0);

            return actual.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string ResolveTitle(SongListNode? selectedSong)
        {
            if (!string.IsNullOrWhiteSpace(selectedSong?.DatabaseSong?.Title))
                return selectedSong.DatabaseSong.Title;

            if (!string.IsNullOrWhiteSpace(selectedSong?.DisplayTitle))
                return selectedSong.DisplayTitle;

            return "Unknown Song";
        }

        private static string ResolveArtist(SongListNode? selectedSong)
        {
            if (!string.IsNullOrWhiteSpace(selectedSong?.DatabaseSong?.Artist))
                return selectedSong.DatabaseSong.Artist;

            return "Unknown Artist";
        }

        private static string ResolvePreviewImagePath(SongChart? chart)
        {
            if (chart == null || string.IsNullOrWhiteSpace(chart.PreviewImage))
                return TexturePath.ResultDefaultPreview;

            if (Path.IsPathRooted(chart.PreviewImage))
                return chart.PreviewImage;

            var chartDirectory = !string.IsNullOrWhiteSpace(chart.FilePath)
                ? Path.GetDirectoryName(chart.FilePath)
                : null;

            return string.IsNullOrWhiteSpace(chartDirectory)
                ? chart.PreviewImage
                : Path.Combine(chartDirectory, chart.PreviewImage);
        }

        private static bool IsNewRecord(PerformanceSummary summary, SongScore? previousScore)
        {
            if (previousScore == null)
                return false;

            if (previousScore.PlayCount <= 0)
                return summary.Score > 0 || summary.GameSkill > 0.0;

            return summary.Score > previousScore.BestScore
                || summary.GameSkill > previousScore.HighSkill;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ResultScreenModelTests"
```

Expected: tests pass.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Stage/Result/ResultScreenModel.cs DTXMania.Test/Stage/Result/ResultScreenModelTests.cs
git commit -m "feat: add result screen model"
```

## Task 3: Add Reveal State

**Files:**
- Create: `DTXMania.Game/Lib/Stage/Result/ResultRevealState.cs`
- Create: `DTXMania.Test/Stage/Result/ResultRevealStateTests.cs`

- [ ] **Step 1: Write failing reveal state tests**

Create `DTXMania.Test/Stage/Result/ResultRevealStateTests.cs`:

```csharp
using DTXMania.Game.Lib.Stage.Result;
using Xunit;

namespace DTXMania.Test.Stage.Result;

[Trait("Category", "Unit")]
public class ResultRevealStateTests
{
    [Fact]
    public void NewState_ShouldStartAtZeroProgress()
    {
        var state = new ResultRevealState();

        Assert.Equal(0.0, state.ElapsedSeconds);
        Assert.Equal(0.0f, state.RankProgress);
        Assert.Equal(0.0f, state.PanelProgress);
        Assert.False(state.IsComplete);
    }

    [Fact]
    public void Update_ShouldAdvanceRankBeforePanel()
    {
        var state = new ResultRevealState();

        state.Update(ResultRevealState.RankRevealSeconds / 2.0);

        Assert.InRange(state.RankProgress, 0.49f, 0.51f);
        Assert.Equal(0.0f, state.PanelProgress);
        Assert.False(state.IsComplete);
    }

    [Fact]
    public void Update_AfterRankAndDelay_ShouldAdvancePanel()
    {
        var state = new ResultRevealState();

        state.Update(ResultRevealState.RankRevealSeconds + ResultRevealState.PanelDelaySeconds + ResultRevealState.PanelRevealSeconds / 2.0);

        Assert.Equal(1.0f, state.RankProgress);
        Assert.InRange(state.PanelProgress, 0.49f, 0.51f);
        Assert.False(state.IsComplete);
    }

    [Fact]
    public void Update_PastTotalDuration_ShouldClampAndComplete()
    {
        var state = new ResultRevealState();

        state.Update(ResultRevealState.TotalRevealSeconds + 10.0);

        Assert.Equal(ResultRevealState.TotalRevealSeconds, state.ElapsedSeconds);
        Assert.Equal(1.0f, state.RankProgress);
        Assert.Equal(1.0f, state.PanelProgress);
        Assert.True(state.IsComplete);
    }

    [Fact]
    public void Update_WithNegativeDelta_ShouldNotMoveBackwards()
    {
        var state = new ResultRevealState();

        state.Update(0.25);
        state.Update(-1.0);

        Assert.Equal(0.25, state.ElapsedSeconds);
    }

    [Fact]
    public void Complete_ShouldJumpToEnd()
    {
        var state = new ResultRevealState();

        state.Complete();

        Assert.Equal(ResultRevealState.TotalRevealSeconds, state.ElapsedSeconds);
        Assert.True(state.IsComplete);
        Assert.Equal(1.0f, state.RankProgress);
        Assert.Equal(1.0f, state.PanelProgress);
    }

    [Fact]
    public void Reset_ShouldReturnToBeginning()
    {
        var state = new ResultRevealState();
        state.Complete();

        state.Reset();

        Assert.Equal(0.0, state.ElapsedSeconds);
        Assert.False(state.IsComplete);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ResultRevealStateTests"
```

Expected: build fails because `ResultRevealState` does not exist.

- [ ] **Step 3: Add reveal state implementation**

Create `DTXMania.Game/Lib/Stage/Result/ResultRevealState.cs`:

```csharp
#nullable enable

using System;

namespace DTXMania.Game.Lib.Stage.Result
{
    public sealed class ResultRevealState
    {
        public const double RankRevealSeconds = 0.5;
        public const double PanelDelaySeconds = 0.15;
        public const double PanelRevealSeconds = 0.5;
        public const double TotalRevealSeconds = RankRevealSeconds + PanelDelaySeconds + PanelRevealSeconds;

        public double ElapsedSeconds { get; private set; }

        public bool IsComplete => ElapsedSeconds >= TotalRevealSeconds;

        public float RankProgress => Clamp01(ElapsedSeconds / RankRevealSeconds);

        public float PanelProgress => Clamp01(
            (ElapsedSeconds - RankRevealSeconds - PanelDelaySeconds) / PanelRevealSeconds);

        public void Update(double deltaSeconds)
        {
            if (deltaSeconds <= 0.0)
                return;

            ElapsedSeconds = Math.Min(TotalRevealSeconds, ElapsedSeconds + deltaSeconds);
        }

        public void Complete()
        {
            ElapsedSeconds = TotalRevealSeconds;
        }

        public void Reset()
        {
            ElapsedSeconds = 0.0;
        }

        private static float Clamp01(double value)
        {
            if (value <= 0.0) return 0.0f;
            if (value >= 1.0) return 1.0f;
            return (float)value;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ResultRevealStateTests"
```

Expected: tests pass.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Stage/Result/ResultRevealState.cs DTXMania.Test/Stage/Result/ResultRevealStateTests.cs
git commit -m "feat: add result reveal timing"
```

## Task 4: Add Result Screen Renderer

**Files:**
- Create: `DTXMania.Game/Lib/Stage/Result/ResultScreenRenderer.cs`
- Create: `DTXMania.Test/Stage/Result/ResultScreenRendererTests.cs`

- [ ] **Step 1: Write failing renderer tests**

Create `DTXMania.Test/Stage/Result/ResultScreenRendererTests.cs`:

```csharp
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Stage.Result;
using Microsoft.Xna.Framework;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Result;

[Trait("Category", "Unit")]
public class ResultScreenRendererTests
{
    [Fact]
    public void CreateViewportTransform_ExactNXSize_ShouldUseIdentityScale()
    {
        var matrix = ResultScreenRenderer.CreateViewportTransform(new Viewport(0, 0, 1280, 720));

        Assert.Equal(1.0f, matrix.M11, 3);
        Assert.Equal(1.0f, matrix.M22, 3);
        Assert.Equal(0.0f, matrix.M41, 3);
        Assert.Equal(0.0f, matrix.M42, 3);
    }

    [Fact]
    public void CreateViewportTransform_FourByThreeViewport_ShouldLetterboxVertically()
    {
        var matrix = ResultScreenRenderer.CreateViewportTransform(new Viewport(0, 0, 1024, 768));

        Assert.Equal(0.8f, matrix.M11, 3);
        Assert.Equal(0.8f, matrix.M22, 3);
        Assert.Equal(0.0f, matrix.M41, 3);
        Assert.Equal(96.0f, matrix.M42, 3);
    }

    [Fact]
    public void Load_ShouldLoadStructuralAssetsThatExist()
    {
        var resources = new Mock<IResourceManager>();
        var texture = new Mock<ITexture>();
        resources.Setup(r => r.ResourceExists(It.IsAny<string>())).Returns(true);
        resources.Setup(r => r.LoadTexture(It.IsAny<string>())).Returns(texture.Object);
        var renderer = new ResultScreenRenderer(resources.Object, null, null, null);
        var model = ResultScreenModel.Create(
            new PerformanceSummary { ClearFlag = true, PerfectCount = 100, TotalNotes = 100, PlayingSkill = 100.0 },
            null,
            0,
            null,
            null);

        renderer.Load(model);

        resources.Verify(r => r.LoadTexture(TexturePath.ResultBackground), Times.Once);
        resources.Verify(r => r.LoadTexture(TexturePath.ResultBackgroundRankSS), Times.Once);
        resources.Verify(r => r.LoadTexture(TexturePath.ResultRankSS), Times.Once);
        resources.Verify(r => r.LoadTexture(TexturePath.ResultPlateExcellent), Times.Once);
        resources.Verify(r => r.LoadTexture(TexturePath.ResultJacketPanel), Times.Once);
        resources.Verify(r => r.LoadTexture(TexturePath.ResultSkillPanel), Times.Once);
        resources.Verify(r => r.LoadTexture(TexturePath.ResultDefaultPreview), Times.Once);
    }

    [Fact]
    public void Load_FailedPlate_ShouldNotLoadClearPlateTexture()
    {
        var resources = new Mock<IResourceManager>();
        var texture = new Mock<ITexture>();
        resources.Setup(r => r.ResourceExists(It.IsAny<string>())).Returns(true);
        resources.Setup(r => r.LoadTexture(It.IsAny<string>())).Returns(texture.Object);
        var renderer = new ResultScreenRenderer(resources.Object, null, null, null);
        var model = ResultScreenModel.Create(
            new PerformanceSummary { ClearFlag = false, MissCount = 1, TotalNotes = 1 },
            null,
            0,
            null,
            null);

        renderer.Load(model);

        resources.Verify(r => r.LoadTexture(TexturePath.ResultPlateExcellent), Times.Never);
        resources.Verify(r => r.LoadTexture(TexturePath.ResultPlateFullCombo), Times.Never);
        resources.Verify(r => r.LoadTexture(TexturePath.ResultPlateStageCleared), Times.Never);
    }

    [Fact]
    public void Dispose_ShouldReleaseLoadedTextures()
    {
        var resources = new Mock<IResourceManager>();
        var texture = new Mock<ITexture>();
        resources.Setup(r => r.ResourceExists(It.IsAny<string>())).Returns(true);
        resources.Setup(r => r.LoadTexture(It.IsAny<string>())).Returns(texture.Object);
        var renderer = new ResultScreenRenderer(resources.Object, null, null, null);
        var model = ResultScreenModel.Create(
            new PerformanceSummary { ClearFlag = true, PerfectCount = 100, TotalNotes = 100, PlayingSkill = 100.0 },
            null,
            0,
            null,
            null);

        renderer.Load(model);
        renderer.Dispose();

        texture.Verify(t => t.RemoveReference(), Times.AtLeastOnce);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ResultScreenRendererTests"
```

Expected: build fails because `ResultScreenRenderer` does not exist.

- [ ] **Step 3: Add renderer implementation**

Create `DTXMania.Game/Lib/Stage/Result/ResultScreenRenderer.cs`:

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.Stage.Result
{
    public sealed class ResultScreenRenderer : IDisposable
    {
        private readonly IResourceManager _resources;
        private readonly IFont? _smallFont;
        private readonly IFont? _normalFont;
        private readonly IFont? _largeFont;
        private readonly List<ITexture> _loadedTextures = new();

        private ITexture? _backgroundTexture;
        private ITexture? _rankBackgroundTexture;
        private ITexture? _rankTexture;
        private ITexture? _plateTexture;
        private ITexture? _jacketPanelTexture;
        private ITexture? _skillPanelTexture;
        private ITexture? _previewTexture;
        private ITexture? _newRecordTexture;
        private bool _disposed;

        public ResultScreenRenderer(
            IResourceManager resources,
            IFont? smallFont,
            IFont? normalFont,
            IFont? largeFont)
        {
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            _smallFont = smallFont;
            _normalFont = normalFont;
            _largeFont = largeFont;
        }

        public static Matrix CreateViewportTransform(Viewport viewport)
        {
            float scaleX = viewport.Width / (float)ResultUILayout.NXViewport.Width;
            float scaleY = viewport.Height / (float)ResultUILayout.NXViewport.Height;
            float scale = Math.Min(scaleX, scaleY);
            float offsetX = (viewport.Width - ResultUILayout.NXViewport.Width * scale) / 2.0f;
            float offsetY = (viewport.Height - ResultUILayout.NXViewport.Height * scale) / 2.0f;

            return Matrix.CreateScale(scale, scale, 1.0f)
                * Matrix.CreateTranslation(offsetX, offsetY, 0.0f);
        }

        public void Load(ResultScreenModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            ReleaseLoadedTextures();

            _backgroundTexture = LoadIfExists(TexturePath.ResultBackground);
            _rankBackgroundTexture = LoadIfExists(GetRankBackgroundPath(model.Rank));
            _rankTexture = LoadIfExists(GetRankTexturePath(model.Rank));
            _plateTexture = model.PlateKind == ResultPlateKind.Failed
                ? null
                : LoadIfExists(GetPlateTexturePath(model.PlateKind));
            _jacketPanelTexture = LoadIfExists(TexturePath.ResultJacketPanel);
            _skillPanelTexture = LoadIfExists(TexturePath.ResultSkillPanel);
            _previewTexture = LoadIfExists(model.PreviewImagePath) ?? LoadIfExists(TexturePath.ResultDefaultPreview);
            _newRecordTexture = model.NewRecord ? LoadIfExists(TexturePath.ResultNewRecord) : null;
        }

        [ExcludeFromCodeCoverage]
        public void Draw(SpriteBatch spriteBatch, ResultScreenModel model, ResultRevealState reveal)
        {
            if (_disposed || spriteBatch == null || model == null || reveal == null)
                return;

            DrawTexture(spriteBatch, _backgroundTexture, Vector2.Zero);
            DrawTexture(spriteBatch, _rankBackgroundTexture, Vector2.Zero);
            DrawRank(spriteBatch, reveal);

            if (reveal.PanelProgress > 0.0f)
            {
                DrawTexture(spriteBatch, _plateTexture, ResultUILayout.ResultPlate.Position);
                if (model.PlateKind == ResultPlateKind.Failed)
                    DrawText(spriteBatch, _largeFont, "FAILED", ResultUILayout.ResultPlate.FailedTextPosition, Color.Red);

                DrawTexture(spriteBatch, _jacketPanelTexture, ResultUILayout.Jacket.PanelPosition);
                DrawPreview(spriteBatch);
                DrawTexture(spriteBatch, _skillPanelTexture, ResultUILayout.SkillPanel.PanelPosition);
                DrawModelText(spriteBatch, model);

                if (model.NewRecord)
                    DrawTexture(spriteBatch, _newRecordTexture, ResultUILayout.NewRecord.BadgePosition);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            ReleaseLoadedTextures();
            _disposed = true;
        }

        private ITexture? LoadIfExists(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !_resources.ResourceExists(path))
                return null;

            try
            {
                var texture = _resources.LoadTexture(path);
                if (texture != null)
                    _loadedTextures.Add(texture);
                return texture;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResultScreenRenderer: Failed to load texture '{path}': {ex.Message}");
                return null;
            }
        }

        private void ReleaseLoadedTextures()
        {
            foreach (var texture in _loadedTextures)
            {
                texture.RemoveReference();
            }

            _loadedTextures.Clear();
            _backgroundTexture = null;
            _rankBackgroundTexture = null;
            _rankTexture = null;
            _plateTexture = null;
            _jacketPanelTexture = null;
            _skillPanelTexture = null;
            _previewTexture = null;
            _newRecordTexture = null;
        }

        [ExcludeFromCodeCoverage]
        private static void DrawTexture(SpriteBatch spriteBatch, ITexture? texture, Vector2 position)
        {
            texture?.Draw(spriteBatch, position);
        }

        [ExcludeFromCodeCoverage]
        private void DrawRank(SpriteBatch spriteBatch, ResultRevealState reveal)
        {
            if (_rankTexture == null || reveal.RankProgress <= 0.0f)
                return;

            int visibleHeight = Math.Clamp(
                (int)Math.Round(_rankTexture.Height * reveal.RankProgress),
                0,
                _rankTexture.Height);

            if (visibleHeight <= 0)
                return;

            var source = new Rectangle(
                0,
                _rankTexture.Height - visibleHeight,
                _rankTexture.Width,
                visibleHeight);
            var destination = new Rectangle(
                (int)ResultUILayout.Rank.BadgePosition.X,
                (int)ResultUILayout.Rank.BadgePosition.Y + _rankTexture.Height - visibleHeight,
                _rankTexture.Width,
                visibleHeight);

            _rankTexture.Draw(spriteBatch, destination, source, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0f);
        }

        [ExcludeFromCodeCoverage]
        private void DrawPreview(SpriteBatch spriteBatch)
        {
            if (_previewTexture == null)
                return;

            _previewTexture.Draw(
                spriteBatch,
                ResultUILayout.Jacket.PreviewDestination,
                null,
                Color.White,
                0f,
                Vector2.Zero,
                SpriteEffects.None,
                0f);
        }

        [ExcludeFromCodeCoverage]
        private void DrawModelText(SpriteBatch spriteBatch, ResultScreenModel model)
        {
            DrawText(spriteBatch, _largeFont, model.ScoreText, ResultUILayout.Score.Position, Color.White);
            DrawText(spriteBatch, _normalFont, model.Title, ResultUILayout.SongInfo.TitlePosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.Artist, ResultUILayout.SongInfo.ArtistPosition, Color.LightGray);

            DrawText(spriteBatch, _normalFont, model.ChartLevelText, ResultUILayout.SkillPanel.LevelPosition, Color.White);
            DrawText(spriteBatch, _normalFont, model.PlayingSkillText, ResultUILayout.SkillPanel.PlayingSkillPosition, Color.White);
            DrawText(spriteBatch, _normalFont, model.GameSkillText, ResultUILayout.SkillPanel.GameSkillPosition, Color.White);

            DrawText(spriteBatch, _smallFont, model.PerfectCountText, ResultUILayout.SkillPanel.PerfectCountPosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.GreatCountText, ResultUILayout.SkillPanel.GreatCountPosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.GoodCountText, ResultUILayout.SkillPanel.GoodCountPosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.PoorCountText, ResultUILayout.SkillPanel.PoorCountPosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.MissCountText, ResultUILayout.SkillPanel.MissCountPosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.MaxComboText, ResultUILayout.SkillPanel.MaxComboCountPosition, Color.White);

            DrawText(spriteBatch, _smallFont, model.PerfectPercentText, ResultUILayout.SkillPanel.PerfectPercentPosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.GreatPercentText, ResultUILayout.SkillPanel.GreatPercentPosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.GoodPercentText, ResultUILayout.SkillPanel.GoodPercentPosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.PoorPercentText, ResultUILayout.SkillPanel.PoorPercentPosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.MissPercentText, ResultUILayout.SkillPanel.MissPercentPosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.MaxComboPercentText, ResultUILayout.SkillPanel.MaxComboPercentPosition, Color.White);
        }

        [ExcludeFromCodeCoverage]
        private static void DrawText(SpriteBatch spriteBatch, IFont? font, string text, Vector2 position, Color color)
        {
            if (font == null || string.IsNullOrEmpty(text))
                return;

            font.DrawString(spriteBatch, text, position, color);
        }

        private static string GetRankTexturePath(ResultRank rank)
        {
            return rank switch
            {
                ResultRank.SS => TexturePath.ResultRankSS,
                ResultRank.S => TexturePath.ResultRankS,
                ResultRank.A => TexturePath.ResultRankA,
                ResultRank.B => TexturePath.ResultRankB,
                ResultRank.C => TexturePath.ResultRankC,
                ResultRank.D => TexturePath.ResultRankD,
                ResultRank.E => TexturePath.ResultRankE,
                _ => TexturePath.ResultRankE
            };
        }

        private static string GetRankBackgroundPath(ResultRank rank)
        {
            return rank switch
            {
                ResultRank.SS => TexturePath.ResultBackgroundRankSS,
                ResultRank.S => TexturePath.ResultBackgroundRankS,
                ResultRank.A => TexturePath.ResultBackgroundRankA,
                ResultRank.B => TexturePath.ResultBackgroundRankB,
                ResultRank.C => TexturePath.ResultBackgroundRankC,
                ResultRank.D => TexturePath.ResultBackgroundRankD,
                ResultRank.E => TexturePath.ResultBackgroundRankE,
                _ => TexturePath.ResultBackgroundRankE
            };
        }

        private static string GetPlateTexturePath(ResultPlateKind plateKind)
        {
            return plateKind switch
            {
                ResultPlateKind.Excellent => TexturePath.ResultPlateExcellent,
                ResultPlateKind.FullCombo => TexturePath.ResultPlateFullCombo,
                ResultPlateKind.StageCleared => TexturePath.ResultPlateStageCleared,
                _ => TexturePath.ResultPlateStageCleared
            };
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ResultScreenRendererTests"
```

Expected: tests pass.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Stage/Result/ResultScreenRenderer.cs DTXMania.Test/Stage/Result/ResultScreenRendererTests.cs
git commit -m "feat: add result screen renderer"
```

## Task 5: Wire NX Result Screen Into `ResultStage`

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/ResultStage.cs`
- Modify: `DTXMania.Test/Stage/ResultStageTests.cs`
- Modify: `DTXMania.Test/Stage/ResultStageAdditionalCoverageTests.cs`

- [ ] **Step 1: Write failing stage behavior tests**

In `DTXMania.Test/Stage/ResultStageTests.cs`, add these tests near the existing input tests:

```csharp
[Fact]
public void ExecuteInputCommand_WhenRevealIncomplete_ShouldCompleteRevealWithoutNavigating()
{
#pragma warning disable SYSLIB0050
    var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
    var game = DTXMania.Test.TestData.ReflectionHelpers.CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
    var stageManager = new Mock<IStageManager>();
    var reveal = new DTXMania.Game.Lib.Stage.Result.ResultRevealState();

    SetPrivateField(stage, "_game", game);
    stage.StageManager = stageManager.Object;
    SetPrivateField(stage, "_revealState", reveal);

    InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Activate, 0.0));

    Assert.True(reveal.IsComplete);
    stageManager.Verify(
        manager => manager.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
        Times.Never);
}

[Fact]
public void OnUpdate_WhenRevealCompletesAndNewRecordSoundExists_ShouldPlayNewRecordSoundOnce()
{
#pragma warning disable SYSLIB0050
    var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
    var sound = new Mock<ISound>();
    var model = DTXMania.Game.Lib.Stage.Result.ResultScreenModel.Create(
        new PerformanceSummary { Score = 900000, GameSkill = 100.0 },
        null,
        0,
        null,
        new SongScore { PlayCount = 1, BestScore = 100, HighSkill = 1.0 });
    var reveal = new DTXMania.Game.Lib.Stage.Result.ResultRevealState();

    SetPrivateField(stage, "_inputManager", null);
    SetPrivateField(stage, "_uiManager", new UIManager());
    SetPrivateField(stage, "_elapsedTime", 0.0);
    SetPrivateField(stage, "_resultModel", model);
    SetPrivateField(stage, "_revealState", reveal);
    SetPrivateField(stage, "_newRecordSound", sound.Object);
    SetPrivateField(stage, "_newRecordSoundPlayed", false);

    InvokePrivateMethod(stage, "OnUpdate", DTXMania.Game.Lib.Stage.Result.ResultRevealState.TotalRevealSeconds);
    InvokePrivateMethod(stage, "OnUpdate", 0.1);

    sound.Verify(s => s.Play(), Times.Once);
}
```

In `DTXMania.Test/Stage/ResultStageAdditionalCoverageTests.cs`, add this activation test:

```csharp
[Fact]
public void OnActivate_ShouldBuildModelBeforePersistenceAndInitializeReveal()
{
#pragma warning disable SYSLIB0050
    var stage = (InspectableNullInputResultStage)FormatterServices.GetUninitializedObject(typeof(InspectableNullInputResultStage));
#pragma warning restore SYSLIB0050
    var summary = new PerformanceSummary
    {
        Score = 123456,
        ClearFlag = true,
        PerfectCount = 10,
        TotalNotes = 10,
        PlayingSkill = 100.0,
        GameSkill = 100.0
    };

    SetPrivateField(stage, "_inputManager", null);
    SetPrivateField(stage, "_sharedData", new Dictionary<string, object>
    {
        ["performanceSummary"] = summary
    });

    var exception = Record.Exception(() => InvokePrivateMethod(stage, "OnActivate"));

    Assert.Null(exception);
    Assert.True(stage.ResultFontRequested);
    Assert.NotNull(GetPrivateField<object>(stage, "_resultModel"));
    Assert.NotNull(GetPrivateField<object>(stage, "_revealState"));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ResultStageTests|FullyQualifiedName~ResultStageAdditionalCoverageTests"
```

Expected: build fails because `ResultStage` has no `_revealState`, `_resultModel`, `_newRecordSound`, or updated behavior.

- [ ] **Step 3: Add new usings and fields to `ResultStage`**

In `DTXMania.Game/Lib/Stage/ResultStage.cs`, add:

```csharp
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Result;
```

Add these fields near the existing UI fields:

```csharp
        private ResultScreenModel _resultModel;
        private ResultRevealState _revealState;
        private ResultScreenRenderer _resultRenderer;
        private IFont _smallResultFont;
        private IFont _largeResultFont;
        private ISound _resultSound;
        private ISound _newRecordSound;
        private bool _newRecordSoundPlayed;
```

Add sound path constants near the fields:

```csharp
        private const string StageClearSoundPath = "Sounds/Stage Clear.ogg";
        private const string FullComboSoundPath = "Sounds/Full Combo.ogg";
        private const string ExcellentSoundPath = "Sounds/Excellent.ogg";
        private const string NewRecordSoundPath = "Sounds/New Record.ogg";
```

- [ ] **Step 4: Replace activation flow**

In `OnActivate`, replace the body before input queue clearing with this sequence:

```csharp
            ExtractSharedData();

            var selectedChart = ResolveSelectedChart();
            var previousScore = ResolvePreviousScore(selectedChart);
            _resultModel = CreateResultScreenModel(_performanceSummary, _selectedSong, _selectedDifficulty, selectedChart, previousScore);

            PersistPerformanceSummary(selectedChart);

            InitializeComponents();
            _revealState = new ResultRevealState();
            _newRecordSoundPlayed = false;
            PlayResultSound();
```

Keep the existing input queue clearing and debug line after this sequence.

Add these helper methods under `Components`:

```csharp
        internal virtual SongChart ResolveSelectedChart()
        {
            return _selectedSong?.GetCurrentDifficultyChart(_selectedDifficulty);
        }

        internal virtual SongScore ResolvePreviousScore(SongChart selectedChart)
        {
            if (_selectedSong?.Scores != null && selectedChart != null)
            {
                foreach (var score in _selectedSong.Scores)
                {
                    if (score?.ChartId == selectedChart.Id && selectedChart.Id != 0)
                        return score;
                }
            }

            return _selectedSong?.GetScore(_selectedDifficulty);
        }

        internal virtual ResultScreenModel CreateResultScreenModel(
            PerformanceSummary summary,
            SongListNode selectedSong,
            int selectedDifficulty,
            SongChart selectedChart,
            SongScore previousScore)
        {
            return ResultScreenModel.Create(summary, selectedSong, selectedDifficulty, selectedChart, previousScore);
        }

        private void PersistPerformanceSummary(SongChart selectedChart)
        {
            if (selectedChart == null || selectedChart.Id <= 0 || _performanceSummary == null)
                return;

            _ = SongManager.Instance.UpdateScoreAsync(
                    selectedChart.Id,
                    EInstrumentPart.DRUMS,
                    _performanceSummary)
                .ContinueWith(
                    task => System.Diagnostics.Debug.WriteLine($"ResultStage: Failed to persist score: {task.Exception?.GetBaseException().Message}"),
                    TaskContinuationOptions.OnlyOnFaulted);
        }
```

- [ ] **Step 5: Update component initialization and cleanup**

Change `CreateResultFont()` to load a 20px font:

```csharp
        internal virtual IFont CreateResultFont()
        {
            return _resourceManager.LoadFont("NotoSerifJP", ResultUILayout.Fonts.Normal);
        }
```

Add:

```csharp
        internal virtual IFont CreateSmallResultFont()
        {
            return _resourceManager.LoadFont("NotoSerifJP", ResultUILayout.Fonts.Small);
        }

        internal virtual IFont CreateLargeResultFont()
        {
            return _resourceManager.LoadFont("NotoSerifJP", ResultUILayout.Fonts.Large, FontStyle.Bold);
        }

        internal virtual ResultScreenRenderer CreateResultRenderer()
        {
            return new ResultScreenRenderer(_resourceManager, _smallResultFont, _resultFont, _largeResultFont);
        }
```

In `InitializeComponents`, after `_resultFont = CreateResultFont();`, add:

```csharp
                _smallResultFont = CreateSmallResultFont();
                _largeResultFont = CreateLargeResultFont();
                _resultRenderer = CreateResultRenderer();
                _resultRenderer.Load(_resultModel);
                _resultSound = LoadSoundForPlate(_resultModel?.PlateKind ?? ResultPlateKind.StageCleared);
                _newRecordSound = _resultModel?.NewRecord == true ? TryLoadSound(NewRecordSoundPath) : null;
```

In the `catch`, clear all font and renderer fields:

```csharp
                _resultFont = null;
                _smallResultFont = null;
                _largeResultFont = null;
                _resultRenderer = null;
```

In `CleanupComponents`, add releases:

```csharp
            _smallResultFont?.RemoveReference();
            _smallResultFont = null;
            _largeResultFont?.RemoveReference();
            _largeResultFont = null;
            _resultRenderer?.Dispose();
            _resultRenderer = null;
            _resultSound?.RemoveReference();
            _resultSound = null;
            _newRecordSound?.RemoveReference();
            _newRecordSound = null;
```

- [ ] **Step 6: Add result sound helpers**

Add these methods under input or components:

```csharp
        private ISound LoadSoundForPlate(ResultPlateKind plateKind)
        {
            return plateKind switch
            {
                ResultPlateKind.Excellent => TryLoadSound(ExcellentSoundPath),
                ResultPlateKind.FullCombo => TryLoadSound(FullComboSoundPath),
                _ => TryLoadSound(StageClearSoundPath)
            };
        }

        private ISound TryLoadSound(string path)
        {
            if (_resourceManager == null || string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                if (!_resourceManager.ResourceExists(path))
                    return null;

                return _resourceManager.LoadSound(path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResultStage: Failed to load sound '{path}': {ex.Message}");
                return null;
            }
        }

        private void PlayResultSound()
        {
            _resultSound?.Play();
        }

        private void PlayNewRecordSoundIfReady()
        {
            if (_newRecordSoundPlayed || _resultModel?.NewRecord != true || _revealState?.IsComplete != true)
                return;

            _newRecordSound?.Play();
            _newRecordSoundPlayed = true;
        }
```

- [ ] **Step 7: Update `OnUpdate` and input handling**

In `OnUpdate`, after `_elapsedTime += deltaTime;`, add:

```csharp
            _revealState?.Update(deltaTime);
            PlayNewRecordSoundIfReady();
```

In `ExecuteInputCommand`, replace the activate/back case body with:

```csharp
                    if (_revealState != null && !_revealState.IsComplete)
                    {
                        _revealState.Complete();
                        PlayNewRecordSoundIfReady();
                        break;
                    }

                    if (_game is BaseGame baseGame && baseGame.CanPerformStageTransition())
                    {
                        baseGame.MarkStageTransition();
                        ReturnToSongSelect();
                    }
                    break;
```

- [ ] **Step 8: Replace drawing**

Replace `OnDraw` with:

```csharp
        [ExcludeFromCodeCoverage]
        protected override void OnDraw(double deltaTime)
        {
            if (_spriteBatch == null)
                return;

            var viewport = _spriteBatch.GraphicsDevice.Viewport;
            var transform = ResultScreenRenderer.CreateViewportTransform(viewport);

            _spriteBatch.Begin(transformMatrix: transform);

            if (_resultRenderer != null && _resultModel != null && _revealState != null)
            {
                _resultRenderer.Draw(_spriteBatch, _resultModel, _revealState);
            }
            else
            {
                DrawBackground();
            }

            _spriteBatch.End();
        }
```

Leave `DrawBackground`, `DrawFallbackBackground`, and `DrawTexture` in place for existing fallback tests. Remove `DrawResults` and `DrawResultLine` only after updating tests that invoke them.

- [ ] **Step 9: Update test subclass overrides**

In `ResultStageTests.cs`, update `InspectableResultStage` with these overrides:

```csharp
            internal override IFont CreateSmallResultFont()
            {
                return null!;
            }

            internal override IFont CreateLargeResultFont()
            {
                return null!;
            }

            internal override ResultScreenRenderer CreateResultRenderer()
            {
                return null!;
            }
```

In `ResultStageAdditionalCoverageTests.cs`, update `InspectableNullInputResultStage` similarly:

```csharp
        internal override IFont CreateSmallResultFont()
        {
            return null!;
        }

        internal override IFont CreateLargeResultFont()
        {
            return null!;
        }

        internal override ResultScreenRenderer CreateResultRenderer()
        {
            return null!;
        }
```

Delete or rewrite the old `DrawResultLine_WhenTextIsEmpty_ShouldNotAdvanceCurrentY` test, because centered text lines are no longer part of result rendering. Replace it with:

```csharp
[Fact]
public void ExecuteInputCommand_WhenRevealAlreadyComplete_ShouldNavigate()
{
    var stage = CreateUninitializedResultStageWithStageManager();
    var reveal = new DTXMania.Game.Lib.Stage.Result.ResultRevealState();
    reveal.Complete();
    SetPrivateField(stage, "_revealState", reveal);

    InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Activate, 0.0));

    VerifySongSelectTransition(stage);
}
```

- [ ] **Step 10: Run focused result-stage tests**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ResultStageTests|FullyQualifiedName~ResultStageAdditionalCoverageTests"
```

Expected: tests pass.

- [ ] **Step 11: Commit**

```bash
git add DTXMania.Game/Lib/Stage/ResultStage.cs DTXMania.Test/Stage/ResultStageTests.cs DTXMania.Test/Stage/ResultStageAdditionalCoverageTests.cs
git commit -m "feat: show NX-style result stage"
```

## Task 6: Verification And Cleanup

**Files:**
- Inspect: `DTXMania.Game/Lib/Stage/ResultStage.cs`
- Inspect: `DTXMania.Game/Lib/Stage/Result/*.cs`
- Inspect: `DTXMania.Test/Stage/Result*.cs`
- Inspect: `DTXMania.Test/Stage/Result/*.cs`

- [ ] **Step 1: Run all focused result tests**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~Result"
```

Expected: all result-related tests pass.

- [ ] **Step 2: Run the Mac test suite**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: all Mac-safe tests pass.

- [ ] **Step 3: Build the Mac game project**

Run:

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
```

Expected: build succeeds without warnings introduced by this change.

- [ ] **Step 4: Inspect changed files for accidental broad scope**

Run:

```bash
git diff --stat HEAD
```

Expected: changes are limited to result-stage model/renderer/reveal code, result layout/texture constants, and result tests.

Run:

```bash
git diff -- DTXManiaNX
```

Expected: no output.

- [ ] **Step 5: Commit verification-only cleanup if any was needed**

If Step 1-4 required small fixes, commit those fixes:

```bash
git add DTXMania.Game/Lib/Stage/ResultStage.cs DTXMania.Game/Lib/Stage/Result DTXMania.Game/Lib/Resources/TexturePath.cs DTXMania.Game/Lib/UI/Layout/ResultUILayout.cs DTXMania.Test/Stage DTXMania.Test/Resources/TexturePathTests.cs DTXMania.Test/UI/PerformanceUILayoutTests.cs
git commit -m "test: verify result stage NX parity"
```

If no fixes were needed after Task 5, skip this commit.
