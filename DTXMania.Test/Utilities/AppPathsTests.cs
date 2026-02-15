using System;
using System.IO;
using System.Reflection;
using DTXMania.Game.Lib.Utilities;

namespace DTXMania.Test.Utilities;

public class AppPathsTests
{
    [Fact]
    public void GetConfigFilePath_ShouldBeRootedAndPointToConfigIni()
    {
        var path = AppPaths.GetConfigFilePath();

        Assert.True(Path.IsPathRooted(path));
        Assert.Equal("Config.ini", Path.GetFileName(path));
    }

    [Fact]
    public void GetDefaultPaths_ShouldUseExpectedNames()
    {
        Assert.Equal("DTXFiles", Path.GetFileName(AppPaths.GetDefaultSongsPath().TrimEnd(Path.DirectorySeparatorChar)));
        Assert.Equal("System", Path.GetFileName(AppPaths.GetDefaultSystemSkinRoot().TrimEnd(Path.DirectorySeparatorChar)));
        Assert.Equal("songs.db", Path.GetFileName(AppPaths.GetSongsDatabasePath()));
    }

    [Fact]
    public void ResolvePathOrDefault_WhenConfiguredPathIsNull_ReturnsDefaultFullPath()
    {
        var defaultPath = Path.Combine(Path.GetTempPath(), "dtx-default");

        var resolved = AppPaths.ResolvePathOrDefault(null, defaultPath);

        Assert.Equal(Path.GetFullPath(defaultPath), resolved);
    }

    [Fact]
    public void ResolvePathOrDefault_WhenConfiguredPathProvided_ShouldResolveConfiguredPath()
    {
        var defaultPath = Path.Combine(Path.GetTempPath(), "dtx-default-unused");
        var configured = "Songs/Configured";

        var resolved = AppPaths.ResolvePathOrDefault(configured, defaultPath);

        Assert.True(Path.IsPathRooted(resolved));
        Assert.EndsWith(Path.Combine("Songs", "Configured"), resolved);
    }

    [Fact]
    public void ResolvePath_WhenPathIsBlank_ShouldReturnBasePath()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "base");

        var resolved = AppPaths.ResolvePath("   ", basePath);

        Assert.Equal(Path.GetFullPath(basePath), resolved);
    }

    [Fact]
    public void ResolvePath_WhenPathIsRelative_ShouldJoinWithBasePath()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "base");

        var resolved = AppPaths.ResolvePath("Songs/Test", basePath);

        Assert.Equal(Path.GetFullPath(Path.Combine(basePath, "Songs/Test")), resolved);
    }

    [Fact]
    public void ResolvePath_WhenPathIsAbsolute_ShouldReturnAbsolutePath()
    {
        var absolute = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "absolute-path"));

        var resolved = AppPaths.ResolvePath(absolute, "/ignored");

        Assert.Equal(absolute, resolved);
    }

    [Fact]
    public void ResolvePath_WhenUsingHomeTilde_ShouldExpandToHomeDirectory()
    {
        var home = GetHomeDirectory();
        if (string.IsNullOrWhiteSpace(home))
        {
            return;
        }

        var resolved = AppPaths.ResolvePath("~/DTXFiles", "/ignored");

        Assert.Equal(Path.GetFullPath(Path.Combine(home, "DTXFiles")), resolved);
    }

    [Fact]
    public void EnsureDirectory_ShouldCreateDirectoryAndIgnoreBlankPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "dtx-app-path-tests", Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "nested");

        try
        {
            AppPaths.EnsureDirectory("   ");
            AppPaths.EnsureDirectory(target);

            Assert.True(Directory.Exists(target));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void IsMacLibraryRelativePath_ShouldMatchLibraryPrefixes()
    {
        var method = typeof(AppPaths).GetMethod("IsMacLibraryRelativePath", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        Assert.True((bool)method!.Invoke(null, new object[] { "Library/Application Support/DTX" })!);
        Assert.True((bool)method.Invoke(null, new object[] { "Library\\Application Support\\DTX" })!);
        Assert.False((bool)method.Invoke(null, new object[] { "Songs/DTX" })!);
    }

    [Fact]
    public void ExpandHomePath_ShouldHandleTildeVariantsAndPassThrough()
    {
        var method = typeof(AppPaths).GetMethod("ExpandHomePath", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var home = GetHomeDirectory();
        if (string.IsNullOrWhiteSpace(home))
        {
            return;
        }

        var tildeOnly = (string)method!.Invoke(null, new object[] { "~" })!;
        var tildeSlash = (string)method.Invoke(null, new object[] { "~/Songs" })!;
        var passthrough = (string)method.Invoke(null, new object[] { "relative/path" })!;

        Assert.Equal(home, tildeOnly);
        Assert.Equal(Path.Combine(home, "Songs"), tildeSlash);
        Assert.Equal("relative/path", passthrough);
    }

    private static string GetHomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        }

        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
        }

        return home;
    }
}
