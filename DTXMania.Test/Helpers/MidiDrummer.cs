using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Input.Midi;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using Xunit;

namespace DTXMania.Test.Helpers;

/// <summary>
/// A scripted drummer for in-process MIDI automation tests. Pad strikes go through the
/// real input path (SimulatedMidiDeviceBackend, MidiInputSource, InputRouter, JudgementManager)
/// on a fixed 60 fps frame clock, in the game loop's order: input update, then judgement on
/// the latency-compensated clock. A strike lands on the first frame at or after its time, so
/// it is judged 0 to one frame late, as in the game.
/// </summary>
internal sealed class MidiDrummer : IDisposable
{
    internal const double FrameMs = 1000.0 / 60.0;

    /// <summary>
    /// An Alesis-style e-drum kit bound like a working DTXManiaNX key assign:
    /// pad MIDI note to CX lane.
    /// </summary>
    internal static readonly IReadOnlyDictionary<int, int> NxStyleKit = new Dictionary<int, int>
    {
        [49] = 0,                                // LC: crash
        [42] = 1, [46] = 1, [23] = 1,            // HH: closed, open, half-open
        [44] = 2,                                // LP: hi-hat pedal
        [38] = 3, [40] = 3, [37] = 3,            // SN: head, rim, cross-stick
        [48] = 4, [50] = 4,                      // HT: head, rim
        [36] = 5,                                // BD
        [45] = 6, [47] = 6,                      // LT: head, rim
        [43] = 7, [58] = 7,                      // FT: head, rim
        [51] = 8, [52] = 8, [55] = 8, [57] = 8,  // CY: ride bow and other right cymbals
        [53] = 9, [59] = 9,                      // RD: ride bell, ride edge
    };

    private readonly SimulatedMidiDeviceBackend _midi = new();
    private readonly InputManagerCompat _input;
    private readonly ConfigData _config;
    private readonly List<PadStrike> _strikes = new();
    private readonly List<(double FromMs, double ToMs)> _stalls = new();
    private bool _disposed;

    internal MidiDrummer(ParsedChart chart, Action<ConfigData>? configure = null)
    {
        var configManager = new ConfigManager();
        _config = configManager.Config;
        foreach (var (pad, lane) in NxStyleKit)
            _config.KeyBindings[KeyBindings.CreateMidiButtonId(pad)] = lane;
        configure?.Invoke(_config);

        _input = new InputManagerCompat(configManager, _midi);
        Chart = chart;
        Rules = new GameplayRules(_config);
        Rules.BindChart(chart);
        Rules.InitializeJudgement(_input);
        Rules.InitializeScoreAndCombo();
        Rules.InitializeGaugeAndSkill();
        Rules.JudgementManager!.IsActive = true;
    }

    internal ParsedChart Chart { get; }

    internal GameplayRules Rules { get; }

    /// <summary>
    /// The pad a player on this kit strikes for a chart note. The kit has one right cymbal,
    /// the ride, so CY and RD notes are both played on the ride bow (51, bound to CY).
    /// </summary>
    internal static int KitPadFor(Note note) => note.Channel switch
    {
        0x1A => 49,
        0x11 => 42,
        0x18 => 46,
        0x1B or 0x1C => 44,
        0x12 => 38,
        0x14 => 48,
        0x13 => 36,
        0x15 => 45,
        0x17 => 43,
        0x16 or 0x19 => 51,
        _ => throw new ArgumentOutOfRangeException(nameof(note), note.Channel, "Not a drum channel."),
    };

    /// <summary>Strikes <paramref name="pad"/> at song time <paramref name="timeMs"/>.</summary>
    internal MidiDrummer Strike(double timeMs, int pad, int velocity = 100)
    {
        _strikes.Add(new PadStrike(timeMs, pad, velocity));
        return this;
    }

    /// <summary>
    /// Strikes every chart note <paramref name="offsetMs"/> from its time (positive is late)
    /// on the pad <paramref name="pickPad"/> chooses, <see cref="KitPadFor"/> by default.
    /// </summary>
    internal MidiDrummer PlayChart(double offsetMs = 0, Func<Note, int>? pickPad = null, int velocity = 100)
    {
        pickPad ??= KitPadFor;
        foreach (var note in Chart.Notes)
            Strike(note.TimeMs + offsetMs, pickPad(note), velocity);
        return this;
    }

    /// <summary>Freezes the game loop between two song times, like a GC pause or hitch.</summary>
    internal MidiDrummer Stall(double fromMs, double toMs)
    {
        _stalls.Add((fromMs, toMs));
        return this;
    }

    /// <summary>
    /// Runs frames until every note is hit or timed out and returns the HUD state.
    /// </summary>
    /// <param name="sendNoteOff">
    /// True sends each strike's note-off in the same frame as its note-on (the hardest case
    /// for a press to survive); false models modules that never send note-off.
    /// </param>
    internal PerformanceFrameState Run(bool sendNoteOff = true)
    {
        var strikes = _strikes.OrderBy(strike => strike.TimeMs).ToList();
        var lastNoteMs = Chart.Notes.Count > 0 ? Chart.Notes.Max(note => note.TimeMs) : 0.0;
        var lastStrikeMs = strikes.Count > 0 ? strikes[^1].TimeMs : 0.0;
        var lastStallEndMs = _stalls.Count > 0 ? _stalls.Max(stall => stall.ToMs) : 0.0;
        var endMs = Math.Max(Math.Max(lastNoteMs, lastStrikeMs), lastStallEndMs)
            + Math.Abs(_config.AudioLatencyOffsetMs)
            + JudgementManager.MissTimeoutMs
            + 2 * FrameMs;
        var modifiers = new PlaybackModifiers(PlaySpeedRange.Default, PitchRange.Default);

        var next = 0;
        var timeMs = 0.0;
        for (var frame = 1; timeMs <= endMs; frame++)
        {
            timeMs = frame * FrameMs;

            // MIDI keeps arriving while the game loop is frozen; it waits in the input queue.
            for (; next < strikes.Count && strikes[next].TimeMs <= timeMs; next++)
            {
                var strike = strikes[next];
                Assert.True(_midi.TryInjectNote(strike.Pad, strike.Velocity, isPressed: true));
                if (sendNoteOff)
                    Assert.True(_midi.TryInjectNote(strike.Pad, 0, isPressed: false));
            }

            if (_stalls.Any(stall => timeMs > stall.FromMs && timeMs < stall.ToMs))
                continue;

            _input.Update(FrameMs / 1000.0);
            Rules.UpdateJudgement(
                GameplayRules.GetPlayerJudgementTimeMs(timeMs, _config.AudioLatencyOffsetMs, modifiers));
        }

        Assert.Equal(strikes.Count, next);
        return Rules.BuildFrameState(timeMs);
    }

    internal JudgementEvent? JudgementOf(Note note) =>
        Rules.JudgementManager!.GetNoteRuntimeData(note.Id)?.JudgementEvent;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Rules.ClearJudgement();
        Rules.ClearScoreAndCombo();
        Rules.ClearGaugeAndSkill();
        _input.Dispose();
    }

    private readonly record struct PadStrike(double TimeMs, int Pad, int Velocity);
}
