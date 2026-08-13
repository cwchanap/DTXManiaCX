using System.Text.Json;
using DTXMania.Automation.Telemetry;
using DTXMania.VideoRecorder.Diagnostics;

namespace DTXMania.VideoRecorder.Tests.Diagnostics;

public sealed class RecorderDiagnosticsTests
{
    [Fact]
    public async Task WriteAsync_ShouldEmitExactlyThreeSanitizedFilesAndSelectedEvidence()
    {
        var output = CreateTemporaryDirectory();
        try
        {
            const string apiKey = "api-secret-123";
            const string obsPassword = "obs-password-456";
            var diagnostics = new RecorderDiagnostics(output, "run-1", apiKey, obsPassword);
            diagnostics.RecordStep("SongSelectReady", Snapshot("SongSelect", "indexed chart"));
            diagnostics.RecordObsOutcome("Connect", succeeded: true, detail: "connected");
            diagnostics.RecordObsOutcome("Start", succeeded: true, detail: "password=obs-password-456");
            diagnostics.SetRawOutputPath(Path.Combine(output, "raw.mp4"));
            diagnostics.SetPublishedPath(Path.Combine(output, "published.mp4"));
            diagnostics.SetVerifierWarning("ffprobe unavailable on PATH");
            diagnostics.MarkCompleted();

            await diagnostics.WriteAsync(
                $"CX started with API key {apiKey}\nConfig.ini should never be copied",
                $"OBS password {obsPassword}");

            var runDirectory = Path.Combine(output, "diagnostics", "run-1");
            Assert.True(Directory.Exists(runDirectory));
            Assert.Equal(
                new[] { "cx-stderr.log", "cx-stdout.log", "run.json" },
                Directory.EnumerateFiles(runDirectory)
                    .Select(Path.GetFileName)
                    .OrderBy(static name => name, StringComparer.Ordinal)
                    .ToArray());
            Assert.DoesNotContain(
                Directory.EnumerateFiles(runDirectory, "*", SearchOption.AllDirectories),
                path => Path.GetFileName(path).Equals("Config.ini", StringComparison.OrdinalIgnoreCase));

            var allText = string.Join(
                "\n",
                Directory.EnumerateFiles(runDirectory).Select(File.ReadAllText));
            Assert.DoesNotContain(apiKey, allText, StringComparison.Ordinal);
            Assert.DoesNotContain(obsPassword, allText, StringComparison.Ordinal);

            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(runDirectory, "run.json")));
            var root = document.RootElement;
            Assert.Equal("Completed", root.GetProperty("status").GetString());
            Assert.Equal("SongSelectReady", root.GetProperty("lastCompletedStep").GetString());
            Assert.Equal("SongSelect", root.GetProperty("steps")[0].GetProperty("telemetry").GetProperty("stageType").GetString());
            Assert.Equal("indexed chart", root.GetProperty("steps")[0].GetProperty("telemetry").GetProperty("selectedSongTitle").GetString());
            Assert.Equal("raw.mp4", Path.GetFileName(root.GetProperty("rawOutputPath").GetString()));
            Assert.Equal("published.mp4", Path.GetFileName(root.GetProperty("publishedPath").GetString()));
            Assert.Equal("ffprobe unavailable on PATH", root.GetProperty("verifierWarning").GetString());
            Assert.Equal(2, root.GetProperty("obsOutcomes").GetArrayLength());
        }
        finally
        {
            DeleteDirectory(output);
        }
    }

    [Fact]
    public async Task WriteAsync_OnFailure_ShouldRetainFailureAndSandboxEvidence()
    {
        var output = CreateTemporaryDirectory();
        try
        {
            var diagnostics = new RecorderDiagnostics(output, "run-failure", "api-secret", "obs-secret");
            diagnostics.RecordStep("ObsStarted");
            diagnostics.MarkFailure(
                new InvalidOperationException("capture failed"),
                retainedSandboxPath: Path.Combine(output, "sandbox", "run-failure"));

            await diagnostics.WriteAsync("stdout", "stderr");

            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(output, "diagnostics", "run-failure", "run.json")));
            var root = document.RootElement;
            Assert.Equal("Failed", root.GetProperty("status").GetString());
            Assert.Equal("ObsStarted", root.GetProperty("lastCompletedStep").GetString());
            Assert.Equal("capture failed", root.GetProperty("failure").GetString());
            Assert.Equal(
                Path.Combine(output, "sandbox", "run-failure"),
                root.GetProperty("retainedSandboxPath").GetString());
        }
        finally
        {
            DeleteDirectory(output);
        }
    }

    private static GameStateSnapshot Snapshot(string stage, string selectedSongTitle)
    {
        using var document = JsonDocument.Parse(
            $"{{\"stageType\":\"{stage}\",\"selectedSongTitle\":\"{selectedSongTitle}\",\"totalNotes\":3}}");
        return new GameStateSnapshot
        {
            CustomData = new Dictionary<string, JsonElement>
            {
                ["telemetry"] = document.RootElement.Clone()
            }
        };
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "dtx-video-diagnostics-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
