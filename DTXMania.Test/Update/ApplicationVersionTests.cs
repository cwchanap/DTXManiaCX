using System;
using System.Reflection;
using System.Text.RegularExpressions;
using DTXMania.Game.Lib.Update;
using Xunit;

namespace DTXMania.Test.Update;

[Trait("Category", "Unit")]
public class ApplicationVersionTests
{
    [Fact]
    public void Parse_WhenThreeComponents_ShouldReturnExactVersion()
    {
        Assert.Equal(new Version(1, 2, 3), ApplicationVersion.Parse("1.2.3"));
    }

    [Fact]
    public void Parse_WhenAssemblyStyleFourComponents_ShouldNormalizeToThreeComponents()
    {
        // Raw System.Version ordering treats 1.2.3 < 1.2.3.0, so comparison must
        // go through normalization: both collapse to the same three components.
        Assert.Equal(new Version(1, 2, 3), ApplicationVersion.Parse("1.2.3.0"));
        Assert.Equal(ApplicationVersion.Parse("1.2.3"), ApplicationVersion.Parse("1.2.3.0"));
    }

    [Fact]
    public void Parse_WhenVersionsEqualAfterNormalization_ShouldCompareEqual()
    {
        var tag = ApplicationVersion.Parse("v1.2.3");
        var assembly = ApplicationVersion.Parse("1.2.3.0");

        Assert.Equal(tag, assembly);
        Assert.Equal(0, assembly!.CompareTo(tag));
    }

    [Fact]
    public void Parse_WhenTagPrefixOrMetadataPresent_ShouldUseNumericCore()
    {
        Assert.Equal(new Version(2, 0, 1), ApplicationVersion.Parse("v2.0.1"));
        Assert.Equal(new Version(2, 0, 1), ApplicationVersion.Parse("2.0.1-beta.1+build"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1.2")]
    [InlineData("vX.Y.Z")]
    public void Parse_WhenNoNumericCore_ShouldReturnNull(string? value)
    {
        Assert.Null(ApplicationVersion.Parse(value));
    }

    [Fact]
    public void Display_WhenReadFromGameAssembly_ShouldBeExactlyThreeComponents()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", ApplicationVersion.Display);
        Assert.Equal(-1, ApplicationVersion.Current.Revision);
    }

    [Fact]
    public void DisplayWithPrerelease_WhenReadFromGameAssembly_ShouldKeepPrereleaseTagAndStripBuildMetadata()
    {
        var informational = typeof(ApplicationVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;
        var expected = informational.Split('+')[0];

        Assert.Equal(expected, ApplicationVersion.DisplayWithPrerelease);
        Assert.DoesNotContain('+', ApplicationVersion.DisplayWithPrerelease);
    }

    [Fact]
    public void BuildId_WhenReadFromGameAssembly_ShouldNotBeEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(ApplicationVersion.BuildId));
    }
}
