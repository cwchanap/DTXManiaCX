#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Notation;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Per-activation gameplay audio session: owns the prepared audio set,
    /// audio loader, song timer transport, chip sound cache, scheduled/active
    /// BGM playback and the metronome. Created and bound on the update thread
    /// after <see cref="Prepare"/> completes on the background-work worker.
    /// <para>
    /// Ownership: <see cref="Bind"/> takes ownership of the supplied
    /// <see cref="PreparedGameplayAudioSet"/> and all sounds borrowed from it;
    /// <see cref="Dispose"/> releases the set exactly once.
    /// </para>
    /// <para>
    /// Scheduling stays with the stage: the worker-pure <see cref="Prepare"/>
    /// is invoked inside <see cref="BaseStage.BackgroundWork"/>'s Run delegate
    /// and all update-thread callbacks remain stage-owned.
    /// </para>
    /// </summary>
    internal sealed class GameplayAudioSession : IDisposable
    {
        private readonly IResourceManager? _resourceManager;
        private readonly ILoggerFactory? _loggerFactory;
        private readonly ILogger _logger;
        private readonly PlaybackModifiers _playbackModifiers;
        private readonly bool _metronomeEnabled;
        private readonly Action<BeatMarker> _playClick;

        private ParsedChart? _chart;
        private PreparedGameplayAudioSet? _preparedAudioSet;
        private AudioLoader? _audioLoader;
        private ChipSoundCache? _chipSoundCache;
        private SongTimer? _songTimer;
        private readonly Dictionary<string, ISound> _bgmSounds = new();
        private readonly HashSet<string> _bgmPlayFailureReported = new();
        private List<BGMEvent> _scheduledBGMEvents = new();
        private readonly List<SoundEffectInstance> _activeBgmInstances = new();
        private MetronomePlayer? _metronomePlayer;
        private ISound? _metronomeBeatSound;
        private ISound? _metronomeAccentSound;
        private readonly HashSet<ISound> _metronomeFailureReportedSounds = new();

        /// <summary>
        /// Creates a session from activation-time snapshots. All inputs are
        /// frozen before the worker launch that produces the prepared set;
        /// the session itself is constructed later, on the update thread.
        /// </summary>
        /// <param name="resourceManager">Resource manager for metronome sounds and the audio loader.</param>
        /// <param name="loggerFactory">Logger factory for owned components (may be null).</param>
        /// <param name="logger">Logger for warnings/errors (the stage's captured logger).</param>
        /// <param name="playbackModifiers">Frozen playback profile (speed/pitch) for the run.</param>
        /// <param name="metronomeEnabled">Frozen metronome config flag.</param>
        /// <param name="playClick">
        /// Click sink for the metronome. When null the session plays its own
        /// click sounds; the stage passes its protected virtual so test
        /// subclasses can observe markers.
        /// </param>
        public GameplayAudioSession(
            IResourceManager? resourceManager,
            ILoggerFactory? loggerFactory,
            ILogger logger,
            PlaybackModifiers playbackModifiers,
            bool metronomeEnabled,
            Action<BeatMarker>? playClick = null)
        {
            _resourceManager = resourceManager;
            _loggerFactory = loggerFactory;
            _logger = logger;
            _playbackModifiers = playbackModifiers;
            _metronomeEnabled = metronomeEnabled;
            _playClick = playClick ?? PlayMetronomeClick;
        }

        /// <summary>
        /// Worker-pure audio preparation. Every input is a snapshot parameter
        /// or a method-local; nothing here touches stage fields or GPU state.
        /// The chart/notation publication is posted first so the playfield is
        /// up behind LOADING while the audio work below runs. The
        /// prepared-audio variant dependencies are factory delegates so they
        /// are only constructed for non-default playback profiles, exactly as
        /// before. Logging uses the worker logger resolved before Run.
        /// </summary>
        internal static PreparedGameplayAudioSet Prepare(
            ParsedChart? chartSnapshot,
            string? fallbackChartPath,
            DrumsNoteDisplayStyle requestedNoteDisplayStyle,
            PlaybackModifiers playbackModifiers,
            ILogger workerLogger,
            IProgress<AudioPreparationProgress> progress,
            Action<ParsedChart, NotationChart?> publishChart,
            Func<IAudioVariantProcessor> createAudioVariantProcessor,
            Func<PlaybackAudioVariantCache> createAudioVariantCache,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ParsedChart chart = chartSnapshot ??
                DTXChartParser.ParseAsync(fallbackChartPath!).GetAwaiter().GetResult();
            cancellationToken.ThrowIfCancellationRequested();

            // HPA-411: build the pure notation chart on the worker when the requested
            // style is Tab. Fonts are never touched here: the fallback ParseAsync above
            // can resume on a thread-pool thread, so resource loads are deferred to the
            // update-thread publication. A build failure is logged locally and
            // gameplay continues in Lanes.
            NotationChart? notationChart = null;
            if (requestedNoteDisplayStyle == DrumsNoteDisplayStyle.Tab)
            {
                try
                {
                    notationChart = NotationChartBuilder.Build(chart);
                }
                catch (Exception ex)
                {
                    LogCapturedError(
                        workerLogger,
                        "PerformanceStage: Tab notation build failed; staying in Lanes for this song.",
                        ex);
                }
            }

            // Everything the playfield needs is ready: publish it now so the right
            // playfield is drawn behind LOADING while the (possibly slow) audio
            // preparation below runs.
            publishChart(chart, notationChart);

            var noteWavIds = new HashSet<string>(
                chart.Notes.Select(n => n.Value)
                    .Where(v => !string.IsNullOrEmpty(v)));
            var noteWavDefs = chart.WavDefinitions
                .Where(kvp => noteWavIds.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            return PreparedGameplayAudioSet.PrepareAsync(
                chart.BackgroundAudioPath,
                chart.BGMEvents.Select(bgm => bgm.AudioFilePath),
                noteWavDefs,
                playbackModifiers,
                playbackModifiers.IsDefault ? null : createAudioVariantProcessor(),
                playbackModifiers.IsDefault ? null : createAudioVariantCache(),
                progress,
                cancellationToken,
                reportSoundLoadFailure: (path, ex) =>
                    LogCapturedError(
                        workerLogger,
                        $"PerformanceStage: Gameplay audio load failed ({path})",
                        ex)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Binds the session to the prepared audio set. Takes ownership of the
        /// set immediately so it is disposed exactly once even if binding work
        /// throws partway through. Runs on the update thread.
        /// </summary>
        internal void Bind(ParsedChart chart, PreparedGameplayAudioSet preparedSet)
        {
            _chart = chart;
            _preparedAudioSet = preparedSet;

            _audioLoader = new AudioLoader(_resourceManager!);
            _audioLoader.BindBorrowedBackground(
                preparedSet.MainBackground,
                chart.BackgroundAudioPath);

            _chipSoundCache = new ChipSoundCache(
                preparedSet.ChipSoundsByWavId,
                _loggerFactory?.CreateLogger<ChipSoundCache>());

            BindPreparedBGMSounds();

            // Schedule BGM events for playback
            _scheduledBGMEvents = chart.BGMEvents
                .OrderBy(bgm => bgm.TimeMs)
                .ToList();

            // Create song timer with logging support
            var songTimer = _audioLoader.CreateSongTimer(
                _playbackModifiers.PlaySpeedPercent,
                _preparedAudioSet.RuntimePitch,
                message => LogPerformanceError(message));
            if (songTimer == null)
            {
                LogPerformanceError("PerformanceStage: No audio timer available; using silent GameTime clock");
                songTimer = new SongTimer(
                    _playbackModifiers.PlaySpeedPercent,
                    message => LogPerformanceError(message));
            }
            _songTimer = songTimer;

            InitializeMetronome();
        }

        /// <summary>
        /// Whether the timer reports the song as playing.
        /// </summary>
        internal bool IsPlaying => _songTimer?.IsPlaying == true;

        /// <summary>
        /// Whether the timer reports the song clock as paused.
        /// </summary>
        internal bool IsPaused => _songTimer?.IsPaused == true;

        /// <summary>
        /// Per-frame timer update; keeps the same position in the stage
        /// update order as the old direct SongTimer call.
        /// </summary>
        internal void UpdateTimer(GameTime gameTime)
        {
            _songTimer?.Update(gameTime);
        }

        /// <summary>
        /// Speed-adjusted logical song time; 0 when no timer exists.
        /// </summary>
        internal double GetCurrentMs(GameTime gameTime)
        {
            return _songTimer?.GetCurrentMs(gameTime) ?? 0.0;
        }

        /// <summary>
        /// Starts song playback. Applies the BGM mute/per-WAV volume/pan
        /// policy after the timer starts, exactly as the stage did.
        /// Returns false when the timer is missing or playback failed to start.
        /// </summary>
        internal bool Start(GameTime gameTime, ParsedChart? chart)
        {
            if (_songTimer == null)
            {
                return false;
            }

            // Start the song timer - this provides the master clock for notes and BGM events
            _songTimer.SetPosition(0.0, gameTime);
            bool playbackStarted = _songTimer.Play(gameTime);

            // Choose playback strategy based on BGM events
            if (_scheduledBGMEvents.Count > 0)
            {
                // New approach: Use BGM events for timed playback, silence the background audio
                _songTimer.Volume = 0.0f; // Mute the background audio since we'll use BGM events
            }
            else
            {
                // Legacy approach: Play background audio immediately (no BGM events).
                // Honor the chart's per-WAV #VOLUME/#PAN for the master background track
                // when a background WAV id is defined; otherwise default to full volume.
                var backgroundWavId = chart?.BackgroundWavId;
                if (!string.IsNullOrEmpty(backgroundWavId))
                {
                    _songTimer.Volume = chart!.GetVolume(backgroundWavId);
                    _songTimer.Pan = chart!.GetPan(backgroundWavId);
                }
                else
                {
                    _songTimer.Volume = 1.0f; // Ensure background audio is audible
                }
            }

            return playbackStarted;
        }

        /// <summary>
        /// Advances scheduled BGM events to the raw logical song time. Must be
        /// called at the same point in the update order the stage previously
        /// invoked ProcessBGMEvents.
        /// </summary>
        internal void Advance(double rawSongTimeMs)
        {
            var dueEvents = _scheduledBGMEvents
                .Where(bgm => rawSongTimeMs >= bgm.TimeMs)
                .OrderBy(bgm => bgm.TimeMs)
                .ToArray();

            // Trigger from earliest to latest. Iterating backward here reverses
            // simultaneous/overdue BGM events after a slow frame.
            foreach (var bgmEvent in dueEvents)
            {
                TriggerBGMEvent(bgmEvent);
                _scheduledBGMEvents.Remove(bgmEvent);
            }

            // Reap finished instances so their native audio resources are released
            // during gameplay rather than lingering until stage teardown. Mirrors
            // ChipSoundCache.CleanupStoppedInstances.
            CleanupStoppedBgmInstances();
        }

        /// <summary>
        /// Advances the metronome to the raw logical song time.
        /// </summary>
        internal void UpdateMetronome(double rawSongTimeMs)
        {
            _metronomePlayer?.Update(rawSongTimeMs);
        }

        /// <summary>
        /// Plays the drum chip sound associated with a note's WAV id, if loaded.
        /// No-op when cache is unavailable or note has no associated WAV.
        /// Honors the chart's per-WAV #VOLUME/#PAN; defaults to full volume,
        /// centered.
        /// </summary>
        internal void PlayChip(Note? note)
        {
            if (note == null || string.IsNullOrEmpty(note.Value))
                return;

            // NOTE: ConfigData.MasterVolume is not yet combined here — this is the
            // integration point once master-volume routing is wired into Lib/.
            float volume = _chart?.GetVolume(note.Value) ?? 1.0f;
            float pan = _chart?.GetPan(note.Value) ?? 0.0f;
            _chipSoundCache?.Play(
                note.Value,
                volume,
                _preparedAudioSet?.RuntimePitch ?? 0.0f,
                pan);
        }

        /// <summary>
        /// Plays the metronome click sound for a beat marker. Measure-start
        /// markers use the accent sound. Sound failures are reported once per
        /// sound per run and suppressed (the metronome is auxiliary — never
        /// let it crash gameplay). Also the default click sink when no
        /// external <c>playClick</c> delegate was supplied.
        /// </summary>
        internal void PlayMetronomeClick(BeatMarker marker)
        {
            var sound = marker.IsMeasureStart
                ? _metronomeAccentSound
                : _metronomeBeatSound;
            try
            {
                sound?.SoundEffect?.Play(1.0f, 0.0f, 0.0f);
            }
            catch (Exception ex)
            {
                // Report once per affected sound per run: a persistently failing
                // metronome click must not fill the error log on every beat.
                if (sound != null && _metronomeFailureReportedSounds.Add(sound))
                {
                    LogPerformanceError($"PerformanceStage: Metronome click playback failed: {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// Stops the song timer and every live gameplay audio instance
        /// immediately so nothing bleeds into the next stage.
        /// </summary>
        internal void StopAll()
        {
            _songTimer?.Stop();

            // Stop scheduled BGM instances; full stop-and-dispose happens in Dispose.
            foreach (var instance in _activeBgmInstances)
            {
                try { instance.Stop(); }
                catch { /* Best effort — teardown will dispose. */ }
            }

            // StopAll leaves the borrowed ISound objects and the instances
            // themselves owned by the cache; Dispose handles final
            // stop-and-dispose.
            _chipSoundCache?.StopAll();
        }

        /// <summary>
        /// Bound timer for scenarios that observe or drive clock state
        /// directly (e.g. paused-clock draw/telemetry paths).
        /// </summary>
        internal SongTimer? Timer => _songTimer;

        /// <summary>
        /// Bound prepared set; ownership stays with the session.
        /// </summary>
        internal PreparedGameplayAudioSet? PreparedSet => _preparedAudioSet;

        /// <summary>
        /// Pending scheduled BGM events, consumed in chronological order.
        /// </summary>
        internal IReadOnlyList<BGMEvent> ScheduledBgmEvents => _scheduledBGMEvents;

        /// <summary>
        /// Bound metronome player when the click track is enabled.
        /// </summary>
        internal MetronomePlayer? Metronome => _metronomePlayer;

        /// <summary>
        /// Live scheduled-BGM playback instances owned by the session.
        /// </summary>
        internal IReadOnlyList<SoundEffectInstance> ActiveBgmInstances => _activeBgmInstances;

        /// <summary>
        /// Releases the owned timer, loader, live BGM instances, chip cache,
        /// metronome sound references and the prepared set. Borrowed BGM/chip
        /// sounds are not disposed individually — the prepared set owns them.
        /// </summary>
        public void Dispose()
        {
            _songTimer?.Dispose();
            _songTimer = null;
            _audioLoader?.Dispose();
            _audioLoader = null;

            foreach (var instance in _activeBgmInstances)
            {
                try { instance.Stop(); instance.Dispose(); }
                catch { }
            }
            _activeBgmInstances.Clear();
            _bgmSounds.Clear();
            _bgmPlayFailureReported.Clear();
            _scheduledBGMEvents.Clear();

            _chipSoundCache?.Dispose();
            _chipSoundCache = null;

            _metronomePlayer = null;
            _metronomeBeatSound?.RemoveReference();
            _metronomeBeatSound = null;
            _metronomeAccentSound?.RemoveReference();
            _metronomeAccentSound = null;

            _preparedAudioSet?.Dispose();
            _preparedAudioSet = null;
            _chart = null;
        }

        private void BindPreparedBGMSounds()
        {
            _bgmSounds.Clear();
            _bgmPlayFailureReported.Clear();
            if (_preparedAudioSet == null || _chart?.BGMEvents == null)
                return;

            foreach (var bgmEvent in _chart.BGMEvents)
            {
                if (string.IsNullOrWhiteSpace(bgmEvent.AudioFilePath))
                    continue;

                var sourcePath = Path.GetFullPath(bgmEvent.AudioFilePath);
                if (_preparedAudioSet.ScheduledBgmBySourcePath.TryGetValue(
                    sourcePath,
                    out var sound))
                {
                    _bgmSounds[bgmEvent.WavId] = sound;
                }
            }
        }

        /// <summary>
        /// Removes and disposes BGM instances that have finished playing, retaining
        /// active instances for the teardown stop-and-dispose pass.
        /// </summary>
        private void CleanupStoppedBgmInstances()
        {
            for (int i = _activeBgmInstances.Count - 1; i >= 0; i--)
            {
                var instance = _activeBgmInstances[i];
                if (instance.State == SoundState.Stopped)
                {
                    try { instance.Dispose(); }
                    catch { }
                    _activeBgmInstances.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Triggers a BGM event by playing its associated sound.
        /// </summary>
        private void TriggerBGMEvent(BGMEvent bgmEvent)
        {
            if (_bgmSounds.TryGetValue(bgmEvent.WavId, out var sound))
            {
                try
                {
                    // Honor the chart's per-WAV #VOLUME/#PAN for backing-track audio;
                    // defaults to full volume, centered when undefined.
                    // NOTE: ConfigData.MasterVolume is not yet combined here — this is
                    // the integration point once master-volume routing is wired into Lib/.
                    float volume = _chart?.GetVolume(bgmEvent.WavId) ?? 1.0f;
                    float pan = _chart?.GetPan(bgmEvent.WavId) ?? 0.0f;
                    var instance = sound.Play(
                        volume,
                        _preparedAudioSet?.RuntimePitch ?? 0.0f,
                        pan);
                    if (instance != null)
                        _activeBgmInstances.Add(instance);
                }
                catch (Exception ex)
                {
                    // Per-WAV dedupe: BGM events fire on the hot path, so a persistent
                    // failure must not flood the log (mirrors ChipSoundCache).
                    if (_bgmPlayFailureReported.Add(bgmEvent.WavId))
                    {
                        _logger.LogWarning(ex, "BGM playback failed for WAV id '{WavId}'", bgmEvent.WavId);
                    }
                }
            }
        }

        private void InitializeMetronome()
        {
            if (!_metronomeEnabled || _chart == null || _metronomePlayer != null)
                return;

            _metronomeBeatSound = TryLoadMetronomeSound(SoundPath.MetronomeBeat);
            _metronomeAccentSound = TryLoadMetronomeSound(SoundPath.MetronomeAccent);

            var speed = _playbackModifiers.PlaySpeedPercent > 0
                ? _playbackModifiers.Speed
                : 1.0;
            var maxLateChartMs = 100.0 * speed;
            _metronomePlayer = new MetronomePlayer(
                _chart.BeatMarkers,
                maxLateChartMs,
                _playClick);
        }

        private ISound? TryLoadMetronomeSound(string path)
        {
            try
            {
                if (_resourceManager == null)
                    return null;
                _resourceManager.TryLoadSound(path, out var sound);
                return sound;
            }
            catch (Exception ex)
            {
                LogPerformanceError($"PerformanceStage: Metronome sound unavailable ({path}): {ex.Message}");
                return null;
            }
        }

        private void LogPerformanceError(string message, Exception? exception = null)
        {
            LogCapturedError(_logger, message, exception);
        }

        /// <summary>
        /// Logs through a logger captured before the background work started
        /// (HPA-799): worker code must not touch the lazily assigned stage logger,
        /// so its reporting uses the captured snapshot instead.
        /// </summary>
        internal static void LogCapturedError(ILogger logger, string message, Exception? exception = null)
        {
            if (exception != null)
            {
                logger.LogError(exception, "{Message}", message);
            }
            else
            {
                logger.LogError("{Message}", message);
            }
        }
    }
}
