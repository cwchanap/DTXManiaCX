using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace DTXMania.VideoRecorder.Sandbox;

internal sealed class RecordingSandbox
{
    private const string NormalizationHint = "Open CX once and exit normally, then retry dtx-video.";
    private const string ConfigFileName = "Config.ini";
    private const string SandboxDirectoryName = "DTXManiaCX-video";

    private static readonly IReadOnlyDictionary<string, string> OwnedConfigValues =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["EnableGameApi"] = "True",
            ["AutoPlay"] = "True",
            ["NoFail"] = "True",
            ["ScreenWidth"] = "1280",
            ["ScreenHeight"] = "720",
            ["FullScreen"] = "False"
        };

    private RecordingSandbox(
        string runRoot,
        string appDataRoot,
        string configPath,
        int apiPort,
        string apiKey)
    {
        RunRoot = runRoot;
        AppDataRoot = appDataRoot;
        ConfigPath = configPath;
        ApiPort = apiPort;
        ApiKey = apiKey;
    }

    public string RunRoot { get; }

    public string AppDataRoot { get; }

    public string ConfigPath { get; }

    public int ApiPort { get; }

    public string ApiKey { get; }

    public static RecordingSandbox Create(string sourceAppDataRoot)
        => CreateCore(sourceAppDataRoot, afterRunRootCreated: null);

    internal static RecordingSandbox CreateForTests(
        string sourceAppDataRoot,
        Action<string> afterRunRootCreated)
    {
        ArgumentNullException.ThrowIfNull(afterRunRootCreated);
        return CreateCore(sourceAppDataRoot, afterRunRootCreated);
    }

    private static RecordingSandbox CreateCore(
        string sourceAppDataRoot,
        Action<string>? afterRunRootCreated)
    {
        var sourceRoot = NormalizeSourceRoot(sourceAppDataRoot);
        var sourceConfigPath = Path.Combine(sourceRoot, ConfigFileName);
        if (!File.Exists(sourceConfigPath))
        {
            throw new InvalidOperationException(
                $"Source Config.ini was not found at '{sourceConfigPath}'. " +
                NormalizationHint);
        }

        var sourceConfig = File.ReadAllText(sourceConfigPath, Encoding.UTF8);
        ValidateAndPatchConfig(sourceConfig, out var normalizedConfig);

        var runRoot = Path.Combine(
            Path.GetTempPath(),
            SandboxDirectoryName,
            Guid.NewGuid().ToString("N"));
        var appDataRoot = Path.Combine(runRoot, "appdata");
        var configPath = Path.Combine(appDataRoot, ConfigFileName);

        Directory.CreateDirectory(appDataRoot);
        afterRunRootCreated?.Invoke(runRoot);

        var apiPort = FindEphemeralApiPort();
        var apiKey = GenerateApiKey();
        normalizedConfig = PatchOwnedConfigValues(
            normalizedConfig,
            apiPort,
            apiKey);
        File.WriteAllText(configPath, normalizedConfig, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new RecordingSandbox(runRoot, appDataRoot, configPath, apiPort, apiKey);
    }

    /// <summary>
    /// Validates the source configuration without creating a run directory.
    /// The recorder calls this gate before it owns any per-run resources.
    /// </summary>
    public static void ValidateSourceConfig(string sourceAppDataRoot)
    {
        var sourceRoot = NormalizeSourceRoot(sourceAppDataRoot);
        var sourceConfigPath = Path.Combine(sourceRoot, ConfigFileName);
        if (!File.Exists(sourceConfigPath))
        {
            throw new InvalidOperationException(
                $"Source Config.ini was not found at '{sourceConfigPath}'. " +
                NormalizationHint);
        }

        ValidateAndPatchConfig(File.ReadAllText(sourceConfigPath, Encoding.UTF8), out _);
    }

    public Task DeleteOnSuccessAsync()
    {
        TryDeleteRunRoot(RunRoot);
        return Task.CompletedTask;
    }

    internal static bool IsDefaultSkin(string value) =>
        value.Trim().Equals("Default", StringComparison.OrdinalIgnoreCase);

    internal static void RequireAbsolute(string key, string value)
    {
        if (!Path.IsPathFullyQualified(value.Trim()))
        {
            throw new InvalidOperationException(
                $"Source Config.ini key '{key}' is not normalized. " +
                NormalizationHint);
        }
    }

    private static string NormalizeSourceRoot(string sourceAppDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAppDataRoot);
        return Path.GetFullPath(sourceAppDataRoot);
    }

    private static void ValidateAndPatchConfig(string config, out string normalizedConfig)
    {
        var newline = config.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : "\n";
        var usesTrailingNewline = config.EndsWith('\n');
        var lines = config.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .ToList();
        if (usesTrailingNewline && lines.Count > 0)
            lines.RemoveAt(lines.Count - 1);

        var dtxPaths = FindValues(lines, "DTXPath");
        if (dtxPaths.Count == 0)
            RequireAbsolute("DTXPath", string.Empty);
        foreach (var dtxPath in dtxPaths)
            RequireAbsolute("DTXPath", dtxPath);

        var songRootKeys = new List<(string Key, string Value)>();
        foreach (var line in lines)
        {
            if (!TryReadAssignment(line, out var key, out var value) ||
                !IsIndexedSongRootKey(key))
                continue;

            songRootKeys.Add((key, value));
        }

        if (songRootKeys.Count == 0)
        {
            throw new InvalidOperationException(
                "Source Config.ini must contain at least one SongRoot.N entry. " +
                NormalizationHint);
        }

        foreach (var songRoot in songRootKeys)
            RequireAbsolute(songRoot.Key, songRoot.Value);

        var systemSkinRoots = FindValues(lines, "SystemSkinRoot");
        if (systemSkinRoots.Count == 0)
            RequireAbsolute("SystemSkinRoot", string.Empty);
        foreach (var systemSkinRoot in systemSkinRoots)
            RequireAbsolute("SystemSkinRoot", systemSkinRoot);

        var skinPaths = FindValues(lines, "SkinPath");
        foreach (var skinPath in skinPaths)
        {
            if (string.IsNullOrWhiteSpace(skinPath))
            {
                throw new InvalidOperationException(
                    "Source Config.ini key 'SkinPath' is not normalized. " +
                    NormalizationHint);
            }

            if (!IsDefaultSkin(skinPath))
                RequireAbsolute("SkinPath", skinPath);
        }

        normalizedConfig = string.Join(newline, lines);
        if (usesTrailingNewline)
            normalizedConfig += newline;
    }

    private static string PatchOwnedConfigValues(string config, int apiPort, string apiKey)
    {
        var newline = config.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : "\n";
        var usesTrailingNewline = config.EndsWith('\n');
        var lines = config.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .ToList();
        if (usesTrailingNewline && lines.Count > 0)
            lines.RemoveAt(lines.Count - 1);

        var values = new Dictionary<string, string>(OwnedConfigValues, StringComparer.Ordinal)
        {
            ["GameApiPort"] = apiPort.ToString(CultureInfo.InvariantCulture),
            ["GameApiKey"] = apiKey
        };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var patchedLines = new List<string>(lines.Count + values.Count);

        foreach (var line in lines)
        {
            if (!TryReadAssignment(line, out var key, out _) ||
                !values.TryGetValue(key, out var replacement))
            {
                patchedLines.Add(line);
                continue;
            }

            var equalsIndex = line.IndexOf('=');
            patchedLines.Add(line[..(equalsIndex + 1)] + replacement);
            seen.Add(key);
        }

        foreach (var pair in values)
        {
            if (!seen.Contains(pair.Key))
                patchedLines.Add($"{pair.Key}={pair.Value}");
        }

        var patched = string.Join(newline, patchedLines);
        if (usesTrailingNewline)
            patched += newline;
        return patched;
    }

    private static List<string> FindValues(IEnumerable<string> lines, string expectedKey)
    {
        var values = new List<string>();
        foreach (var line in lines)
        {
            if (TryReadAssignment(line, out var key, out var value) &&
                key.Equals(expectedKey, StringComparison.Ordinal))
                values.Add(value);
        }

        return values;
    }

    private static bool TryReadAssignment(string line, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(';'))
            return false;

        var equalsIndex = line.IndexOf('=');
        if (equalsIndex <= 0)
            return false;

        key = line[..equalsIndex].Trim();
        value = line[(equalsIndex + 1)..].Trim();
        return key.Length > 0;
    }

    private static bool IsIndexedSongRootKey(string key)
    {
        const string prefix = "SongRoot.";
        return key.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(
                key[prefix.Length..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var index) &&
            index >= 0;
    }

    private static int FindEphemeralApiPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string GenerateApiKey() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static void TryDeleteRunRoot(string runRoot)
    {
        try
        {
            if (Directory.Exists(runRoot))
                Directory.Delete(runRoot, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Cleanup is intentionally idempotent.
        }
    }
}
