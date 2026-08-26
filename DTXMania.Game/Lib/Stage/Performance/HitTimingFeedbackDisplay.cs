#nullable enable

using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.Stage.Performance;

internal sealed class HitTimingFeedbackState
{
    public HitTimingFeedbackState(
        int laneIndex,
        PerformanceUILayout.HitTimingFeedback.DeltaProjection projection)
    {
        LaneIndex = laneIndex;
        Projection = projection;
    }

    public int LaneIndex { get; }
    public PerformanceUILayout.HitTimingFeedback.DeltaProjection Projection { get; }
    public double ElapsedSeconds { get; private set; }
    public float Alpha { get; private set; } = 1f;
    public bool IsActive { get; private set; } = true;
    public int GlyphCount => Projection.GlyphSlots.Count;

    public bool Update(double deltaTime)
    {
        if (!IsActive)
            return false;

        ElapsedSeconds += Math.Max(0.0, deltaTime);
        var duration = PerformanceUILayout.HitTimingFeedback.TotalDurationSeconds;
        if (ElapsedSeconds >= duration)
        {
            Alpha = 0f;
            IsActive = false;
            return false;
        }

        Alpha = MathHelper.Clamp((float)(1.0 - ElapsedSeconds / duration), 0f, 1f);
        return true;
    }
}

public sealed class HitTimingFeedbackDisplay : IDisposable
{
    private readonly HitTimingFeedbackState?[] _activeStates;
    private readonly IResourceManager? _resourceManager;
    private ITexture? _lagNumbersTexture;
    private bool _reloadAttempted;
    private bool _disposed;

    public HitTimingFeedbackDisplay(IResourceManager resourceManager)
    {
        ArgumentNullException.ThrowIfNull(resourceManager);

        _resourceManager = resourceManager;
        _activeStates = new HitTimingFeedbackState?[PerformanceUILayout.LaneCount];
        _lagNumbersTexture = LoadLagNumbersTexture(resourceManager);
        _reloadAttempted = _lagNumbersTexture == null;
    }

    private HitTimingFeedbackDisplay(
        ITexture? lagNumbersTexture,
        IResourceManager? resourceManager,
        HitTimingFeedbackState?[]? activeStates)
    {
        var states = activeStates ?? new HitTimingFeedbackState?[PerformanceUILayout.LaneCount];
        if (states.Length != PerformanceUILayout.LaneCount)
            throw new ArgumentException("Active state storage must match the lane count.", nameof(activeStates));

        _lagNumbersTexture = lagNumbersTexture;
        _resourceManager = resourceManager;
        _activeStates = states;

        _reloadAttempted = lagNumbersTexture == null;
    }

    internal static HitTimingFeedbackDisplay CreateForTesting(
        ITexture? lagNumbersTexture,
        IResourceManager? resourceManager = null,
        HitTimingFeedbackState?[]? activeStates = null)
    {
        return new HitTimingFeedbackDisplay(lagNumbersTexture, resourceManager, activeStates);
    }

    internal IReadOnlyList<HitTimingFeedbackState?> ActiveStatesForTesting => _activeStates;

    internal int ActiveLaneCountForTesting
    {
        get
        {
            var count = 0;
            foreach (var state in _activeStates)
            {
                if (state?.IsActive == true)
                    count++;
            }

            return count;
        }
    }

    public void Spawn(JudgementEvent judgementEvent)
    {
        if (_disposed || judgementEvent == null)
            return;

        var laneIndex = judgementEvent.Lane;
        if (laneIndex < 0 || laneIndex >= _activeStates.Length)
            return;

        if (!TryEnsureTextureAvailable())
            return;

        _activeStates[laneIndex] = new HitTimingFeedbackState(
            laneIndex,
            PerformanceUILayout.HitTimingFeedback.ProjectDelta(judgementEvent.DeltaMs));
    }

    public void Update(double deltaTime)
    {
        if (_disposed)
            return;

        for (var laneIndex = 0; laneIndex < _activeStates.Length; laneIndex++)
        {
            var state = _activeStates[laneIndex];
            if (state == null || !state.Update(deltaTime))
                _activeStates[laneIndex] = null;
        }
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (_disposed || !TryEnsureTextureAvailable() || spriteBatch == null)
            return;

        var texture = _lagNumbersTexture;
        if (texture == null)
            return;

        foreach (var state in _activeStates)
        {
            if (state?.IsActive != true || state.Alpha <= 0f)
                continue;

            var position = PerformanceUILayout.HitTimingFeedback.GetLaneRunPosition(
                state.LaneIndex,
                state.GlyphCount);
            var tint = state.Projection.IsSlow ? Color.Red : Color.Cyan;

            try
            {
                for (var glyphIndex = 0; glyphIndex < state.Projection.GlyphSlots.Count; glyphIndex++)
                {
                    var source = PerformanceUILayout.HitTimingFeedback.GetSourceRectangle(
                        state.Projection.GlyphSlots[glyphIndex],
                        state.Projection.IsSlow);
                    var destination = new Rectangle(
                        (int)MathF.Round(position.X + glyphIndex * PerformanceUILayout.HitTimingFeedback.GlyphWidth),
                        (int)MathF.Round(position.Y),
                        PerformanceUILayout.HitTimingFeedback.GlyphWidth,
                        PerformanceUILayout.HitTimingFeedback.GlyphHeight);

                    texture.Draw(
                        spriteBatch,
                        destination,
                        source,
                        tint * state.Alpha,
                        0f,
                        Vector2.Zero,
                        SpriteEffects.None,
                        0.5f);
                }
            }
            catch (Exception ex)
            {
                if (!HandleTextureDrawFailure(texture, ex))
                    throw;

                return;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Array.Clear(_activeStates, 0, _activeStates.Length);
        ReleaseHeldTexture();
        _disposed = true;
    }

    private static ITexture? LoadLagNumbersTexture(IResourceManager resourceManager)
    {
        ITexture? texture = null;
        try
        {
            if (!resourceManager.ResourceExists(TexturePath.LagNumbers))
                return null;

            texture = resourceManager.LoadTexture(TexturePath.LagNumbers);
            if (texture == null)
                return null;

            if (IsInvalidTexture(texture))
            {
                var invalidTexture = texture;
                texture = null;
                invalidTexture.RemoveReference();
                return null;
            }

            return texture;
        }
        catch (Exception ex)
        {
            try
            {
                texture?.RemoveReference();
            }
            catch
            {
            }

            System.Diagnostics.Debug.WriteLine(
                $"HitTimingFeedbackDisplay: {ex.GetType().Name} loading {TexturePath.LagNumbers}: {ex.Message}");
            return null;
        }
    }

    private bool TryEnsureTextureAvailable()
    {
        var texture = _lagNumbersTexture;
        if (texture == null)
            return TryReloadTexture();

        try
        {
            if (!IsInvalidTexture(texture))
                return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"HitTimingFeedbackDisplay: {ex.GetType().Name} validating held {TexturePath.LagNumbers}: {ex.Message}");
        }

        ReleaseHeldTexture();
        return TryReloadTexture();
    }

    private bool TryReloadTexture()
    {
        if (_reloadAttempted || _resourceManager == null || _disposed)
            return false;

        _reloadAttempted = true;
        var reloaded = LoadLagNumbersTexture(_resourceManager);
        if (reloaded == null)
            return false;

        _lagNumbersTexture = reloaded;
        _reloadAttempted = false;
        return true;
    }

    private void ReleaseHeldTexture()
    {
        var texture = _lagNumbersTexture;
        if (texture == null)
            return;

        _lagNumbersTexture = null;
        try
        {
            texture.RemoveReference();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"HitTimingFeedbackDisplay: {ex.GetType().Name} releasing {TexturePath.LagNumbers}: {ex.Message}");
        }
    }

    private bool HandleTextureDrawFailure(ITexture texture, Exception exception)
    {
        var invalid = true;
        try
        {
            invalid = IsInvalidTexture(texture);
        }
        catch
        {
        }

        if (!invalid)
            return false;

        System.Diagnostics.Debug.WriteLine(
            $"HitTimingFeedbackDisplay: {exception.GetType().Name} drawing {TexturePath.LagNumbers}: {exception.Message}");
        ReleaseHeldTexture();
        TryReloadTexture();
        return true;
    }

    private static bool IsInvalidTexture(ITexture texture)
    {
        if (texture.IsDisposed)
            return true;

        if (texture.Width < PerformanceUILayout.HitTimingFeedback.RequiredTextureWidth
            || texture.Height < PerformanceUILayout.HitTimingFeedback.RequiredTextureHeight)
            return true;

        var underlyingTexture = texture.Texture;
        return underlyingTexture == null || underlyingTexture.IsDisposed;
    }
}
