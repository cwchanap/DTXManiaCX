using System.Diagnostics;
using System.Text;
using DTXMania.E2E.Fixtures;

namespace DTXMania.E2E.Process;

public sealed class GameProcessDriver : IAsyncDisposable
{
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();
    private readonly object _outputLock = new();
    private System.Diagnostics.Process? _process;

    public string StandardOutput { get { lock (_outputLock) { return _stdout.ToString(); } } }

    public string StandardError { get { lock (_outputLock) { return _stderr.ToString(); } } }

    public int? ExitCode => _process?.HasExited == true ? _process.ExitCode : null;

    public void Start(string repoRoot, string gameProjectPath, E2EFixture fixture)
        => Start(repoRoot, gameProjectPath, fixture, enableSimulatedMidi: false);

    /// <summary>
    /// Starts the game process.
    /// </summary>
    /// <param name="enableSimulatedMidi">
    /// When true, sets <c>DTXMANIA_ENABLE_SIMULATED_MIDI=1</c> so the game uses the injectable
    /// simulated MIDI backend (required for MIDI-driven E2E scenarios). When false, the env var is
    /// not set and the game uses its default production MIDI backend.
    /// </param>
    public void Start(string repoRoot, string gameProjectPath, E2EFixture fixture, bool enableSimulatedMidi)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameProjectPath);
        ArgumentNullException.ThrowIfNull(fixture);

        if (_process != null)
            throw new InvalidOperationException("Game process has already been started.");

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
        startInfo.Environment["DTXMANIA_APPDATA_ROOT"] = fixture.AppDataRoot;
        startInfo.Environment["DTXMANIA_LAUNCH_TOKEN"] = Guid.NewGuid().ToString("N");
        if (enableSimulatedMidi)
            startInfo.Environment["DTXMANIA_ENABLE_SIMULATED_MIDI"] = "1";
        else
            // Explicitly remove the variable so the child does NOT inherit a value from the
            // parent/test-runner environment. ProcessStartInfo.Environment is seeded from the
            // current process environment, so without this a parent-side DTXMANIA_ENABLE_SIMULATED_MIDI=1
            // would silently switch the non-simulated path onto the simulated backend.
            startInfo.Environment.Remove("DTXMANIA_ENABLE_SIMULATED_MIDI");

        _process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start game process.");
        _process.OutputDataReceived += (_, e) => { if (e.Data != null) { lock (_outputLock) { _stdout.AppendLine(e.Data); } } };
        _process.ErrorDataReceived += (_, e) => { if (e.Data != null) { lock (_outputLock) { _stderr.AppendLine(e.Data); } } };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
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

                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }
}
