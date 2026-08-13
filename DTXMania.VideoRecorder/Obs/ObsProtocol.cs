using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DTXMania.VideoRecorder.Obs;

internal sealed record ObsHello(int RpcVersion, string? Salt, string? Challenge);

internal sealed record ObsRequestResponse(
    string RequestType,
    string RequestId,
    bool Succeeded,
    int Code,
    string? Comment,
    JsonElement ResponseData);

/// <summary>
/// The small OBS WebSocket v5 protocol surface used by the recorder.
/// This type deliberately has no socket, file-system, or OBS scene knowledge.
/// </summary>
internal static class ObsProtocol
{
    private const int IdentifyOpCode = 1;
    private const int RequestOpCode = 6;
    internal const int HelloOpCode = 0;
    internal const int IdentifiedOpCode = 2;
    internal const int EventOpCode = 5;
    internal const int RequestResponseOpCode = 7;

    internal static string ComputeAuthentication(string password, string salt, string challenge)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(challenge);

        var secret = Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    internal static ObsHello ParseHello(string message)
    {
        using var document = ParseDocument(message, "Hello");
        var root = document.RootElement;
        RequireOpCode(root, HelloOpCode, "Hello");

        var data = RequireObject(root, "d", "Hello data");
        var rpcVersion = RequireInt32(data, "rpcVersion", "Hello rpcVersion");
        string? salt = null;
        string? challenge = null;
        if (data.TryGetProperty("authentication", out var authentication))
        {
            if (authentication.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("OBS Hello authentication must be an object.");

            salt = RequireString(authentication, "salt", "Hello authentication salt");
            challenge = RequireString(authentication, "challenge", "Hello authentication challenge");
        }

        return new ObsHello(rpcVersion, salt, challenge);
    }

    internal static string BuildIdentifyRequest(
        string password,
        string? salt,
        string? challenge,
        int rpcVersion)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (rpcVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(rpcVersion));

        var data = new Dictionary<string, object?>
        {
            ["rpcVersion"] = rpcVersion
        };

        if (salt is not null || challenge is not null)
        {
            if (string.IsNullOrEmpty(salt) || string.IsNullOrEmpty(challenge))
            {
                throw new InvalidOperationException(
                    "OBS Hello authentication must include both salt and challenge.");
            }

            data["authentication"] = ComputeAuthentication(password, salt, challenge);
        }

        return JsonSerializer.Serialize(new
        {
            op = IdentifyOpCode,
            d = data
        });
    }

    internal static void EnsureIdentified(string message)
    {
        using var document = ParseDocument(message, "Identified");
        RequireOpCode(document.RootElement, IdentifiedOpCode, "Identified");
    }

    internal static string BuildRequest(
        string requestType,
        string requestId,
        object? requestData = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestType);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        return JsonSerializer.Serialize(new
        {
            op = RequestOpCode,
            d = new
            {
                requestType,
                requestId,
                requestData = requestData ?? new Dictionary<string, object?>()
            }
        });
    }

    internal static ObsRecordStatus ParseRecordStatus(string message)
    {
        var response = ParseSuccessfulResponse(message, "GetRecordStatus");
        if (response.ResponseData.ValueKind != JsonValueKind.Object ||
            !response.ResponseData.TryGetProperty("outputActive", out var outputActive) ||
            outputActive.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidOperationException(
                "OBS GetRecordStatus response did not contain boolean responseData.outputActive.");
        }

        return new ObsRecordStatus(outputActive.GetBoolean());
    }

    internal static string ParseStopRecordOutputPath(string message)
    {
        var response = ParseSuccessfulResponse(message, "StopRecord");
        if (response.ResponseData.ValueKind != JsonValueKind.Object ||
            !response.ResponseData.TryGetProperty("outputPath", out var outputPath) ||
            outputPath.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(outputPath.GetString()))
        {
            throw new InvalidOperationException(
                "OBS StopRecord response did not contain required responseData.outputPath.");
        }

        return outputPath.GetString()!;
    }

    internal static void EnsureRequestSucceeded(string message, string expectedRequestType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRequestType);
        _ = ParseSuccessfulResponse(message, expectedRequestType);
    }

    internal static bool TryGetOpCode(string message, out int opCode)
    {
        opCode = -1;
        if (string.IsNullOrWhiteSpace(message))
            return false;

        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("op", out var op) &&
                op.ValueKind == JsonValueKind.Number &&
                op.TryGetInt32(out opCode);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static ObsRequestResponse ParseRequestResponse(string message)
    {
        using var document = ParseDocument(message, "request response");
        var root = document.RootElement;
        RequireOpCode(root, RequestResponseOpCode, "request response");

        var data = RequireObject(root, "d", "request response data");
        var requestType = RequireString(data, "requestType", "request response requestType");
        var requestId = RequireString(data, "requestId", "request response requestId");
        var requestStatus = RequireObject(data, "requestStatus", "request response requestStatus");
        var result = RequireBoolean(requestStatus, "result", "request response result");
        var code = RequireInt32(requestStatus, "code", "request response code");
        string? comment = null;
        if (requestStatus.TryGetProperty("comment", out var commentElement))
        {
            if (commentElement.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException("OBS request response comment must be a string.");
            comment = commentElement.GetString();
        }

        var responseData = data.TryGetProperty("responseData", out var responseDataElement)
            ? responseDataElement.Clone()
            : default;
        return new ObsRequestResponse(requestType, requestId, result, code, comment, responseData);
    }

    private static ObsRequestResponse ParseSuccessfulResponse(string message, string expectedRequestType)
    {
        var response = ParseRequestResponse(message);
        if (!response.RequestType.Equals(expectedRequestType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"OBS response requestType was '{response.RequestType}', expected '{expectedRequestType}'.");
        }

        if (!response.Succeeded)
        {
            var comment = string.IsNullOrWhiteSpace(response.Comment)
                ? "no comment provided"
                : response.Comment;
            throw new InvalidOperationException(
                $"OBS {expectedRequestType} failed (code {response.Code}): {comment}");
        }

        return response;
    }

    private static JsonDocument ParseDocument(string message, string kind)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new InvalidOperationException($"OBS {kind} response was empty.");

        try
        {
            return JsonDocument.Parse(message);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"OBS {kind} response was malformed JSON.", exception);
        }
    }

    private static void RequireOpCode(JsonElement root, int expected, string kind)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("op", out var op) ||
            op.ValueKind != JsonValueKind.Number ||
            !op.TryGetInt32(out var actual) ||
            actual != expected)
        {
            throw new InvalidOperationException(
                $"OBS message was not a {kind} response (expected op {expected}).");
        }
    }

    private static JsonElement RequireObject(JsonElement parent, string name, string description)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"OBS {description} was missing object '{name}'.");
        return value;
    }

    private static string RequireString(JsonElement parent, string name, string description)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidOperationException($"OBS {description} was missing string '{name}'.");
        }

        return value.GetString()!;
    }

    private static int RequireInt32(JsonElement parent, string name, string description)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var result))
        {
            throw new InvalidOperationException($"OBS {description} was missing integer '{name}'.");
        }

        return result;
    }

    private static bool RequireBoolean(JsonElement parent, string name, string description)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidOperationException($"OBS {description} was missing boolean '{name}'.");
        }

        return value.GetBoolean();
    }
}
