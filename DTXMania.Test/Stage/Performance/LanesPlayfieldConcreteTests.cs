using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance;

[Trait("Category", "Unit")]
public class LanesPlayfieldConcreteTests
{
    [Fact]
    public void JudgementsAndInput_ShouldReachConcretePlayfieldLeavesInEveryFrame()
    {
        var resources = CreateResources(out _);
        var visuals = new RecordingVisuals(resources.Object);
        using var field = new LanesPlayfield(resources.Object, new ConfigData { EnableLaneFlush = true }, visuals);

        field.OnChartPublished(150, 200);
        field.OnScrollSpeedChanged(125);
        field.OnLaneInput(3);
        field.OnAutoHit(4);
        field.OnJudgement(new JudgementEvent(1, 2, 12, JudgementType.Great),
            new PerformanceFrameState { Score = 100, Combo = 1, Achievement = 50 }, true);
        field.OnJudgement(new JudgementEvent(2, 2, 0, JudgementType.Perfect),
            new PerformanceFrameState { Score = 200, Combo = 2, Achievement = 75 }, false);
        field.OnJudgement(new JudgementEvent(3, 5, 0, JudgementType.Miss),
            new PerformanceFrameState { Score = 200, Combo = 0, Achievement = 60 }, false);

        Assert.Equal(new[] { 150.0 }, visuals.Notes.Bpms);
        Assert.Equal(new[] { 200, 125 }, visuals.Notes.Speeds);
        Assert.Equal(new[] { (3, false), (4, true), (2, true), (2, true) }, visuals.Pads.Presses);
        Assert.Equal(new[] { 2, 2 }, visuals.Notes.Flashes);
        Assert.Equal(new[] { 100, 200, 200 }, visuals.Score.Values);
        Assert.Equal(new[] { 1, 2, 0 }, visuals.Combo.Values);
        Assert.Equal(new[] { 50.0, 75.0, 60.0 }, visuals.Meter.Values);
#if DEBUG
        Assert.Equal(2, visuals.Effects.SpawnCallCountForTesting);
#endif
        Assert.Equal(3, visuals.FontPopups.ActivePopupCount);
    }

    [Fact]
    public void ConstructorFailure_ShouldRollBackCompletedLeavesAndPreserveOriginalException()
    {
        var resources = CreateResources(out var pixel);
        var visuals = new RecordingVisuals(resources.Object) { FailOnCombo = true, ThrowOnScoreDispose = true };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new LanesPlayfield(resources.Object, new ConfigData(), visuals));

        Assert.Same(visuals.Failure, exception);
        Assert.Equal(new[] { "line", "score" }, visuals.Disposals);
        pixel.Verify(p => p.RemoveReference(), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConstructorFailure_WhenSkillTextureValidationThrows_ShouldReleaseTextureAndPrefix(bool cached)
    {
        var resources = CreateResources(out var pixel);
        var visuals = new RecordingVisuals(resources.Object);
        var texture = new Mock<ITexture>();
        var failure = new InvalidOperationException("texture dimensions unavailable");
        texture.SetupGet(t => t.Width).Throws(failure);
        var value = texture.Object;
        resources.Setup(r => r.TryLoadTexture(TexturePath.SkillPanel, out value)).Returns(cached);

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            new LanesPlayfield(resources.Object, new ConfigData(), visuals)));

        if (cached)
        {
            texture.Verify(t => t.RemoveReference(), Times.Once);
            texture.Verify(t => t.Dispose(), Times.Never);
        }
        else
        {
            texture.Verify(t => t.Dispose(), Times.Once);
            texture.Verify(t => t.RemoveReference(), Times.Never);
        }
        Assert.Equal(new[] { "line", "score", "combo", "panel", "meter", "notes", "pads" }, visuals.Disposals);
        pixel.Verify(t => t.RemoveReference(), Times.Once);
    }

    [Fact]
    public void JudgementWithoutLaneFlush_ShouldKeepFlashOffAndStillShowMissPopup()
    {
        var resources = CreateResources(out _);
        var visuals = new RecordingVisuals(resources.Object);
        using var field = new LanesPlayfield(resources.Object, new ConfigData { EnableLaneFlush = false }, visuals);

        field.OnJudgement(new JudgementEvent(1, 2, 12, JudgementType.Great), default, false);
        field.OnJudgement(new JudgementEvent(2, 5, 0, JudgementType.Miss), default, false);

        Assert.Empty(visuals.Notes.Flashes);
        Assert.Equal(new[] { (2, true) }, visuals.Pads.Presses);
        Assert.Equal(2, visuals.FontPopups.ActivePopupCount);
#if DEBUG
        Assert.Equal(1, visuals.Effects.SpawnCallCountForTesting);
#endif
    }

    [Fact]
    public void TimingFeedback_ShouldOnlyBeRequestedForEligibleJudgements()
    {
        var resources = CreateResources(out _);
        var texture = new Mock<ITexture>();
        int validationCalls = 0;
        texture.SetupGet(t => t.IsDisposed).Callback(() => validationCalls++).Returns(true);
        var visuals = new RecordingVisuals(resources.Object, texture.Object);
        using var field = new LanesPlayfield(resources.Object, new ConfigData(), visuals);

        field.OnJudgement(new JudgementEvent(1, 0, 0, JudgementType.Perfect), default, false);
        field.OnJudgement(new JudgementEvent(2, 1, 0, JudgementType.Miss), default, false);
        Assert.Equal(0, validationCalls);

        field.OnJudgement(new JudgementEvent(3, 2, -12, JudgementType.Great), default, true);

        Assert.Equal(1, validationCalls);
        texture.Verify(t => t.RemoveReference(), Times.Once);
        Assert.Equal(0, visuals.Timing.ActiveLaneCountForTesting);
        Assert.Equal(3, visuals.FontPopups.ActivePopupCount);
    }

    [Fact]
    public void Judgements_ShouldDriveRealSpriteAndTimingFeedbackAndGateTheirUpdates()
    {
        var resources = CreateResources(out _);
        var spriteTexture = new Mock<ITexture>();
        var timingTexture = new Mock<ITexture>();
        var visuals = new RecordingVisuals(resources.Object, timingTexture.Object, spriteTexture.Object);
        using var field = new LanesPlayfield(resources.Object, new ConfigData { EnableLaneFlush = true }, visuals);

        field.OnJudgement(new JudgementEvent(1, 2, -12, JudgementType.Great), default, true);
        field.OnJudgement(new JudgementEvent(2, 5, 0, JudgementType.Miss), default, false);

        Assert.Equal(new[] { JudgementType.Great, JudgementType.Miss },
            visuals.SpritePopups!.ActivePopupsForTesting.Select(p => p.JudgementType));
        Assert.Equal(0, visuals.FontPopups.ActivePopupCount);
        Assert.Equal("-12", visuals.Timing.ActiveStatesForTesting[2]!.Projection.Text);
        Assert.Null(visuals.Timing.ActiveStatesForTesting[5]);
        Assert.Equal(new[] { 2 }, visuals.Notes.Flashes);
        Assert.Equal(new[] { (2, true) }, visuals.Pads.Presses);
#if DEBUG
        Assert.Equal(1, visuals.Effects.SpawnCallCountForTesting);
#endif
        field.Update(0.1, false);
        Assert.Equal(0, visuals.Timing.ActiveStatesForTesting[2]!.ElapsedSeconds);
        Assert.Equal(2, visuals.SpritePopups.ActivePopupCount);
        field.Update(0.1, true);
        Assert.Equal(0.1, visuals.Timing.ActiveStatesForTesting[2]!.ElapsedSeconds, 3);
        field.Update(1, true);
        Assert.Null(visuals.Timing.ActiveStatesForTesting[2]);
        Assert.Equal(0, visuals.SpritePopups.ActivePopupCount);
    }

    [Fact]
    public void BaseAndOverlay_ShouldQueryVisibleChartWindowsOnlyWhenTransportIsActive()
    {
        var resources = CreateResources(out _);
        var visuals = new RecordingVisuals(resources.Object);
        visuals.Notes.EffectiveLookAheadMs = 1000;
        visuals.Notes.MeasureLinePastGraceMs = 100;
        using var field = new LanesPlayfield(resources.Object, new ConfigData(), visuals);
        var chart = new ParsedChart("draw-windows.dtx") { Bpm = 120 };
        chart.Notes.Add(new Note { TimeMs = 1200, LaneIndex = 1 });
        chart.Notes.Add(new Note { TimeMs = 2201, LaneIndex = 2 });
        chart.MeasureLines.Add(new MeasureLine { TimeMs = 1100 });
        chart.MeasureLines.Add(new MeasureLine { TimeMs = 2201 });
        var manager = new ChartManager(chart);

        field.DrawBase(null!, manager, false, 1200);
        field.DrawOverlay(null!, manager, false, 1200);
        field.DrawBase(null!, null, true, 1200);
        field.DrawOverlay(null!, null, true, 1200);

        Assert.Empty(visuals.Notes.NoteDraws);
        Assert.Empty(visuals.Notes.OverlayDraws);
        Assert.Empty(visuals.Notes.MeasureDraws);
        Assert.Equal(2, visuals.Pads.DrawCount);
        Assert.Equal(2, visuals.Line.DrawCount);

        field.DrawBase(null!, manager, true, 1200);
        field.DrawOverlay(null!, manager, true, 1200);

        var noteDraw = Assert.Single(visuals.Notes.NoteDraws);
        Assert.Equal(1200, noteDraw.Time);
        Assert.Equal(new[] { 1200.0 }, noteDraw.Times);
        var overlayDraw = Assert.Single(visuals.Notes.OverlayDraws);
        Assert.Equal(1200, overlayDraw.Time);
        Assert.Equal(new[] { 1200.0 }, overlayDraw.Times);
        var measureDraw = Assert.Single(visuals.Notes.MeasureDraws);
        Assert.Equal(1200, measureDraw.Time);
        Assert.Equal(new[] { 1100.0 }, measureDraw.Times);
    }

    [Fact]
    public void HiddenMeasureAndJudgementLine_ShouldSkipThoseLeavesWithoutHidingNotesOrPads()
    {
        var resources = CreateResources(out _);
        var visuals = new RecordingVisuals(resources.Object);
        using var field = new LanesPlayfield(resources.Object,
            new ConfigData { LaneDisplayMode = DrumsLaneDisplayMode.LineOff, ShowJudgementLine = false }, visuals);
        var chart = new ParsedChart("hidden-line.dtx");
        chart.Notes.Add(new Note { TimeMs = 1000, LaneIndex = 0 });
        chart.MeasureLines.Add(new MeasureLine { TimeMs = 1000 });

        field.DrawBase(null!, new ChartManager(chart), true, 1000);

        Assert.Empty(visuals.Notes.MeasureDraws);
        Assert.Single(visuals.Notes.NoteDraws);
        Assert.Equal(1, visuals.Pads.DrawCount);
        Assert.Equal(0, visuals.Line.DrawCount);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Hud_ShouldDrawFrameAndRespectComboGateWhileKeepingComboUpdates(bool showCombo, int comboDraws)
    {
        var resources = CreateResources(out _);
        var visuals = new RecordingVisuals(resources.Object);
        var fills = new List<(Rectangle Bounds, Color Color, float Depth)>();
        using var field = new LanesPlayfield(resources.Object, new ConfigData { ShowCombo = showCombo }, visuals,
            (_, bounds, color, depth) => fills.Add((bounds, color, depth)));
        field.OnJudgement(new JudgementEvent(1, 0, 0, JudgementType.Perfect),
            new PerformanceFrameState { Combo = 87 }, false);

        field.DrawHud(null!, new PerformanceFrameState { Progress = 0.4f, Life = 0.5f });

        Assert.Equal(new[] { 87 }, visuals.Combo.Values);
        Assert.Equal(comboDraws, visuals.Combo.DrawCount);
        Assert.Equal(1, visuals.Score.DrawCount);
        Assert.Equal(1, visuals.Panel.DrawCount);
        Assert.Equal(1, visuals.Meter.DrawCount);
        var fill = Assert.Single(fills);
        var bar = PerformanceUILayout.Progress.BarBounds;
        var height = (int)(bar.Height * 0.4f);
        Assert.Equal(new Rectangle(bar.X, bar.Bottom - height, bar.Width, height), fill.Bounds);
        Assert.Equal(Color.LightBlue, fill.Color);
        Assert.Equal(0.2f, fill.Depth);
    }

    [Fact]
    public void Update_ShouldAdvanceHudAndPadsThroughoutLoadingAndGameplayLeavesOnlyWhenActive()
    {
        var resources = CreateResources(out _);
        var visuals = new RecordingVisuals(resources.Object);
        using var field = new LanesPlayfield(resources.Object, new ConfigData(), visuals);
        field.OnJudgement(new JudgementEvent(1, 0, 0, JudgementType.Perfect), default, false);

        field.Update(0.1, false);
        Assert.Equal(0, visuals.Notes.UpdateCount);
        Assert.Equal(1, visuals.Pads.UpdateCount);
        Assert.Equal(1, visuals.Combo.UpdateCount);
        Assert.Equal(1, visuals.FontPopups.ActivePopupCount);
        field.Update(1, true);

        Assert.Equal(1, visuals.Notes.UpdateCount);
        Assert.Equal(2, visuals.Pads.UpdateCount);
        Assert.Equal(2, visuals.Combo.UpdateCount);
        Assert.Equal(0, visuals.FontPopups.ActivePopupCount);
    }

    [Fact]
    public void Dispose_ShouldReleaseAllOwnedLeavesAndTexturesExactlyOnce()
    {
        var resources = CreateResources(out var pixel);
        var assets = new List<Mock<ITexture>>();
        var releasedPaths = new List<string>();
        var paths = new[]
        {
            TexturePath.LaneStrips, TexturePath.HitBar, TexturePath.GaugeFrame, TexturePath.GaugeFill,
            TexturePath.ProgressFrame, TexturePath.SkillPanel
        };
        foreach (var path in paths)
        {
            var texture = new Mock<ITexture>();
            texture.SetupGet(t => t.Width).Returns(100);
            texture.SetupGet(t => t.Height).Returns(100);
            texture.Setup(t => t.RemoveReference()).Callback(() => releasedPaths.Add(path));
            var value = texture.Object;
            resources.Setup(r => r.TryLoadTexture(path, out value)).Returns(true);
            assets.Add(texture);
        }
        var timingTexture = new Mock<ITexture>();
        var spriteTexture = new Mock<ITexture>();
        var visuals = new RecordingVisuals(resources.Object, timingTexture.Object, spriteTexture.Object);
        var field = new LanesPlayfield(resources.Object, new ConfigData(), visuals);

        field.Dispose();
        field.Dispose();

        Assert.Equal(new[] { "line", "score", "combo", "panel", "meter", "notes", "pads" }, visuals.Disposals);
        pixel.Verify(t => t.RemoveReference(), Times.Once);
        foreach (var texture in assets)
            texture.Verify(t => t.RemoveReference(), Times.Once);
        timingTexture.Verify(t => t.RemoveReference(), Times.Once);
        spriteTexture.Verify(t => t.RemoveReference(), Times.Once);
        Assert.Equal(paths, releasedPaths);
    }

    private static Mock<IResourceManager> CreateResources(out Mock<ITexture> pixel)
    {
        pixel = new Mock<ITexture>();
        var resources = new Mock<IResourceManager>();
        resources.Setup(r => r.CreateTextureFromColor(Color.White)).Returns(pixel.Object);
        return resources;
    }

    internal sealed class RecordingVisuals : ILanesVisualFactory
    {
        private readonly IResourceManager _resources;
        private readonly ITexture? _spriteTexture;
        public readonly List<string> Disposals = new();
        public readonly NotesLeaf Notes;
        public readonly PadsLeaf Pads;
        public readonly LineLeaf Line;
        public readonly ScoreLeaf Score;
        public readonly ComboLeaf Combo;
        public readonly PanelLeaf Panel;
        public readonly MeterLeaf Meter;
        public readonly NxAttackEffectManager Effects;
        public readonly JudgementTextPopupManager FontPopups;
        public readonly HitTimingFeedbackDisplay Timing;
        public SpriteJudgementTextPopupManager? SpritePopups;
        public readonly InvalidOperationException Failure = new("combo construction failed");
        public bool FailOnCombo;
        public bool ThrowOnScoreDispose;

        public RecordingVisuals(IResourceManager resources, ITexture? timingTexture = null, ITexture? spriteTexture = null)
        {
            _resources = resources;
            _spriteTexture = spriteTexture;
            Notes = new NotesLeaf(Disposals);
            Pads = new PadsLeaf(Disposals);
            Line = new LineLeaf(Disposals);
            Score = new ScoreLeaf(Disposals, () => ThrowOnScoreDispose);
            Combo = new ComboLeaf(Disposals);
            Panel = new PanelLeaf(Disposals);
            Meter = new MeterLeaf(Disposals);
            Effects = new NxAttackEffectManager(resources);
            FontPopups = new JudgementTextPopupManager(resources);
            Timing = timingTexture == null
                ? new HitTimingFeedbackDisplay(resources)
                : HitTimingFeedbackDisplay.CreateForTesting(timingTexture,
                    isInvalidTexture: spriteTexture == null ? null : _ => false);
        }

        public LaneBackgroundRenderer CreateBackground() => new(_resources);
        public ILanesJudgementLine CreateLine() => Line;
        public ILanesScoreDisplay CreateScore() => Score;
        public ILanesComboDisplay CreateCombo() => FailOnCombo ? throw Failure : Combo;
        public ILanesSkillPanel CreateSkillPanel() => Panel;
        public ILanesSkillMeter CreateSkillMeter() => Meter;
        public ILanesNoteRenderer CreateNotes() => Notes;
        public NxAttackEffectManager CreateEffects() => Effects;
        public JudgementTextPopupManager CreateFontPopups() => FontPopups;
        public SpriteJudgementTextPopupManager CreateSpritePopups(Action<JudgementEvent> fallback) =>
            SpritePopups = _spriteTexture == null
                ? new SpriteJudgementTextPopupManager(_resources, fallback)
                : SpriteJudgementTextPopupManager.CreateForTesting(_spriteTexture, fallback, isInvalidTexture: _ => false);
        public HitTimingFeedbackDisplay CreateTiming() => Timing;
        public ILanesPadRenderer CreatePads() => Pads;
    }

    internal sealed class NotesLeaf(List<string> disposals) : ILanesNoteRenderer
    {
        public readonly List<double> Bpms = new();
        public readonly List<int> Speeds = new();
        public readonly List<int> Flashes = new();
        public readonly List<(double Time, double[] Times)> NoteDraws = new();
        public readonly List<(double Time, double[] Times)> OverlayDraws = new();
        public readonly List<(double Time, double[] Times)> MeasureDraws = new();
        public int UpdateCount;
        public double EffectiveLookAheadMs { get; set; } = 1200;
        public double MeasureLinePastGraceMs { get; set; } = 100;
        public void SetBpm(double bpm) => Bpms.Add(bpm);
        public void SetScrollSpeed(int speed) => Speeds.Add(speed);
        public void TriggerLaneFlash(int lane) => Flashes.Add(lane);
        public void Update(double deltaTime) => UpdateCount++;
        public void DrawNotes(SpriteBatch batch, IEnumerable<Note> notes, double time) =>
            NoteDraws.Add((time, notes.Select(n => n.TimeMs).ToArray()));
        public void DrawMeasureLines(SpriteBatch batch, IEnumerable<MeasureLine> lines, double time) =>
            MeasureDraws.Add((time, lines.Select(l => l.TimeMs).ToArray()));
        public void DrawNoteOverlays(SpriteBatch batch, IEnumerable<Note> notes, double time) =>
            OverlayDraws.Add((time, notes.Select(n => n.TimeMs).ToArray()));
        public void Dispose() => disposals.Add("notes");
    }

    internal sealed class PadsLeaf(List<string> disposals) : ILanesPadRenderer
    {
        public readonly List<(int, bool)> Presses = new();
        public int UpdateCount;
        public int DrawCount;
        public void TriggerPadPress(int lane, bool judged) => Presses.Add((lane, judged));
        public void Update(double deltaTime) => UpdateCount++;
        public void Draw(SpriteBatch batch) => DrawCount++;
        public void Dispose() => disposals.Add("pads");
    }

    internal sealed class LineLeaf(List<string> disposals) : ILanesJudgementLine
    {
        public int DrawCount;
        public void Update(double deltaTime) { }
        public void Draw(SpriteBatch batch) => DrawCount++;
        public void Dispose() => disposals.Add("line");
    }

    internal sealed class ScoreLeaf(List<string> disposals, Func<bool> shouldThrow) : ILanesScoreDisplay
    {
        public readonly List<int> Values = new();
        public int DrawCount;
        public Action<int>? OnSet;
        public int Score { set { Values.Add(value); OnSet?.Invoke(value); } }
        public void Update(double deltaTime) { }
        public void Draw(SpriteBatch batch) => DrawCount++;
        public void Dispose()
        {
            disposals.Add("score");
            if (shouldThrow()) throw new InvalidOperationException("score dispose failed");
        }
    }

    internal sealed class ComboLeaf(List<string> disposals) : ILanesComboDisplay
    {
        public readonly List<int> Values = new();
        public int UpdateCount;
        public int DrawCount;
        public int Combo { set => Values.Add(value); }
        public void Update(double deltaTime) => UpdateCount++;
        public void Draw(SpriteBatch batch) => DrawCount++;
        public void Dispose() => disposals.Add("combo");
    }

    internal sealed class PanelLeaf(List<string> disposals) : ILanesSkillPanel
    {
        public int DrawCount;
        public void Update(double deltaTime) { }
        public void Draw(SpriteBatch batch, in PerformanceFrameState frame) => DrawCount++;
        public void Dispose() => disposals.Add("panel");
    }

    internal sealed class MeterLeaf(List<string> disposals) : ILanesSkillMeter
    {
        public readonly List<double> Values = new();
        public int DrawCount;
        public double Skill { set => Values.Add(value); }
        public void Update(double deltaTime) { }
        public void Draw(SpriteBatch batch) => DrawCount++;
        public void Dispose() => disposals.Add("meter");
    }
}
