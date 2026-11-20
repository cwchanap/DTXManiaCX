#nullable enable

using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.Stage.Performance
{
    internal interface ILanesNoteRenderer : IDisposable
    {
        double EffectiveLookAheadMs { get; }
        double MeasureLinePastGraceMs { get; }
        void SetBpm(double bpm);
        void SetScrollSpeed(int speed);
        void TriggerLaneFlash(int lane);
        void Update(double deltaTime);
        void DrawNotes(SpriteBatch batch, IEnumerable<Note> notes, double time);
        void DrawMeasureLines(SpriteBatch batch, IEnumerable<MeasureLine> lines, double time);
        void DrawNoteOverlays(SpriteBatch batch, IEnumerable<Note> notes, double time);
    }

    internal interface ILanesPadRenderer : IDisposable
    {
        void TriggerPadPress(int lane, bool judged);
        void Update(double deltaTime);
        void Draw(SpriteBatch batch);
    }

    internal interface ILanesJudgementLine : IDisposable
    {
        void Update(double deltaTime);
        void Draw(SpriteBatch batch);
    }

    internal interface ILanesScoreDisplay : IDisposable
    {
        int Score { set; }
        void Update(double deltaTime);
        void Draw(SpriteBatch batch);
    }

    internal interface ILanesComboDisplay : IDisposable
    {
        int Combo { set; }
        void Update(double deltaTime);
        void Draw(SpriteBatch batch);
    }

    internal interface ILanesSkillPanel : IDisposable
    {
        void Update(double deltaTime);
        void Draw(SpriteBatch batch, in PerformanceFrameState frame);
    }

    internal interface ILanesSkillMeter : IDisposable
    {
        double Skill { set; }
        void Update(double deltaTime);
        void Draw(SpriteBatch batch);
    }

    internal interface ILanesVisualFactory
    {
        LaneBackgroundRenderer CreateBackground();
        ILanesJudgementLine CreateLine();
        ILanesScoreDisplay CreateScore();
        ILanesComboDisplay CreateCombo();
        ILanesSkillPanel CreateSkillPanel();
        ILanesSkillMeter CreateSkillMeter();
        ILanesNoteRenderer CreateNotes();
        NxAttackEffectManager CreateEffects();
        JudgementTextPopupManager CreateFontPopups();
        SpriteJudgementTextPopupManager CreateSpritePopups(Action<JudgementEvent> fallback);
        HitTimingFeedbackDisplay CreateTiming();
        ILanesPadRenderer CreatePads();
    }

    internal sealed class LanesVisualFactory(
        GraphicsDevice device, IResourceManager resources, Texture2D? whitePixel,
        SongChart? chart, string? difficultyLabel) : ILanesVisualFactory
    {
        public LaneBackgroundRenderer CreateBackground() => new(resources);
        public ILanesJudgementLine CreateLine() => new JudgementLineRenderer(device, whitePixel!);
        public ILanesScoreDisplay CreateScore() => new ScoreDisplay(resources, device);
        public ILanesComboDisplay CreateCombo() => new ComboDisplay(resources, device);
        public ILanesSkillPanel CreateSkillPanel() => new SkillPanelDisplay(resources, device, chart, difficultyLabel);
        public ILanesSkillMeter CreateSkillMeter() => new SkillMeterDisplay(resources, device);
        public ILanesNoteRenderer CreateNotes() => new NoteRenderer(device, resources);
        public NxAttackEffectManager CreateEffects() => new(resources);
        public JudgementTextPopupManager CreateFontPopups() => new(device, resources);
        public SpriteJudgementTextPopupManager CreateSpritePopups(Action<JudgementEvent> fallback) => new(resources, fallback);
        public HitTimingFeedbackDisplay CreateTiming() => new(resources);
        public ILanesPadRenderer CreatePads() => new PadRenderer(device, resources);
    }
}
