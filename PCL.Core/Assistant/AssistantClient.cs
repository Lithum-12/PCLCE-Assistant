using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.IO.Net;

namespace PCL.Core.Assistant;

public sealed record AssistantMessage(string Role, string Content)
{
    /// <summary>role=="hint" 时的严重级别：info / warn / error。提示只给人看，不进模型上下文。</summary>
    public string Level { get; init; } = "";

    /// <summary>落地时间（Unix 毫秒）。旧数据没有这个字段时为 0，界面上就不显示时间。</summary>
    public long At { get; init; }

    /// <summary>这条回答用了哪些工具的痕迹，只进卡片脚注，不进模型上下文。</summary>
    public IReadOnlyList<string>? Trace { get; init; }

    /// <summary>role=="tool" 时对应的调用 id。</summary>
    public string? ToolCallId { get; init; }

    /// <summary>assistant 回合里模型要求执行的工具调用；下一轮必须带着它一起回传。</summary>
    public IReadOnlyList<AssistantToolCall>? ToolCalls { get; init; }

    public static AssistantMessage Tool(string toolCallId, string content) =>
        new("tool", content) { ToolCallId = toolCallId };

    public static AssistantMessage AssistantWithTools(IReadOnlyList<AssistantToolCall> calls, string content = "") =>
        new("assistant", content) { ToolCalls = calls };
}

public sealed class AssistantException(string message, string? detail = null, int statusCode = 0) : Exception(message)
{
    public string? Detail { get; } = detail;
    public int StatusCode { get; } = statusCode;

    /// <summary>4xx 一般是请求本身不被接受，可以用来判断"这家不支持 tools"。</summary>
    public bool IsRequestRejected => StatusCode is >= 400 and < 500;
}

/// <summary>
/// OpenAI 兼容的 /chat/completions 客户端，流式读取增量内容，并支持 function calling。
/// </summary>
public static class AssistantClient
{
    /// <summary>
    /// 用户常把 https:// 省掉，这里统一补齐并去掉尾部斜杠。
    /// </summary>
    public static string NormalizeBaseUrl(string? raw)
    {
        var text = (raw ?? "").Trim();
        if (text.Length == 0) return "";
        if (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            text = "https://" + text;
        return text.TrimEnd('/');
    }

    public static async Task<AssistantReply> SendAsync(IEnumerable<AssistantMessage> messages, string baseUrl,
        string model, string apiKey, IReadOnlyList<AssistantToolDef>? tools, IProgress<string>? onDelta,
        CancellationToken token)
    {
        baseUrl = NormalizeBaseUrl(baseUrl);
        if (!Uri.IsWellFormedUriString(baseUrl, UriKind.Absolute))
            throw new AssistantException("服务地址无效", baseUrl);
        if (string.IsNullOrWhiteSpace(model))
            throw new AssistantException("未设置模型名称");

        var history = messages.ToList();
        try
        {
            return await SendOnceAsync(history, baseUrl, model, apiKey, tools, onDelta, token);
        }
        catch (AssistantException e) when (tools is { Count: > 0 } && e.IsRequestRejected)
        {
            // 国产兼容层对 tools 的支持参差不齐：4xx 说明请求本身不被接受，
            // 此时还没有任何增量吐出来，不带工具重试一次，让对话至少还能继续
            var fallback = await SendOnceAsync(history, baseUrl, model, apiKey, null, onDelta, token);
            return fallback with { ToolsRejected = true };
        }
    }

    private static async Task<AssistantReply> SendOnceAsync(IReadOnlyList<AssistantMessage> messages, string baseUrl,
        string model, string apiKey, IReadOnlyList<AssistantToolDef>? tools, IProgress<string>? onDelta,
        CancellationToken token)
    {
        var body = BuildBody(model, messages, tools);
        using var request = new HttpRequestMessage(HttpMethod.Post, Combine(baseUrl, "chat/completions"))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);

        var client = NetworkService.GetClient();
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(token);
            throw new AssistantException($"服务返回 {(int)response.StatusCode} {response.ReasonPhrase}", Trim(text),
                (int)response.StatusCode);
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        var accumulator = new AssistantStreamAccumulator();
        if (!contentType.Contains("event-stream", StringComparison.OrdinalIgnoreCase))
        {
            var raw = await response.Content.ReadAsStringAsync(token);
            var text = accumulator.FeedWhole(raw);
            if (text.Length > 0) onDelta?.Report(text);
            var whole = accumulator.Build();
            if (whole.Content.Length == 0 && whole.ToolCalls.Count == 0)
                throw new AssistantException("响应中没有内容", Trim(raw));
            return whole;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (!token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(token);
            if (line is null) break;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var data = line[5..].Trim();
            if (data.Length == 0 || data == "[DONE]") continue;

            var delta = accumulator.FeedData(data);
            if (delta.Length > 0) onDelta?.Report(delta);
        }

        var reply = accumulator.Build();
        if (reply.Content.Length == 0 && reply.ToolCalls.Count == 0)
            throw new AssistantException("流式响应没有返回内容");
        return reply;
    }

    internal static string BuildBody(string model, IEnumerable<AssistantMessage> messages,
        IReadOnlyList<AssistantToolDef>? tools)
    {
        var root = new JsonObject
        {
            ["model"] = model,
            ["stream"] = true
        };

        var history = new JsonArray();
        foreach (var message in messages) history.Add(ToJson(message));
        root["messages"] = history;

        if (tools is not { Count: > 0 }) return root.ToJsonString();

        var list = new JsonArray();
        foreach (var tool in tools)
        {
            var properties = new JsonObject();
            var required = new JsonArray();
            foreach (var parameter in tool.Parameters)
            {
                properties[parameter.Name] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = parameter.Description
                };
                required.Add(JsonValue.Create(parameter.Name));
            }

            list.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = properties,
                        ["required"] = required
                    }
                }
            });
        }

        root["tools"] = list;
        root["tool_choice"] = "auto";
        return root.ToJsonString();
    }

    internal static JsonObject ToJson(AssistantMessage message)
    {
        var obj = new JsonObject { ["role"] = message.Role };
        if (message.Role == "tool")
        {
            obj["tool_call_id"] = message.ToolCallId ?? "";
            obj["content"] = message.Content;
            return obj;
        }

        if (message.ToolCalls is { Count: > 0 })
        {
            var calls = new JsonArray();
            foreach (var call in message.ToolCalls)
                calls.Add(new JsonObject
                {
                    ["id"] = call.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = call.Name,
                        // 空参数也必须是合法 json，否则部分服务商直接 400
                        ["arguments"] = string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson
                    }
                });
            obj["tool_calls"] = calls;
        }

        // 用空串而不是 null：各家兼容层对 null content 的容忍度不一致
        obj["content"] = message.Content;
        return obj;
    }

    private static string Combine(string baseUrl, string path) => baseUrl.TrimEnd('/') + "/" + path;

    private static string Trim(string text) =>
        string.IsNullOrEmpty(text) ? "" : text.Length > 400 ? text[..400] + "…" : text;
}
