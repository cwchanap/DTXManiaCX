using DTXMania.VideoRecorder.Media;

namespace DTXMania.VideoRecorder.Tests.Media;

public sealed class RecordingArtifactVerifierTests
{
    [Fact]
    public async Task VerifyAndPublishAsync_ShouldRejectRawPathOutsideObsRoot()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var obsRoot = Directory.CreateDirectory(Path.Combine(root, "obs")).FullName;
            var outside = Path.Combine(root, "outside.mp4");
            await File.WriteAllTextAsync(outside, "raw");
            var verifier = new RecordingArtifactVerifier(TimeSpan.FromSeconds(1), () => null);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                verifier.VerifyAndPublishAsync(outside, obsRoot, Path.Combine(root, "published")));

            Assert.Contains("outside", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VerifyAndPublishAsync_ShouldRejectMissingOrEmptyRawFile(bool empty)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var obsRoot = Directory.CreateDirectory(Path.Combine(root, "obs")).FullName;
            var raw = Path.Combine(obsRoot, "capture.mp4");
            if (empty)
                await File.WriteAllBytesAsync(raw, Array.Empty<byte>());
            var verifier = new RecordingArtifactVerifier(TimeSpan.FromSeconds(1), () => null);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                verifier.VerifyAndPublishAsync(raw, obsRoot, Path.Combine(root, "published")));

            Assert.Contains(empty ? "empty" : "not found", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task VerifyAndPublishAsync_ShouldRejectDestinationCollision()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var obsRoot = Directory.CreateDirectory(Path.Combine(root, "obs")).FullName;
            var publishRoot = Directory.CreateDirectory(Path.Combine(root, "published")).FullName;
            var raw = Path.Combine(obsRoot, "capture.mp4");
            var destination = Path.Combine(publishRoot, "capture.mp4");
            await File.WriteAllTextAsync(raw, "raw");
            await File.WriteAllTextAsync(destination, "existing");
            var verifier = new RecordingArtifactVerifier(TimeSpan.FromSeconds(1), () => null);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                verifier.VerifyAndPublishAsync(raw, obsRoot, publishRoot));

            Assert.Contains("collision", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task VerifyAndPublishAsync_ShouldCopySuccessfullyAndPreserveRawWithFfprobeWarning()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var obsRoot = Directory.CreateDirectory(Path.Combine(root, "obs")).FullName;
            var publishRoot = Directory.CreateDirectory(Path.Combine(root, "published")).FullName;
            var raw = Path.Combine(obsRoot, "capture.mp4");
            var bytes = new byte[] { 1, 2, 3, 4 };
            await File.WriteAllBytesAsync(raw, bytes);
            var verifier = new RecordingArtifactVerifier(TimeSpan.FromSeconds(1), () => null);

            var result = await verifier.VerifyAndPublishAsync(raw, obsRoot, publishRoot);

            Assert.Equal(raw, result.RawPath);
            Assert.Equal(Path.Combine(publishRoot, "capture.mp4"), result.PublishedPath);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(raw));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(result.PublishedPath));
            Assert.Contains("ffprobe", result.Warning, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task VerifyAndPublishAsync_WhenFfprobeReportsOnlyVideo_ShouldFailClosed()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var obsRoot = Directory.CreateDirectory(Path.Combine(root, "obs")).FullName;
            var raw = Path.Combine(obsRoot, "capture.mp4");
            await File.WriteAllTextAsync(raw, "raw");
            var ffprobe = CreateExecutable(
                root,
                "ffprobe",
                "#!/bin/sh\nprintf '%s' '{\"streams\":[{\"codec_type\":\"video\"}]}'\n");
            var verifier = new RecordingArtifactVerifier(TimeSpan.FromSeconds(2), () => ffprobe);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                verifier.VerifyAndPublishAsync(raw, obsRoot, Path.Combine(root, "published")));

            Assert.Contains("video", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("audio", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task VerifyAndPublishAsync_WhenFfprobeReportsAudioAndVideo_ShouldPublishWithoutWarning()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var obsRoot = Directory.CreateDirectory(Path.Combine(root, "obs")).FullName;
            var publishRoot = Directory.CreateDirectory(Path.Combine(root, "published")).FullName;
            var raw = Path.Combine(obsRoot, "capture.mp4");
            await File.WriteAllTextAsync(raw, "raw");
            var ffprobe = CreateExecutable(
                root,
                "ffprobe",
                "#!/bin/sh\nprintf '%s' '{\"streams\":[{\"codec_type\":\"video\"},{\"codec_type\":\"audio\"}]}'\n");
            var verifier = new RecordingArtifactVerifier(TimeSpan.FromSeconds(2), () => ffprobe);

            var result = await verifier.VerifyAndPublishAsync(raw, obsRoot, publishRoot);

            Assert.Null(result.Warning);
            Assert.True(File.Exists(result.PublishedPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string CreateExecutable(string root, string name, string contents)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, contents);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "dtx-video-artifact-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
