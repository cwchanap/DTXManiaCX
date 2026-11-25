#nullable enable

using System;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework.Audio;

namespace DTXMania.Game.Lib.Stage.SongSelection
{
    /// <summary>
    /// Playback state of the song-select preview (HPA-802). <see cref="None"/> is
    /// ordinary interactive mode; <see cref="Prepared"/>, <see cref="Playing"/> and
    /// <see cref="Failed"/> track the stopped prepared preview lifecycle.
    /// </summary>
    internal enum SongSelectionPreviewState
    {
        None,
        Prepared,
        Playing,
        Failed
    }

    /// <summary>
    /// Owns the song-select preview audio facts (HPA-802): the managed preview
    /// sound/instance references, the ordinary delayed start, stopped prepared
    /// playback, playback state and elapsed playback time. Accepts audio paths
    /// only — no songs, charts or UI. The resource manager is resolved lazily
    /// through the constructor-supplied accessor, only when a preview loads.
    /// </summary>
    internal sealed class SongSelectionPreviewController
    {
        private readonly Func<IResourceManager?> _getResourceManager;
        private readonly ILogger _logger;
        private readonly Func<ISound?, ISoundInstance?> _createInstance;
        private ISound? _sound;
        private ISoundInstance? _instance;
        private double _playDelaySeconds;
        private bool _isPlayDelayActive;
        private double _playedMs;
        private SongSelectionPreviewState _state = SongSelectionPreviewState.None;

        public SongSelectionPreviewController(
            Func<IResourceManager?> getResourceManager,
            ILogger logger)
            : this(getResourceManager, logger, CreateProductionInstance)
        {
        }

        // HPA-802 review: narrow test seam — MonoGame's SoundEffectInstance cannot be
        // constructed headlessly, so tests supply this delegate to drive the real
        // successful-play path with fake instances. Production keeps the
        // two-argument constructor and always wraps the real instance.
        internal SongSelectionPreviewController(
            Func<IResourceManager?> getResourceManager,
            ILogger logger,
            Func<ISound?, ISoundInstance?> createInstance)
        {
            _getResourceManager = getResourceManager ?? throw new ArgumentNullException(nameof(getResourceManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _createInstance = createInstance ?? throw new ArgumentNullException(nameof(createInstance));
        }

        private static ISoundInstance? CreateProductionInstance(ISound? previewSound)
        {
            var rawInstance = previewSound?.CreateInstance();
            return rawInstance == null ? null : new SoundInstanceWrapper(rawInstance);
        }

        /// <summary>Playback state of the prepared preview; <see cref="SongSelectionPreviewState.None"/> in ordinary mode.</summary>
        public SongSelectionPreviewState PreparedState => _state;

        /// <summary>Actual playback time of the prepared preview, in milliseconds.</summary>
        public double PlayedMs => _playedMs;

        /// <summary>True while a managed preview sound reference is held.</summary>
        public bool HasLoadedSound => _sound != null;

        /// <summary>
        /// Loads the preview for the ordinary interactive path and arms the delayed
        /// start. Fully resets any previous preview first (the controller is
        /// single-voice, so a caller that skipped Stop cannot leave a previous
        /// instance audible or the delay armed over a prepared state); a failed or
        /// throwing load releases any fallback and reports false without arming.
        /// </summary>
        public bool LoadDelayed(string previewPath)
        {
            Stop();

            try
            {
                // TryLoadSound reports the failure LoadSound hides behind its
                // silent fallback; a failed load releases the fallback it got.
                var resourceManager = _getResourceManager();
                if (resourceManager != null
                    && resourceManager.TryLoadSound(previewPath, out _sound)
                    && _sound != null)
                {
                    // Successfully loaded
                    _playDelaySeconds = 0.0;
                    _isPlayDelayActive = true;
                    return true;
                }

                ReleaseSoundReference();
                return false;
            }
            catch (Exception)
            {
                ReleaseSoundReference();
                return false;
            }
        }

        /// <summary>
        /// Loads the prepared preview but keeps it stopped: no delayed start is
        /// armed and a successful load publishes the <see cref="SongSelectionPreviewState.Prepared"/>
        /// state with a zeroed elapsed time.
        /// </summary>
        public bool LoadStopped(string previewPath)
        {
            if (!LoadDelayed(previewPath))
                return false;

            _playDelaySeconds = 0.0;
            _isPlayDelayActive = false;
            _playedMs = 0.0;
            _state = SongSelectionPreviewState.Prepared;
            return true;
        }

        /// <summary>
        /// Explicitly starts the stopped prepared preview. Idempotent while playing;
        /// an instance that stopped unexpectedly marks the state Failed and never
        /// restarts. Returns the existing error messages verbatim.
        /// </summary>
        public (bool Success, string? Error) StartPrepared()
        {
            if (_state == SongSelectionPreviewState.Playing)
            {
                if (_instance?.State == SoundState.Playing)
                    return (true, null);

                _state = SongSelectionPreviewState.Failed;
                return (false, "The prepared preview stopped unexpectedly.");
            }

            if (_state != SongSelectionPreviewState.Prepared)
                return (false, "The prepared chart preview is not ready to play.");

            try
            {
                if (_instance != null)
                    DisposeInstance();

                _instance = _createInstance(_sound);
                if (_instance == null)
                    return MarkFailed("The prepared chart preview instance could not be created.");

                _instance.Volume = SongSelectionUILayout.Audio.PreviewSoundVolume;
                _instance.IsLooped = true;
                _instance.Play();
                _playedMs = 0.0;
                _state = SongSelectionPreviewState.Playing;
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Prepared chart preview could not be started");
                DisposeInstance();
                return MarkFailed("The prepared chart preview could not be started.");
            }
        }

        /// <summary>
        /// Stop contract: stop/dispose the current preview instance, release the
        /// managed sound reference, clear the ordinary delay and reset the playback
        /// state to None and the elapsed time to zero. Idempotent.
        /// </summary>
        public void Stop()
        {
            // Stop preview sound instance
            if (_instance != null)
            {
                try
                {
                    if (_instance.State == SoundState.Playing)
                    {
                        _instance.Stop();
                    }
                }
                catch (Exception)
                {
                }
                finally
                {
                    DisposeInstance();
                }
            }

            // Release managed preview sound reference.
            ReleaseSoundReference();

            // Reset timers and prepared playback state.
            _playDelaySeconds = 0.0;
            _isPlayDelayActive = false;
            _state = SongSelectionPreviewState.None;
            _playedMs = 0.0;
        }

        /// <summary>
        /// Advances prepared playback time (only while actually playing; a
        /// negative delta clamps to zero, an unexpected stop marks the state Failed
        /// without restarting) and fires the ordinary delayed start. Prepared,
        /// Playing and Failed states never auto-start or fall back: the delay is
        /// only armed by the ordinary <see cref="LoadDelayed"/> path.
        /// </summary>
        public void Update(double deltaTime)
        {
            if (_state == SongSelectionPreviewState.Playing)
            {
                if (_instance?.State == SoundState.Playing)
                {
                    _playedMs += Math.Max(0.0, deltaTime) * 1000.0;
                }
                else
                {
                    // Explicitly-started prepared previews never restart themselves or fall
                    // back to the ordinary delayed primary-chart preview after an unexpected
                    // stop.
                    _state = SongSelectionPreviewState.Failed;
                }
            }

            // Update preview play delay timer. The ordinary delayed start only ever
            // runs in None mode: prepared, playing and failed states never auto-start
            // or fall back after the delay.
            if (_isPlayDelayActive && _state == SongSelectionPreviewState.None)
            {
                _playDelaySeconds += deltaTime;

                // Start preview after delay ends
                if (_playDelaySeconds >= SongSelectionUILayout.Audio.PreviewPlayDelaySeconds && _sound != null)
                {
                    _isPlayDelayActive = false;

                    try
                    {
                        // Check if preview sound instance is available and not already playing
                        if (_instance == null || _instance.State != SoundState.Playing)
                        {
                            if (_instance != null)
                            {
                                _instance.Dispose();
                                _instance = null;
                            }

                            _instance = _createInstance(_sound);
                            if (_instance != null)
                            {
                                _instance.Volume = SongSelectionUILayout.Audio.PreviewSoundVolume;
                                _instance.IsLooped = true; // Enable looping
                                _instance.Play();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Song preview playback failed");
                        DisposeInstance();
                    }
                }
            }
        }

        private (bool Success, string? Error) MarkFailed(string error)
        {
            _state = SongSelectionPreviewState.Failed;
            return (false, error);
        }

        private void DisposeInstance()
        {
            if (_instance == null)
                return;

            try
            {
                _instance.Dispose();
            }
            catch (Exception)
            {
            }
            finally
            {
                _instance = null;
            }
        }

        private void ReleaseSoundReference()
        {
            _sound?.RemoveReference();
            _sound = null;
        }
    }
}
