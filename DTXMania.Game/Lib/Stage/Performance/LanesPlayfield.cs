#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>Owns the Lanes visual components; gameplay and the white pixel are borrowed.</summary>
    public sealed class LanesPlayfield : IPerformancePlayfield
    {
        private readonly IResourceManager _resources;
        private readonly Action<SpriteBatch, Rectangle, Color, float>? _drawWhite;
        private readonly bool _hideLaneBackground, _hideMeasureLines, _hideJudgementLine, _hideCombo, _laneFlush;
        private readonly LaneBackgroundRenderer _lanes;
        private readonly ILanesJudgementLine _line;
        private readonly ILanesNoteRenderer _notes;
        private readonly ILanesPadRenderer _pads;
        private readonly NxAttackEffectManager _effects;
        private readonly JudgementTextPopupManager _fontPopups;
        private readonly SpriteJudgementTextPopupManager _spritePopups;
        private readonly HitTimingFeedbackDisplay _timing;
        private readonly ILanesScoreDisplay _score;
        private readonly ILanesComboDisplay _combo;
        private readonly ILanesSkillPanel _skillPanel;
        private readonly ILanesSkillMeter _skillMeter;
        private ITexture? _laneTexture, _lineTexture, _gaugeBase, _gaugeFill, _progressBase, _skillTexture;
        // Release acquired textures in acquisition order, including on constructor failure.
        private readonly List<ITexture> _ownedTextures = new();
        private bool _disposed;

        public LanesPlayfield(GraphicsDevice device, IResourceManager resources, Texture2D? whitePixel,
            SongChart? chart, string? difficultyLabel, ConfigData config)
            : this(resources, config, new LanesVisualFactory(device, resources, whitePixel, chart, difficultyLabel),
                whitePixel == null ? null : (batch, bounds, color, depth) =>
                    DrawProgressFill(batch, whitePixel, bounds, color, depth))
        {
        }

        internal LanesPlayfield(IResourceManager resources, ConfigData config, ILanesVisualFactory visuals,
            Action<SpriteBatch, Rectangle, Color, float>? drawWhite = null)
        {
            _resources = resources;
            _drawWhite = drawWhite;
            _hideLaneBackground = !config.LaneDisplayMode.ShowsLaneBackground();
            _hideMeasureLines = !config.LaneDisplayMode.ShowsMeasureLines();
            _hideJudgementLine = !config.ShowJudgementLine;
            _hideCombo = !config.ShowCombo;
            _laneFlush = config.EnableLaneFlush;
            try
            {
                _lanes = visuals.CreateBackground();
                _line = visuals.CreateLine();
                _score = visuals.CreateScore();
                _combo = visuals.CreateCombo();
                _skillPanel = visuals.CreateSkillPanel();
                _skillMeter = visuals.CreateSkillMeter();
                _notes = visuals.CreateNotes();
                _effects = visuals.CreateEffects();
                _fontPopups = visuals.CreateFontPopups();
                _spritePopups = visuals.CreateSpritePopups(e => _fontPopups.SpawnPopup(e));
                _timing = visuals.CreateTiming();
                _pads = visuals.CreatePads();
                _laneTexture = Acquire(TexturePath.LaneStrips);
                _lineTexture = Acquire(TexturePath.HitBar);
                _gaugeBase = Acquire(TexturePath.GaugeFrame);
                _gaugeFill = Acquire(TexturePath.GaugeFill);
                _progressBase = Acquire(TexturePath.ProgressFrame);
                _skillTexture = Own(LoadSkillPanelTexture(resources, resources.CurrentTheme ?? SkinTheme.Empty));
            }
            catch
            {
                try { Dispose(); }
                catch { }
                throw;
            }
        }

        public DrumsNoteDisplayStyle Mode => DrumsNoteDisplayStyle.Lanes;

        public void Update(double deltaTime, bool gameplayActive)
        {
            _lanes.Update(deltaTime);
            _line.Update(deltaTime);
            _score.Update(deltaTime);
            _combo.Update(deltaTime);
            _skillPanel.Update(deltaTime);
            _skillMeter.Update(deltaTime);
            _pads.Update(deltaTime);
            if (!gameplayActive) return;
            _notes.Update(deltaTime);
            _effects.Update(deltaTime);
            _spritePopups.Update(deltaTime);
            _fontPopups.Update(deltaTime);
            _timing.Update(deltaTime);
        }

        public void OnChartPublished(double bpm, int scrollSpeedPercent)
        {
            _notes.SetBpm(bpm);
            _notes.SetScrollSpeed(scrollSpeedPercent);
        }

        public void OnLaneInput(int lane) => _pads.TriggerPadPress(lane, false);
        public void OnAutoHit(int lane) => _pads.TriggerPadPress(lane, true);
        public void OnScrollSpeedChanged(int scrollSpeedPercent) => _notes.SetScrollSpeed(scrollSpeedPercent);

        public void OnJudgement(JudgementEvent judgement, PerformanceFrameState frame, bool showHitTiming)
        {
            _score.Score = frame.Score;
            _combo.Combo = frame.Combo;
            _skillMeter.Skill = frame.Achievement;
            if (judgement.IsHit())
            {
                _effects.Spawn(judgement.Lane, judgement.Type);
                _pads.TriggerPadPress(judgement.Lane, true);
                if (_laneFlush) _notes.TriggerLaneFlash(judgement.Lane);
            }
            _spritePopups.SpawnPopup(judgement);
            if (showHitTiming) _timing.Spawn(judgement);
        }

        public void DrawBase(SpriteBatch batch, ChartManager? chart, bool transportActive, double songTimeMs)
        {
            DrawLaneBackgrounds(batch, _laneTexture, _lanes, _hideLaneBackground);
            if (transportActive && chart != null && !_hideMeasureLines)
            {
                double lookAhead = _notes.EffectiveLookAheadMs > 0
                    ? _notes.EffectiveLookAheadMs : PerformanceUILayout.NoteDefaultLookAheadMs;
                _notes.DrawMeasureLines(batch, chart.GetActiveMeasureLines(songTimeMs, lookAhead, _notes.MeasureLinePastGraceMs), songTimeMs);
            }
            _pads.Draw(batch);
            if (transportActive && chart != null)
                _notes.DrawNotes(batch, chart.GetActiveNotes(songTimeMs, NoteLookAhead()), songTimeMs);
            DrawJudgementLine(batch, _lineTexture, _line, _hideJudgementLine);
            _spritePopups.Draw(batch);
            _fontPopups.Draw(batch);
            _timing.Draw(batch);
        }

        public void DrawOverlay(SpriteBatch batch, ChartManager? chart, bool transportActive, double songTimeMs)
        {
            if (transportActive && chart != null)
                _notes.DrawNoteOverlays(batch, chart.GetActiveNotes(songTimeMs, NoteLookAhead()), songTimeMs);
        }

        private double NoteLookAhead() => _notes.EffectiveLookAheadMs > 0 ? _notes.EffectiveLookAheadMs : 1500.0;

        public void DrawHud(SpriteBatch batch, PerformanceFrameState frame)
        {
            DrawSkillPanel(batch, _skillTexture);
            DrawProgressFrame(batch, _progressBase);
            if (ProgressFill(frame.Progress) is { } fill)
                _drawWhite?.Invoke(batch, fill.Bounds, fill.Color, fill.Depth);
            _skillPanel.Draw(batch, frame);
            _skillMeter.Draw(batch);
            DrawGauge(batch, _gaugeBase, _gaugeFill, frame.Life);
            _score.Draw(batch);
            if (!_hideCombo) _combo.Draw(batch);
        }

        internal static void DrawLaneBackgrounds(SpriteBatch batch, ITexture? texture,
            LaneBackgroundRenderer? fallback, bool hidden)
        {
            if (hidden) return;
            if (texture == null)
            {
                fallback?.Draw(batch);
                return;
            }
            int count = Math.Min(PerformanceUILayout.LaneCount, PerformanceUILayout.LaneStrips.SourceRects.Length);
            for (int i = 0; i < count; i++)
                texture.Draw(batch, PerformanceUILayout.LaneStrips.GetDestinationRect(i),
                    PerformanceUILayout.LaneStrips.SourceRects[i], Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0.8f);
        }

        internal static void DrawJudgementLine(SpriteBatch batch, ITexture? texture,
            ILanesJudgementLine? fallback, bool hidden)
        {
            if (hidden) return;
            if (texture == null)
                fallback?.Draw(batch);
            else
                texture.Draw(batch, PerformanceUILayout.HitBar.Bounds,
                    PerformanceUILayout.HitBar.GetSourceRect(texture.Width, texture.Height),
                    Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0.6f);
        }

        internal static void DrawSkillPanel(SpriteBatch batch, ITexture? texture)
        {
            if (texture != null)
            {
                var pos = PerformanceUILayout.SkillPanel.PanelPosition;
                texture.Draw(batch, new Rectangle((int)pos.X, (int)pos.Y, texture.Width, texture.Height),
                    null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0.2f);
            }
        }

        internal static void DrawProgressFrame(SpriteBatch batch, ITexture? texture)
        {
            if (texture != null)
            {
                var bounds = PerformanceUILayout.Progress.FrameBounds;
                texture.Draw(batch, new Rectangle(bounds.X, bounds.Y, texture.Width, texture.Height),
                    null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0.2f);
            }
        }

        internal static (Rectangle Bounds, Color Color, float Depth)? ProgressFill(float progress)
        {
            if (!(progress > 0)) return null;
            var bar = PerformanceUILayout.Progress.BarBounds;
            var height = (int)(bar.Height * progress);
            return (new Rectangle(bar.X, bar.Bottom - height, bar.Width, height), Color.LightBlue, 0.2f);
        }

        internal static void DrawProgressFill(SpriteBatch batch, Texture2D? whitePixel,
            Rectangle bounds, Color color, float depth)
        {
            if (whitePixel != null)
                batch.Draw(whitePixel, bounds, null, color, 0f, Vector2.Zero, SpriteEffects.None, depth);
        }

        internal static void DrawGauge(SpriteBatch batch, ITexture? gaugeBase, ITexture? gaugeFill, float life)
        {
            if (gaugeBase != null)
            {
                var pos = PerformanceUILayout.Gauge.FramePosition;
                gaugeBase.Draw(batch, new Rectangle((int)pos.X, (int)pos.Y, gaugeBase.Width, gaugeBase.Height),
                    null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0.19f);
            }
            if (gaugeFill != null && life > 0)
            {
                var origin = PerformanceUILayout.Gauge.FillOrigin;
                var width = (int)(gaugeFill.Width * life);
                var height = PerformanceUILayout.Gauge.FillHeight;
                gaugeFill.Draw(batch, new Rectangle((int)origin.X, (int)origin.Y, width, height),
                    new Rectangle(0, 0, width, height), Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0.18f);
            }
        }

        public void DrawAdditive(SpriteBatch batch) => _effects.Draw(batch);

        private ITexture? Acquire(string path) => Own(TryLoad(_resources, path));

        private ITexture? Own(ITexture? texture)
        {
            if (texture != null) _ownedTextures.Add(texture);
            return texture;
        }

        internal static ITexture? LoadSkillPanelTexture(IResourceManager resources, ISkinTheme theme)
        {
            string path = ResolveSkillPanelTexturePath(theme);
            var texture = TryLoad(resources, path, out var cached);
            try
            {
                if (texture != null && texture.Width > 1 && texture.Height > 1) return texture;
                var invalid = texture;
                texture = null;
                invalid?.Dispose();
                if (string.Equals(path, TexturePath.SkillPanel, StringComparison.Ordinal)) return null;
                texture = TryLoad(resources, TexturePath.SkillPanel, out cached);
                if (texture != null && texture.Width > 1 && texture.Height > 1) return texture;
                invalid = texture;
                texture = null;
                invalid?.Dispose();
                return null;
            }
            catch
            {
                try
                {
                    if (cached) texture?.RemoveReference();
                    else texture?.Dispose();
                }
                catch { }
                throw;
            }
        }

        private static ITexture? TryLoad(IResourceManager resources, string path) =>
            TryLoad(resources, path, out _);

        private static ITexture? TryLoad(IResourceManager resources, string path, out bool cached)
        {
            try { cached = resources.TryLoadTexture(path, out var texture); return texture; }
            catch { cached = false; return null; }
        }

        internal static string ResolveSkillPanelTexturePath(ISkinTheme theme)
        {
            var path = theme.GetString("Performance.SkillPanelTexture", TexturePath.SkillPanel);
            return string.IsNullOrWhiteSpace(path) ? TexturePath.SkillPanel : path;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Exception? firstFailure = null;
            void Release(Action action)
            {
                try { action(); }
                catch (Exception ex) { firstFailure ??= ex; }
            }
            Release(() => _lanes?.Dispose());
            Release(() => _line?.Dispose());
            Release(() => _score?.Dispose());
            Release(() => _combo?.Dispose());
            Release(() => _skillPanel?.Dispose());
            Release(() => _skillMeter?.Dispose());
            Release(() => _notes?.Dispose());
            Release(() => _effects?.Dispose());
            Release(() => _spritePopups?.Dispose());
            Release(() => _fontPopups?.Dispose());
            Release(() => _timing?.Dispose());
            Release(() => _pads?.Dispose());
            foreach (var texture in _ownedTextures)
                Release(texture.RemoveReference);
            _ownedTextures.Clear();
            _laneTexture = _lineTexture = _gaugeBase = _gaugeFill = _progressBase = _skillTexture = null;
            if (firstFailure != null) ExceptionDispatchInfo.Capture(firstFailure).Throw();
        }
    }
}
