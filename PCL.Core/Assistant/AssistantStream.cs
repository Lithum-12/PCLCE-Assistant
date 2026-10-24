using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PCL.Core.Assistant;

/// <summary>一个可暴露给模型的工具声明。参数统一是字符串型，减少各家接口对 schema 支持的差异。</summary>
public sealed record AssistantToolParam(string Name, string Description);

public sealed record AssistantToolDef(string Name, string Description, IReadOnlyList<AssistantToolParam> Parameters);

public sealed record AssistantToolCall(string Id, string Name, string ArgumentsJson);

/// <param name="ToolsRejected">服务商拒绝了 tools 字段，本次是不带工具重试的结果。</param>
public sealed record AssistantReply(string Content, IReadOnlyList<AssistantToolCall> ToolCalls, string FinishReason,
    bool ToolsRejected = false);

/// <summary>
/// 把 SSE 分片累积成一次完整回复：正文按到达顺序拼接，工具调用按 index 归并。
/// 抽成纯类是为了能拿样例文本直接测，不用真连服务商。
/// </summary>
public sealed class AssistantStreamAccumulator
{
    private readonly StringBuilder _content = new();
    private readonly Dictionary<int, ToolCallSlot> _calls = new();
    private string _finishReason = "";

    /// <summary>处理一行 <c>data:</c> 之后的 JSON 载荷；解析失败就跳过这一片，返回本片新增的正文。</summary>
    public string FeedData(string json) => Feed(json, preferDelta: true);

    /// <summary>处理非流式响应（整段在 message 里），返回新增的正文。</summary>
    public string FeedWhole(string json) => Feed(json, preferDelta: false);

    public AssistantReply Build()
    {
        List<AssistantToolCall> calls = [];
        foreach (var index in _calls.Keys.OrderBy(i => i))
        {
            var slot = _calls[index];
            if (slot.Name.Length == 0 && slot.Id.Length == 0) continue;
            calls.Add(new AssistantToolCall(slot.Id, slot.Name, slot.Arguments.ToString()));
        }

        return new AssistantReply(_content.ToString(), calls, _finishReason);
    }

    private string Feed(string json, bool preferDelta)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, default);
        }
        catch (JsonException)
        {
            return "";
        }

        var before = _content.Length;
        // JsonElement 指向 doc 的缓冲区，必须在 doc 释放前把字符串取出来
        using (doc)
        {
            if (!TryGetFirstChoice(doc.RootElement, out var choice)) return "";
            if (preferDelta && choice.TryGetProperty("delta", out var delta)) ApplyFragment(delta);
            else if (choice.TryGetProperty("message", out var message)) ApplyFragment(message);
            else if (choice.TryGetProperty("delta", out var late)) ApplyFragment(late);
            if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String)
                _finishReason = reason.GetString() ?? "";
        }

        return _content.ToString(before, _content.Length - before);
    }

    private void ApplyFragment(JsonElement fragment)
    {
        if (fragment.ValueKind != JsonValueKind.Object) return;
        AppendContent(fragment);
        if (!fragment.TryGetProperty("tool_calls", out var toolCalls) ||
            toolCalls.ValueKind != JsonValueKind.Array) return;

        var guess = 0;
        foreach (var call in toolCalls.EnumerateArray())
        {
            if (call.ValueKind != JsonValueKind.Object) continue;
            var index = call.TryGetProperty("index", out var i) && i.ValueKind == JsonValueKind.Number
                ? (int)i.GetDouble()
                : guess;
            guess = index + 1;
            var slot = GetOrAdd(index);

            if (call.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                slot.Id = id.GetString() ?? slot.Id;
            if (!call.TryGetProperty("function", out var function) || function.ValueKind != JsonValueKind.Object)
                continue;
            if (function.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                slot.Name = name.GetString() ?? slot.Name;
            if (function.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.String)
            {
                var value = args.GetString();
                if (!string.IsNullOrEmpty(value)) AppendArguments(slot, value);
            }
        }
    }

    private void AppendContent(JsonElement fragment)
    {
        if (!fragment.TryGetProperty("content", out var content)) return;
        switch (content.ValueKind)
        {
            case JsonValueKind.String:
                _content.Append(content.GetString());
                break;
            case JsonValueKind.Array:
                // 兼容层里见过把 content 发成分段数组（Claude 风格）的实现
                foreach (var part in content.EnumerateArray())
                    if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                        _content.Append(text.GetString());
                break;
        }
    }

    private ToolCallSlot GetOrAdd(int index)
    {
        if (!_calls.TryGetValue(index, out var slot)) _calls[index] = slot = new ToolCallSlot();
        return slot;
    }

    private static void AppendArguments(ToolCallSlot slot, string value)
    {
        var current = slot.Arguments.ToString();
        if (current.Length == 0)
        {
            slot.Arguments.Append(value);
            return;
        }

        // 有的服务商每个分片都重发全量参数，重复的整段要挑掉而不是拼成两遍
        if (value == current) return;
        if (value.Length > current.Length && value.StartsWith(current, StringComparison.Ordinal))
        {
            slot.Arguments.Clear();
            slot.Arguments.Append(value);
            return;
        }

        slot.Arguments.Append(value);
    }

    private static bool TryGetFirstChoice(JsonElement root, out JsonElement choice)
    {
        choice = default;
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            return false;
        choice = choices[0];
        return choice.ValueKind == JsonValueKind.Object;
    }

    private sealed class ToolCallSlot
    {
        public string Id = "";
        public string Name = "";
        public readonly StringBuilder Arguments = new();
    }
}
