#nullable enable

using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Song.Components;

namespace DTXMania.Game.Lib.Stage.Performance;

internal sealed class MetronomePlayer
{
    private readonly IReadOnlyList<BeatMarker> _markers;
    private readonly double _maxLateChartMs;
    private readonly Action<BeatMarker> _playClick;
    private int _nextMarkerIndex;

    internal MetronomePlayer(
        IReadOnlyList<BeatMarker> markers,
        double maxLateChartMs,
        Action<BeatMarker> playClick)
    {
        _markers = markers;
        _maxLateChartMs = maxLateChartMs;
        _playClick = playClick;
    }

    internal void Update(double currentChartTimeMs)
    {
        BeatMarker? latestConsumed = null;
        while (_nextMarkerIndex < _markers.Count &&
               _markers[_nextMarkerIndex].TimeMs <= currentChartTimeMs)
        {
            latestConsumed = _markers[_nextMarkerIndex];
            _nextMarkerIndex++;
        }

        if (latestConsumed is not null &&
            currentChartTimeMs - latestConsumed.TimeMs <= _maxLateChartMs)
        {
            _playClick(latestConsumed);
        }
    }
}
