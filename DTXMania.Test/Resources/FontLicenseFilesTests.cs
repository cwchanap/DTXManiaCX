using System;
using System.IO;
using Xunit;

namespace DTXMania.Test.Resources
{
    [Trait("Category", "Unit")]
    public class FontLicenseFilesTests
    {
        // The SIL Open Font License requires every redistributed copy to
        // include the copyright notice and license text. These tests verify
        // the source files exist and the .csproj files declare them for
        // output copying so they ship in every artifact.

        private static string FindRepoRoot()
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

        private static string ContentDir => Path.Combine(FindRepoRoot(), "DTXMania.Game", "Content");

        [Theory]
        [InlineData("Orbitron-OFL.txt")]
        [InlineData("ShareTechMono-OFL.txt")]
        public void OfLLicenseFile_ShouldExistInContentDirectory(string fileName)
        {
            var path = Path.Combine(ContentDir, fileName);
            Assert.True(File.Exists(path),
                $"OFL license file missing: {path}. The SIL Open Font License requires " +
                "the license text to accompany every redistribution.");
        }

        [Theory]
        [InlineData("DTXMania.Game.Windows.csproj")]
        [InlineData("DTXMania.Game.Mac.csproj")]
        public void Csproj_ShouldDeclareOfLLicenseFilesForOutputCopying(string csprojName)
        {
            var csprojPath = Path.Combine(FindRepoRoot(), "DTXMania.Game", csprojName);
            Assert.True(File.Exists(csprojPath), $"Missing: {csprojPath}");

            var content = File.ReadAllText(csprojPath);

            Assert.Contains("Orbitron-OFL.txt", content);
            Assert.Contains("ShareTechMono-OFL.txt", content);
            Assert.Contains("Licenses\\Orbitron-OFL.txt", content);
            Assert.Contains("Licenses\\ShareTechMono-OFL.txt", content);
            Assert.Contains("CopyToOutputDirectory", content);
        }
    }
}
