using System;

namespace PCL.Core.Assistant;

public enum AssistantPermissionMode
{
    /// <summary>只能读和分析，执行类工具根本不挂载。</summary>
    Read,

    /// <summary>执行类工具挂上，但每一下都要先问用户。</summary>
    Ask,

    /// <summary>执行类工具直接跑，不弹确认。</summary>
    Full
}

/// <summary>
/// 权限档位的取值与判定。放在 Core 里是为了能被单测覆盖，
/// 也让工具集只认这一个入口，加执行类工具时不用到处写 if。
/// </summary>
public static class AssistantPermissions
{
    public const string ReadValue = "read";
    public const string AskValue = "ask";
    public const string FullValue = "full";

    /// <summary>认不出来的一律回到"需要审批"这一中间档：既不静默放开，也不静默关掉他配好的能力。</summary>
    public static AssistantPermissionMode Parse(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
    {
        FullValue => AssistantPermissionMode.Full,
        ReadValue => AssistantPermissionMode.Read,
        _ => AssistantPermissionMode.Ask
    };

    public static string ToConfig(AssistantPermissionMode mode) => mode switch
    {
        AssistantPermissionMode.Full => FullValue,
        AssistantPermissionMode.Read => ReadValue,
        _ => AskValue
    };

    /// <summary>只读档连工具都不给，也就不会有确认框；完全控制则全免。</summary>
    public static bool AllowsActions(AssistantPermissionMode mode) => mode != AssistantPermissionMode.Read;

    public static bool NeedsApproval(AssistantPermissionMode mode, bool isAction) =>
        isAction && mode == AssistantPermissionMode.Ask;

    /// <summary>点按钮时的下一档：只读 → 需要审批 → 完全控制 → 只读。</summary>
    public static AssistantPermissionMode Next(AssistantPermissionMode mode) => mode switch
    {
        AssistantPermissionMode.Read => AssistantPermissionMode.Ask,
        AssistantPermissionMode.Ask => AssistantPermissionMode.Full,
        _ => AssistantPermissionMode.Read
    };
}
