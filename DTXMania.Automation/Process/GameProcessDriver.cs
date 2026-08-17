using System.Diagnostics;
using System.Text;

namespace DTXMania.Automation.Process;

public sealed class GameProcessDriver : IAsyncDisposable
{
    private const string AppDataRootEnvironmentVariable = "DTXMANIA_APPDATA_ROOT";
    private const string LaunchTokenEnvironmentVariable = "DTXMANIA_LAUNCH_TOKEN";

    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();
    private readonly object _outputLock = new();
    private TaskCompletionSource _stdoutDrained = CreateDrainCompletionSource();
    private TaskCompletionSource _stderrDrained = CreateDrainCompletionSource();
    private System.Diagnostics.Process? _process;
    private GameLaunchKind? _launchKind;
    private string? _launchToken;

    public string StandardOutput
    {
        get
        {
            lock (_outputLock)
            {
                return _stdout.ToString();
            }
        }
    }

    public string StandardError
    {
        get
        {
            lock (_outputLock)
            {
                return _stderr.ToString();
            }
        }
    }

    public int? ProcessId => _process?.Id;

    public int? ExitCode
    {
        get
        {
            var process = _process;
            return process?.HasExited == true ? process.ExitCode : null;
        }
    }

    public void Start(GameProcessStartOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.WorkingDirectory);
        ArgumentNullException.ThrowIfNull(options.Target);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AppDataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LaunchToken);

        if (_process is not null)
            throw new InvalidOperationException("Game process has already been started.");

        ValidateEnvironmentOverrides(options.EnvironmentOverrides);
        ValidateProjectRunArguments(options);

        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = options.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (options.Target.Kind == GameLaunchKind.Project)
        {
            startInfo.FileName = "dotnet";
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add(options.Target.Path);
            if (options.ProjectRunArguments is not null)
            {
                foreach (var argument in options.ProjectRunArguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }
            }
        }
        else
        {
            startInfo.FileName = options.Target.Path;
        }

        startInfo.Environment[AppDataRootEnvironmentVariable] = options.AppDataRoot;
        startInfo.Environment[LaunchTokenEnvironmentVariable] = options.LaunchToken;

        if (options.EnvironmentOverrides is not null)
        {
            foreach (var overrideValue in options.EnvironmentOverrides)
            {
                if (overrideValue.Value is null)
                    startInfo.Environment.Remove(overrideValue.Key);
                else
                    startInfo.Environment[overrideValue.Key] = overrideValue.Value;
            }
        }

        lock (_outputLock)
        {
            _stdout.Clear();
            _stderr.Clear();
        }
        _stdoutDrained = CreateDrainCompletionSource();
        _stderrDrained = CreateDrainCompletionSource();

        var process = new System.Diagnostics.Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        process.OutputDataReceived += (_, e) => AppendOutput(e.Data, _stdout, _stdoutDrained);
        process.ErrorDataReceived += (_, e) => AppendOutput(e.Data, _stderr, _stderrDrained);

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Failed to start game process.");

            _process = process;
            _launchKind = options.Target.Kind;
            _launchToken = options.LaunchToken;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public async Task WaitForStartupAsync(
        Func<CancellationToken, Task<GameHealthSnapshot?>> healthProbe,
        TimeSpan timeout,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(healthProbe);
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));

        EnsureStarted();
        GameHealthSnapshot? lastObserved = null;
        var deadline = Stopwatch.StartNew();
        using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        while (deadline.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ThrowIfOwnedProcessExitedAsync().ConfigureAwait(false);

            var remaining = timeout - deadline.Elapsed;
            if (remaining <= TimeSpan.Zero)
                break;

            GameHealthSnapshot? health;
            Task<GameHealthSnapshot?> probeTask = Task.FromResult<GameHealthSnapshot?>(null);
            try
            {
                probeTask = healthProbe(probeCancellation.Token);
                health = await probeTask.WaitAsync(remaining, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                probeCancellation.Cancel();
                await ObserveProbeCompletionAsync(probeTask).ConfigureAwait(false);
                break;
            }

            if (health is not null)
            {
                lastObserved = health;

                var matchesReadinessRule = string.Equals(
                        health.LaunchToken,
                        _launchToken,
                        StringComparison.Ordinal)
                    || (_launchKind == GameLaunchKind.Executable
                        && health.ProcessId.HasValue
                        && health.ProcessId == _process!.Id);
                if (matchesReadinessRule && deadline.Elapsed < timeout)
                {
                    // Re-check the owned process before accepting the matching
                    // health. If the owned process answered /health and exited
                    // immediately before this continuation runs, we must report
                    // the exit rather than declaring startup successful. This
                    // honors the "before and after each health probe" rule and
                    // closes the TOCTOU window between the probe response and the
                    // successful return.
                    await ThrowIfOwnedProcessExitedAsync().ConfigureAwait(false);
                    return;
                }
            }

            await ThrowIfOwnedProcessExitedAsync().ConfigureAwait(false);

            remaining = timeout - deadline.Elapsed;
            if (remaining <= TimeSpan.Zero)
                break;

            await Task.Delay(
                remaining < interval ? remaining : interval,
                cancellationToken).ConfigureAwait(false);
        }

        throw CreateStartupTimeoutException(lastObserved);
    }

    public async Task<int> WaitForExitAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var process = EnsureStarted();
        var completion = Task.WhenAll(
            process.WaitForExitAsync(cancellationToken),
            _stdoutDrained.Task,
            _stderrDrained.Task);
        await completion.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    public async ValueTask DisposeAsync()
    {
        var process = _process;
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Process already exited between the HasExited check and Kill.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Process-exit race on Windows.
                }
            }

            try
            {
                await WaitForExitAsync(TimeSpan.FromSeconds(10), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Output draining exceeded the disposal timeout.
            }
            catch (OperationCanceledException)
            {
                // Disposal never propagates cancellation.
            }
        }
        finally
        {
            process.Dispose();
            _process = null;
            _launchKind = null;
            _launchToken = null;
        }
    }

    private async Task ThrowIfOwnedProcessExitedAsync()
    {
        var process = EnsureStarted();
        if (!process.HasExited)
            return;

        try
        {
            await WaitForExitAsync(TimeSpan.FromSeconds(10), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Include the captured output available so far in the startup error below.
        }

        throw new InvalidOperationException(
            $"Owned game process exited before startup (launch kind: {_launchKind}; "
            + $"owned PID: {process.Id}; exit code: {process.ExitCode}; "
            + $"stdout: {StandardOutput}; stderr: {StandardError}");
    }

    private TimeoutException CreateStartupTimeoutException(GameHealthSnapshot? lastObserved)
    {
        var observed = lastObserved is null
            ? "no parseable health identity observed"
            : $"last observed PID: {Format(lastObserved.ProcessId)}; "
              + $"launch token: {Format(lastObserved.LaunchToken)}";
        var matchingRule = _launchKind == GameLaunchKind.Project
            ? "matching launch token required"
            : "matching launch token or owned PID accepted";
        return new TimeoutException(
            $"Game process startup timed out (launch kind: {_launchKind}; "
            + $"owned PID: {ProcessId}; {matchingRule}; {observed}).");
    }

    private System.Diagnostics.Process EnsureStarted()
    {
        return _process
            ?? throw new InvalidOperationException("Game process has not been started.");
    }

    private static async Task ObserveProbeCompletionAsync(Task<GameHealthSnapshot?> probeTask)
    {
        try
        {
            await probeTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort observation: the probe was canceled, timed out, or faulted
            // after the startup timeout fired. If it still hasn't completed within the
            // observation window, it will be left to the GC — acceptable since the
            // startup timeout has already fired and the caller has given up waiting.
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

    private static TaskCompletionSource CreateDrainCompletionSource()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static string Format(int? value)
    {
        return value?.ToString() ?? "<null>";
    }

    private static string Format(string? value)
    {
        return value ?? "<null>";
    }

    private static void ValidateProjectRunArguments(GameProcessStartOptions options)
    {
        if (options.Target.Kind != GameLaunchKind.Project
            && options.ProjectRunArguments is { Count: > 0 })
        {
            throw new ArgumentException(
                "Project run arguments are only valid for project targets.",
                nameof(options.ProjectRunArguments));
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
                    $"Environment override '{overrideValue.Key}' is reserved for the game process driver.",
                    nameof(environmentOverrides));
            }
        }
    }
}
