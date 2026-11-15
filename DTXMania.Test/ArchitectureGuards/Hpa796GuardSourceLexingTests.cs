#nullable enable

using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace DTXMania.Test.ArchitectureGuards;

// Regression tests for the review findings on the HPA-796 guard internals:
// - Comment stripping must be a string-aware lexical pass: a "//" or "/*"
//   inside a string/char literal must not hide real code, a stripped comment
//   must not merge neighbouring tokens (new/**/Texture2D stays two tokens),
//   and a "/*" inside a line comment must not open a block over real code.
// - The 1x1 Texture2D check must match complete dimension arguments anchored
//   to the width/height positions, so "new Texture2D(device, 1, 128)" and
//   "new Texture2D(device, width, height, 1, 1)" never match.
[Trait("Category", "Architecture")]
public sealed class Hpa796GuardSourceLexingTests
{
    private static string Strip(string source) =>
        Hpa796ArchitectureGuardTests.StripCommentsAndLiteralContents(source);

    // --- review finding: string-aware comment stripping ---

    [Fact]
    public void StripComments_ShouldKeepCodeAfterCommentMarkerInsideStringLiteral()
    {
        var stripped = Strip("\"https://example\"; Debug.WriteLine(\"x\");");

        Assert.DoesNotContain("//", stripped);
        Assert.Contains("Debug.WriteLine(", stripped);
    }

    [Fact]
    public void StripComments_ShouldKeepCodeAfterCommentMarkerInsideInterpolatedString()
    {
        var stripped = Strip("return $\"http://localhost:{port}\"; Debug.WriteLine(\"x\");");

        Assert.DoesNotContain("//", stripped);
        Assert.Contains("Debug.WriteLine(", stripped);
    }

    [Fact]
    public void StripComments_ShouldKeepCodeAfterCommentMarkerInsideVerbatimString()
    {
        var stripped = Strip("var p = @\"a//b\";\nDebug.WriteLine(\"x\");");

        Assert.DoesNotContain("//", stripped);
        Assert.Contains("Debug.WriteLine(", stripped);
    }

    [Fact]
    public void StripComments_ShouldKeepCodeAfterCommentMarkerInsideRawString()
    {
        var stripped = Strip("\"\"\"(?:https?)://x\"\"\";\nDebug.WriteLine(\"x\");");

        Assert.DoesNotContain("//", stripped);
        Assert.Contains("Debug.WriteLine(", stripped);
    }

    [Fact]
    public void StripComments_ShouldNotMergeTokensAroundBlockComment()
    {
        var stripped = Strip("new/**/Texture2D(device, 1, 1)");

        Assert.Matches(@"new\s+Texture2D\(", stripped);
    }

    [Fact]
    public void StripComments_ShouldNotOpenBlockCommentInsideLineComment()
    {
        var stripped = Strip("// start /*\nDebug.WriteLine(\"x\");\n// tail */");

        Assert.DoesNotContain("/*", stripped);
        Assert.Contains("Debug.WriteLine(", stripped);
    }

    [Fact]
    public void StripComments_ShouldNotOpenStringInsideCharLiteral()
    {
        var stripped = Strip("var q = '\"';\nTrace.Write(\"hi\");");

        Assert.Contains("Trace.Write(", stripped);
    }

    [Fact]
    public void StripComments_ShouldPreserveNewlinesInsideBlockComments()
    {
        var stripped = Strip("a/*\n\n*/b");

        Assert.Equal("a  \n\n  b", stripped);
    }

    [Fact]
    public void StripComments_ShouldBlankStringLiteralContents()
    {
        // Guard-relevant text inside a message string must not trip the
        // regex guards, so literal contents are blanked (delimiters kept).
        var stripped = Strip("var s = \"Dictionary<string, object>\";");

        Assert.DoesNotContain("Dictionary", stripped);
        Assert.Contains("\"", stripped);
    }

    // --- review finding: complete dimension-argument matching ---

    [Theory]
    [InlineData("new Texture2D(device, 1, 1)", true)]
    [InlineData("new Texture2D(device, 1,\n 1)", true)]
    [InlineData("new Texture2D(device, 1, 1, false, SurfaceFormat.Color)", true)]
    [InlineData("new Texture2D(Resolve(1), 1, 1)", true)]
    [InlineData("new Texture2D(device, 1, 128)", false)]
    [InlineData("new Texture2D(device, 11, 1)", false)]
    [InlineData("new Texture2D(device, width, height)", false)]
    [InlineData("new Texture2D(GetDevice(1, 1), width, height)", false)]
    [InlineData("new Texture2D(device, width, height, 1, 1)", false)]
    [InlineData("new Texture2D(device, width, height, false, format)", false)]
    public void TextureScan_ShouldMatchOnlyTrueOneByOneInstantiations(
        string source,
        bool expected)
    {
        var matches =
            Hpa796ArchitectureGuardTests.FindRawOneByOneTextureInstantiationLines(
                Strip(source));

        Assert.Equal(expected, matches.Any());
    }

    [Fact]
    public void TextureScan_ShouldReportFirstLineOfMultiLineMatch()
    {
        var matches =
            Hpa796ArchitectureGuardTests.FindRawOneByOneTextureInstantiationLines(
                Strip("var t = new Texture2D(\n device,\n 1,\n 1);"));

        Assert.Single(matches);
        Assert.Equal(1, matches[0].Line);
    }

    // --- review finding round 2: interpolation holes ---

    [Fact]
    public void StripComments_ShouldPreserveInterpolationHoleExpression()
    {
        // The hole expression is real code: it must survive stripping so the
        // texture guard can see the prohibited allocation.
        var stripped = Strip("var text = $\"{new Texture2D(device, 1, 1)}\";");

        Assert.Contains("new Texture2D(device, 1, 1)", stripped);
    }

    [Fact]
    public void TextureScan_ShouldCatchOneByOneAllocationInsideInterpolatedHole()
    {
        var matches =
            Hpa796ArchitectureGuardTests.FindRawOneByOneTextureInstantiationLines(
                Strip("var text = $\"{new Texture2D(device, 1, 1)}\";"));

        Assert.Single(matches);
    }

    [Fact]
    public void StripComments_ShouldBlankNestedLiteralInsideInterpolatedHole()
    {
        // The inner opening quote must not end the outer string scan and
        // expose the nested literal as apparent executable code.
        var stripped = Strip("var text = $\"{Echo(\"Debug.WriteLine\")}\";");

        Assert.DoesNotContain("Debug.WriteLine", stripped);
        Assert.Contains("Echo(", stripped);
    }

    [Fact]
    public void StripComments_ShouldKeepCodeAfterInterpolatedStringWithNestedLiteral()
    {
        var stripped = Strip("var text = $\"{Echo(\"Debug.WriteLine\")}\";\nTrace.Write(\"x\");");

        Assert.Contains("Trace.Write(", stripped);
    }

    [Fact]
    public void StripComments_ShouldTreatDoubledBracesAsLiteralInInterpolatedString()
    {
        var stripped = Strip("var s = $\"{{not code}}\"; Debug.WriteLine(\"x\");");

        Assert.DoesNotContain("not code", stripped);
        Assert.Contains("Debug.WriteLine(", stripped);
    }

    [Fact]
    public void StripComments_ShouldKeepScanningAfterFormatSpecifierHole()
    {
        var stripped = Strip("var s = $\"{value:N2}\"; Debug.WriteLine(\"x\");");

        Assert.Contains("Debug.WriteLine(", stripped);
    }

    [Fact]
    public void StripComments_ShouldPreserveHoleInsideRawInterpolatedString()
    {
        var stripped =
            Strip("var s = $\"\"\"{new Texture2D(device, 1, 1)}\"\"\";");

        Assert.Contains("new Texture2D(device, 1, 1)", stripped);
    }

    [Fact]
    public void StripComments_ShouldPreserveHoleInsideVerbatimInterpolatedString()
    {
        var stripped =
            Strip("var s = $@\"a\"\"{new Texture2D(device, 1, 1)}\"\"b\";");

        Assert.Contains("new Texture2D(device, 1, 1)", stripped);
    }

    // --- review finding round 3: empty ordinary string literal ---

    [Fact]
    public void StripComments_ShouldKeepCodeAfterEmptyOrdinaryString()
    {
        // "" is a complete empty literal: scanning must resume after both
        // delimiters, not hunt for a second closing quote and blank the
        // rest of the file over real code.
        var stripped =
            Strip("var s = \"\"; var texture = new Texture2D(device, 1, 1);");

        Assert.Contains("new Texture2D(device, 1, 1)", stripped);
    }

    [Fact]
    public void TextureScan_ShouldCatchOneByOneAllocationAfterEmptyOrdinaryString()
    {
        var matches =
            Hpa796ArchitectureGuardTests.FindRawOneByOneTextureInstantiationLines(
                Strip("var s = \"\"; var texture = new Texture2D(device, 1, 1);"));

        Assert.Single(matches);
    }
}
