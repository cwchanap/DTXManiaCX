using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using DTXMania.Game.Lib.Utilities;

namespace DTXMania.Test.Utilities;

[Trait("Category", "Unit")]
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
        // Use platform-safe path construction
        var configured = Path.Combine("Songs", "Configured");

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
        // Use platform-safe path construction instead of forward slashes
        var relativePath = Path.Combine("Songs", "Test");

        var resolved = AppPaths.ResolvePath(relativePath, basePath);

        Assert.Equal(Path.GetFullPath(Path.Combine(basePath, "Songs", "Test")), resolved);
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
    public void ResolvePath_WhenPathTargetsMacLibraryRelativeLocation_ShouldUsePlatformSpecificBase()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "base");
        var libraryRelativePath = Path.Combine("Library", "Application Support", "DTX");
        var resolved = AppPaths.ResolvePath(libraryRelativePath, basePath);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = GetHomeDirectory();
            if (string.IsNullOrWhiteSpace(home))
            {
                return;
            }

            Assert.Equal(Path.GetFullPath(Path.Combine(home, libraryRelativePath)), resolved);
        }
        else
        {
            Assert.Equal(Path.GetFullPath(Path.Combine(basePath, libraryRelativePath)), resolved);
        }
    }

    [Fact]
    public void GetAppDataRoot_OnRepeatedCalls_ShouldBeIdempotent()
    {
        var a = AppPaths.GetAppDataRoot();
        var b = AppPaths.GetAppDataRoot();

        Assert.Equal(a, b);
    }

    [Fact]
    public void GetHomeDirectoryInternal_ShouldResolveNonEmptyHomeDirectory()
    {
        var method = typeof(AppPaths).GetMethod("GetHomeDirectory", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var home = (string)method!.Invoke(null, null)!;

        Assert.False(string.IsNullOrWhiteSpace(home));
        Assert.True(Path.IsPathRooted(home));
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
    public void IsMacLibraryRelativePath_WhenPathIsBlank_ShouldReturnFalse()
    {
        var method = typeof(AppPaths).GetMethod("IsMacLibraryRelativePath", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        Assert.False((bool)method!.Invoke(null, new object[] { "   " })!);
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

    [Fact]
    public void ExpandHomePath_WhenPathIsBlank_ShouldReturnOriginalPath()
    {
        var method = typeof(AppPaths).GetMethod("ExpandHomePath", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var blank = "   ";
        var expanded = (string)method!.Invoke(null, new object[] { blank })!;

        Assert.Equal(blank, expanded);
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

[Trait("Category", "Unit")]
[Collection("AppPaths")]
public class AppPathsEnvironmentTests
{
    [Fact]
    public void GetAppDataRoot_ShouldReturnRootedPathEndingInAppName()
    {
        const string envName = "DTXMANIA_APPDATA_ROOT";
        var previous = Environment.GetEnvironmentVariable(envName);

        try
        {
            Environment.SetEnvironmentVariable(envName, null);

            var root = AppPaths.GetAppDataRoot();

            Assert.False(string.IsNullOrWhiteSpace(root));
            Assert.True(Path.IsPathRooted(root));
            Assert.Equal("DTXManiaCX", Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, previous);
        }
    }

    [Fact]
    public void GetAppDataRoot_WhenEnvironmentOverrideIsSet_ShouldUseOverride()
    {
        const string envName = "DTXMANIA_APPDATA_ROOT";
        var previous = Environment.GetEnvironmentVariable(envName);
        var overrideRoot = Path.Combine(Path.GetTempPath(), "dtx-appdata-root-" + Guid.NewGuid().ToString("N"));

        try
        {
            Environment.SetEnvironmentVariable(envName, overrideRoot);

            var root = AppPaths.GetAppDataRoot();

            Assert.Equal(Path.GetFullPath(overrideRoot), root);
            Assert.Equal(Path.Combine(Path.GetFullPath(overrideRoot), "Config.ini"), AppPaths.GetConfigFilePath());
            Assert.Equal(Path.Combine(Path.GetFullPath(overrideRoot), "DTXFiles"), AppPaths.GetDefaultSongsPath());
            Assert.Equal(Path.Combine(Path.GetFullPath(overrideRoot), "System"), AppPaths.GetDefaultSystemSkinRoot());
            Assert.Equal(Path.Combine(Path.GetFullPath(overrideRoot), "songs.db"), AppPaths.GetSongsDatabasePath());
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, previous);
        }
    }

    [Fact]
    public void GetAppDataRoot_WhenEnvironmentOverrideIsBlank_ShouldUseDefaultRoot()
    {
        const string envName = "DTXMANIA_APPDATA_ROOT";
        var previous = Environment.GetEnvironmentVariable(envName);

        try
        {
            Environment.SetEnvironmentVariable(envName, "   ");

            var root = AppPaths.GetAppDataRoot();

            Assert.False(string.IsNullOrWhiteSpace(root));
            Assert.True(Path.IsPathRooted(root));
            Assert.Equal("DTXManiaCX", Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, previous);
        }
    }

    /// <summary>
    /// After clearing HOME/USERPROFILE env vars, checks whether the null-home fallback
    /// branch can be exercised. On most CI runners, SpecialFolder.UserProfile/Personal
    /// still resolve, so the fallback cannot be forced. The test asserts the actual
    /// behavior observed: if SpecialFolders resolve, expansion succeeds; if they don't,
    /// the original tilde path is returned unchanged.
    /// </summary>
    [Fact]
    public void ExpandHomePath_WhenHomeDirectoryUnavailable_ShouldReturnOriginalPath()
    {
        var method = typeof(AppPaths).GetMethod("ExpandHomePath", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var savedHome = Environment.GetEnvironmentVariable("HOME");
        var savedUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        try
        {
            Environment.SetEnvironmentVariable("HOME", "");
            Environment.SetEnvironmentVariable("USERPROFILE", "");

            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var personal = Environment.GetFolderPath(Environment.SpecialFolder.Personal);

            if (string.IsNullOrWhiteSpace(profile) && string.IsNullOrWhiteSpace(personal))
            {
                // Null-home branch can be exercised: expansion should return original path
                var expanded = (string)method!.Invoke(null, new object[] { "~/Songs" })!;
                Assert.Equal("~/Songs", expanded);
            }
            else
            {
                // SpecialFolders still resolve on this platform — the null-home fallback
                // cannot be forced. Verify that expansion uses the resolved home instead.
                var home = profile;
                if (string.IsNullOrWhiteSpace(home)) home = personal;
                var expanded = (string)method!.Invoke(null, new object[] { "~/Songs" })!;
                Assert.Equal(Path.Combine(home, "Songs"), expanded);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", savedHome);
            Environment.SetEnvironmentVariable("USERPROFILE", savedUserProfile);
        }
    }

    [Fact]
    public void GetBundledSystemSkinRootCandidates_ShouldReturnAbsoluteRootedPaths()
    {
        var candidates = AppPaths.GetBundledSystemSkinRootCandidates();

        Assert.NotEmpty(candidates);
        Assert.All(candidates, c =>
        {
            Assert.True(Path.IsPathRooted(c), $"Expected rooted candidate, got '{c}'");
        });
    }

    [Fact]
    public void GetBundledSystemSkinRootCandidates_ShouldIncludeMacOSBundleAndPortableLayouts()
    {
        // The candidates are derived from AppContext.BaseDirectory, so assert against
        // the exact expected paths rather than path suffixes (the bin dir is not inside
        // a real .app/Contents/MacOS/ structure during tests).
        var baseDir = AppContext.BaseDirectory;
        var expectedBundle = Path.GetFullPath(Path.Combine(baseDir, "..", "Resources", "System"));
        var expectedPortable = Path.GetFullPath(Path.Combine(baseDir, "System"));

        var candidates = AppPaths.GetBundledSystemSkinRootCandidates().ToList();

        Assert.Contains(expectedBundle, candidates);
        Assert.Contains(expectedPortable, candidates);
    }
}
