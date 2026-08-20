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
        var sourceValues = LoadValidatedSourceValues(sourceRoot);

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
        var patchedValues = PatchOwnedConfigValues(sourceValues, apiPort, apiKey);
        File.WriteAllText(
            configPath,
            SerializeConfigIni(patchedValues),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new RecordingSandbox(runRoot, appDataRoot, configPath, apiPort, apiKey);
    }

    /// <summary>
    /// Validates the source configuration database without creating a run
    /// directory. The recorder calls this gate before it owns any per-run
    /// resources.
    /// </summary>
    public static void ValidateSourceConfig(string sourceAppDataRoot)
    {
        var sourceRoot = NormalizeSourceRoot(sourceAppDataRoot);
        _ = LoadValidatedSourceValues(sourceRoot);
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
                $"Source config database key '{key}' is not normalized. " +
                NormalizationHint);
        }
    }

    /// <summary>
    /// Loads the source database rows (read-only, v1 + ConfigEntries
    /// required) and validates the path-bearing logical values the sandbox
    /// game must resolve: absolute SongRoot.N entries (at least one),
    /// absolute SystemSkinRoot, and SkinPath as either the Default token or
    /// an absolute path. The legacy DTXPath representation is never
    /// persisted to the database, so no DTXPath validation remains.
    /// </summary>
    private static IReadOnlyDictionary<string, string> LoadValidatedSourceValues(string sourceRoot)
    {
        var sourceDatabasePath = SourceConfigDatabase.GetDatabasePath(sourceRoot);
        if (!File.Exists(sourceDatabasePath))
        {
            throw new InvalidOperationException(
                $"Source config database was not found at '{sourceDatabasePath}'. " +
                NormalizationHint);
        }

        var sourceValues = SourceConfigDatabase.Load(sourceDatabasePath);

        var hasSongRoot = false;
        foreach (var pair in sourceValues)
        {
            if (!IsIndexedSongRootKey(pair.Key))
                continue;

            hasSongRoot = true;
            RequireAbsolute(pair.Key, pair.Value);
        }

        if (!hasSongRoot)
        {
            throw new InvalidOperationException(
                "Source config database must contain at least one SongRoot.N entry. " +
                NormalizationHint);
        }

        RequireAbsolute(
            "SystemSkinRoot",
            sourceValues.TryGetValue("SystemSkinRoot", out var systemSkinRoot)
                ? systemSkinRoot
                : string.Empty);

        if (!sourceValues.TryGetValue("SkinPath", out var skinPath) ||
            string.IsNullOrWhiteSpace(skinPath))
        {
            throw new InvalidOperationException(
                "Source config database key 'SkinPath' is not normalized. " +
                NormalizationHint);
        }

        if (!IsDefaultSkin(skinPath))
            RequireAbsolute("SkinPath", skinPath);

        return sourceValues;
    }

    /// <summary>
    /// Patches the recorder-owned rows in memory; the source database is
    /// never modified.
    /// </summary>
    private static IReadOnlyDictionary<string, string> PatchOwnedConfigValues(
        IReadOnlyDictionary<string, string> sourceValues,
        int apiPort,
        string apiKey)
    {
        var patched = new Dictionary<string, string>(sourceValues, StringComparer.Ordinal);
        foreach (var pair in OwnedConfigValues)
            patched[pair.Key] = pair.Value;
        patched["GameApiPort"] = apiPort.ToString(CultureInfo.InvariantCulture);
        patched["GameApiKey"] = apiKey;
        return patched;
    }

    /// <summary>
    /// Serializes a FRESH sandbox Config.ini containing the patched logical
    /// values (ordinal key order, LF newlines, trailing newline, UTF-8
    /// without BOM). The sandbox game imports this INI and creates its own
    /// config.db through the production ConfigManager.
    /// </summary>
    private static string SerializeConfigIni(IReadOnlyDictionary<string, string> values)
    {
        foreach (var pair in values)
        {
            // A CR/LF inside a value would inject forged Key=Value lines into
            // the bootstrap INI; such a row can only come from a hand-edited
            // source database.
            if (pair.Value.Contains('\r') || pair.Value.Contains('\n'))
            {
                throw new InvalidOperationException(
                    $"Source config database key '{pair.Key}' has a value containing a line break; " +
                    $"values must be single-line. {NormalizationHint}");
            }
        }

        return string.Join(
            '\n',
            values.Keys
                .OrderBy(key => key, StringComparer.Ordinal)
                .Select(key => $"{key}={values[key]}")) + "\n";
    }

    private static string NormalizeSourceRoot(string sourceAppDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAppDataRoot);
        return Path.GetFullPath(sourceAppDataRoot);
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
