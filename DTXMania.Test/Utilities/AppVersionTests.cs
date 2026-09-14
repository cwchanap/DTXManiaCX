using System.Reflection;
using DTXMania.Game.Lib.Utilities;
using Xunit;

namespace DTXMania.Test.Utilities;

/// <summary>
/// The display version comes from AssemblyInformationalVersion so prerelease release tags
/// (e.g. "0.1.0-beta") survive stamping, with any "+&lt;commit&gt;" build metadata stripped.
/// </summary>
[Trait("Category", "Unit")]
public class AppVersionTests
{
    [Fact]
    public void GetDisplayVersion_ShouldReturnInformationalVersionWithoutBuildMetadata()
    {
        var informational = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;
        var expected = informational.Split('+')[0];

        Assert.Equal(expected, AppVersion.GetDisplayVersion());
    }

    [Fact]
    public void GetDisplayVersion_ShouldNotContainBuildMetadataSuffix()
    {
        Assert.DoesNotContain('+', AppVersion.GetDisplayVersion());
    }
}
