using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Core.Assistant;

/// <summary>
/// 左侧列表那条截断的用户原文换成一句短标题。问的是对话同一个服务商、同一个模型，
/// 一次对话只问一回；问不到就保留 AppendUserMessage 里截出来的原标题。
/// </summary>
public static class AssistantTitles
{
    public const int MaxChars = 24;

    // 模型很爱给标题套一层引号或书名号，两端这些字符一律剥掉（char[] 比在正则里再套引号好读）
    private static readonly char[] Wrappers = " \t\r'\"“”‘’「」『』《》〈〉【】[]()（）".ToCharArray();

    private static readonly Regex PrefixRegex = new("^(标题|题目|Title)\\s*[:：]\\s*",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex WhitespaceRegex = new("\\s{2,}", RegexOptions.CultureInvariant);

    /// <param name="instruction">本地化好的改写要求，由界面层传进来（Core 不碰 Lang）。</param>
    public static async Task<string?> GenerateAsync(string instruction, string userRequest, string baseUrl,
        string model, string apiKey, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(userRequest)) return null;

        List<AssistantMessage> messages =
        [
            new("system", instruction),
            new("user", userRequest.Length > 600 ? userRequest[..600] : userRequest)
        ];
        var reply = await AssistantClient.SendAsync(messages, baseUrl, model, apiKey, null, null, token);
        return Clean(reply.Content);
    }

    /// <summary>去壳、去"标题："前缀、去尾标点，超长就截断；模型答出一段废话也不会污染列表。</summary>
    internal static string? Clean(string? raw)
    {
        var text = WhitespaceRegex.Replace((raw ?? "").Replace("\r", " ").Replace("\n", " "), " ").Trim();
        text = text.Trim(Wrappers);
        text = PrefixRegex.Replace(text, "");
        text = text.Trim(Wrappers).TrimEnd('。', '！', '？', '；', '，', '.', '!', '?', ';', ',').Trim();
        if (text.Length == 0) return null;
        return text.Length > MaxChars ? text[..MaxChars] + "…" : text;
    }
}
