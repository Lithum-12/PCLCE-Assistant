using System;
using System.Collections.Generic;

namespace PCL.Core.Assistant;

public enum AssistantApprovalDecision
{
    Deny,
    /// <summary>只放行这一个操作。</summary>
    Once,
    /// <summary>这一次发送里后面的操作都不再问。</summary>
    All
}

/// <summary>
/// 确认框三个按钮的含义。上游 MyMsgBox 返回按钮序号（从 1 开始），
/// 关窗口、按 ESC 之类的 0 或其他值一律按拒绝处理。
/// </summary>
public static class AssistantApproval
{
    public const string OnceValue = "once";
    public const string AllValue = "all";
    public const string DenyValue = "deny";

    public static AssistantApprovalDecision FromDialogResult(int result) => result switch
    {
        1 => AssistantApprovalDecision.Once,
        2 => AssistantApprovalDecision.All,
        _ => AssistantApprovalDecision.Deny
    };

    public static string ToConfig(AssistantApprovalDecision decision) => decision switch
    {
        AssistantApprovalDecision.Once => OnceValue,
        AssistantApprovalDecision.All => AllValue,
        _ => DenyValue
    };
}

/// <summary>
/// Minecraft 版本号的比较。这里踩过一次真实的坑：用"包含"来判断，
/// 结果玩家要 1.21.1，而 "1.21.11".Contains("1.21.1") 也是真，装错了版本。
/// </summary>
public static class AssistantVersions
{
    /// <param name="declared">文件自己声明的一个版本号，例如 1.21.1 或 1.21.1-pre1。</param>
    /// <param name="wanted">玩家或实例要的版本；为空表示不做版本筛选。</param>
    public static bool Matches(string? declared, string? wanted)
    {
        if (string.IsNullOrWhiteSpace(wanted)) return true;
        if (string.IsNullOrWhiteSpace(declared)) return false;
        var d = declared.Trim();
        var w = wanted.Trim();
        if (d.Equals(w, StringComparison.OrdinalIgnoreCase)) return true;
        // 只允许带分隔符的更细写法（1.21.1-pre1、1.21.1+build2），
        // 数字或点接着往下写的都是另一个版本（1.21.11、1.21.1.2）
        if (d.Length > w.Length && d.StartsWith(w, StringComparison.OrdinalIgnoreCase))
        {
            var next = d[w.Length];
            return next is not (>= '0' and <= '9') and not '.';
        }

        return false;
    }

    public static bool AnyMatches(IReadOnlyList<string> declared, string? wanted)
    {
        if (string.IsNullOrWhiteSpace(wanted)) return true;
        foreach (var value in declared)
            if (Matches(value, wanted)) return true;

        return false;
    }
}
