#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace DTXMania.Test.Config;

[Trait("Category", "Architecture")]
public sealed class DTXPathCompatibilityArchitectureTests
{
    [Fact]
    public void ProductionDTXPathReferences_ShouldBeLimitedToCompatibilityBoundary()
    {
        var gameDirectory = FindGameDirectory();
        var allowedFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "Lib/Config/ConfigData.cs",
            "Lib/Config/ConfigManager.cs",
            "Lib/Stage/ConfigStage.cs",
        };

        var offenders = Directory.EnumerateFiles(
                gameDirectory,
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                           && !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains(
                "DTXPath",
                StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(gameDirectory, path)
                .Replace(Path.DirectorySeparatorChar, '/'))
            .Where(relativePath => !allowedFiles.Contains(relativePath))
            .OrderBy(relativePath => relativePath, StringComparer.Ordinal)
            .ToArray();

        Assert.True(offenders.Length == 0,
            "The following files reference DTXPath outside the compatibility boundary: " +
            string.Join(", ", offenders));
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
