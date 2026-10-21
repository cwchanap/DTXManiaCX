using System;
using System.IO;

namespace DTXMania.Test.TestData
{
    /// <summary>
    /// Locates repository files from a test binary running under bin/.
    /// </summary>
    internal static class RepoPaths
    {
        internal static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "DTXMania.Game")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "System")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate repository root from " + AppContext.BaseDirectory + ".");
        }

        internal static string GameContentDir => Path.Combine(FindRepoRoot(), "DTXMania.Game", "Content");

        internal static string TestDataDir => Path.Combine(FindRepoRoot(), "DTXMania.Test", "TestData");
    }
}
