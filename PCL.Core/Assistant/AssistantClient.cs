using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.IO.Net;

namespace PCL.Core.Assistant;

public sealed record AssistantMessage(string Role, string Content);

public sealed class AssistantException(string message, string? detail = null) : Exception(message)
{
    public string? Detail { get; } = detail;
}

/// <summary>
/// OpenAI 兼容的 /chat/completions 客户端，流式读取增量内容。
/// </summary>
public static class AssistantClient
{
    public static async Task<string> ChatAsync(IEnumerable<AssistantMessage> messages, string baseUrl, string model,
        string apiKey, IProgress<string>? onDelta, CancellationToken token)
    {
        if (!Uri.IsWellFormedUriString(baseUrl, UriKind.Absolute))
            throw new AssistantException("服务地址无效", baseUrl);
        if (string.IsNullOrWhiteSpace(model))
            throw new AssistantException("未设置模型名称");

        var body = JsonSerializer.Serialize(new
        {
            model,
            stream = true,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray()
        });

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
            throw new AssistantException($"服务返回 {(int)response.StatusCode} {response.ReasonPhrase}", Trim(text));
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (!contentType.Contains("event-stream", StringComparison.OrdinalIgnoreCase))
            return await ReadWholeResponseAsync(response, token);

        var answer = new StringBuilder();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (!token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(token);
            if (line is null) break;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var data = line[5..].Trim();
            if (data.Length == 0 || data == "[DONE]") continue;

            var delta = ExtractDelta(data);
            if (delta.Length > 0)
            {
                answer.Append(delta);
                onDelta?.Report(delta);
            }
        }

        return answer.ToString();
    }

    private static string ExtractDelta(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, default);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return "";
            var choice = choices[0];
            if (choice.TryGetProperty("delta", out var delta) &&
                delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                return content.GetString() ?? "";
            // 少数实现把整段内容放在 message 里
            if (choice.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var full) && full.ValueKind == JsonValueKind.String)
                return full.GetString() ?? "";
        }
        catch (JsonException)
        {
            // 单行解析失败不中断整个流，交由最终文本判断
        }

        return "";
    }

    private static async Task<string> ReadWholeResponseAsync(HttpResponseMessage response, CancellationToken token)
    {
        var text = await response.Content.ReadAsStringAsync(token);
        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            throw new AssistantException("响应中没有 choices", Trim(text));
        var message = choices[0].TryGetProperty("message", out var m) ? m : default;
        if (message.ValueKind == JsonValueKind.Undefined ||
            !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
            throw new AssistantException("响应中没有内容", Trim(text));
        var answer = content.GetString() ?? "";
        return answer;
    }

    private static string Combine(string baseUrl, string path) =>
        baseUrl.TrimEnd('/') + "/" + path;

    private static string Trim(string text) =>
        string.IsNullOrEmpty(text) ? "" : text.Length > 400 ? text[..400] + "…" : text;
}
