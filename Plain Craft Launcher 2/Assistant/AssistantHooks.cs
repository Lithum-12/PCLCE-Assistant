using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using PCL.Core.App.IoC;
using PCL.Core.Assistant;

namespace PCL.Assistant;

/// <summary>
/// fork 侧启动注入的唯一入口，上游只在 Program.Main 调用一次 Install()。
/// </summary>
internal static class AssistantHooks
{
    internal static void Install()
    {
        RestrictedFeaturePolicy.RegionOverride = AssistantRegionBypass.Evaluate;
        InstallStrings();
    }

    private static void InstallStrings()
    {
        if (Lifecycle.CurrentApplication is not null)
        {
            ApplyStrings(Lifecycle.CurrentApplication.Resources);
            return;
        }

        Lifecycle.StateChanged += OnState;
        return;

        void OnState(LifecycleState state)
        {
            if (Lifecycle.CurrentApplication is null) return;
            Lifecycle.StateChanged -= OnState;
            ApplyStrings(Lifecycle.CurrentApplication.Resources);
        }
    }

    private static void ApplyStrings(ResourceDictionary resources)
    {
        var dict = AssistantStrings.IsChineseUi() ? AssistantStrings.Zh : AssistantStrings.En;
        foreach (var (key, value) in dict) resources[key] = value;
    }
}

internal static class AssistantStrings
{
    internal static bool IsChineseUi() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";

    internal static readonly IReadOnlyDictionary<string, string> Zh = new Dictionary<string, string>
    {
        ["Main.Tab.Assistant"] = "聊天",
        ["Assistant.Left.Category"] = "AI 助手",
        ["Assistant.Left.Chat"] = "对话",
        ["Assistant.Chat.Empty"] = "向 AI 助手提问，它可以读取当前实例的崩溃报告、模组列表，或查询 CurseForge / Modrinth 上的资源。",
        ["Assistant.Chat.InputHint"] = "输入内容，Enter 发送，Shift+Enter 换行",
        ["Assistant.Chat.Send"] = "发送",
        ["Assistant.Chat.RoleUser"] = "你",
        ["Assistant.Chat.RoleAssistant"] = "AI 助手",
        ["Assistant.Chat.NotConfigured"] = "尚未配置模型。请在 设置 → AI 助手 中填写 API 密钥、服务地址与模型名称。",
        ["Assistant.Chat.Disabled"] = "AI 助手已在设置中关闭。",
        ["Assistant.Chat.SelectInstance"] = "选择实例",
        ["Assistant.Chat.Model.None"] = "未配置模型",
        ["Assistant.Chat.Stop"] = "停止",
        ["Assistant.Chat.Stopped"] = "（已停止）",
        ["Assistant.Chat.System"] = "你是 PCL CE 启动器内置的 Minecraft 助手。回答保持简洁，涉及操作时给出可执行的步骤。",
        ["Assistant.Chat.System.Instance"] = "当前选中的游戏实例：{0}",
        ["Setup.Left.Item.Assistant"] = "AI 助手",
        ["Setup.Assistant.Card.Main"] = "AI 助手",
        ["Setup.Assistant.Enabled"] = "启用 AI 助手",
        ["Setup.Assistant.Provider"] = "服务商",
        ["Setup.Assistant.BaseUrl"] = "服务地址",
        ["Setup.Assistant.BaseUrl.Tip"] = "OpenAI 兼容接口的根地址，例如 https://api.deepseek.com/v1",
        ["Setup.Assistant.Model"] = "模型名称",
        ["Setup.Assistant.Model.Tip"] = "对话所使用的模型 ID",
        ["Setup.Assistant.ApiKey"] = "API 密钥",
        ["Setup.Assistant.ApiKey.Tip"] = "密钥以加密配置存储，不会写入明文 config。",
        ["Setup.Assistant.Card.Region"] = "区域限制",
        ["Setup.Assistant.Region"] = "放行被上游按区域禁用的功能",
        ["Setup.Assistant.Region.Tip"] = "上游自 2.15.1 起依据系统区域信息禁用下载、安装与整合包功能。本 fork 默认放行，关闭后恢复上游判定。"
    };

    internal static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>
    {
        ["Main.Tab.Assistant"] = "Chat",
        ["Assistant.Left.Category"] = "AI Assistant",
        ["Assistant.Left.Chat"] = "Conversation",
        ["Assistant.Chat.Empty"] = "Ask the AI assistant. It can read crash reports and mod lists of the current instance, or query CurseForge / Modrinth.",
        ["Assistant.Chat.InputHint"] = "Enter to send, Shift+Enter for a new line",
        ["Assistant.Chat.Send"] = "Send",
        ["Assistant.Chat.RoleUser"] = "You",
        ["Assistant.Chat.RoleAssistant"] = "AI Assistant",
        ["Assistant.Chat.NotConfigured"] = "No model configured. Open Settings → AI Assistant to fill in API key, base URL and model name.",
        ["Assistant.Chat.Disabled"] = "The AI assistant is disabled in settings.",
        ["Assistant.Chat.SelectInstance"] = "Select instance",
        ["Assistant.Chat.Model.None"] = "No model configured",
        ["Assistant.Chat.Stop"] = "Stop",
        ["Assistant.Chat.Stopped"] = "(stopped)",
        ["Assistant.Chat.System"] = "You are the Minecraft assistant built into PCL CE. Answer briefly; give executable steps when the task involves actions.",
        ["Assistant.Chat.System.Instance"] = "Currently selected instance: {0}",
        ["Setup.Left.Item.Assistant"] = "AI Assistant",
        ["Setup.Assistant.Card.Main"] = "AI Assistant",
        ["Setup.Assistant.Enabled"] = "Enable AI assistant",
        ["Setup.Assistant.Provider"] = "Provider",
        ["Setup.Assistant.BaseUrl"] = "Base URL",
        ["Setup.Assistant.BaseUrl.Tip"] = "OpenAI-compatible endpoint root, e.g. https://api.deepseek.com/v1",
        ["Setup.Assistant.Model"] = "Model",
        ["Setup.Assistant.Model.Tip"] = "Model id used for chat",
        ["Setup.Assistant.ApiKey"] = "API key",
        ["Setup.Assistant.ApiKey.Tip"] = "Stored in the encrypted config, never in plaintext.",
        ["Setup.Assistant.Card.Region"] = "Region restriction",
        ["Setup.Assistant.Region"] = "Allow features upstream gates by region",
        ["Setup.Assistant.Region.Tip"] = "Since 2.15.1 upstream disables download, install and modpack features by system region. This fork allows them by default."
    };
}
