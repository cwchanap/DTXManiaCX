using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using DTXMania.Game.Lib.Song;
using DTXMania.Test.TestData;

namespace DTXMania.Test.Song;

[Collection("SongManager")]
[Trait("Category", "Unit")]
public class SongManagerParsingTests : IDisposable
{
    private readonly SongManager _manager;

    public SongManagerParsingTests()
    {
        SongManager.ResetInstanceForTesting();
        _manager = SongManager.Instance;
    }

    public void Dispose()
    {
        _manager.Clear();
        SongManager.ResetInstanceForTesting();
    }

    [Fact]
    public void NormalizeSetDefLine_WhenNullLineIsProvided_ReturnsEmptyString()
    {
        var result = NormalizeSetDefLine(null);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void NormalizeSetDefLine_WhenHashSeparatedCommandIsUsed_CollapsesToSingleCommand()
    {
        var result = NormalizeSetDefLine("#  TITLE My Song");

        Assert.Equal("#TITLE My Song", result);
    }

    [Fact]
    public void ReconstructCorruptedLine_WhenSpacedKnownCommandIsUsed_RebuildsCommandAndValue()
    {
        var result = ReconstructCorruptedLine("#T I T L E My Song");

        Assert.Equal("#TITLE My Song", result);
    }

    [Fact]
    public void ReconstructCorruptedLine_WhenCommandCannotBeCompleted_UsesFinalFallback()
    {
        var result = ReconstructCorruptedLine("#A B C Mystery Value");

        Assert.Equal("#A B C Mystery Value", result);
    }

    [Fact]
    public void NormalizeSetDefLine_WhenBomAndNullBytesArePresent_StripsNoiseBeforeReturning()
    {
        var result = NormalizeSetDefLine("\uFEFF#TITLE My Song\u0000");

        Assert.Equal("#TITLE My Song", result);
    }

    [Theory]
    [MemberData(nameof(TryParseColorCases))]
    public void TryParseColor_WhenInputIsProvided_ReturnsExpectedResult(string colorValue, bool expectedParsed, Color expectedColor)
    {
        var (parsed, color) = InvokeTryParseColor(colorValue);

        Assert.Equal(expectedParsed, parsed);
        Assert.Equal(expectedColor, color);
    }

    [Fact]
    public void TryParseColor_WhenNullIsProvided_ReturnsFalseAndLeavesDefaultColor()
    {
        var (parsed, color) = InvokeTryParseColor(null);

        Assert.False(parsed);
        Assert.Equal(Color.White, color);
    }

    public static IEnumerable<object[]> TryParseColorCases()
    {
        yield return new object[] { "#112233", true, Color.FromArgb(0x11, 0x22, 0x33) };
        yield return new object[] { "Blue", true, Color.Blue };
        yield return new object[] { "#GGGGGG", false, Color.White };
    }

    private string NormalizeSetDefLine(string? line)
    {
        return ReflectionHelpers.InvokePrivateMethod<string>(_manager, "NormalizeSetDefLine", line)!;
    }

    private string ReconstructCorruptedLine(string line)
    {
        return ReflectionHelpers.InvokePrivateMethod<string>(_manager, "ReconstructCorruptedLine", line)!;
    }

    private (bool Parsed, Color Color) InvokeTryParseColor(string? colorValue)
    {
        var method = typeof(SongManager).GetMethod("TryParseColor", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var args = new object[] { colorValue, Color.Empty };
        var parsed = (bool)method!.Invoke(_manager, args)!;
        return (parsed, (Color)args[1]);
    }
}
