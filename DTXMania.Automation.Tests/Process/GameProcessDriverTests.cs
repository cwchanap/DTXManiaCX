using System.Diagnostics;
using System.Text;
using DTXMania.Automation.Process;

namespace DTXMania.Automation.Tests.Process;

[Trait("Category", "Automation-Process")]
public sealed class GameProcessDriverTests
{
    private const string SetVariableName = "DTX_AUTOMATION_SET";
    private const string RemovedVariableName = "DTX_AUTOMATION_REMOVE";

    [Fact(Timeout = 60_000)]
    public async Task Start_ProjectTarget_ShouldSetOwnedEnvironmentAndDrainOutput()
    {
        var fixture = CreateChildFixture();
        var previousRemovedValue = Environment.GetEnvironmentVariable(RemovedVariableName);

        try
        {
            Environment.SetEnvironmentVariable(RemovedVariableName, "inherited-value");
            await using var process = new GameProcessDriver();
            var options = CreateOptions(
                fixture,
                GameLaunchTarget.Project(fixture.ProjectPath),
                environmentOverrides: new Dictionary<string, string?>
                {
                    [SetVariableName] = "override-value",
                    [RemovedVariableName] = null
                });

            process.Start(options);

            var exitCode = await process.WaitForExitAsync(
                TimeSpan.FromSeconds(30),
                CancellationToken.None);

            Assert.Equal(23, exitCode);
            Assert.Contains("appdata=" + options.AppDataRoot, process.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("token=" + options.LaunchToken, process.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("set=override-value", process.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("removed=<null>", process.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("child-stderr", process.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(RemovedVariableName, previousRemovedValue);
            fixture.Dispose();
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task Start_ExecutableTarget_ShouldRunExactBuiltAppHostPath()
    {
        using var fixture = CreateChildFixture();
        BuildChild(fixture);
        var appHostPath = GetBuiltAppHostPath(fixture.Root);
        Assert.True(File.Exists(appHostPath), $"Expected built apphost at {appHostPath}");

        await using var process = new GameProcessDriver();
        var options = CreateOptions(fixture, GameLaunchTarget.Executable(appHostPath));

        process.Start(options);

        var exitCode = await process.WaitForExitAsync(
            TimeSpan.FromSeconds(30),
            CancellationToken.None);

        Assert.Equal(23, exitCode);
        Assert.Contains("appdata=" + options.AppDataRoot, process.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("token=" + options.LaunchToken, process.StandardOutput, StringComparison.Ordinal);
    }

    [Fact(Timeout = 60_000)]
    public async Task Start_WhenCalledTwice_ShouldRejectDuplicateOwnership()
    {
        using var fixture = CreateChildFixture();
        await using var process = new GameProcessDriver();
        var options = CreateOptions(
            fixture,
            GameLaunchTarget.Project(fixture.ProjectPath),
            new Dictionary<string, string?> { ["DTX_AUTOMATION_CHILD_MODE"] = "wait" });

        process.Start(options);

        var exception = Assert.Throws<InvalidOperationException>(() => process.Start(options));

        Assert.Contains("already been started", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("dtxmania_appdata_root")]
    [InlineData("DtXmAnIa_LaUnCh_ToKeN")]
    public void Start_WhenOverrideTargetsOwnedVariableCaseInsensitively_ShouldReject(string variableName)
    {
        using var fixture = CreateChildFixture();
        var process = new GameProcessDriver();
        var options = CreateOptions(
            fixture,
            GameLaunchTarget.Project(fixture.ProjectPath),
            new Dictionary<string, string?> { [variableName] = "not-allowed" });

        var exception = Assert.Throws<ArgumentException>(() => process.Start(options));

        Assert.Contains(variableName, exception.Message, StringComparison.Ordinal);
        Assert.Null(process.ProcessId);
    }

    [Fact(Timeout = 60_000)]
    public async Task WaitForExit_BurstOutput_ShouldDrainTerminalText()
    {
        using var fixture = CreateChildFixture();
        await using var process = new GameProcessDriver();
        process.Start(CreateOptions(
            fixture,
            GameLaunchTarget.Project(fixture.ProjectPath),
            new Dictionary<string, string?> { ["DTX_AUTOMATION_CHILD_MODE"] = "burst" }));

        var exitCode = await process.WaitForExitAsync(
            TimeSpan.FromSeconds(30),
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains("terminal-output", process.StandardOutput, StringComparison.Ordinal);
        Assert.True(process.StandardOutput.Length >= 1_000_000);
    }

    [Fact(Timeout = 60_000)]
    public async Task DisposeAsync_CalledTwice_ShouldBeIdempotent()
    {
        using var fixture = CreateChildFixture();
        var process = new GameProcessDriver();
        process.Start(CreateOptions(
            fixture,
            GameLaunchTarget.Project(fixture.ProjectPath),
            new Dictionary<string, string?> { ["DTX_AUTOMATION_CHILD_MODE"] = "wait" }));

        await process.DisposeAsync();
        await process.DisposeAsync();
    }

    [Fact(Timeout = 60_000)]
    public async Task WaitForStartup_WhenCallerCancels_ShouldPropagateCancellation()
    {
        using var fixture = CreateChildFixture();
        await using var process = new GameProcessDriver();
        process.Start(CreateOptions(
            fixture,
            GameLaunchTarget.Project(fixture.ProjectPath),
            new Dictionary<string, string?> { ["DTX_AUTOMATION_CHILD_MODE"] = "wait" }));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => process.WaitForStartupAsync(
            _ => Task.FromResult<GameHealthSnapshot?>(null),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(10),
            cancellation.Token));
    }

    [Fact(Timeout = 60_000)]
    public async Task WaitForStartup_WhenOwnedProcessExitsEarly_ShouldReportExitAndCapturedOutput()
    {
        using var fixture = CreateChildFixture();
        BuildChild(fixture);
        await using var process = new GameProcessDriver();
        process.Start(CreateOptions(
            fixture,
            GameLaunchTarget.Executable(GetBuiltAppHostPath(fixture.Root)),
            new Dictionary<string, string?> { ["DTX_AUTOMATION_CHILD_MODE"] = "exit-early" }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => process.WaitForStartupAsync(
            _ => Task.FromResult<GameHealthSnapshot?>(null),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None));

        Assert.Contains("42", exception.Message, StringComparison.Ordinal);
        Assert.Contains("early-stdout", exception.Message, StringComparison.Ordinal);
        Assert.Contains("early-stderr", exception.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 60_000)]
    public async Task WaitForStartup_ProjectMatchingToken_ShouldSucceed()
    {
        using var fixture = CreateChildFixture();
        await using var process = new GameProcessDriver();
        var options = CreateOptions(
            fixture,
            GameLaunchTarget.Project(fixture.ProjectPath),
            new Dictionary<string, string?> { ["DTX_AUTOMATION_CHILD_MODE"] = "wait" });
        process.Start(options);

        await process.WaitForStartupAsync(
            _ => Task.FromResult<GameHealthSnapshot?>(
                new(process.ProcessId, options.LaunchToken)),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None);
    }

    [Fact(Timeout = 60_000)]
    public async Task WaitForStartup_ProjectMatchingPidButWrongToken_ShouldTimeoutWithObservedIdentity()
    {
        using var fixture = CreateChildFixture();
        await using var process = new GameProcessDriver();
        var options = CreateOptions(
            fixture,
            GameLaunchTarget.Project(fixture.ProjectPath),
            new Dictionary<string, string?> { ["DTX_AUTOMATION_CHILD_MODE"] = "wait" });
        process.Start(options);
        var ownedProcessId = process.ProcessId!.Value;

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => process.WaitForStartupAsync(
            _ => Task.FromResult<GameHealthSnapshot?>(
                new(ownedProcessId, "stale-token")),
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None));

        Assert.Contains("stale-token", exception.Message, StringComparison.Ordinal);
        Assert.Contains(ownedProcessId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("Project", exception.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 60_000)]
    public async Task WaitForStartup_ExecutableMatchingPid_ShouldSucceedWhenTokenDoesNotMatch()
    {
        using var fixture = CreateChildFixture();
        BuildChild(fixture);
        await using var process = new GameProcessDriver();
        var options = CreateOptions(
            fixture,
            GameLaunchTarget.Executable(GetBuiltAppHostPath(fixture.Root)),
            new Dictionary<string, string?> { ["DTX_AUTOMATION_CHILD_MODE"] = "wait" });
        process.Start(options);

        await process.WaitForStartupAsync(
            _ => Task.FromResult<GameHealthSnapshot?>(
                new(process.ProcessId, "different-token")),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None);
    }

    [Fact(Timeout = 60_000)]
    public async Task WaitForStartup_WhenHealthIsNeverParseable_ShouldTimeoutWithNoObservationMarker()
    {
        using var fixture = CreateChildFixture();
        await using var process = new GameProcessDriver();
        var options = CreateOptions(
            fixture,
            GameLaunchTarget.Project(fixture.ProjectPath),
            new Dictionary<string, string?> { ["DTX_AUTOMATION_CHILD_MODE"] = "wait" });
        process.Start(options);

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => process.WaitForStartupAsync(
            _ => Task.FromResult<GameHealthSnapshot?>(null),
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None));

        Assert.Contains("no parseable health identity observed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Project", exception.Message, StringComparison.Ordinal);
        Assert.Contains(process.ProcessId!.Value.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 60_000)]
    public async Task WaitForStartup_WhenHealthProbeNeverCompletes_ShouldHonorStartupTimeout()
    {
        using var fixture = CreateChildFixture();
        await using var process = new GameProcessDriver();
        var options = CreateOptions(
            fixture,
            GameLaunchTarget.Project(fixture.ProjectPath),
            new Dictionary<string, string?> { ["DTX_AUTOMATION_CHILD_MODE"] = "wait" });
        process.Start(options);

        var neverCompletingProbe = new TaskCompletionSource<GameHealthSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var startup = process.WaitForStartupAsync(
            _ => neverCompletingProbe.Task,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None);

        try
        {
            var completed = await Task.WhenAny(startup, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.Same(startup, completed);
            await Assert.ThrowsAsync<TimeoutException>(() => startup);
        }
        finally
        {
            neverCompletingProbe.TrySetResult(null);
            try
            {
                await startup;
            }
            catch (Exception)
            {
                // The assertion above owns the expected startup result.
            }
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task WaitForStartup_WhenOwnedProcessExitsAfterMatchingHealth_ShouldReportExitInsteadOfReturningSuccess()
    {
        using var fixture = CreateChildFixture();
        await using var process = new GameProcessDriver();
        var options = CreateOptions(
            fixture,
            GameLaunchTarget.Project(fixture.ProjectPath),
            new Dictionary<string, string?> { ["DTX_AUTOMATION_CHILD_MODE"] = "wait" });
        process.Start(options);
        var ownedPid = process.ProcessId!.Value;

        var killed = false;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => process.WaitForStartupAsync(
            _ =>
            {
                // Simulate the TOCTOU race: the owned process answers /health
                // with a matching identity, then exits immediately before the
                // continuation runs. The driver must report the exit rather than
                // declare startup successful.
                if (!killed)
                {
                    killed = true;
                    try
                    {
                        using var owned = System.Diagnostics.Process.GetProcessById(ownedPid);
                        owned.Kill(entireProcessTree: true);
                        owned.WaitForExit();
                    }
                    catch (ArgumentException)
                    {
                        // Process already exited.
                    }
                    catch (InvalidOperationException)
                    {
                        // Process already exited.
                    }
                }
                return Task.FromResult<GameHealthSnapshot?>(
                    new(ownedPid, options.LaunchToken));
            },
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None));

        Assert.Contains("exited before startup", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ownedPid.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 60_000)]
    public async Task WaitForStartup_WhenMatchingHealthArrivesAfterDeadline_ShouldTimeout()
    {
        using var fixture = CreateChildFixture();
        await using var process = new GameProcessDriver();
        var options = CreateOptions(
            fixture,
            GameLaunchTarget.Project(fixture.ProjectPath),
            new Dictionary<string, string?> { ["DTX_AUTOMATION_CHILD_MODE"] = "wait" });
        process.Start(options);

        var lateHealthProbe = new TaskCompletionSource<GameHealthSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var startup = process.WaitForStartupAsync(
            _ => lateHealthProbe.Task,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None);
        var lateHealthCompletion = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200));
            lateHealthProbe.TrySetResult(new(process.ProcessId, options.LaunchToken));
        });
        var completionGuard = Task.Delay(TimeSpan.FromMilliseconds(500));

        try
        {
            var completedLateHealth = await Task.WhenAny(lateHealthCompletion, completionGuard);
            Assert.Same(lateHealthCompletion, completedLateHealth);

            var completedStartup = await Task.WhenAny(startup, completionGuard);
            Assert.Same(startup, completedStartup);
            await Assert.ThrowsAsync<TimeoutException>(() => startup);
        }
        finally
        {
            lateHealthProbe.TrySetResult(null);
            try
            {
                await lateHealthCompletion;
            }
            catch (Exception)
            {
                // The assertion above owns the expected scheduled completion.
            }
            try
            {
                await startup;
            }
            catch (Exception)
            {
                // The assertion above owns the expected startup result.
            }
        }
    }

    private static GameProcessStartOptions CreateOptions(
        ChildFixture fixture,
        GameLaunchTarget target,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null)
    {
        return new GameProcessStartOptions(
            fixture.Root,
            target,
            Path.Combine(fixture.Root, "appdata"),
            "automation-token-123",
            environmentOverrides);
    }

    private static ChildFixture CreateChildFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "dtx-automation-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var projectPath = Path.Combine(root, "Child.csproj");
        File.WriteAllText(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """,
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(root, "Program.cs"),
            """
            var mode = Environment.GetEnvironmentVariable("DTX_AUTOMATION_CHILD_MODE");

            if (mode == "wait")
            {
                await Task.Delay(TimeSpan.FromMinutes(1));
                return 0;
            }

            if (mode == "exit-early")
            {
                Console.WriteLine("early-stdout");
                Console.Error.WriteLine("early-stderr");
                return 42;
            }

            if (mode == "burst")
            {
                Console.Write(new string('x', 1_000_000));
                Console.Write("terminal-output");
                return 0;
            }

            Console.WriteLine("appdata=" + Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT"));
            Console.WriteLine("token=" + Environment.GetEnvironmentVariable("DTXMANIA_LAUNCH_TOKEN"));
            Console.WriteLine("set=" + Environment.GetEnvironmentVariable("DTX_AUTOMATION_SET"));
            Console.WriteLine("removed=" + (Environment.GetEnvironmentVariable("DTX_AUTOMATION_REMOVE") ?? "<null>"));
            Console.Error.WriteLine("child-stderr");
            return 23;
            """,
            Encoding.UTF8);
        return new ChildFixture(root, projectPath);
    }

    private static void BuildChild(ChildFixture fixture)
    {
        using var process = System.Diagnostics.Process.Start(new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = fixture.Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Arguments = $"build \"{fixture.ProjectPath}\" --configuration Debug --nologo"
        }) ?? throw new InvalidOperationException("Failed to start dotnet build.");
        process.WaitForExit();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(
            process.ExitCode == 0,
            $"Child build failed with exit code {process.ExitCode}. stdout={stdout} stderr={stderr}");
    }

    private static string GetBuiltAppHostPath(string root)
    {
        var fileName = OperatingSystem.IsWindows() ? "Child.exe" : "Child";
        return Path.Combine(root, "bin", "Debug", "net8.0", fileName);
    }

    private sealed class ChildFixture(string root, string projectPath) : IDisposable
    {
        private const int MaxDeleteAttempts = 5;
        private static readonly TimeSpan DeleteRetryDelay = TimeSpan.FromMilliseconds(100);

        public string Root { get; } = root;
        public string ProjectPath { get; } = projectPath;

        public void Dispose()
        {
            if (!Directory.Exists(Root))
                return;

            for (var attempt = 0; attempt < MaxDeleteAttempts; attempt++)
            {
                try
                {
                    Directory.Delete(Root, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < MaxDeleteAttempts - 1)
                {
                    Thread.Sleep(DeleteRetryDelay);
                }
                catch (UnauthorizedAccessException) when (attempt < MaxDeleteAttempts - 1)
                {
                    Thread.Sleep(DeleteRetryDelay);
                }
            }
        }
    }
}
