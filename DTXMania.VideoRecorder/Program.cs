using DTXMania.VideoRecorder.Configuration;
using DTXMania.VideoRecorder.Diagnostics;
using DTXMania.VideoRecorder.Media;
using DTXMania.VideoRecorder.Obs;
using DTXMania.VideoRecorder.Sandbox;
using DTXMania.VideoRecorder.Workflow;

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

            var target = RecorderGameLaunchPolicy.ResolveTarget(Environment.CurrentDirectory);
            var preflight = RecorderPlatformPreflight.Evaluate(
                RecorderPlatformPreflight.CaptureFacts(),
                target);
            return await RunRecordAsync(command, environment, target, preflight)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 2;
        }
    }

    internal static async Task<int> RunRecordAsync(
        RecorderCommand command,
        RecorderEnvironment environment,
        ResolvedRecorderTarget target,
        RecorderPlatformPreflightResult preflight)
    {
        RejectFailedPreflight(preflight);

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            var sandbox = RecordingSandbox.Create(environment.SourceAppDataRoot);
            var diagnostics = new RecorderDiagnostics(
                command.OutputDirectory!,
                Path.GetFileName(sandbox.RunRoot),
                sandbox.ApiKey,
                environment.ObsPassword);
            AutomationGameRecordingControl? game = null;
            ObsWebSocketRecorder? obs = null;
            try
            {
                Console.WriteLine($"Recorder sandbox ready at '{sandbox.RunRoot}'.");
                var startOptions = RecorderGameLaunchPolicy.CreateOptions(sandbox, target);
                game = new AutomationGameRecordingControl(
                    sandbox.ApiPort,
                    sandbox.ApiKey);
                obs = new ObsWebSocketRecorder(
                    environment.ObsUrl,
                    environment.ObsPassword);
                var workflow = new RecordWorkflow(
                    game,
                    obs,
                    command.ChartPath!,
                    startOptions,
                    diagnostics: diagnostics);
                var rawOutputPath = await workflow.RunAsync(cancellation.Token)
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(rawOutputPath))
                {
                    throw new InvalidOperationException(
                        "OBS did not return a raw output path after recording.");
                }

                var verifier = new RecordingArtifactVerifier();
                var recordingGame = game ?? throw new InvalidOperationException(
                    "Recorder game control was not initialized.");
                var artifact = await RecordFinalization.CompleteAsync(
                        new FinalizationCallbacks
                        {
                            VerifyAndPublish = token => verifier.VerifyAndPublishAsync(
                                rawOutputPath,
                                environment.ObsOutputDirectory,
                                command.OutputDirectory!,
                                token),
                            RecordArtifact = verifiedArtifact =>
                            {
                                diagnostics.SetRawOutputPath(verifiedArtifact.RawPath);
                                diagnostics.SetPublishedPath(verifiedArtifact.PublishedPath);
                                diagnostics.SetVerifierWarning(verifiedArtifact.Warning);
                                diagnostics.RecordStep("ArtifactVerified");
                                diagnostics.RecordStep("Completed");
                            },
                            MarkCompleted = diagnostics.MarkCompleted,
                            WriteDiagnostics = () => diagnostics.WriteAsync(
                                recordingGame.StandardOutput,
                                recordingGame.StandardError,
                                CancellationToken.None),
                            DeleteSandbox = sandbox.DeleteOnSuccessAsync
                        },
                        cancellation.Token)
                    .ConfigureAwait(false);
                Console.WriteLine($"OBS raw output: '{artifact.RawPath}'.");
                Console.WriteLine($"Published output: '{artifact.PublishedPath}'.");
                return 0;
            }
            catch (Exception exception)
            {
                diagnostics.MarkFailure(exception, sandbox.RunRoot);
                try
                {
                    await diagnostics.WriteAsync(
                            game?.StandardOutput,
                            game?.StandardError,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception diagnosticsException)
                {
                    // Diagnostics are secondary evidence; preserve the primary
                    // recording failure and make the write problem visible.
                    Console.Error.WriteLine(
                        $"Warning: recorder diagnostics could not be written: {diagnosticsException.Message}");
                }

                // Keep the sandbox for diagnostics when a record run fails.
                throw;
            }
            finally
            {
                if (game is not null)
                    await game.DisposeAsync().ConfigureAwait(false);
                if (obs is not null)
                    await obs.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    /// <summary>
    /// Rejects a failed platform preflight before any run-owned resource
    /// exists: no sandbox, no diagnostics run-root, no game/OBS client, no
    /// workflow.
    /// </summary>
    private static void RejectFailedPreflight(RecorderPlatformPreflightResult preflight)
    {
        ArgumentNullException.ThrowIfNull(preflight);
        if (preflight.Passed)
            return;

        var failures = string.Join(
            "; ",
            preflight.Gates
                .Where(gate => !gate.Passed)
                .Select(gate => $"{gate.Name}: {gate.Detail}"));
        throw new InvalidOperationException($"Recorder platform preflight failed. {failures}");
    }

    private static async Task<int> RunDoctorAsync(RecorderEnvironment environment)
    {
        var passed = true;
        var sourceConfig = Path.Combine(environment.SourceAppDataRoot, "Config.ini");
        var ffprobe = RecordingArtifactVerifier.FindFfprobeOnPath();

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

        ResolvedRecorderTarget? target = null;
        string? targetFailure = null;
        try
        {
            target = RecorderGameLaunchPolicy.ResolveTarget(Environment.CurrentDirectory);
        }
        catch (Exception exception)
        {
            targetFailure = exception.Message;
        }

        var facts = RecorderPlatformPreflight.CaptureFacts();
        var preflight = target is null
            ? new RecorderPlatformPreflightResult(
                new[] { new RecorderPreflightGate("Launch target", Passed: false, Detail: targetFailure!) },
                null)
            : RecorderPlatformPreflight.Evaluate(facts, target);
        passed &= ReportPlatformPreflight(Console.Out, facts, preflight);

        passed &= ReportGate(Console.Out, "Source config", File.Exists(sourceConfig), sourceConfig);
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
        var obsUrlValid = RecorderCommandLine.IsObsUrlValid(environment.ObsUrl);
        if (string.IsNullOrWhiteSpace(environment.ObsOutputDirectory))
        {
            passed = false;
            Console.WriteLine("Raw output directory: <unset> (DTXMANIA_VIDEO_OBS_OUTPUT_DIR)");
        }
        else
        {
            var rawOutput = Path.GetFullPath(environment.ObsOutputDirectory);
            var rawOutputValid = Directory.Exists(rawOutput) && Path.IsPathFullyQualified(rawOutput);
            passed &= ReportGate(Console.Out, "Raw output directory", rawOutputValid, rawOutput);
        }

        Console.WriteLine("Warning: each HPA-503 run uses a fresh songs.db; cold enumeration may take minutes.");

        if (ffprobe is null)
            Console.WriteLine("ffprobe: unavailable on PATH (optional; media verification will warn)");
        else
            Console.WriteLine($"ffprobe: available ({ffprobe})");

        if (obsUrlValid)
        {
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
        }
        else
        {
            // The OBS URL failed the loopback-only contract. doctor reports the
            // gate failure above but must never contact a URL it specifically
            // prohibits, so the live probe is skipped.
            passed = false;
            Console.WriteLine("OBS auth/status: skipped (OBS URL failed loopback validation)");
        }

        Console.WriteLine("OBS state mutation: none (doctor only performs Hello/Identify/GetRecordStatus)");
        Console.WriteLine($"dtx-video doctor: {(passed ? "all gates passed" : "one or more gates failed")}");
        return passed ? 0 : 2;
    }

    /// <summary>
    /// Reports the shared platform preflight gates plus the platform-specific
    /// manual OBS prerequisites. Manual items are never claimed to be
    /// programmatically verified. Factored over injected facts/result so Mac
    /// parity is testable without a live OBS server.
    /// </summary>
    internal static bool ReportPlatformPreflight(
        TextWriter writer,
        RecorderPlatformFacts facts,
        RecorderPlatformPreflightResult preflight)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(preflight);

        var passed = true;
        foreach (var gate in preflight.Gates)
            passed &= ReportGate(writer, gate.Name, gate.Passed, gate.Detail);

        writer.WriteLine("Manual OBS prerequisites:");
        foreach (var line in facts.IsMacOS ? MacManualPrerequisites : WindowsManualPrerequisites)
            writer.WriteLine(line);

        return passed;
    }

    private static readonly string[] MacManualPrerequisites =
    {
        "Dedicated profile/collection/scene selected",
        "ScreenCaptureKit application/window capture scoped to CX",
        "CX application audio configured",
        "Desktop Audio disabled",
        "Microphone disabled",
        "Hybrid MP4 configured",
        "Screen Recording permission granted manually",
        "WebSocket enabled/authenticated",
        "Raw output directory matches DTXMANIA_VIDEO_OBS_OUTPUT_DIR"
    };

    private static readonly string[] WindowsManualPrerequisites =
    {
        "Dedicated profile/collection/scene already selected",
        "CX window/program capture configured",
        "CX application audio configured",
        "Hybrid MP4 configured",
        "WebSocket enabled",
        "raw output dir matches DTXMANIA_VIDEO_OBS_OUTPUT_DIR"
    };

    private static bool ReportGate(TextWriter writer, string name, bool passed, string detail)
    {
        writer.WriteLine($"{name}: {(passed ? "passed" : "FAILED")} ({detail})");
        return passed;
    }
}
