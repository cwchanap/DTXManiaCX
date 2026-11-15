#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace DTXMania.Test.ArchitectureGuards;

// Source-level architecture guards for the HPA-796 refactor (plain English):
// 1. Game production code must not emit ad-hoc diagnostics (Debug.Write*/Print,
//    Console.Write/WriteLine, Trace.Write*/Trace.Trace*); structured logging is
//    the approved channel. Debug.Assert/Trace.Assert and the pre-logger startup
//    Console.Error usage stay exempt.
// 2. Raw 1x1 Texture2D creation is only allowed inside the resource manager so
//    every texture flows through reference counting and skin fallback.
// 3. Stage transitions must keep the typed StageTransitionPayload hand-off; the
//    former untyped Dictionary<string, object> must not return in the stage
//    contracts. The nullable Dictionary<string, object?> crash-breadcrumb
//    telemetry in StageManager.cs is intentionally out of scope (it is not the
//    stage hand-off).
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

    // Raw 1x1 texture creation. The "\s*" gaps cross line breaks, so splitting
    // the call across lines cannot evade the guard.
    private static readonly Regex RawOneByOneTextureRegex = new(
        @"new\s+Texture2D\s*\([^()]*,\s*1\s*,\s*1",
        RegexOptions.Compiled);

    // The untyped stage hand-off that StageTransitionPayload replaced.
    // Matches only the non-nullable form so the crash-breadcrumb
    // Dictionary<string, object?> telemetry stays out of scope.
    private static readonly Regex UntypedStageHandOffRegex = new(
        @"\bDictionary\s*<\s*string\s*,\s*object\s*>",
        RegexOptions.Compiled);

    private static readonly Regex BlockCommentRegex = new(
        @"/\*.*?\*/",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex LineCommentRegex = new(
        @"//[^\r\n]*",
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
            .SelectMany(file => FindMatches(
                RawOneByOneTextureRegex,
                StripComments(file.Source),
                file.RelativePath))
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
                StripComments(file.Source),
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
                StripComments(file.Source),
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

    // Comments are removed so documented intent cannot trip the guards and
    // commented-out code cannot hide from them. Newline counts are preserved
    // so reported line numbers stay accurate.
    private static string StripComments(string source)
    {
        var withoutBlockComments = BlockCommentRegex.Replace(
            source,
            match => new string('\n', match.Value.Count(c => c == '\n')));
        return LineCommentRegex.Replace(withoutBlockComments, string.Empty);
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
