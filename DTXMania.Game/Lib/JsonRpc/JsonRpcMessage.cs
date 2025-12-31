#nullable enable

using System.Text.Json.Serialization;

namespace DTXMania.Game.Lib.JsonRpc;

/// <summary>
/// Base JSON-RPC 2.0 message
/// </summary>
public abstract class JsonRpcMessage
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; }
}

/// <summary>
/// JSON-RPC 2.0 request
/// </summary>
public class JsonRpcRequest : JsonRpcMessage
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public object? Params { get; set; }

    /// <summary>
    /// Check if this is a notification (no id)
    /// </summary>
    [JsonIgnore]
    public bool IsNotification => Id == null;
}

/// <summary>
/// JSON-RPC 2.0 response
/// </summary>
public class JsonRpcResponse : JsonRpcMessage
{
    [JsonPropertyName("result")]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; set; }

    /// <summary>
    /// Check if this response contains an error
    /// </summary>
    [JsonIgnore]
    public bool IsError => Error != null;
}

/// <summary>
/// JSON-RPC 2.0 error object
/// </summary>
public class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

/// <summary>
/// Common JSON-RPC error codes
/// </summary>
public static class JsonRpcErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;

    // Application-specific error codes (start from -32000)
    public const int GameNotRunning = -32001;
    public const int InvalidInput = -32002;
    public const int WindowNotFound = -32003;
}