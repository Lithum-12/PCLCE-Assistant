using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace PCL.Core.Assistant;

/// <param name="Question">画在选择栏上方的一句话。</param>
/// <param name="Options">按顺序画成按钮的选项，第一条由界面标成"推荐"。</param>
public sealed record AssistantChoiceSet(string Question, IReadOnlyList<string> Options);

/// <summary>
/// 把 offer_choices 传来的参数收成能画的按钮。这层归一化不能省：选择栏是一排胶囊按钮，
/// 选项太多或某一条写成三行就画不下了。而且模型给 options 的形式并不稳定——有时是 json 数组，
/// 有时是"数组的字符串形式"，有时干脆是换行分隔的文本，所以三种都得能吃；凑不出两条就不当有选择。
/// </summary>
public static class AssistantChoices
{
    public const int MaxOptions = 4;
    public const int MaxOptionChars = 24;
    public const int MaxQuestionChars = 40;

    // 逗号也一起剥：按行兜底切出来的碎片是「"装 JEI",」，两头都得清干净
    private static readonly char[] QuoteWrappers = ['"', '\'', '“', '”', '`', ','];

    private static readonly char[] TextSeparators = ['\n', '\r', ';', '；', '|'];

    public static AssistantChoiceSet? Parse(string? question, string? options)
    {
        var items = Split(options).Select(Clean).Where(s => s.Length > 0)
            .Select(s => s.Length > MaxOptionChars ? s[..MaxOptionChars] + "…" : s)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxOptions)
            .ToList();
        if (items.Count < 2) return null; // 只有一个方案就不该问，直接让它去做

        var title = Clean(question ?? "").Replace('\n', ' ').Replace('\r', ' ');
        if (title.Length > MaxQuestionChars) title = title[..MaxQuestionChars] + "…";
        return new AssistantChoiceSet(title, items);
    }

    private static IEnumerable<string> Split(string? raw)
    {
        var text = (raw ?? "").Trim();
        if (text.Length == 0) return [];

        if (text.Length > 1 && text[0] == '"' && text[^1] == '"')
        {
            // 数组被当成字符串传过来：先解开这一层再按数组读
            try
            {
                text = (JsonSerializer.Deserialize<string>(text) ?? "").Trim();
            }
            catch (JsonException)
            {
                return [];
            }
        }

        if (text.StartsWith('['))
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    return doc.RootElement.EnumerateArray()
                        .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.GetRawText())
                        .ToList();
            }
            catch (JsonException)
            {
                // 数组没闭合（模型写到一半断了）：丢掉外壳再按行切
                text = text[1..];
            }

        return text.Split(TextSeparators, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string Clean(string value) => (value ?? "").Trim().Trim(QuoteWrappers).Trim();
}
