#nullable enable

using System;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>Visual feedback and drawing, inside the stage's sprite batches.</summary>
    public interface IPerformancePlayfield : IDisposable
    {
        DrumsNoteDisplayStyle Mode { get; }
        void Update(double deltaTime, bool gameplayActive);
        void OnChartPublished(double bpm, int scrollSpeedPercent);
        void OnLaneInput(int lane);
        void OnAutoHit(int lane);
        void OnJudgement(JudgementEvent judgement, PerformanceFrameState frame, bool showHitTiming);
        void OnScrollSpeedChanged(int scrollSpeedPercent);
        void DrawBase(SpriteBatch spriteBatch, ChartManager? chart, bool transportActive, double songTimeMs);
        void DrawOverlay(SpriteBatch spriteBatch, ChartManager? chart, bool transportActive, double songTimeMs);
        void DrawHud(SpriteBatch spriteBatch, PerformanceFrameState frame);
        void DrawAdditive(SpriteBatch spriteBatch);
    }
}
