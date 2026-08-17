using System.Net;
using DTXMania.VideoRecorder.Configuration;
using DTXMania.VideoRecorder.Sandbox;

namespace DTXMania.VideoRecorder;

internal enum RecorderVerb
{
    Doctor,
    Record
}

internal sealed record RecorderCommand(
    RecorderVerb Verb,
    string? ChartPath = null,
    string? OutputDirectory = null);

internal static class RecorderCommandLine
{
    private const string ObsUrlEnvironmentVariable = "DTXMANIA_VIDEO_OBS_URL";
    private const string ObsPasswordEnvironmentVariable = "DTXMANIA_VIDEO_OBS_PASSWORD";
    private const string ObsOutputEnvironmentVariable = "DTXMANIA_VIDEO_OBS_OUTPUT_DIR";
    private const string AppDataEnvironmentVariable = "DTXMANIA_APPDATA_ROOT";
    private const string DefaultObsUrl = "ws://127.0.0.1:4455";

    public static RecorderCommand Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0)
            throw new ArgumentException(Usage(), nameof(args));

        if (args[0].Equals("doctor", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count != 1)
                throw new ArgumentException("doctor does not accept arguments.\n" + Usage(), nameof(args));

            return new RecorderCommand(RecorderVerb.Doctor);
        }

        if (!args[0].Equals("record", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Unknown command '{args[0]}'.\n{Usage()}", nameof(args));

        string? chartPath = null;
        string? outputDirectory = null;
        for (var index = 1; index < args.Count; index++)
        {
            var option = args[index];
            if (option is not ("--chart" or "--output"))
                throw new ArgumentException($"Unknown record option '{option}'.\n{Usage()}", nameof(args));
            if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
                throw new ArgumentException($"Record option '{option}' requires a value.\n{Usage()}", nameof(args));

            var value = args[++index];
            if (option == "--chart")
            {
                if (chartPath is not null)
                    throw new ArgumentException("record accepts --chart only once.", nameof(args));
                chartPath = value;
            }
            else
            {
                if (outputDirectory is not null)
                    throw new ArgumentException("record accepts --output only once.", nameof(args));
                outputDirectory = value;
            }
        }

        if (chartPath is null || outputDirectory is null)
            throw new ArgumentException("record requires --chart and --output.\n" + Usage(), nameof(args));

        return new RecorderCommand(RecorderVerb.Record, chartPath, outputDirectory);
    }

    public static RecorderEnvironment ReadEnvironment(bool requireOutputDirectory = false)
        => ReadEnvironment(Environment.GetEnvironmentVariable, requireOutputDirectory);

    internal static RecorderEnvironment ReadEnvironment(
        Func<string, string?> getEnvironmentVariable,
        bool requireOutputDirectory)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        var obsUrlValue = getEnvironmentVariable(ObsUrlEnvironmentVariable);
        var obsUrl = ParseObsUrl(string.IsNullOrWhiteSpace(obsUrlValue) ? DefaultObsUrl : obsUrlValue);
        var obsPassword = getEnvironmentVariable(ObsPasswordEnvironmentVariable) ?? string.Empty;
        var obsOutputDirectory = getEnvironmentVariable(ObsOutputEnvironmentVariable) ?? string.Empty;
        if (requireOutputDirectory && string.IsNullOrWhiteSpace(obsOutputDirectory))
        {
            throw new InvalidOperationException(
                $"{ObsOutputEnvironmentVariable} is required for record.");
        }

        var sourceAppDataRoot = getEnvironmentVariable(AppDataEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(sourceAppDataRoot))
            sourceAppDataRoot = GetDefaultSourceAppDataRoot();

        return new RecorderEnvironment(
            obsUrl,
            obsPassword,
            obsOutputDirectory,
            Path.GetFullPath(sourceAppDataRoot));
    }

    public static void Validate(RecorderCommand command, RecorderEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(environment);

        ValidateObsUrl(environment.ObsUrl);
        if (command.Verb == RecorderVerb.Doctor)
        {
            ValidateSource(environment.SourceAppDataRoot);
            if (!string.IsNullOrWhiteSpace(environment.ObsOutputDirectory))
                ValidateExistingDirectory(
                    environment.ObsOutputDirectory,
                    "OBS output directory",
                    probeWritable: false);
            return;
        }

        ValidateRecord(command, environment);
    }

    public static void ValidateRecord(RecorderCommand command, RecorderEnvironment environment)
        => ValidateRecord(
            command,
            environment,
            OperatingSystem.IsWindows(),
            OperatingSystem.IsMacOS());

    internal static void ValidateRecord(
        RecorderCommand command,
        RecorderEnvironment environment,
        bool isWindows,
        bool isMacOS)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(environment);

        if (!isWindows && !isMacOS)
        {
            throw new PlatformNotSupportedException(
                "dtx-video record is supported on Windows and macOS only.");
        }

        ValidateObsUrl(environment.ObsUrl);
        ValidateChart(command.ChartPath);
        ValidatePublishDirectory(command.OutputDirectory);
        if (string.IsNullOrWhiteSpace(environment.ObsOutputDirectory))
        {
            throw new InvalidOperationException(
                $"{ObsOutputEnvironmentVariable} is required for record.");
        }

        ValidateExistingDirectory(environment.ObsOutputDirectory, "OBS output directory");
        ValidateSource(environment.SourceAppDataRoot);
    }

    public static string Usage() =>
        "Usage:\n  dtx-video doctor\n  dtx-video record --chart <absolute path> --output <directory>";

    private static Uri ParseObsUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri is null ||
            (uri.Scheme != Uri.UriSchemeWs && uri.Scheme != Uri.UriSchemeWss))
        {
            throw new InvalidOperationException(
                $"{ObsUrlEnvironmentVariable} must be a valid ws:// or wss:// URL.");
        }

        return uri;
    }

    private static void ValidateObsUrl(Uri uri)
    {
        if (!IsLoopback(uri))
        {
            throw new InvalidOperationException(
                $"{ObsUrlEnvironmentVariable} must target a loopback OBS URL.");
        }
    }

    /// <summary>
    /// Reports whether <paramref name="uri"/> satisfies the loopback-only OBS
    /// contract without throwing. Used by <c>doctor</c> to decide whether the
    /// live OBS probe is permitted at all.
    /// </summary>
    internal static bool IsObsUrlValid(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return IsLoopback(uri);
    }

    private static bool IsLoopback(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeWs && uri.Scheme != Uri.UriSchemeWss)
            return false;
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        return IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
    }

    private static void ValidateChart(string? chartPath)
    {
        if (string.IsNullOrWhiteSpace(chartPath))
            throw new InvalidOperationException("--chart is required.");
        if (!Path.IsPathFullyQualified(chartPath))
            throw new InvalidOperationException("--chart must be an absolute path.");
        if (!File.Exists(chartPath))
            throw new FileNotFoundException($"Chart file was not found: '{chartPath}'.", chartPath);
    }

    private static void ValidatePublishDirectory(string? outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidOperationException("--output is required.");

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(outputDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"Publish output directory '{outputDirectory}' is not usable.",
                exception);
        }

        if (File.Exists(fullPath))
        {
            throw new InvalidOperationException(
                $"Publish output path '{fullPath}' is a file, not a directory.");
        }

        var parent = FindExistingParent(fullPath);
        if (parent is null)
        {
            throw new InvalidOperationException(
                $"Publish output directory '{fullPath}' is not usable.");
        }

        ProbeWritable(parent, "publish output directory");
    }

    private static void ValidateExistingDirectory(
        string path,
        string description,
        bool probeWritable = true)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException(
                $"{description} must be an absolute path.");
        }

        if (!Directory.Exists(path))
        {
            throw new InvalidOperationException(
                $"{description} '{path}' does not exist or is not a directory.");
        }

        if (probeWritable)
            ProbeWritable(path, description);
    }

    private static void ValidateSource(string sourceAppDataRoot)
        => RecordingSandbox.ValidateSourceConfig(sourceAppDataRoot);

    private static string? FindExistingParent(string path)
    {
        var candidate = path;
        while (!Directory.Exists(candidate))
        {
            var parent = Directory.GetParent(candidate);
            if (parent is null)
                return null;
            candidate = parent.FullName;
        }

        return candidate;
    }

    private static void ProbeWritable(string directory, string description)
    {
        var probePath = Path.Combine(directory, $".dtx-video-write-test-{Guid.NewGuid():N}");
        try
        {
            using (File.Open(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }
            File.Delete(probePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"{description} '{directory}' is not writable.",
                exception);
        }
    }

    /// <summary>
    /// Mirrors the default (non-override) resolution of
    /// <c>AppPaths.GetAppDataRoot()</c> in <c>DTXMania.Game/Lib/Utilities/AppPaths.cs</c>,
    /// which is the authoritative contract — keep the two in sync. Deliberately
    /// duplicated here instead of referencing the game assembly so the recorder
    /// stays a standalone tool.
    /// </summary>
    internal static string GetDefaultSourceAppDataRoot(
        bool isWindows,
        bool isMacOS,
        Func<Environment.SpecialFolder, string> getFolderPath,
        Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getFolderPath);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        string basePath;
        if (isWindows)
        {
            basePath = getFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }
        else if (isMacOS)
        {
            var home = GetHomeDirectory(getFolderPath, getEnvironmentVariable);
            basePath = Path.Combine(home, "Library", "Application Support");
        }
        else
        {
            basePath = getFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        if (string.IsNullOrWhiteSpace(basePath) || !Path.IsPathRooted(basePath))
        {
            var fallbackHome = GetHomeDirectory(getFolderPath, getEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(fallbackHome))
                throw new InvalidOperationException("Unable to determine the CX app-data root.");
            basePath = isMacOS
                ? Path.Combine(fallbackHome, "Library", "Application Support")
                : Path.Combine(fallbackHome, ".config");
        }

        return Path.Combine(basePath, "DTXManiaCX");
    }

    private static string GetDefaultSourceAppDataRoot()
        => GetDefaultSourceAppDataRoot(
            OperatingSystem.IsWindows(),
            OperatingSystem.IsMacOS(),
            Environment.GetFolderPath,
            Environment.GetEnvironmentVariable);

    private static string GetHomeDirectory(
        Func<Environment.SpecialFolder, string> getFolderPath,
        Func<string, string?> getEnvironmentVariable)
    {
        var home = getFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
            home = getFolderPath(Environment.SpecialFolder.Personal);

        if (string.IsNullOrWhiteSpace(home))
            home = getEnvironmentVariable("HOME") ?? string.Empty;

        return home;
    }
}
