using System.Diagnostics;
using System.Text;
using DTXMania.E2E.Fixtures;

namespace DTXMania.E2E.Process;

public sealed class GameProcessDriver : IAsyncDisposable
{
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();
    private System.Diagnostics.Process? _process;

    public string StandardOutput => _stdout.ToString();

    public string StandardError => _stderr.ToString();

    public int? ExitCode => _process?.HasExited == true ? _process.ExitCode : null;

    public void Start(string repoRoot, string gameProjectPath, E2EFixture fixture)
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
            CreateNoWindow = false
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(gameProjectPath);
        startInfo.Environment["DTXMANIA_APPDATA_ROOT"] = fixture.AppDataRoot;
        startInfo.Environment["DTXMANIA_LAUNCH_TOKEN"] = Guid.NewGuid().ToString("N");

        _process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start game process.");
        _process.OutputDataReceived += (_, e) => { if (e.Data != null) _stdout.AppendLine(e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data != null) _stderr.AppendLine(e.Data); };
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
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }
}
