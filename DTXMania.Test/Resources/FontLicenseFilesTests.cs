using System.IO;
using DTXMania.Test.TestData;
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

        private static string ContentDir => RepoPaths.GameContentDir;

        [Theory]
        [InlineData("Orbitron-OFL.txt")]
        [InlineData("ShareTechMono-OFL.txt")]
        [InlineData("Bravura-OFL.txt")]
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
            var csprojPath = Path.Combine(RepoPaths.FindRepoRoot(), "DTXMania.Game", csprojName);
            Assert.True(File.Exists(csprojPath), $"Missing: {csprojPath}");

            var content = File.ReadAllText(csprojPath);

            Assert.Contains("Orbitron-OFL.txt", content);
            Assert.Contains("ShareTechMono-OFL.txt", content);
            Assert.Contains("Bravura-OFL.txt", content);
            Assert.Contains("Licenses\\Orbitron-OFL.txt", content);
            Assert.Contains("Licenses\\ShareTechMono-OFL.txt", content);
            Assert.Contains("Licenses\\Bravura-OFL.txt", content);
            Assert.Contains("CopyToOutputDirectory", content);
        }
    }
}
