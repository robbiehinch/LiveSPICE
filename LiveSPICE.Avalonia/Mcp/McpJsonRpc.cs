using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LiveSPICE.Avalonia.Mcp
{
    /// <summary>
    /// Minimal JSON-RPC 2.0 + MCP framing types. We hand-roll instead of pulling in the
    /// ModelContextProtocol SDK so the Avalonia app stays free of an ASP.NET Core
    /// dependency — the wire format is small and stable enough that this is cheaper to
    /// maintain than a heavier hosting stack.
    /// </summary>
    public sealed class JsonRpcRequest
    {
        [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";
        [JsonPropertyName("id")] public JsonNode Id { get; set; }
        [JsonPropertyName("method")] public string Method { get; set; }
        [JsonPropertyName("params")] public JsonNode Params { get; set; }
    }

    public sealed class JsonRpcResponse
    {
        [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";
        [JsonPropertyName("id")] public JsonNode Id { get; set; }
        [JsonPropertyName("result"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public JsonNode Result { get; set; }
        [JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public JsonRpcError Error { get; set; }

        public static JsonRpcResponse Ok(JsonNode id, JsonNode result) =>
            new JsonRpcResponse { Id = id, Result = result };
        public static JsonRpcResponse Fail(JsonNode id, int code, string message) =>
            new JsonRpcResponse { Id = id, Error = new JsonRpcError { Code = code, Message = message } };
    }

    public sealed class JsonRpcError
    {
        [JsonPropertyName("code")] public int Code { get; set; }
        [JsonPropertyName("message")] public string Message { get; set; }
        [JsonPropertyName("data"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public JsonNode Data { get; set; }
    }

    public static class JsonRpcCodes
    {
        public const int ParseError = -32700;
        public const int InvalidRequest = -32600;
        public const int MethodNotFound = -32601;
        public const int InvalidParams = -32602;
        public const int InternalError = -32603;
    }

    /// <summary>MCP <c>tools/call</c> result envelope.</summary>
    public static class McpResult
    {
        public static JsonNode TextContent(string text, bool isError = false)
        {
            JsonArray content = new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = text },
            };
            return new JsonObject { ["content"] = content, ["isError"] = isError };
        }

        public static JsonNode StructuredContent(JsonNode payload, bool isError = false)
        {
            string text = payload?.ToJsonString() ?? "null";
            JsonArray content = new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = text },
            };
            JsonObject envelope = new JsonObject { ["content"] = content, ["isError"] = isError };
            if (payload != null) envelope["structuredContent"] = payload.DeepClone();
            return envelope;
        }
    }

    internal static class JsonRpcSerialization
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };
    }
}
