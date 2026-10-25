using System;
using System.Collections.Generic;

namespace PCL.Core.Assistant;

/// <summary>
/// AI 能改哪些启动器设置。白名单是一个个具体键名，不做前缀匹配——
/// 前缀匹配会把 Assistant* 一起放进来，等于让模型自己改自己的权限与脱敏开关。
/// </summary>
public static class AssistantConfigKeys
{
    /// <summary>只放全局键；按实例存的 Version* 需要额外的上下文参数，第一版不碰。</summary>
    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "LaunchRamCustom",
        "LaunchRamType",
        "LaunchAdvanceJvm",
        "LaunchAdvanceRenderer",
        "LaunchAdvanceDisableJLW",
        "LaunchAdvanceDisableLwjglUnsafeAgent",
        "LaunchArgumentPriority",
        "LaunchArgumentWindowType",
        "LaunchArgumentWindowWidth",
        "LaunchArgumentWindowHeight",
        "ToolDownloadAutoSelectVersion",
        "ToolDownloadAutoInstallDependencies"
    };

    public static bool IsAllowed(string? key) =>
        !string.IsNullOrWhiteSpace(key) && Allowed.Contains(key.Trim());

    /// <summary>模型只会给字符串，这里按配置项自己声明的类型转一次；转不动就拒绝而不是硬塞。</summary>
    public static bool TryCoerce(Type type, string? raw, out object value)
    {
        var text = (raw ?? "").Trim();
        // 失败路径上调用方不会读它；不能写 Activator.CreateInstance(type)——string 的默认值造不出来
        value = text;
        if (type == typeof(string)) return true;

        if (type == typeof(bool))
        {
            if (text.Equals("true", StringComparison.OrdinalIgnoreCase) || text is "1" or "开" or "是" or "on")
                value = true;
            else if (text.Equals("false", StringComparison.OrdinalIgnoreCase) || text is "0" or "关" or "否" or "off")
                value = false;
            else
                return false;
            return true;
        }

        if (type == typeof(int))
            return int.TryParse(text, out var i) && Assign((object)i, ref value);
        if (type == typeof(long))
            return long.TryParse(text, out var l) && Assign((object)l, ref value);
        if (type == typeof(double))
            return double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d) && Assign((object)d, ref value);

        if (type.IsEnum)
        {
            // 只认枚举名：Enum.TryParse 对 "3" 也会成功（当成未定义值），那是模型在猜数字
            if (text.Length == 0 || char.IsDigit(text[0])) return false;
            if (!Enum.TryParse(type, text, true, out var parsed) || parsed is null) return false;
            value = parsed;
            return true;
        }

        return false;
    }

    private static bool Assign(object source, ref object target)
    {
        target = source;
        return true;
    }
}
