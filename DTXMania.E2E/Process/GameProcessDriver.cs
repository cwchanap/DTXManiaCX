using System.Diagnostics;
using System.Text;
using DTXMania.E2E.Fixtures;

namespace DTXMania.E2E.Process;

public sealed class GameProcessDriver : IAsyncDisposable
{
    private const string AppDataRootEnvironmentVariable = "DTXMANIA_APPDATA_ROOT";
    private const string LaunchTokenEnvironmentVariable = "DTXMANIA_LAUNCH_TOKEN";
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();
    private readonly object _outputLock = new();
    private readonly TaskCompletionSource _stdoutDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stderrDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private System.Diagnostics.Process? _process;

    public string StandardOutput { get { lock (_outputLock) { return _stdout.ToString(); } } }

    public string StandardError { get { lock (_outputLock) { return _stderr.ToString(); } } }

    public int? ExitCode => _process?.HasExited == true ? _process.ExitCode : null;

    /// <summary>
    /// Starts the game process.
    /// </summary>
    /// <param name="enableSimulatedMidi">
    /// When true, sets <c>DTXMANIA_ENABLE_SIMULATED_MIDI=1</c> so the game uses the injectable
    /// simulated MIDI backend (required for MIDI-driven E2E scenarios). When false, the env var is
    /// not set and the game uses its default production MIDI backend.
    /// </param>
    /// <param name="environmentOverrides">
    /// Optional process environment changes for a single launched game. A null value removes an
    /// inherited variable. Fixture-owned app-data and launch-token variables cannot be overridden.
    /// </param>
    public void Start(
        string repoRoot,
        string gameProjectPath,
        E2EFixture fixture,
        bool enableSimulatedMidi = false,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameProjectPath);
        ArgumentNullException.ThrowIfNull(fixture);

        if (_process != null)
            throw new InvalidOperationException("Game process has already been started.");

        ValidateEnvironmentOverrides(environmentOverrides);

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(gameProjectPath);
        startInfo.Environment[AppDataRootEnvironmentVariable] = fixture.AppDataRoot;
        startInfo.Environment[LaunchTokenEnvironmentVariable] = Guid.NewGuid().ToString("N");
        if (enableSimulatedMidi)
            startInfo.Environment["DTXMANIA_ENABLE_SIMULATED_MIDI"] = "1";
        else
            // Explicitly remove the variable so the child does NOT inherit a value from the
            // parent/test-runner environment. ProcessStartInfo.Environment is seeded from the
            // current process environment, so without this a parent-side DTXMANIA_ENABLE_SIMULATED_MIDI=1
            // would silently switch the non-simulated path onto the simulated backend.
            startInfo.Environment.Remove("DTXMANIA_ENABLE_SIMULATED_MIDI");

        if (environmentOverrides is not null)
        {
            foreach (var overrideValue in environmentOverrides)
            {
                if (overrideValue.Value is null)
                    startInfo.Environment.Remove(overrideValue.Key);
                else
                    startInfo.Environment[overrideValue.Key] = overrideValue.Value;
            }
        }

        _process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start game process.");
        _process.OutputDataReceived += (_, e) => AppendOutput(e.Data, _stdout, _stdoutDrained);
        _process.ErrorDataReceived += (_, e) => AppendOutput(e.Data, _stderr, _stderrDrained);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public async Task<int> WaitForExitAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var process = _process
            ?? throw new InvalidOperationException("Game process has not been started.");

        var completion = Task.WhenAll(
            process.WaitForExitAsync(cancellationToken),
            _stdoutDrained.Task,
            _stderrDrained.Task);
        await completion.WaitAsync(timeout, cancellationToken);
        return process.ExitCode;
    }

    public async ValueTask DisposeAsync()
    {
        if (_process == null)
            return;

        try
        {
            if (!_process.HasExited)
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Process already exited between the HasExited check and Kill — safe to ignore.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Process-exit race on Windows — safe to ignore.
                }

                await WaitForExitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            }
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    private void AppendOutput(
        string? line,
        StringBuilder destination,
        TaskCompletionSource drained)
    {
        if (line is null)
        {
            drained.TrySetResult();
            return;
        }

        lock (_outputLock)
        {
            destination.AppendLine(line);
        }
    }

    private static void ValidateEnvironmentOverrides(
        IReadOnlyDictionary<string, string?>? environmentOverrides)
    {
        if (environmentOverrides is null)
            return;

        foreach (var overrideValue in environmentOverrides)
        {
            if (string.Equals(
                    overrideValue.Key,
                    AppDataRootEnvironmentVariable,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    overrideValue.Key,
                    LaunchTokenEnvironmentVariable,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Environment override '{overrideValue.Key}' is reserved for the E2E fixture.",
                    nameof(environmentOverrides));
            }
        }
    }
}
