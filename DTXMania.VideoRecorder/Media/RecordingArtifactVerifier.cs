using System.Diagnostics;
using System.Text.Json;

namespace DTXMania.VideoRecorder.Media;

internal sealed record RecordingArtifactVerification(
    string RawPath,
    string PublishedPath,
    string? Warning)
{
    public string? VerifierWarning => Warning;
}

/// <summary>
/// The sole owner of recorder artifact policy. Raw OBS paths are checked here,
/// optional local media inspection happens here, and publication is a
/// non-destructive copy from the raw file.
/// </summary>
internal sealed class RecordingArtifactVerifier
{
    private static readonly TimeSpan DefaultExternalIoTimeout = TimeSpan.FromSeconds(15);

    private readonly TimeSpan _externalIoTimeout;
    private readonly Func<string?> _ffprobePathResolver;

    public RecordingArtifactVerifier()
        : this(DefaultExternalIoTimeout, FindOnPath)
    {
    }

    public RecordingArtifactVerifier(
        TimeSpan externalIoTimeout,
        Func<string?>? ffprobePathResolver = null)
    {
        if (externalIoTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(externalIoTimeout));

        _externalIoTimeout = externalIoTimeout;
        _ffprobePathResolver = ffprobePathResolver ?? FindOnPath;
    }

    internal RecordingArtifactVerifier(
        Func<string?> ffprobePathResolver,
        TimeSpan? externalIoTimeout = null)
        : this(externalIoTimeout ?? DefaultExternalIoTimeout, ffprobePathResolver)
    {
        ArgumentNullException.ThrowIfNull(ffprobePathResolver);
    }

    public async Task<RecordingArtifactVerification> VerifyAndPublishAsync(
        string rawPath,
        string obsRoot,
        string publishDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(obsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(publishDirectory);

        var containedRawPath = EnsureContained(rawPath, obsRoot);
        if (!File.Exists(containedRawPath))
        {
            throw new InvalidOperationException(
                $"Raw OBS output file was not found: '{containedRawPath}'.");
        }

        var rawInfo = new FileInfo(containedRawPath);
        if (rawInfo.Length <= 0)
        {
            throw new InvalidOperationException(
                $"Raw OBS output file is empty: '{containedRawPath}'.");
        }

        var warning = await VerifyMediaStreamsAsync(containedRawPath, cancellationToken)
            .ConfigureAwait(false);

        var publishRoot = Path.GetFullPath(publishDirectory);
        Directory.CreateDirectory(publishRoot);
        var fileName = Path.GetFileName(containedRawPath);
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or "..")
        {
            throw new InvalidOperationException(
                $"Raw OBS output path does not contain a file name: '{containedRawPath}'.");
        }

        var destination = Path.Combine(publishRoot, fileName);
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new InvalidOperationException(
                $"Published artifact collision: destination '{destination}' already exists.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        File.Copy(containedRawPath, destination, overwrite: false);
        return new RecordingArtifactVerification(containedRawPath, destination, warning);
    }

    private async Task<string?> VerifyMediaStreamsAsync(
        string rawPath,
        CancellationToken cancellationToken)
    {
        var ffprobePath = _ffprobePathResolver();
        if (string.IsNullOrWhiteSpace(ffprobePath))
        {
            return "ffprobe unavailable on PATH; media stream validation skipped.";
        }

        var startInfo = CreateFfprobeStartInfo(ffprobePath, rawPath);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Unable to start ffprobe.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_externalIoTimeout);
            var standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var standardError = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await Task.WhenAll(
                        standardOutput,
                        standardError,
                        process.WaitForExitAsync(timeout.Token))
                    .ConfigureAwait(false);
            }
            catch
            {
                TryKill(process);
                throw;
            }

            var output = await standardOutput.ConfigureAwait(false);
            var error = await standardError.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"ffprobe failed while validating '{rawPath}' (exit code {process.ExitCode}): {error}");
            }

            if (!ContainsAudioAndVideo(output))
            {
                throw new InvalidOperationException(
                    "ffprobe output must contain at least one video and one audio stream.");
            }

            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"ffprobe did not finish within {_externalIoTimeout}.");
        }
    }

    private static bool ContainsAudioAndVideo(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            if (!document.RootElement.TryGetProperty("streams", out var streams) ||
                streams.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var hasAudio = false;
            var hasVideo = false;
            foreach (var stream in streams.EnumerateArray())
            {
                if (!stream.TryGetProperty("codec_type", out var codecType) ||
                    codecType.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                hasAudio |= string.Equals(codecType.GetString(), "audio", StringComparison.OrdinalIgnoreCase);
                hasVideo |= string.Equals(codecType.GetString(), "video", StringComparison.OrdinalIgnoreCase);
            }

            return hasAudio && hasVideo;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ProcessStartInfo CreateFfprobeStartInfo(string ffprobePath, string rawPath)
    {
        var arguments = new[]
        {
            "-v",
            "error",
            "-show_entries",
            "stream=codec_type",
            "-of",
            "json",
            rawPath
        };

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        var extension = Path.GetExtension(ffprobePath);
        var isWindowsBatch = OperatingSystem.IsWindows() &&
            (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
             extension.Equals(".bat", StringComparison.OrdinalIgnoreCase));

        if (isWindowsBatch)
        {
            var batchArguments = new[] { ffprobePath }.Concat(arguments);
            foreach (var argument in batchArguments)
            {
                if (argument.Contains('%'))
                {
                    throw new InvalidOperationException(
                        $"Refusing to pass an argument containing a percent sign to the Windows " +
                        $"batch invocation of ffprobe ('{argument}'). Batch variable expansion via " +
                        $"ComSpec is unsafe for recorder-controlled paths such as capture%NAME%.mp4.");
                }
            }

            var command = string.Join(
                " ",
                batchArguments.Select(QuoteCommandArgument));
            startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            startInfo.Arguments = $"/d /s /c call {command}";
            return startInfo;
        }

        startInfo.FileName = ffprobePath;
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private static string QuoteCommandArgument(string value) =>
        "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string EnsureContained(string rawPath, string obsRoot)
    {
        // The verifier is the sole trust boundary for raw OBS paths and the
        // design requires OBS to return a fully qualified path. Reject a
        // relative path before normalization: GetFullPath would silently fold
        // it against the recorder working directory, which could then pass or
        // fail containment based on where the recorder happened to run.
        if (!Path.IsPathFullyQualified(rawPath))
        {
            throw new InvalidOperationException(
                $"Raw OBS output path '{rawPath}' must be a fully qualified path.");
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(obsRoot));
        var raw = Path.GetFullPath(rawPath);
        var relative = Path.GetRelativePath(root, raw);
        var escapes = Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        if (escapes)
        {
            throw new InvalidOperationException(
                $"Raw OBS output path '{raw}' is outside configured OBS output directory '{root}'.");
        }

        return raw;
    }

    private static string? FindOnPath()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, "ffprobe");
            if (File.Exists(candidate))
                return candidate;
            if (OperatingSystem.IsWindows() && File.Exists(candidate + ".exe"))
                return candidate + ".exe";
        }

        return null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
