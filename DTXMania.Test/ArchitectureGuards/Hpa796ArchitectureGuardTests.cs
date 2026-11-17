#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace DTXMania.Test.ArchitectureGuards;

// Source-level architecture guards for the HPA-796 refactor (plain English):
// 1. Game production code must not emit ad-hoc diagnostics (Debug.Write*/Print,
//    Console.Write/WriteLine, Trace.Write*/Trace.Trace*); structured logging is
//    the approved channel. Debug.Assert/Trace.Assert and the pre-logger startup
//    Console.Error usage stay exempt.
// 2. Raw 1x1 Texture2D creation is only allowed inside the resource manager so
//    every texture flows through reference counting and skin fallback. The
//    constructor arguments are parsed explicitly (regexes only locate the
//    call), so partial numbers like "1, 128" never match.
// 3. Stage transitions must keep the typed StageTransitionPayload hand-off; the
//    former untyped Dictionary<string, object> must not return in the stage
//    contracts. The nullable Dictionary<string, object?> crash-breadcrumb
//    telemetry in StageManager.cs is intentionally out of scope (it is not the
//    stage hand-off).
//
// All guards scan sources through StripCommentsAndLiteralContents, a small
// string-aware lexical pass (see its doc comment), so comment markers inside
// literals cannot hide violations and stripped comments cannot merge tokens.
[Trait("Category", "Architecture")]
public sealed class Hpa796ArchitectureGuardTests
{
    // Diagnostic write calls: Debug.Write/WriteLine/WriteIf/Print and
    // Trace.Write/WriteLine/WriteIf plus Trace.TraceWarning/TraceError/
    // TraceInformation/TraceTransfer. Debug.Assert/Trace.Assert and
    // Console.Error intentionally do not match this pattern.
    private static readonly Regex DiagnosticOutputRegex = new(
        @"\b(?:Debug\s*\.\s*(?:Write|Print)|Console\s*\.\s*Write|Trace\s*\.\s*(?:Write|Trace))",
        RegexOptions.Compiled);

    // Locates the start of Texture2D constructor calls; the argument check
    // itself is done by HasOneByOneDimensions (argument-level, not regex).
    private static readonly Regex Texture2DNewRegex = new(
        @"new\s+Texture2D\s*\(",
        RegexOptions.Compiled);

    // The untyped stage hand-off that StageTransitionPayload replaced.
    // Matches only the non-nullable form so the crash-breadcrumb
    // Dictionary<string, object?> telemetry stays out of scope.
    private static readonly Regex UntypedStageHandOffRegex = new(
        @"\bDictionary\s*<\s*string\s*,\s*object\s*>",
        RegexOptions.Compiled);

    [Fact]
    public void GameProductionSources_ShouldNotWriteDebugConsoleOrTraceDiagnostics()
    {
        AssertNoMatches(
            DiagnosticOutputRegex,
            ReadGameSources(),
            "Game production sources must not call Debug.Write*/Debug.Print, " +
            "Console.Write/Console.WriteLine or Trace.Write*/Trace.Trace* " +
            "(Trace.TraceWarning/TraceError/TraceInformation); use the " +
            "structured game logger instead. Debug.Assert/Trace.Assert and " +
            "pre-logger Console.Error remain exempt.");
    }

    [Fact]
    public void RawOneByOneTexture2DInstantiations_ShouldBeLimitedToResourceManager()
    {
        var allowedFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "Lib/Resources/ResourceManager.cs",
        };

        var offenders = ReadGameSources()
            .Where(file => !allowedFiles.Contains(file.RelativePath))
            .SelectMany(file => FindRawOneByOneTextureInstantiationLines(
                StripCommentsAndLiteralContents(file.Source))
                .Select(offender => $"{file.RelativePath}:{offender.Line}: {offender.Snippet}"))
            .ToArray();

        Assert.True(offenders.Length == 0,
            "Raw 1x1 Texture2D creation is only allowed in " +
            "Lib/Resources/ResourceManager.cs (textures must flow through " +
            "the shared cached white pixel): " +
            string.Join("; ", offenders));
    }

    [Fact]
    public void StageHandOff_ShouldNotUseUntypedDictionaryOfStringToObject()
    {
        var guardedFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "Lib/Stage/IStageManager.cs",
            "Lib/Stage/StageManager.cs",
            "Lib/Stage/BaseStage.cs",
        };

        var offenders = ReadGameSources()
            .Where(file => guardedFiles.Contains(file.RelativePath))
            .SelectMany(file => FindMatches(
                UntypedStageHandOffRegex,
                StripCommentsAndLiteralContents(file.Source),
                file.RelativePath))
            .ToArray();

        Assert.True(offenders.Length == 0,
            "Stage hand-off must stay typed via StageTransitionPayload; the " +
            "former untyped Dictionary<string, object> must not return in " +
            "IStageManager.cs, StageManager.cs or BaseStage.cs: " +
            string.Join("; ", offenders));
    }

    private static IEnumerable<string> FindMatches(
        Regex pattern,
        string source,
        string relativePath)
    {
        foreach (Match match in pattern.Matches(source))
        {
            var lineNumber = 1 + source.AsSpan(0, match.Index)
                .Count('\n');
            yield return $"{relativePath}:{lineNumber}: {match.Value}";
        }
    }

    private static void AssertNoMatches(
        Regex pattern,
        IReadOnlyList<(string RelativePath, string Source)> sources,
        string reason)
    {
        var offenders = sources
            .SelectMany(file => FindMatches(
                pattern,
                StripCommentsAndLiteralContents(file.Source),
                file.RelativePath))
            .ToArray();

        Assert.True(offenders.Length == 0, reason +
            " Offending sources: " + string.Join("; ", offenders));
    }

    private static IReadOnlyList<(string RelativePath, string Source)> ReadGameSources()
    {
        var gameDirectory = FindGameDirectory();
        return Directory.EnumerateFiles(
                gameDirectory,
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                           && !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Select(path => (RelativePath: Path.GetRelativePath(gameDirectory, path)
                    .Replace(Path.DirectorySeparatorChar, '/'),
                Source: File.ReadAllText(path)))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    // A small string-aware lexical pass: comments and literal contents are
    // blanked to whitespace (delimiters and newlines kept), so comment
    // markers inside literals cannot hide real code, stripped comments
    // cannot merge neighbouring tokens, a "/*" inside a line comment cannot
    // open a block over code, guard-relevant text inside message strings
    // cannot trip the regex guards, and documented intent plus
    // commented-out code stay inert while line numbers stay accurate.
    // Interpolation holes keep their expressions executable (see
    // BlankInterpolatedStringContents), so a hole can neither hide a real
    // violation nor leak its nested literals as apparent code.
    // Known cheap-guard limit: multi-dollar raw interpolated strings
    // ($$"""…""" with doubled-brace holes) are not modelled and their text
    // is partially preserved; no game source uses them.
    [Fact]
    public void StripCommentsAndLiteralContents_ApostropheInDirectiveText_ShouldNotBlankFollowingCode()
    {
        // Non-verbatim literals cannot span lines in C#, so an apostrophe in
        // free-text directives (#region/#error/#warning) must not act as an
        // unterminated char literal that blanks real code below the directive.
        var source = string.Join(
            "\n",
            "#region Don't track this region",
            "var kept = 1;",
            "Debug.WriteLine(\"violation\");",
            "#endregion");

        var stripped = StripCommentsAndLiteralContents(source);

        Assert.Contains("Debug", stripped);
        Assert.Contains("WriteLine", stripped);
    }

    [Fact]
    public void StripCommentsAndLiteralContents_SingleQuoteCharLiteral_ShouldStillBlankLiteralAndRestOfLine()
    {
        // A genuine char literal is still blanked; blanking stays bounded to
        // its own line even when the literal is unterminated on that line.
        var source = string.Join(
            "\n",
            "var literal = 'a';",
            "var kept = 1;");

        var stripped = StripCommentsAndLiteralContents(source);

        Assert.DoesNotContain("'a'", stripped);
        Assert.Contains("var kept = 1;", stripped);
    }

    [Fact]
    public void StripCommentsAndLiteralContents_AtDollarVerbatimString_ShouldNotHideFollowingCode()
    {
        // @$"…" is verbatim like $@"…": a backslash does not escape the
        // closing quote, so the literal must not extend over the statement
        // that follows it.
        var source = string.Join(
            "\n",
            "var path = @$\"C:\\\";",
            "Debug.WriteLine(\"violation\");");

        var stripped = StripCommentsAndLiteralContents(source);

        Assert.Contains("Debug", stripped);
        Assert.Contains("WriteLine", stripped);
    }

    [Fact]
    public void FindRawOneByOneTextureInstantiationLines_PositionalDimensions_ShouldFlagOneByOne()
    {
        var offenders = FindRawOneByOneTextureInstantiationLines(
            "new Texture2D(device, 1, 1);");

        Assert.Single(offenders);
    }

    [Fact]
    public void FindRawOneByOneTextureInstantiationLines_NamedDimensionsInEitherOrder_ShouldFlagOneByOne()
    {
        Assert.Single(FindRawOneByOneTextureInstantiationLines(
            "new Texture2D(device, width: 1, height: 1);"));
        Assert.Single(FindRawOneByOneTextureInstantiationLines(
            "new Texture2D(device, height: 1, width: 1);"));
    }

    [Fact]
    public void FindRawOneByOneTextureInstantiationLines_MixedPositionalAndNamed_ShouldFlagOneByOne()
    {
        var offenders = FindRawOneByOneTextureInstantiationLines(
            "new Texture2D(device, 1, height: 1);");

        Assert.Single(offenders);
    }

    [Fact]
    public void FindRawOneByOneTextureInstantiationLines_NamedDimensionsNotOneByOne_ShouldNotFlag()
    {
        Assert.Empty(FindRawOneByOneTextureInstantiationLines(
            "new Texture2D(device, width: 1, height: 2);"));
        Assert.Empty(FindRawOneByOneTextureInstantiationLines(
            "new Texture2D(device, width: 2, height: 2);"));
    }

    internal static string StripCommentsAndLiteralContents(string source)
    {
        var output = source.ToCharArray();
        LexCode(source, output, 0, trackHoleBraces: false);
        return new string(output);
    }

    // Lexes one code region starting at start: comments and string/char
    // literal contents are blanked to whitespace (delimiters and newlines
    // kept) while executable text is preserved. Inside an interpolation
    // hole (trackHoleBraces) the scan stops at the '}' closing the hole;
    // bare braces (e.g. anonymous types) nest and literals or comments
    // cannot smuggle braces past the count. Returns the index of that
    // closing brace, or source.Length elsewhere/when unterminated.
    private static int LexCode(
        string source,
        char[] output,
        int start,
        bool trackHoleBraces)
    {
        var position = start;
        var holeDepth = trackHoleBraces ? 1 : 0;
        while (position < source.Length)
        {
            var c = source[position];
            if (c == '/' && Peek(source, position + 1) == '/')
            {
                while (position < source.Length && source[position] != '\n')
                    output[position++] = ' ';
            }
            else if (c == '/' && Peek(source, position + 1) == '*')
            {
                output[position++] = ' ';
                output[position++] = ' ';
                while (position < source.Length)
                {
                    var commentChar = source[position];
                    if (commentChar == '*' && Peek(source, position + 1) == '/')
                    {
                        output[position++] = ' ';
                        output[position++] = ' ';
                        break;
                    }

                    output[position++] = commentChar == '\n' ? '\n' : ' ';
                }
            }
            else if (c == '"')
            {
                var quoteRunLength = CountQuoteRun(source, position);
                var interpolated = IsInterpolatedString(source, position);
                if (quoteRunLength >= 3)
                {
                    position = BlankRawStringContents(
                        source, output, position, quoteRunLength, interpolated);
                }
                else if (interpolated)
                {
                    position = BlankInterpolatedStringContents(
                        source, output, position, quoteRunLength, isVerbatim: false);
                }
                else
                {
                    position = BlankQuotedLiteral(
                        source, output, position, quoteRunLength, isVerbatim: false);
                }
            }
            else if (c == '\'')
            {
                position = BlankQuotedLiteral(source, output, position, 1, isVerbatim: false);
            }
            else if (c == '@' && Peek(source, position + 1) == '$' && Peek(source, position + 2) == '"')
            {
                // @$"…" — verbatim interpolated with the reversed prefix order;
                // the quote branch alone would miss the '@' and lose verbatim mode.
                position = BlankInterpolatedStringContents(
                    source, output, position + 2, 1, isVerbatim: true);
            }
            else if (c == '@' && Peek(source, position + 1) == '"')
            {
                position = IsInterpolatedString(source, position + 1)
                    ? BlankInterpolatedStringContents(
                        source, output, position + 1, 1, isVerbatim: true)
                    : BlankQuotedLiteral(source, output, position + 1, 1, isVerbatim: true);
            }
            else if (c == '{' && trackHoleBraces)
            {
                holeDepth++;
                position++;
            }
            else if (c == '}' && trackHoleBraces)
            {
                holeDepth--;
                if (holeDepth == 0)
                    return position;

                position++;
            }
            else
            {
                position++;
            }
        }

        return position;
    }

    // True when the quote at quoteIndex opens an interpolated string, i.e.
    // it is prefixed by "$" ($"…", $@"…", @$"…").
    private static bool IsInterpolatedString(string source, int quoteIndex)
    {
        var i = quoteIndex - 1;
        if (i >= 0 && source[i] == '@')
            i--;

        return i >= 0 && source[i] == '$';
    }

    // Blanks an interpolated string's literal text while keeping each
    // interpolation hole ({expression}) executable: the hole is lexed as
    // code (nested strings, chars and comments inside stay blanked, bare
    // braces from anonymous types nest), so a real violation in a hole is
    // still caught and a nested literal cannot end the outer string scan
    // early and leak its text as apparent code.
    private static int BlankInterpolatedStringContents(
        string source,
        char[] output,
        int openQuote,
        int quoteRunLength,
        bool isVerbatim)
    {
        var position = openQuote + quoteRunLength;
        if (quoteRunLength >= 2)
            return position; // $"" — the run covers the empty literal

        while (position < source.Length)
        {
            var c = source[position];
            if (c == '\\' && !isVerbatim)
            {
                output[position] = ' ';
                if (position + 1 < source.Length)
                    output[position + 1] = ' ';
                position += 2;
                continue;
            }

            if (c == '"')
            {
                if (isVerbatim && Peek(source, position + 1) == '"')
                {
                    output[position++] = ' ';
                    output[position++] = ' ';
                    continue;
                }

                return position + 1; // closing quote
            }

            if (c == '{')
            {
                if (Peek(source, position + 1) == '{')
                {
                    position += 2; // "{{" literal brace escape
                    continue;
                }

                position = LexCode(source, output, position + 1, trackHoleBraces: true) + 1;
                continue;
            }

            if (c == '}' && Peek(source, position + 1) == '}')
            {
                position += 2; // "}}" literal brace escape
                continue;
            }

            output[position++] = c == '\n' ? '\n' : ' ';
        }

        return position; // unterminated literal: blanked to end of file
    }

    internal static IReadOnlyList<(int Line, string Snippet)> FindRawOneByOneTextureInstantiationLines(
        string source)
    {
        var offenders = new List<(int Line, string Snippet)>();
        foreach (Match match in Texture2DNewRegex.Matches(source))
        {
            if (!HasOneByOneDimensions(source, match.Index + match.Length - 1))
                continue;

            var lineNumber = 1 + source.AsSpan(0, match.Index).Count('\n');
            offenders.Add((lineNumber, "new Texture2D(..., 1, 1)"));
        }

        return offenders;
    }

    // Parses the full constructor argument list (balanced parens, top-level
    // commas) so the 1x1 check is anchored to the width/height argument
    // positions and requires a real argument boundary: "…, 1, 128)" or
    // "…, 11, 1)" never match, and "…, 1, 1, false, format" does. Runs on
    // stripped source, so literals cannot smuggle parens or commas.
    private static bool HasOneByOneDimensions(string source, int openParen)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        var depth = 1;
        for (var position = openParen + 1; position < source.Length; position++)
        {
            var c = source[position];
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                if (--depth == 0)
                {
                    arguments.Add(current.ToString());
                    if (arguments.Count < 3)
                        return false;

                    // Positional arguments fix width/height to slots 1 and 2;
                    // named arguments ("width: 1") name their target and may
                    // appear in either order or past unrelated named arguments.
                    string? width = null, height = null;
                    for (var i = 1; i < arguments.Count; i++)
                    {
                        var (name, value) = SplitArgumentName(arguments[i]);
                        if (name == "width")
                            width = value;
                        else if (name == "height")
                            height = value;
                        else if (name == null)
                        {
                            if (i == 1)
                                width = value;
                            else if (i == 2)
                                height = value;
                        }
                    }

                    return width == "1" && height == "1";
                }
            }
            else if (c == ',' && depth == 1)
            {
                arguments.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        return false; // unterminated constructor call
    }

    // Splits a "name: value" named argument into its parts; a positional
    // argument yields Name == null. "::" (alias qualifier) and ternary ":"
    // are not name prefixes.
    private static (string? Name, string Value) SplitArgumentName(string argument)
    {
        var trimmed = argument.Trim();
        var colonIndex = trimmed.IndexOf(':');
        if (colonIndex <= 0
            || colonIndex + 1 >= trimmed.Length
            || trimmed[colonIndex + 1] == ':')
        {
            return (null, trimmed);
        }

        var candidate = trimmed.Substring(0, colonIndex).TrimEnd();
        if (candidate.Length == 0
            || !(char.IsLetter(candidate[0]) || candidate[0] == '_')
            || candidate.Skip(1).Any(c => !(char.IsLetterOrDigit(c) || c == '_')))
        {
            return (null, trimmed);
        }

        return (candidate, trimmed.Substring(colonIndex + 1).Trim());
    }

    private static char Peek(string source, int index) =>
        index < source.Length ? source[index] : '\0';

    private static int CountQuoteRun(string source, int start)
    {
        var end = start;
        while (end < source.Length && source[end] == '"')
            end++;
        return end - start;
    }

    // Blanks the contents between the opening quote at openQuote and its
    // closing delimiter, keeping the delimiters themselves ("\n" preserved).
    private static int BlankQuotedLiteral(
        string source,
        char[] output,
        int openQuote,
        int quoteRunLength,
        bool isVerbatim)
    {
        var closing = source[openQuote];
        var position = openQuote + quoteRunLength;
        if (quoteRunLength >= 2)
            return position; // "" empty literal: the run covered both delimiters

        while (position < source.Length)
        {
            var c = source[position];
            if (c == '\\' && !isVerbatim)
            {
                output[position] = ' ';
                if (position + 1 < source.Length)
                    output[position + 1] = ' ';
                position += 2;
                continue;
            }

            if (c == closing)
            {
                if (isVerbatim && Peek(source, position + 1) == '"')
                {
                    output[position++] = ' ';
                    output[position++] = ' ';
                    continue;
                }

                return position + 1;
            }

            if (c == '\n' && !isVerbatim)
            {
                // C# non-verbatim literals cannot span lines, so an apostrophe
                // in #region/#error/#warning directive text must not act as an
                // unterminated char literal blanking the rest of the file.
                return position;
            }

            output[position++] = c == '\n' ? '\n' : ' ';
        }

        return position; // unterminated literal: blanked to end of file
    }

    // Blanks the contents of a raw string whose delimiter is quoteRunLength
    // quotes; the opening run is already copied through, the closing run
    // (first quote run of at least that length) is kept verbatim.
    // Interpolated raw strings keep their holes executable via LexCode.
    private static int BlankRawStringContents(
        string source,
        char[] output,
        int openQuote,
        int quoteRunLength,
        bool interpolated)
    {
        var position = openQuote + quoteRunLength;
        while (position < source.Length)
        {
            if (source[position] == '"')
            {
                var runEnd = CountQuoteRun(source, position);
                if (runEnd >= quoteRunLength)
                    return position + runEnd;

                for (var i = position; i < position + runEnd; i++)
                    output[i] = ' ';
                position += runEnd;
                continue;
            }

            if (interpolated && source[position] == '{')
            {
                if (Peek(source, position + 1) == '{')
                {
                    position += 2; // "{{" literal brace escape
                    continue;
                }

                position = LexCode(source, output, position + 1, trackHoleBraces: true) + 1;
                continue;
            }

            if (interpolated && source[position] == '}' && Peek(source, position + 1) == '}')
            {
                position += 2; // "}}" literal brace escape
                continue;
            }

            output[position] = source[position] == '\n' ? '\n' : ' ';
            position++;
        }

        return position; // unterminated raw string: blanked to end of file
    }

    private static string FindGameDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "DTXMania.Game");
            if (Directory.Exists(candidate))
                return candidate;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the DTXMania.Game source directory.");
    }
}
