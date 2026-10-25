using System;

namespace PCL.Core.Assistant;

public enum AssistantPermissionMode
{
    /// <summary>会改动东西的操作先问用户一句。</summary>
    Ask,

    /// <summary>不询问，直接执行。</summary>
    Full
}

/// <summary>
/// 权限档位的取值与判定。放在 Core 里是为了能被单测覆盖，
/// 也让工具集只认这一个入口，第二阶段加执行类工具时不用到处写 if。
/// </summary>
public static class AssistantPermissions
{
    public const string AskValue = "ask";
    public const string FullValue = "full";

    /// <summary>认不出来的一律按更严的那档处理。</summary>
    public static AssistantPermissionMode Parse(string? value) =>
        string.Equals((value ?? "").Trim(), FullValue, StringComparison.OrdinalIgnoreCase)
            ? AssistantPermissionMode.Full
            : AssistantPermissionMode.Ask;

    public static string ToConfig(AssistantPermissionMode mode) =>
        mode == AssistantPermissionMode.Full ? FullValue : AskValue;

    /// <summary>只有"这个工具本身要确认"且"档位是需要审批"时才弹窗。</summary>
    public static bool NeedsApproval(AssistantPermissionMode mode, bool toolRequiresApproval) =>
        toolRequiresApproval && mode == AssistantPermissionMode.Ask;
}
