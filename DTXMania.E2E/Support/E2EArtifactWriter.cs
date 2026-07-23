using System.Text;
using System.Text.Json;
using DTXMania.E2E.Fixtures;

namespace DTXMania.E2E.Support;

public static class E2EArtifactWriter
{
    public static async Task WriteTextAsync(E2EFixture fixture, string fileName, string content)
    {
        var path = GetArtifactPath(fixture, fileName);
        await File.WriteAllTextAsync(path, content, Encoding.UTF8);
    }

    public static async Task WriteJsonAsync(E2EFixture fixture, string fileName, object value)
    {
        var path = GetArtifactPath(fixture, fileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, new JsonSerializerOptions { WriteIndented = true });
    }

    public static void CopyFixtureFiles(E2EFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        Directory.CreateDirectory(fixture.ArtifactRoot);
        File.Copy(fixture.ConfigPath, Path.Combine(fixture.ArtifactRoot, "config.ini"), overwrite: true);
        File.Copy(fixture.ChartPath, Path.Combine(fixture.ArtifactRoot, "autoplay-smoke.dtx"), overwrite: true);
        File.Copy(fixture.AudioPath, Path.Combine(fixture.ArtifactRoot, E2EFixtureBuilder.AudioFileName), overwrite: true);
    }

    private static string GetArtifactPath(E2EFixture fixture, string fileName)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        Directory.CreateDirectory(fixture.ArtifactRoot);
        return Path.Combine(fixture.ArtifactRoot, fileName);
    }
}
