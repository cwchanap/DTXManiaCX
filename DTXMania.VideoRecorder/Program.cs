using DTXMania.VideoRecorder.Configuration;
using DTXMania.VideoRecorder.Obs;
using DTXMania.VideoRecorder.Sandbox;

namespace DTXMania.VideoRecorder;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var command = RecorderCommandLine.Parse(args);
            var environment = RecorderCommandLine.ReadEnvironment(
                command.Verb == RecorderVerb.Record);

            if (command.Verb == RecorderVerb.Doctor)
                return await RunDoctorAsync(environment).ConfigureAwait(false);

            RecorderCommandLine.Validate(command, environment);
            var sandbox = RecordingSandbox.Create(environment.SourceAppDataRoot);
            try
            {
                Console.WriteLine($"Recorder sandbox ready at '{sandbox.RunRoot}'.");
                Console.WriteLine("Recorder workflow is not yet configured.");
                await sandbox.DeleteOnSuccessAsync().ConfigureAwait(false);
                return 0;
            }
            catch
            {
                // Keep the sandbox for diagnostics when a record run fails.
                throw;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 2;
        }
    }

    private static async Task<int> RunDoctorAsync(RecorderEnvironment environment)
    {
        var passed = true;
        var repoRoot = FindRepositoryRoot(Environment.CurrentDirectory);
        var gameProject = repoRoot is null
            ? null
            : Path.Combine(repoRoot, "DTXMania.Game", "DTXMania.Game.Windows.csproj");
        var sourceConfig = Path.Combine(environment.SourceAppDataRoot, "Config.ini");
        var ffprobe = FindOnPath("ffprobe");

        Console.WriteLine("dtx-video doctor");
        try
        {
            RecorderCommandLine.Validate(
                new RecorderCommand(RecorderVerb.Doctor),
                environment);
            Console.WriteLine("Recorder configuration validation: passed");
        }
        catch (Exception exception)
        {
            passed = false;
            Console.WriteLine($"Recorder configuration validation: FAILED ({exception.Message})");
        }

        passed &= ReportGate(
            "Windows",
            OperatingSystem.IsWindows(),
            OperatingSystem.IsWindows() ? "available" : "record is Windows-only");
        passed &= ReportGate(
            "Repository",
            repoRoot is not null,
            repoRoot ?? "not found from current directory");
        passed &= ReportGate(
            "Game project",
            gameProject is not null && File.Exists(gameProject),
            gameProject ?? "not found");
        passed &= ReportGate("Source config", File.Exists(sourceConfig), sourceConfig);
        if (File.Exists(sourceConfig))
        {
            try
            {
                RecordingSandbox.ValidateSourceConfig(environment.SourceAppDataRoot);
                Console.WriteLine("Source config validation: passed");
            }
            catch (Exception exception)
            {
                passed = false;
                Console.WriteLine($"Source config validation: FAILED ({exception.Message})");
            }
        }

        Console.WriteLine($"OBS URL: {environment.ObsUrl}");
        if (string.IsNullOrWhiteSpace(environment.ObsOutputDirectory))
        {
            passed = false;
            Console.WriteLine("Raw output directory: <unset> (DTXMANIA_VIDEO_OBS_OUTPUT_DIR)");
        }
        else
        {
            var rawOutput = Path.GetFullPath(environment.ObsOutputDirectory);
            var rawOutputValid = Directory.Exists(rawOutput) && Path.IsPathFullyQualified(rawOutput);
            passed &= ReportGate("Raw output directory", rawOutputValid, rawOutput);
        }

        Console.WriteLine("Manual OBS prerequisites:");
        Console.WriteLine("Dedicated profile/collection/scene already selected");
        Console.WriteLine("CX window/program capture configured");
        Console.WriteLine("CX application audio configured");
        Console.WriteLine("Hybrid MP4 configured");
        Console.WriteLine("WebSocket enabled");
        Console.WriteLine("raw output dir matches DTXMANIA_VIDEO_OBS_OUTPUT_DIR");
        Console.WriteLine("Warning: each HPA-503 run uses a fresh songs.db; cold enumeration may take minutes.");

        if (ffprobe is null)
            Console.WriteLine("ffprobe: unavailable on PATH (optional; media verification will warn)");
        else
            Console.WriteLine($"ffprobe: available ({ffprobe})");

        await using (var recorder = new ObsWebSocketRecorder(environment.ObsUrl, environment.ObsPassword))
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await recorder.ConnectAsync(timeout.Token).ConfigureAwait(false);
                var status = await recorder.GetRecordStatusAsync(timeout.Token).ConfigureAwait(false);
                Console.WriteLine("OBS auth/status: Hello + Identify succeeded");
                Console.WriteLine($"OBS recording status: {(status.IsRecording ? "ACTIVE (stop it before record)" : "inactive")}");
                if (status.IsRecording)
                    passed = false;
            }
            catch (Exception exception)
            {
                passed = false;
                Console.WriteLine($"OBS auth/status: FAILED ({exception.Message})");
            }
        }

        Console.WriteLine("OBS state mutation: none (doctor only performs Hello/Identify/GetRecordStatus)");
        Console.WriteLine($"dtx-video doctor: {(passed ? "all gates passed" : "one or more gates failed")}");
        return passed ? 0 : 2;
    }

    private static bool ReportGate(string name, bool passed, string detail)
    {
        Console.WriteLine($"{name}: {(passed ? "passed" : "FAILED")} ({detail})");
        return passed;
    }

    private static string? FindRepositoryRoot(string startDirectory)
    {
        var candidate = Path.GetFullPath(startDirectory);
        while (true)
        {
            if (File.Exists(Path.Combine(candidate, "DTXMania.sln")) ||
                File.Exists(Path.Combine(candidate, "DTXMania.slnx")))
            {
                return candidate;
            }

            var parent = Directory.GetParent(candidate);
            if (parent is null)
                return null;
            candidate = parent.FullName;
        }
    }

    private static string? FindOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate))
                return candidate;
            if (OperatingSystem.IsWindows() && File.Exists(candidate + ".exe"))
                return candidate + ".exe";
        }

        return null;
    }
}
