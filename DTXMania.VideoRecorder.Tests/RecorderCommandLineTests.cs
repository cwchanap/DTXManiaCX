using DTXMania.VideoRecorder;
using DTXMania.VideoRecorder.Configuration;

namespace DTXMania.VideoRecorder.Tests;

public sealed class RecorderCommandLineTests
{
    private const string ObsUrlEnvironmentVariable = "DTXMANIA_VIDEO_OBS_URL";
    private const string ObsOutputEnvironmentVariable = "DTXMANIA_VIDEO_OBS_OUTPUT_DIR";
    private const string AppDataEnvironmentVariable = "DTXMANIA_APPDATA_ROOT";

    [Fact]
    public void Parse_EmptyArgs_ShouldExpectUsageFailure()
    {
        Assert.Throws<ArgumentException>(() => RecorderCommandLine.Parse(Array.Empty<string>()));
    }

    [Fact]
    public void Parse_UnknownVerb_ShouldExpectFailure()
    {
        Assert.Throws<ArgumentException>(() => RecorderCommandLine.Parse(new[] { "frobnicate" }));
    }

    [Fact]
    public void Parse_DoctorWithoutArguments_ShouldExpectDoctorVerb()
    {
        var command = RecorderCommandLine.Parse(new[] { "doctor" });

        Assert.Equal(RecorderVerb.Doctor, command.Verb);
    }

    [Fact]
    public void Parse_DoctorWithArguments_ShouldExpectFailure()
    {
        Assert.Throws<ArgumentException>(
            () => RecorderCommandLine.Parse(new[] { "doctor", "--chart", "x" }));
    }

    [Fact]
    public void Parse_RecordMissingChartAndOutput_ShouldExpectFailure()
    {
        Assert.Throws<ArgumentException>(
            () => RecorderCommandLine.Parse(new[] { "record" }));
    }

    [Fact]
    public void Parse_RecordMissingOutput_ShouldExpectFailure()
    {
        Assert.Throws<ArgumentException>(
            () => RecorderCommandLine.Parse(new[] { "record", "--chart", "/tmp/chart.dtx" }));
    }

    [Fact]
    public void Parse_RecordUnknownOption_ShouldExpectFailure()
    {
        Assert.Throws<ArgumentException>(
            () => RecorderCommandLine.Parse(new[] { "record", "--verbose", "x", "--output", "/tmp/out" }));
    }

    [Fact]
    public void Parse_RecordDuplicateChart_ShouldExpectFailure()
    {
        Assert.Throws<ArgumentException>(
            () => RecorderCommandLine.Parse(new[] { "record", "--chart", "a", "--chart", "b", "--output", "/tmp/out" }));
    }

    [Fact]
    public void Parse_RecordWithChartAndOutput_ShouldExpectRecordVerb()
    {
        var command = RecorderCommandLine.Parse(new[] { "record", "--chart", "/tmp/chart.dtx", "--output", "/tmp/out" });

        Assert.Equal(RecorderVerb.Record, command.Verb);
        Assert.Equal("/tmp/chart.dtx", command.ChartPath);
        Assert.Equal("/tmp/out", command.OutputDirectory);
    }

    [Fact]
    public void ReadEnvironment_AppDataRootOverride_ShouldExpectOverrideToWin()
    {
        var overrideRoot = CreateTempDirectory();

        try
        {
            var environment = RecorderCommandLine.ReadEnvironment(
                name => name == AppDataEnvironmentVariable ? overrideRoot : null,
                requireOutputDirectory: false);

            Assert.Equal(Path.GetFullPath(overrideRoot), environment.SourceAppDataRoot);
            Assert.Equal(new Uri("ws://127.0.0.1:4455"), environment.ObsUrl);
        }
        finally
        {
            Delete(overrideRoot);
        }
    }

    [Fact]
    public void ReadEnvironment_RecordWithoutObsOutputDirectory_ShouldExpectFailure()
    {
        Assert.Throws<InvalidOperationException>(
            () => RecorderCommandLine.ReadEnvironment(
                _ => null,
                requireOutputDirectory: true));
    }

    [Fact]
    public void ReadEnvironment_NonWebSocketObsUrl_ShouldExpectFailure()
    {
        Assert.Throws<InvalidOperationException>(
            () => RecorderCommandLine.ReadEnvironment(
                name => name == ObsUrlEnvironmentVariable ? "http://127.0.0.1:4455" : null,
                requireOutputDirectory: false));
    }

    [Fact]
    public void ValidateRecord_NonLoopbackObsUrl_ShouldExpectFailure()
    {
        var environment = CreateEnvironment(obsUrl: "ws://192.168.0.5:4455");
        var outputDirectory = CreateTempDirectory();
        var command = new RecorderCommand(RecorderVerb.Record, "/nonexistent/chart.dtx", outputDirectory);

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => RecorderCommandLine.ValidateRecord(command, environment, isWindows: true, isMacOS: false));

            Assert.Contains("loopback", exception.Message);
        }
        finally
        {
            Delete(outputDirectory);
        }
    }

    [Fact]
    public void ValidateRecord_RelativeChartPath_ShouldExpectFailure()
    {
        var environment = CreateEnvironment();
        var command = new RecorderCommand(RecorderVerb.Record, "chart.dtx", CreateTempDirectory());

        try
        {
            Assert.Throws<InvalidOperationException>(
                () => RecorderCommandLine.ValidateRecord(command, environment, isWindows: true, isMacOS: false));
        }
        finally
        {
            Delete(command.OutputDirectory!);
        }
    }

    [Fact]
    public void ValidateRecord_MissingChartFile_ShouldExpectFileNotFound()
    {
        var environment = CreateEnvironment();
        var missingChart = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.dtx");
        var command = new RecorderCommand(RecorderVerb.Record, missingChart, CreateTempDirectory());

        try
        {
            Assert.Throws<FileNotFoundException>(
                () => RecorderCommandLine.ValidateRecord(command, environment, isWindows: true, isMacOS: false));
        }
        finally
        {
            Delete(command.OutputDirectory!);
        }
    }

    [Fact]
    public void ValidateRecord_PublishPathIsFile_ShouldExpectFailure()
    {
        var environment = CreateEnvironment();
        var publishFile = Path.Combine(Path.GetTempPath(), $"dtx-video-test-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(publishFile, "file");
        var command = new RecorderCommand(RecorderVerb.Record, CreateChartFile(), publishFile);

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => RecorderCommandLine.ValidateRecord(command, environment, isWindows: true, isMacOS: false));

            Assert.Contains("file, not a directory", exception.Message);
        }
        finally
        {
            File.Delete(publishFile);
            Delete(Path.GetDirectoryName(command.ChartPath!)!);
        }
    }

    [Fact]
    public void ValidateRecord_MissingObsOutputDirectory_ShouldExpectFailure()
    {
        var environment = CreateEnvironment(
            obsOutputDirectory: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var command = new RecorderCommand(RecorderVerb.Record, CreateChartFile(), CreateTempDirectory());

        try
        {
            Assert.Throws<InvalidOperationException>(
                () => RecorderCommandLine.ValidateRecord(command, environment, isWindows: true, isMacOS: false));
        }
        finally
        {
            Delete(Path.GetDirectoryName(command.ChartPath!)!);
            Delete(command.OutputDirectory!);
        }
    }

    [Fact]
    public void ValidateRecord_WindowsWithWritableDirectories_ShouldExpectSuccess()
    {
        using var fixture = RecordFixture.Create();

        RecorderCommandLine.ValidateRecord(
            fixture.Command,
            fixture.Environment,
            isWindows: true,
            isMacOS: false);
    }

    [Fact]
    public void ValidateRecord_MacOsWithWritableDirectories_ShouldExpectSuccess()
    {
        using var fixture = RecordFixture.Create();

        RecorderCommandLine.ValidateRecord(
            fixture.Command,
            fixture.Environment,
            isWindows: false,
            isMacOS: true);
    }

    [Fact]
    public void ValidateRecord_UnsupportedOs_ShouldExpectPlatformNotSupported()
    {
        using var fixture = RecordFixture.Create();

        Assert.Throws<PlatformNotSupportedException>(
            () => RecorderCommandLine.ValidateRecord(
                fixture.Command,
                fixture.Environment,
                isWindows: false,
                isMacOS: false));
    }

    [Fact]
    public void GetDefaultSourceAppDataRoot_Windows_ShouldExpectLocalApplicationData()
    {
        var root = RecorderCommandLine.GetDefaultSourceAppDataRoot(
            isWindows: true,
            isMacOS: false,
            getFolderPath: _ => "/win/local",
            getEnvironmentVariable: _ => null);

        Assert.Equal(Path.Combine("/win/local", "DTXManiaCX"), root);
    }

    [Fact]
    public void GetDefaultSourceAppDataRoot_WindowsUnusableBasePath_ShouldExpectHomeConfigFallback()
    {
        var root = RecorderCommandLine.GetDefaultSourceAppDataRoot(
            isWindows: true,
            isMacOS: false,
            getFolderPath: folder => folder == Environment.SpecialFolder.UserProfile ? "/win/home" : "relative",
            getEnvironmentVariable: _ => null);

        Assert.Equal(Path.Combine("/win/home", ".config", "DTXManiaCX"), root);
    }

    [Fact]
    public void GetDefaultSourceAppDataRoot_MacOsUserProfileAvailable_ShouldExpectLibraryApplicationSupport()
    {
        var root = RecorderCommandLine.GetDefaultSourceAppDataRoot(
            isWindows: false,
            isMacOS: true,
            getFolderPath: folder => folder == Environment.SpecialFolder.UserProfile ? "/Users/tester" : "",
            getEnvironmentVariable: _ => null);

        Assert.Equal(
            Path.Combine(Path.Combine("/Users/tester", "Library", "Application Support"), "DTXManiaCX"),
            root);
    }

    [Fact]
    public void GetDefaultSourceAppDataRoot_MacOsUserProfileEmpty_ShouldFallBackToPersonal()
    {
        var root = RecorderCommandLine.GetDefaultSourceAppDataRoot(
            isWindows: false,
            isMacOS: true,
            getFolderPath: folder => folder == Environment.SpecialFolder.Personal ? "/Users/personal" : "",
            getEnvironmentVariable: _ => null);

        Assert.Equal(
            Path.Combine(Path.Combine("/Users/personal", "Library", "Application Support"), "DTXManiaCX"),
            root);
    }

    [Fact]
    public void GetDefaultSourceAppDataRoot_MacOsUserProfileAndPersonalEmpty_ShouldFallBackToHome()
    {
        var root = RecorderCommandLine.GetDefaultSourceAppDataRoot(
            isWindows: false,
            isMacOS: true,
            getFolderPath: _ => "",
            getEnvironmentVariable: name => name == "HOME" ? "/Users/envhome" : null);

        Assert.Equal(
            Path.Combine(Path.Combine("/Users/envhome", "Library", "Application Support"), "DTXManiaCX"),
            root);
    }

    [Fact]
    public void GetDefaultSourceAppDataRoot_OtherOs_ShouldExpectApplicationData()
    {
        var root = RecorderCommandLine.GetDefaultSourceAppDataRoot(
            isWindows: false,
            isMacOS: false,
            getFolderPath: folder => folder == Environment.SpecialFolder.ApplicationData ? "/xdg/config" : "",
            getEnvironmentVariable: _ => null);

        Assert.Equal(Path.Combine("/xdg/config", "DTXManiaCX"), root);
    }

    [Fact]
    public void GetDefaultSourceAppDataRoot_OtherOsUnusableBasePath_ShouldExpectHomeConfigFallback()
    {
        var root = RecorderCommandLine.GetDefaultSourceAppDataRoot(
            isWindows: false,
            isMacOS: false,
            getFolderPath: folder => folder == Environment.SpecialFolder.UserProfile ? "/home/tester" : "",
            getEnvironmentVariable: _ => null);

        Assert.Equal(Path.Combine("/home/tester", ".config", "DTXManiaCX"), root);
    }

    [Fact]
    public void GetDefaultSourceAppDataRoot_NoHomeDirectoryAnywhere_ShouldExpectFailure()
    {
        Assert.Throws<InvalidOperationException>(
            () => RecorderCommandLine.GetDefaultSourceAppDataRoot(
                isWindows: false,
                isMacOS: true,
                getFolderPath: _ => "",
                getEnvironmentVariable: _ => null));
    }

    private static RecorderEnvironment CreateEnvironment(
        string? obsUrl = null,
        string? obsOutputDirectory = null,
        string? sourceAppDataRoot = null)
        => RecorderCommandLine.ReadEnvironment(
            name => name switch
            {
                ObsUrlEnvironmentVariable => obsUrl,
                ObsOutputEnvironmentVariable => obsOutputDirectory,
                AppDataEnvironmentVariable => sourceAppDataRoot,
                _ => null
            },
            requireOutputDirectory: false);

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "dtx-video-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateChartFile()
    {
        var directory = CreateTempDirectory();
        var chartPath = Path.Combine(directory, "chart.dtx");
        File.WriteAllText(chartPath, "#TITLE: test\n");
        return chartPath;
    }

    private static void Delete(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private sealed class RecordFixture : IDisposable
    {
        private readonly string _root;

        private RecordFixture(string root, RecorderCommand command, RecorderEnvironment environment)
        {
            _root = root;
            Command = command;
            Environment = environment;
        }

        public RecorderCommand Command { get; }

        public RecorderEnvironment Environment { get; }

        public static RecordFixture Create()
        {
            var root = CreateTempDirectory();
            var songs = Path.Combine(root, "Songs");
            Directory.CreateDirectory(songs);
            var systemSkin = Path.Combine(root, "System");
            Directory.CreateDirectory(systemSkin);
            File.WriteAllText(
                Path.Combine(root, "Config.ini"),
                string.Join(
                    '\n',
                    "SkinPath=Default",
                    "DTXPath=" + songs,
                    "SongRoot.0=" + songs,
                    "SystemSkinRoot=" + systemSkin) + "\n");
            var chartPath = Path.Combine(root, "chart.dtx");
            File.WriteAllText(chartPath, "#TITLE: test\n");
            var publishDirectory = Path.Combine(root, "publish");
            var obsOutputDirectory = Path.Combine(root, "obs-output");
            Directory.CreateDirectory(obsOutputDirectory);

            var environment = RecorderCommandLine.ReadEnvironment(
                name => name switch
                {
                    ObsOutputEnvironmentVariable => obsOutputDirectory,
                    AppDataEnvironmentVariable => root,
                    _ => null
                },
                requireOutputDirectory: false);
            return new RecordFixture(
                root,
                new RecorderCommand(RecorderVerb.Record, chartPath, publishDirectory),
                environment);
        }

        public void Dispose() => Delete(_root);
    }
}
