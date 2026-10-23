using System.Linq;

namespace PCL.Core.Assistant;

public sealed record AssistantProvider(string Name, string BaseUrl, string[] Models);

/// <summary>
/// OpenAI 兼容端点的国产服务商预设。模型 id 只是默认候选，实际以账号可用为准，
/// 用户在设置里填的 ModelName 永远优先。
/// </summary>
public static class AssistantProviders
{
    public const string CustomName = "自定义";

    public static readonly AssistantProvider[] All =
    [
        new("DeepSeek", "https://api.deepseek.com/v1", ["deepseek-chat", "deepseek-reasoner"]),
        new("Kimi 月之暗面", "https://api.moonshot.cn/v1", ["kimi-k2-0711-preview", "moonshot-v1-8k"]),
        new("智谱 GLM", "https://open.bigmodel.cn/api/paas/v4", ["glm-4-plus", "glm-4-flash"]),
        new("通义千问", "https://dashscope.aliyuncs.com/compatible-mode/v1", ["qwen-plus", "qwen-turbo"]),
        new("豆包 火山方舟", "https://ark.cn-beijing.volces.com/api/v3", ["doubao-pro-32k", "doubao-lite-32k"]),
        new("MiniMax", "https://api.minimax.chat/v1", ["MiniMax-Text-01"]),
        new("硅基流动", "https://api.siliconflow.cn/v1", ["deepseek-ai/DeepSeek-V3"]),
        new(CustomName, "", [])
    ];

    public static AssistantProvider Match(string baseUrl) =>
        All.FirstOrDefault(p => p.BaseUrl.Length > 0 &&
                                baseUrl.TrimEnd('/').Equals(p.BaseUrl.TrimEnd('/'), System.StringComparison.OrdinalIgnoreCase))
        ?? All[^1];

    /// <summary>
    /// 下拉框里要出现的模型：预设列表 + 用户自填的那个。
    /// </summary>
    public static string[] ModelsFor(string baseUrl, string configured)
    {
        var list = new System.Collections.Generic.List<string>(Match(baseUrl).Models);
        if (!string.IsNullOrWhiteSpace(configured) && !list.Contains(configured)) list.Insert(0, configured);
        return list.ToArray();
    }
}
