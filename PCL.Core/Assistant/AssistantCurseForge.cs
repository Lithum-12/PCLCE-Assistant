using PCL.Core.App;

namespace PCL.Core.Assistant;

/// <summary>
/// CurseForge 密钥的取值顺序：用户在助手设置里填的优先，其次是上游构建期注入的 secret。
/// 上游 release 构建会带一枚 key，本地 Debug 跑永远是空串，所以必须有第一个来源。
/// </summary>
public static class AssistantCurseForge
{
    public static string Key => Resolve(Config.Assistant.CurseForgeKey, Secrets.CurseForgeAPIKey);

    public static bool Available => !string.IsNullOrWhiteSpace(Key);

    /// <summary>用户填的优先；去掉空白后都为空就算不可用。</summary>
    internal static string Resolve(string? configured, string? injected) =>
        !string.IsNullOrWhiteSpace(configured) ? configured.Trim()
        : !string.IsNullOrWhiteSpace(injected) ? injected.Trim()
        : "";
}
