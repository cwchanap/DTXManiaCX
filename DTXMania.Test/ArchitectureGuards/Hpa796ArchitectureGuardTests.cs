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
    // Known cheap-guard limit: combined interpolated+verbatim prefixes
    // (@$"…" / $@"…") are lexed as plain strings, and interpolated holes
    // containing string literals close the literal early; worst case is a
    // false negative, never a false positive.
    internal static string StripCommentsAndLiteralContents(string source)
    {
        var output = source.ToCharArray();
        var position = 0;
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
                position = quoteRunLength >= 3
                    ? BlankRawStringContents(source, output, position, quoteRunLength)
                    : BlankQuotedLiteral(source, output, position, isVerbatim: false);
            }
            else if (c == '\'')
            {
                position = BlankQuotedLiteral(source, output, position, isVerbatim: false);
            }
            else if (c == '@' && Peek(source, position + 1) == '"')
            {
                position = BlankQuotedLiteral(source, output, position + 1, isVerbatim: true);
            }
            else
            {
                position++;
            }
        }

        return new string(output);
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
                    return arguments.Count >= 3
                        && arguments[1].Trim() == "1"
                        && arguments[2].Trim() == "1";
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
        bool isVerbatim)
    {
        var closing = source[openQuote];
        var position = openQuote + 1;
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

            output[position++] = c == '\n' ? '\n' : ' ';
        }

        return position; // unterminated literal: blanked to end of file
    }

    // Blanks the contents of a raw string whose delimiter is quoteRunLength
    // quotes; the opening run is already copied through, the closing run
    // (first quote run of at least that length) is kept verbatim.
    private static int BlankRawStringContents(
        string source,
        char[] output,
        int openQuote,
        int quoteRunLength)
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
