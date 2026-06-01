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
            Summary(perfect: 100, totalNotes: 100, clear: true),
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
    public void Create_PerfectCountAboveTotal_ShouldNotSelectExcellentPlate()
    {
        var model = ResultScreenModel.Create(
            Summary(perfect: 101, totalNotes: 100, clear: true),
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
            Summary(
                score: 123456,
                maxCombo: 87,
                perfect: 70,
                great: 20,
                good: 5,
                poor: 3,
                miss: 2,
                totalNotes: 100,
                playingSkill: 76.543,
                gameSkill: 133.456,
                level: 78,
                levelDec: 33),
            null,
            0,
            null,
            null);

        Assert.Equal("0123456", model.ScoreText);
        Assert.Equal("87", model.MaxComboText);
        Assert.Equal("70", model.PerfectCountText);
        Assert.Equal("70%", model.PerfectPercentText);
        Assert.Equal("20", model.GreatCountText);
        Assert.Equal("20%", model.GreatPercentText);
        Assert.Equal("5", model.GoodCountText);
        Assert.Equal("5%", model.GoodPercentText);
        Assert.Equal("3", model.PoorCountText);
        Assert.Equal("3%", model.PoorPercentText);
        Assert.Equal("2", model.MissCountText);
        Assert.Equal("2%", model.MissPercentText);
        Assert.Equal("87%", model.MaxComboPercentText);
        Assert.Equal("76.54", model.PlayingSkillText);
        Assert.Equal("133.46", model.GameSkillText);
        Assert.Equal("8.13", model.ChartLevelText);
    }

    [Fact]
    public void Create_NegativeValues_ShouldFormatAsZero()
    {
        var model = ResultScreenModel.Create(
            Summary(
                score: -1,
                maxCombo: -1,
                perfect: -1,
                great: -1,
                good: -1,
                poor: -1,
                miss: -1,
                totalNotes: 100,
                playingSkill: -1.0,
                gameSkill: -1.0),
            null,
            0,
            null,
            null);

        Assert.Equal("0000000", model.ScoreText);
        Assert.Equal("0", model.MaxComboText);
        Assert.Equal("0", model.PerfectCountText);
        Assert.Equal("0%", model.PerfectPercentText);
        Assert.Equal("0.00", model.PlayingSkillText);
        Assert.Equal("0.00", model.GameSkillText);
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
    public void Create_WithBackslashPreviewImage_ShouldNormalizeRelativePreviewPath()
    {
        var chartDirectory = Path.Combine(Path.GetTempPath(), "dtx-result-test");
        var chart = new SongChart
        {
            FilePath = Path.Combine(chartDirectory, "chart.dtx"),
            PreviewImage = @"Graphics\jacket.png"
        };

        var model = ResultScreenModel.Create(Summary(), null, 0, chart, null);

        Assert.Equal(Path.Combine(chartDirectory, "Graphics", "jacket.png"), model.PreviewImagePath);
    }

    [Fact]
    public void Create_WithoutPreviewImage_ShouldUseDefaultPreview()
    {
        var model = ResultScreenModel.Create(Summary(), null, 0, null, null);

        Assert.Equal(TexturePath.ResultDefaultPreview, model.PreviewImagePath);
    }

    [Fact]
    public void Create_WithRootedPreviewImage_ShouldKeepPreviewPath()
    {
        var rootedPath = Path.Combine(Path.GetTempPath(), "rooted-jacket.png");
        var chart = new SongChart
        {
            FilePath = Path.Combine(Path.GetTempPath(), "chart.dtx"),
            PreviewImage = rootedPath
        };

        var model = ResultScreenModel.Create(Summary(), null, 0, chart, null);

        Assert.Equal(rootedPath, model.PreviewImagePath);
    }

    [Fact]
    public void Create_WithoutSongMetadata_ShouldUseStableFallbacks()
    {
        var model = ResultScreenModel.Create(Summary(), new SongListNode(), 0, null, null);

        Assert.Equal("Unknown Song", model.Title);
        Assert.Equal("Unknown Artist", model.Artist);
        Assert.Equal("--", model.ChartLevelText);
    }

    [Fact]
    public void Create_WithNodeTitleOnly_ShouldUseNodeTitleAndArtistFallback()
    {
        var model = ResultScreenModel.Create(
            Summary(),
            new SongListNode { Title = "Node Title" },
            0,
            null,
            null);

        Assert.Equal("Node Title", model.Title);
        Assert.Equal("Unknown Artist", model.Artist);
    }

    [Fact]
    public void Create_WithNoPreviousScore_ShouldNotDetectNewRecord()
    {
        var model = ResultScreenModel.Create(
            Summary(score: 1, gameSkill: 1.0),
            null,
            0,
            null,
            previousScore: null);

        Assert.False(model.NewRecord);
    }

    [Fact]
    public void Create_WithUnplayedPreviousScoreAndZeroResult_ShouldNotDetectNewRecord()
    {
        var previous = new SongScore
        {
            PlayCount = 0,
            BestScore = 9999999,
            HighSkill = 999.0
        };

        var model = ResultScreenModel.Create(
            Summary(score: 0, gameSkill: 0.0),
            null,
            0,
            null,
            previous);

        Assert.False(model.NewRecord);
    }

    [Theory]
    [InlineData(1, 0.0)]
    [InlineData(0, 1.0)]
    public void Create_WithUnplayedPreviousScoreAndPositiveResult_ShouldDetectNewRecord(int score, double gameSkill)
    {
        var previous = new SongScore
        {
            PlayCount = 0,
            BestScore = 9999999,
            HighSkill = 999.0
        };

        var model = ResultScreenModel.Create(
            Summary(score: score, gameSkill: gameSkill),
            null,
            0,
            null,
            previous);

        Assert.True(model.NewRecord);
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
    public void Create_WithPreviousHighSkill_ShouldDetectNewRecord()
    {
        var previous = new SongScore
        {
            PlayCount = 4,
            BestScore = 500000,
            HighSkill = 90.0
        };

        var model = ResultScreenModel.Create(
            Summary(score: 500000, gameSkill: 90.01),
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

    [Theory]
    [InlineData(0, 0, "--")]
    [InlineData(-1, 0, "--")]
    [InlineData(120, 0, "1.20")]
    [InlineData(100, 0, "1.00")]
    [InlineData(78, 33, "8.13")]
    [InlineData(50, 0, "5.00")]
    [InlineData(55, 50, "6.00")]
    [InlineData(10, 99, "1.99")]
    [InlineData(99, 99, "10.89")]
    [InlineData(1, 0, "0.10")]
    [InlineData(50, -1, "5.00")]
    public void FormatLevel_ShouldHandleBothEncodingSchemes(int level, int levelDec, string expected)
    {
        Assert.Equal(expected, ResultScreenModel.FormatLevel(level, levelDec));
    }

    [Fact]
    public void Create_WithWindowsAbsolutePathPreview_ShouldKeepPreviewPath()
    {
        var chart = new SongChart
        {
            FilePath = "/some/chart.dtx",
            PreviewImage = @"C:\Jackets\art.png"
        };

        var model = ResultScreenModel.Create(Summary(), null, 0, chart, null);

        // On any OS, a Windows-style absolute path (C:\...) should be detected as rooted
        Assert.Equal(@"C:\Jackets\art.png".Replace('\\', Path.DirectorySeparatorChar), model.PreviewImagePath);
    }

    [Fact]
    public void FormatPercent_MidpointValue_ShouldRoundAwayFromZero()
    {
        // 1/8 = 12.5% → MidpointRounding.AwayFromZero → 13%
        Assert.Equal("13%", ResultScreenModel.FormatPercent(1, 8));
    }

    [Fact]
    public void FormatPercent_ValueExceeds100Percent_ShouldClampTo100()
    {
        // 150 out of 100 notes = 150%, clamped to 100%
        Assert.Equal("100%", ResultScreenModel.FormatPercent(150, 100));
    }

    [Fact]
    public void FormatScore_AboveUpperClamp_ShouldClampTo9999999()
    {
        Assert.Equal("9999999", ResultScreenModel.FormatScore(10_000_000));
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
