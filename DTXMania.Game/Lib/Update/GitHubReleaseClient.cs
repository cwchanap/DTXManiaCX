#nullable enable

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;

namespace DTXMania.Game.Lib.Update;

/// <summary>
/// Thrown when the GitHub API answers with a non-success status code. The
/// status code is the only detail carried — never a response body.
/// </summary>
internal sealed class GameUpdateHttpException : Exception
{
    internal int StatusCode { get; }

    internal GameUpdateHttpException(int statusCode)
        : base($"GitHub API returned HTTP {statusCode}")
    {
        StatusCode = statusCode;
    }
}

internal sealed class GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }

    [JsonPropertyName("digest")]
    public string? Digest { get; set; }
}

internal sealed class GitHubReleaseInfo
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAsset> Assets { get; set; } = new();
}

/// <summary>
/// Fetches the latest GitHub release for the repository crash reporting already
/// targets. Repository identity comes only from
/// <see cref="GitHubCrashIssueBuilder.TargetOwner"/>/<see cref="GitHubCrashIssueBuilder.TargetRepository"/>.
/// </summary>
internal sealed class GitHubReleaseClient
{
    private static string LatestReleaseUrl { get; } =
        $"https://api.github.com/repos/{GitHubCrashIssueBuilder.TargetOwner}/{GitHubCrashIssueBuilder.TargetRepository}/releases/latest";

    private readonly HttpClient _httpClient;

    public GitHubReleaseClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            // GitHub API rejects requests without a User-Agent.
            _httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("DTXManiaCX", ApplicationVersion.Display));
        }
    }

    public async Task<GitHubReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new GameUpdateHttpException((int)response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<GitHubReleaseInfo>(cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new JsonException("Release body was null");
    }
}
