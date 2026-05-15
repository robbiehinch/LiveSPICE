using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace LiveSPICE.Avalonia.Mcp
{
    /// <summary>
    /// Tiny HTTP host that speaks the MCP (Streamable HTTP) transport: a single POST
    /// endpoint accepting JSON-RPC 2.0 requests and replying with JSON-RPC responses.
    /// Listens on <c>http://localhost:&lt;port&gt;/mcp</c>. No auth — bind is strictly
    /// localhost so only processes on this machine can reach it.
    /// </summary>
    public sealed class McpServer : IDisposable
    {
        private readonly McpToolRegistry tools;
        private readonly int port;
        private readonly Action<string> log;
        private HttpListener listener;
        private CancellationTokenSource cts;
        private Task loopTask;

        public McpServer(McpToolRegistry tools, int port, Action<string> log = null)
        {
            this.tools = tools ?? throw new ArgumentNullException(nameof(tools));
            this.port = port;
            this.log = log ?? (_ => { });
        }

        public string Endpoint => "http://localhost:" + port + "/mcp";
        public bool IsRunning => listener != null && listener.IsListening;

        public void Start()
        {
            if (IsRunning) return;
            listener = new HttpListener();
            // localhost-only — avoids URL-ACL prompts on Windows and prevents LAN access.
            listener.Prefixes.Add("http://localhost:" + port + "/");
            listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
            try
            {
                listener.Start();
            }
            catch (HttpListenerException ex)
            {
                log("MCP listener failed on port " + port + ": " + ex.Message);
                listener = null;
                return;
            }
            cts = new CancellationTokenSource();
            loopTask = Task.Run(() => AcceptLoopAsync(cts.Token));
            log("MCP server listening at " + Endpoint);
        }

        public void Stop()
        {
            try { cts?.Cancel(); } catch { }
            try { listener?.Stop(); } catch { }
            listener = null;
        }

        public void Dispose() => Stop();

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && listener != null && listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await listener.GetContextAsync();
                }
                catch
                {
                    return; // listener shut down
                }
                _ = Task.Run(() => HandleAsync(ctx));
            }
        }

        private async Task HandleAsync(HttpListenerContext ctx)
        {
            try
            {
                // CORS pre-flight: harmless for localhost, helps when Claude Code or a
                // test browser tab calls in.
                if (ctx.Request.HttpMethod == "OPTIONS")
                {
                    ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
                    ctx.Response.AddHeader("Access-Control-Allow-Methods", "POST,OPTIONS");
                    ctx.Response.AddHeader("Access-Control-Allow-Headers", "*");
                    ctx.Response.StatusCode = 204;
                    ctx.Response.Close();
                    return;
                }

                if (ctx.Request.Url.AbsolutePath != "/mcp" || ctx.Request.HttpMethod != "POST")
                {
                    ctx.Response.StatusCode = 404;
                    await WriteBody(ctx.Response, "Not found");
                    return;
                }

                string body;
                using (StreamReader sr = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8))
                    body = await sr.ReadToEndAsync();

                JsonRpcResponse response;
                JsonRpcRequest request = null;
                try
                {
                    request = JsonSerializer.Deserialize<JsonRpcRequest>(body, JsonRpcSerialization.Options);
                }
                catch (JsonException ex)
                {
                    response = JsonRpcResponse.Fail(null, JsonRpcCodes.ParseError, "Parse error: " + ex.Message);
                    await WriteJsonRpc(ctx.Response, response);
                    return;
                }

                if (request == null || string.IsNullOrEmpty(request.Method))
                {
                    response = JsonRpcResponse.Fail(request?.Id, JsonRpcCodes.InvalidRequest, "Missing method");
                    await WriteJsonRpc(ctx.Response, response);
                    return;
                }

                // Notifications (no id) get an empty 204; we still process them.
                bool isNotification = request.Id == null;

                response = await DispatchAsync(request);

                if (isNotification)
                {
                    ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
                    ctx.Response.StatusCode = 204;
                    ctx.Response.Close();
                    return;
                }

                await WriteJsonRpc(ctx.Response, response);
            }
            catch (Exception ex)
            {
                log("MCP request failed: " + ex);
                try
                {
                    ctx.Response.StatusCode = 500;
                    await WriteBody(ctx.Response, ex.Message);
                }
                catch { }
            }
        }

        private async Task<JsonRpcResponse> DispatchAsync(JsonRpcRequest request)
        {
            try
            {
                switch (request.Method)
                {
                    case "initialize":
                        return JsonRpcResponse.Ok(request.Id, BuildInitializeResult());
                    case "notifications/initialized":
                        return JsonRpcResponse.Ok(request.Id, null);
                    case "tools/list":
                        return JsonRpcResponse.Ok(request.Id, new JsonObject { ["tools"] = tools.ListWireFormat() });
                    case "tools/call":
                        return await HandleToolCallAsync(request);
                    case "ping":
                        return JsonRpcResponse.Ok(request.Id, new JsonObject());
                    default:
                        return JsonRpcResponse.Fail(request.Id, JsonRpcCodes.MethodNotFound, "Unknown method: " + request.Method);
                }
            }
            catch (Exception ex)
            {
                return JsonRpcResponse.Fail(request.Id, JsonRpcCodes.InternalError, ex.Message);
            }
        }

        private async Task<JsonRpcResponse> HandleToolCallAsync(JsonRpcRequest request)
        {
            JsonNode paramsNode = request.Params;
            string name = paramsNode?["name"]?.GetValue<string>();
            JsonNode args = paramsNode?["arguments"];
            if (string.IsNullOrEmpty(name))
                return JsonRpcResponse.Fail(request.Id, JsonRpcCodes.InvalidParams, "Tool call missing 'name'.");
            if (!tools.TryGet(name, out McpToolDefinition def))
                return JsonRpcResponse.Fail(request.Id, JsonRpcCodes.MethodNotFound, "Unknown tool: " + name);

            try
            {
                JsonNode result = await def.Handler(args ?? new JsonObject());
                return JsonRpcResponse.Ok(request.Id, result);
            }
            catch (Exception ex)
            {
                log("Tool '" + name + "' threw: " + ex);
                return JsonRpcResponse.Ok(request.Id, McpResult.TextContent("Tool error: " + ex.Message, isError: true));
            }
        }

        private static JsonNode BuildInitializeResult() => new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject { ["listChanged"] = false },
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "livespice",
                ["version"] = "1.0.0",
            },
        };

        private static async Task WriteJsonRpc(HttpListenerResponse response, JsonRpcResponse rpc)
        {
            string body = JsonSerializer.Serialize(rpc, JsonRpcSerialization.Options);
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.ContentType = "application/json";
            response.StatusCode = 200;
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            response.Close();
        }

        private static async Task WriteBody(HttpListenerResponse response, string body)
        {
            response.ContentType = "text/plain";
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            response.Close();
        }
    }
}
