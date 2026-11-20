#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Song.Notation;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework.Graphics;
using MeasureLine = DTXMania.Game.Lib.Song.Components.MeasureLine;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>Owns Tab presentation; gameplay, batches and the white pixel are borrowed.</summary>
    internal sealed class TabPlayfield : IPerformancePlayfield
    {
        private readonly IReadOnlySet<int> _automatedLanes;
        private readonly bool _hideCombo;
        private NotationStripRenderer? _strip;
        private TabHudDisplay? _hud;
        private TabModeArt? _art;
        private int _scrollSpeed = ScrollSpeedRange.Default;
        private bool _disposed;

        private TabPlayfield(IReadOnlySet<int> automatedLanes, bool hideCombo)
        {
            _automatedLanes = automatedLanes;
            _hideCombo = hideCombo;
        }

        internal static TabPlayfield? Create(
            IResourceManager resources, NotationChart chart, IReadOnlyList<MeasureLine> measureLines,
            Texture2D? whitePixel, IReadOnlySet<int> automatedLanes, bool hideCombo,
            Func<SkinPalette, TabModeArt, TabHudDisplay?> createHud)
        {
            var field = new TabPlayfield(automatedLanes, hideCombo);
            try
            {
                var theme = resources.CurrentTheme;
                var palette = SkinPalette.Resolve(theme);
                field._art = TabModeArt.Load(resources, theme);
                field._strip = new NotationStripRenderer(resources, chart, measureLines, palette, field._art, whitePixel);
                if (field._strip.IsReady)
                    field._hud = createHud(palette, field._art);
                if (field._strip.IsReady && field._hud?.IsReady == true)
                    return field;
            }
            catch
            {
                try { field.Dispose(); }
                catch { }
                throw;
            }

            field.Dispose();
            return null;
        }

        public DrumsNoteDisplayStyle Mode => DrumsNoteDisplayStyle.Tab;
        public void Update(double deltaTime, bool gameplayActive) => _hud?.Update(deltaTime);
        public void OnChartPublished(double bpm, int scrollSpeedPercent) => _scrollSpeed = scrollSpeedPercent;
        public void OnScrollSpeedChanged(int scrollSpeedPercent) => _scrollSpeed = scrollSpeedPercent;
        public void OnLaneInput(int lane) => _hud?.TriggerKeyFlash(lane);
        public void OnAutoHit(int lane) { }

        public void OnJudgement(JudgementEvent judgement, PerformanceFrameState frame, bool showHitTiming)
        {
            _hud?.TriggerKeyFlash(judgement.Lane, isMiss: !judgement.IsHit());
            if (!_automatedLanes.Contains(judgement.Lane) || _automatedLanes.Count >= PerformanceUILayout.LaneCount)
                _hud?.NotifyJudgement(judgement.Type, showHitTiming, judgement.DeltaMs);
        }

        public void DrawBase(SpriteBatch batch, ChartManager? chart, bool transportActive, double songTimeMs)
        {
            _strip?.Draw(batch, songTimeMs, _scrollSpeed);
            _hud?.DrawKeyFlashes(batch);
        }

        public void DrawOverlay(SpriteBatch batch, ChartManager? chart, bool transportActive, double songTimeMs) { }
        public void DrawHud(SpriteBatch batch, PerformanceFrameState frame) => _hud?.Draw(batch, frame, _hideCombo);
        public void DrawAdditive(SpriteBatch batch) { }

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
            Release(() => _strip?.Dispose());
            Release(() => _hud?.Dispose());
            Release(() => _art?.Release());
            _strip = null;
            _hud = null;
            _art = null;
            if (firstFailure != null) ExceptionDispatchInfo.Capture(firstFailure).Throw();
        }
    }
}
