using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace LiveSPICE.Avalonia.Mcp
{
    /// <summary>
    /// A handler for an MCP tool. Receives the call's <c>arguments</c> JsonNode and returns
    /// the <c>tools/call</c> result content (either a structured payload via
    /// <see cref="McpResult.StructuredContent"/> or a simple text response).
    /// </summary>
    public delegate Task<JsonNode> McpToolHandler(JsonNode arguments);

    public sealed class McpToolDefinition
    {
        public string Name { get; }
        public string Description { get; }
        public JsonNode InputSchema { get; }
        public McpToolHandler Handler { get; }

        public McpToolDefinition(string name, string description, JsonNode inputSchema, McpToolHandler handler)
        {
            Name = name;
            Description = description;
            InputSchema = inputSchema;
            Handler = handler;
        }

        public JsonObject ToWireFormat() => new JsonObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = InputSchema?.DeepClone(),
        };
    }

    public sealed class McpToolRegistry
    {
        private readonly Dictionary<string, McpToolDefinition> tools = new Dictionary<string, McpToolDefinition>();

        public void Register(McpToolDefinition def)
        {
            tools[def.Name] = def ?? throw new ArgumentNullException(nameof(def));
        }

        public bool TryGet(string name, out McpToolDefinition def) => tools.TryGetValue(name, out def);

        public JsonArray ListWireFormat()
        {
            JsonArray array = new JsonArray();
            foreach (McpToolDefinition def in tools.Values) array.Add(def.ToWireFormat());
            return array;
        }
    }
}
