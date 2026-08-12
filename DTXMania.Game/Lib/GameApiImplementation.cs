#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System.Text.Json;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Graphics;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Input;

namespace DTXMania.Game.Lib;

public interface IGameContext
{
    IStageManager? StageManager { get; }
    IConfigManager? ConfigManager { get; }
    IInputManagerCompat? InputManager { get; }
    IGraphicsManager? GraphicsManager { get; }

    /// <summary>
    /// Queues an action to run on the game's main thread during the next Update() tick.
    /// </summary>
    void QueueMainThreadAction(Action action);

    /// <summary>
    /// Schedules a screenshot capture to occur on the next Draw() call.
    /// Returns PNG bytes when fulfilled on the main thread.
    /// </summary>
    Task<byte[]?> CaptureScreenshotAsync();
}

/// <summary>
/// Implementation of IGameApi for the DTXMania game
/// </summary>
public class GameApiImplementation : IGameApi, IDisposable
{
    private readonly IGameContext _game;
    private readonly object _lock = new object();
    private readonly ILogger<GameApiImplementation>? _logger;
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private readonly HashSet<PendingPreparedChartCommand> _pendingPreparedCommands = new();
    private bool _disposed;

    private const string PreparedCommandCanceledError = "The prepared chart command was canceled.";

    public GameApiImplementation(IGameContext game, ILogger<GameApiImplementation>? logger = null)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _logger = logger;
    }

    public bool IsRunning
    {
        get
        {
            lock (_lock)
                return !_disposed;
        }
    }

    public async Task<GameState> GetGameStateAsync()
    {
        return await Task.FromResult(GetGameStateSafe());
    }

    private GameState GetGameStateSafe()
    {
        lock (_lock)
        {
            try
            {
                // This is a simplified implementation
                // In a real game, you'd get actual game state from the game logic
                var gameState = new GameState
                {
                    PlayerPositionX = 0, // Would get from actual player position
                    PlayerPositionY = 0, // Would get from actual player position
                    Score = 0, // Would get from actual score
                    Level = 1, // Would get from actual level
                    CurrentStage = _game.StageManager?.CurrentStage?.ToString() ?? "Unknown",
                    CustomData = new Dictionary<string, object>
                    {
                        ["game_name"] = "DTXManiaCX",
                        ["platform"] = Environment.OSVersion.Platform.ToString(),
                        ["config_screen_width"] = _game.ConfigManager?.Config?.ScreenWidth ?? 0,
                        ["config_screen_height"] = _game.ConfigManager?.Config?.ScreenHeight ?? 0,
                        ["telemetry"] = BuildTelemetrySnapshot()
                    },
                    Timestamp = DateTime.UtcNow
                };

                return gameState;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting game state");
                // Return safe fallback state if game state access fails
                return new GameState
                {
                    PlayerPositionX = 0,
                    PlayerPositionY = 0,
                    Score = 0,
                    Level = 1,
                    CurrentStage = "Error",
                    CustomData = new Dictionary<string, object>
                    {
                        ["error"] = SanitizeExceptionMessage(ex),
                        ["game_name"] = "DTXManiaCX"
                    },
                    Timestamp = DateTime.UtcNow
                };
            }
        }
    }

    private GameTelemetrySnapshot BuildTelemetrySnapshot()
    {
        var stageManager = _game.StageManager;
        var currentStage = stageManager?.CurrentStage;

        var inputManager = _game.InputManager?.ModularInputManager;

        var baseTelemetry = new GameTelemetrySnapshot
        {
            StageName = currentStage?.GetType().Name ?? "Unknown",
            StageType = currentStage?.Type.ToString() ?? "Unknown",
            StagePhase = currentStage?.CurrentPhase.ToString() ?? "Unknown",
            IsTransitioning = stageManager?.IsTransitioning ?? false,
            MidiAvailable = inputManager?.MidiAvailable
        };

        if (currentStage is IStageTelemetryProvider provider)
        {
            try
            {
                var providerTelemetry = new GameTelemetrySnapshot
                {
                    StageName = baseTelemetry.StageName,
                    StageType = baseTelemetry.StageType,
                    StagePhase = baseTelemetry.StagePhase,
                    IsTransitioning = baseTelemetry.IsTransitioning,
                    MidiAvailable = baseTelemetry.MidiAvailable
                };

                provider.PopulateTelemetry(providerTelemetry);
                return providerTelemetry;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Game API: Stage telemetry provider failed for {StageType}", baseTelemetry.StageType);
            }
        }

        return baseTelemetry;
    }

    private static string SanitizeExceptionMessage(Exception ex)
    {
        return $"Internal error ({ex.GetType().Name})";
    }

    private static string? ParseButtonId(JsonElement? data)
    {
        if (!data.HasValue)
        {
            return null;
        }

        var element = data.Value;
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var str = element.GetString();
                if (string.IsNullOrWhiteSpace(str))
                    return null;

                // Normalize casing via Keys enum so it matches KeyBindings (case-sensitive).
                var trimmed = str.StartsWith("Key.", StringComparison.OrdinalIgnoreCase) ? str["Key.".Length..] : str;
                if (Enum.TryParse<Keys>(trimmed, true, out var parsedKey))
                {
                    return $"Key.{parsedKey}";
                }
                return null;

            case JsonValueKind.Number:
                if (element.TryGetInt32(out var keyCode))
                {
                    var keyName = ((Keys)keyCode).ToString();
                    return $"Key.{keyName}";
                }
                return null;

            case JsonValueKind.Object:
                // Object format from MCP bridge: {"key":"Down","holdDurationMs":50,"clientId":"default"}
                if (element.TryGetProperty("key", out var keyProp))
                    return ParseButtonId(keyProp);
                return null;

            default:
                return null;
        }
    }

    private static MidiNoteInput? ParseMidiNoteInput(JsonElement? data)
    {
        // Delegate to GameInputValidator.TryParseMidiNoteData so this path and the
        // upstream ValidateGameInput check share one definition of a valid MIDI note.
        // Previously this inlined the parse with no ValueKind == Number guard, so a
        // JSON string like "36" would have thrown InvalidOperationException — masked
        // only because the validator runs first at both call sites.
        if (!data.HasValue ||
            !GameInputValidator.TryParseMidiNoteData(data.Value, out var noteNumber, out var velocity))
        {
            return null;
        }

        return new MidiNoteInput(noteNumber, velocity);
    }

    /// <summary>
    /// Sends input to the game.
    /// </summary>
    /// <remarks>
    /// TODO: Integrate with ModularInputManager/InputRouter to route MCP-driven input
    /// through the same path as player input for consistent handling.
    /// </remarks>
    /// <param name="input">The input to send to the game.</param>
    public async Task<bool> SendInputAsync(GameInput input)
    {
        try
        {
            if (input == null)
            {
                return false;
            }

            var inputManager = _game.InputManager;
            var modularInput = inputManager?.ModularInputManager;

            if (modularInput == null)
            {
                _logger?.LogWarning("Game API: InputManager/ModularInputManager unavailable - cannot route MCP input");
                return false;
            }

            switch (input.Type)
            {
                case InputType.KeyPress:
                case InputType.KeyRelease:
                {
                    var buttonId = ParseButtonId(input.Data);
                    if (string.IsNullOrWhiteSpace(buttonId))
                    {
                        _logger?.LogWarning("Game API: Missing or invalid key data for MCP input");
                        return false;
                    }

                    var pressed = input.Type == InputType.KeyPress;
                    return modularInput.InjectButton(buttonId, pressed);
                }

                case InputType.MidiNoteOn:
                case InputType.MidiNoteOff:
                {
                    var midi = ParseMidiNoteInput(input.Data);
                    if (midi == null)
                    {
                        _logger?.LogWarning("Game API: Missing or invalid MIDI note data for MCP input");
                        return false;
                    }

                    var pressed = input.Type == InputType.MidiNoteOn;
                    var injected = modularInput.InjectMidiNote(midi.Value.NoteNumber, midi.Value.Velocity, pressed);
                    if (!injected)
                    {
                        _logger?.LogWarning(
                            "Game API: MIDI note injection rejected for note {NoteNumber} velocity {Velocity} pressed {Pressed}. " +
                            "This usually means the active MIDI backend is not an IMidiNoteInjector " +
                            "(production DryWetMidi backend does not support injection; set {EnvVar}=1 to enable the simulated backend).",
                            midi.Value.NoteNumber, midi.Value.Velocity, pressed,
                            "DTXMANIA_ENABLE_SIMULATED_MIDI");
                    }
                    return injected;
                }

                case InputType.MouseClick:
                case InputType.MouseMove:
                    // TODO: When mouse routing is added, translate to UI input. For now, report unsupported.
                    _logger?.LogWarning("Game API: Mouse input not yet supported ({InputType})", input.Type);
                    return false;

                default:
                    _logger?.LogWarning("Game API: Unknown input type: {InputType}", input.Type);
                    return false;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Game API: Error processing input of type {InputType}", input.Type);
            return false;
        }
    }

    private readonly record struct MidiNoteInput(int NoteNumber, int Velocity);

    public async Task<GameWindowInfo> GetWindowInfoAsync()
    {
        return await Task.FromResult(GetWindowInfoSafe());
    }

    public Task<byte[]?> TakeScreenshotAsync()
    {
        return _game.CaptureScreenshotAsync();
    }

    public Task<bool> ChangeStageAsync(string stageName)
    {
        if (!Enum.TryParse<StageType>(stageName, ignoreCase: true, out var stageType)
            || !Enum.IsDefined(typeof(StageType), stageType))
        {
            _logger?.LogWarning("Game API: Unknown stage name '{StageName}'", stageName);
            return Task.FromResult(false);
        }

        _game.QueueMainThreadAction(() =>
        {
            if (_game.StageManager == null)
            {
                _logger?.LogError("Game API: StageManager is null - cannot execute queued stage transition to {StageType}", stageType);
                return;
            }
            _game.StageManager.ChangeStage(stageType, new DTXManiaFadeTransition());
        });

        return Task.FromResult(true);
    }

    public Task<PreparedChartCommandResult> PrepareVideoChartAsync(
        string chartPath,
        CancellationToken cancellationToken = default)
    {
        return QueuePreparedChartCommandAsync(
            stage => stage.PrepareVideoChart(chartPath),
            cancellationToken);
    }

    public Task<PreparedChartCommandResult> StartPreparedPreviewAsync(
        CancellationToken cancellationToken = default)
    {
        return QueuePreparedChartCommandAsync(
            stage => stage.StartPreparedPreview(),
            cancellationToken);
    }

    public Task<PreparedChartCommandResult> ActivatePreparedChartAsync(
        CancellationToken cancellationToken = default)
    {
        return QueuePreparedChartCommandAsync(
            stage => stage.ActivatePreparedChart(),
            cancellationToken);
    }

    public Task<PreparedChartCommandResult> CancelPreparedChartAsync(
        CancellationToken cancellationToken = default)
    {
        return QueuePreparedChartCommandAsync(
            stage => stage.CancelPreparedChart(),
            cancellationToken);
    }

    private Task<PreparedChartCommandResult> QueuePreparedChartCommandAsync(
        Func<SongSelectionStage, (bool Success, string? Error)> command,
        CancellationToken requestCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        PendingPreparedChartCommand pending;
        lock (_lock)
        {
            if (_disposed)
            {
                return Task.FromResult(new PreparedChartCommandResult(
                    false,
                    PreparedCommandCanceledError));
            }

            var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                requestCancellationToken,
                _lifecycleCancellation.Token);
            pending = new PendingPreparedChartCommand(linkedCancellation);
            _pendingPreparedCommands.Add(pending);
        }

        pending.SetCancellationRegistration(
            pending.CancellationToken.Register(() =>
                CompletePreparedCommand(
                    pending,
                    new PreparedChartCommandResult(false, PreparedCommandCanceledError))));

        if (pending.CancellationToken.IsCancellationRequested)
        {
            CompletePreparedCommand(
                pending,
                new PreparedChartCommandResult(false, PreparedCommandCanceledError));
            return pending.Completion.Task;
        }

        try
        {
            _game.QueueMainThreadAction(() =>
            {
                if (!pending.TryBeginExecution())
                    return;

                PreparedChartCommandResult result;
                if (pending.CancellationToken.IsCancellationRequested)
                {
                    result = new PreparedChartCommandResult(
                        false,
                        PreparedCommandCanceledError);
                }
                else
                {
                    try
                    {
                        if (_game.StageManager?.CurrentStage is not SongSelectionStage songSelectionStage)
                        {
                            result = new PreparedChartCommandResult(
                                false,
                                "Prepared chart commands require the Song Select stage.");
                        }
                        else
                        {
                            var commandResult = command(songSelectionStage);
                            result = new PreparedChartCommandResult(
                                commandResult.Success,
                                commandResult.Error);
                        }
                    }
                    catch (Exception exception)
                    {
                        _logger?.LogError(
                            exception,
                            "Game API: Prepared chart command failed on the update thread");
                        result = new PreparedChartCommandResult(
                            false,
                            "The prepared chart command could not be completed.");
                    }
                }

                pending.TryComplete(result);
                RemovePreparedCommand(pending);
            });
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Game API: Could not queue prepared chart command");
            CompletePreparedCommand(
                pending,
                new PreparedChartCommandResult(
                    false,
                    "The prepared chart command could not be completed."));
        }

        return pending.Completion.Task;
    }

    private void CompletePreparedCommand(
        PendingPreparedChartCommand pending,
        PreparedChartCommandResult result)
    {
        pending.TryComplete(result);
        RemovePreparedCommand(pending);
    }

    private void RemovePreparedCommand(PendingPreparedChartCommand pending)
    {
        lock (_lock)
            _pendingPreparedCommands.Remove(pending);

        pending.DisposeCancellation();
    }

    public void Dispose()
    {
        PendingPreparedChartCommand[] pendingCommands;
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            pendingCommands = _pendingPreparedCommands.ToArray();
        }

        try
        {
            _lifecycleCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        foreach (var pending in pendingCommands)
        {
            CompletePreparedCommand(
                pending,
                new PreparedChartCommandResult(false, PreparedCommandCanceledError));
        }

        _lifecycleCancellation.Dispose();
    }

    private sealed class PendingPreparedChartCommand
    {
        private int _completed;
        private int _executionStarted;
        private int _registrationReady;
        private int _disposeRequested;
        private int _resourcesDisposed;
        private CancellationTokenRegistration _cancellationRegistration;

        public PendingPreparedChartCommand(CancellationTokenSource cancellationSource)
        {
            CancellationSource = cancellationSource;
            CancellationToken = cancellationSource.Token;
            Completion = new TaskCompletionSource<PreparedChartCommandResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public TaskCompletionSource<PreparedChartCommandResult> Completion { get; }
        public CancellationTokenSource CancellationSource { get; }
        public CancellationToken CancellationToken { get; }
        public bool IsCompleted => Volatile.Read(ref _completed) != 0;

        public bool TryBeginExecution()
        {
            if (IsCompleted
                || Interlocked.CompareExchange(ref _executionStarted, 1, 0) != 0)
            {
                return false;
            }

            return !IsCompleted;
        }

        public bool TryComplete(PreparedChartCommandResult result)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
                return false;

            Completion.TrySetResult(result);
            return true;
        }

        public void SetCancellationRegistration(CancellationTokenRegistration registration)
        {
            _cancellationRegistration = registration;
            Volatile.Write(ref _registrationReady, 1);
            DisposeCancellationIfRequested();
        }

        public void DisposeCancellation()
        {
            Volatile.Write(ref _disposeRequested, 1);
            DisposeCancellationIfRequested();
        }

        private void DisposeCancellationIfRequested()
        {
            if (Volatile.Read(ref _disposeRequested) == 0
                || Volatile.Read(ref _registrationReady) == 0
                || Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
            {
                return;
            }

            _cancellationRegistration.Dispose();
            CancellationSource.Dispose();
        }
    }

    private GameWindowInfo GetWindowInfoSafe()
    {
        lock (_lock)
        {
            try
            {
                // This is a simplified implementation
                // In a real game, you'd get actual window information
                var windowInfo = new GameWindowInfo
                {
                    Width = _game.ConfigManager?.Config?.ScreenWidth ?? 1920,
                    Height = _game.ConfigManager?.Config?.ScreenHeight ?? 1080,
                    X = 0, // Would get from actual window position
                    Y = 0, // Would get from actual window position
                    Title = "DTXManiaCX",
                    IsVisible = true
                };

                return windowInfo;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Game API: Error getting window info");
                // Return safe fallback window info if access fails
                return new GameWindowInfo
                {
                    Width = 1920,
                    Height = 1080,
                    X = 0,
                    Y = 0,
                    Title = "DTXManiaCX (Error)",
                    IsVisible = false
                };
            }
        }
    }
}
