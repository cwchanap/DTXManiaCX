using System.Text;
using DTXMania.E2E.Fixtures;

namespace DTXMania.E2E.Process;

[Trait("Category", "E2E-Support")]
[Collection("E2EFixture")]
public sealed class GameProcessDriverTests
{
    private const string SetVariableName = "DTXMANIA_PROCESS_DRIVER_TEST_SET";
    private const string RemovedVariableName = "DTXMANIA_PROCESS_DRIVER_TEST_REMOVE";

    [Fact(Timeout = 60_000)]
    public async Task Start_WithEnvironmentOverrides_ShouldSetRemoveAndDrainChildOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "dtx-process-driver-" + Guid.NewGuid().ToString("N"));
        var repoRoot = Path.Combine(root, "child");
        var runRoot = Path.Combine(root, "fixture");
        var previousRemovedValue = Environment.GetEnvironmentVariable(RemovedVariableName);

        try
        {
            Directory.CreateDirectory(repoRoot);
            WriteChildProject(repoRoot);
            Environment.SetEnvironmentVariable(RemovedVariableName, "inherited-value");
            var fixture = E2EFixtureBuilder.Build(runRoot, repoRoot, apiPort: 18080);
            await using var process = new GameProcessDriver();

            process.Start(
                repoRoot,
                "Child.csproj",
                fixture,
                environmentOverrides: new Dictionary<string, string?>
                {
                    [SetVariableName] = "override-value",
                    [RemovedVariableName] = null
                });

            var exitCode = await process.WaitForExitAsync(
                TimeSpan.FromSeconds(30),
                CancellationToken.None);

            Assert.Equal(23, exitCode);
            Assert.Contains("set=override-value", process.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("removed=<null>", process.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("child-stderr", process.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(RemovedVariableName, previousRemovedValue);

            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("DTXMANIA_APPDATA_ROOT")]
    [InlineData("DTXMANIA_LAUNCH_TOKEN")]
    public async Task Start_WhenOverrideTargetsReservedFixtureVariable_ShouldReject(string variableName)
    {
        var root = Path.Combine(Path.GetTempPath(), "dtx-process-driver-" + Guid.NewGuid().ToString("N"));
        var repoRoot = Path.Combine(root, "child");
        var runRoot = Path.Combine(root, "fixture");

        try
        {
            Directory.CreateDirectory(repoRoot);
            WriteChildProject(repoRoot);
            var fixture = E2EFixtureBuilder.Build(runRoot, repoRoot, apiPort: 18080);
            await using var process = new GameProcessDriver();

            var exception = Assert.Throws<ArgumentException>(() => process.Start(
                repoRoot,
                "Child.csproj",
                fixture,
                environmentOverrides: new Dictionary<string, string?>
                {
                    [variableName] = "not-allowed"
                }));

            Assert.Contains(variableName, exception.Message, StringComparison.Ordinal);
            Assert.Null(process.ExitCode);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteChildProject(string repoRoot)
    {
        File.WriteAllText(
            Path.Combine(repoRoot, "Child.csproj"),
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
            Path.Combine(repoRoot, "Program.cs"),
            """
            Console.WriteLine("set=" + (Environment.GetEnvironmentVariable("DTXMANIA_PROCESS_DRIVER_TEST_SET") ?? "<null>"));
            Console.WriteLine("removed=" + (Environment.GetEnvironmentVariable("DTXMANIA_PROCESS_DRIVER_TEST_REMOVE") ?? "<null>"));
            Console.Error.WriteLine("child-stderr");
            return 23;
            """,
            Encoding.UTF8);
    }
}
