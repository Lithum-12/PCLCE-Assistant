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
        ["Assistant.Left.HomeCategory"] = "首页",
        ["Assistant.Left.Home"] = "开始",
        ["Assistant.Left.HistoryCategory"] = "历史对话",
        ["Assistant.Left.Drafts"] = "未发送草稿",
        ["Assistant.Left.Draft"] = "未命名草稿",
        ["Assistant.Left.NewChat"] = "新对话",
        ["Assistant.Left.Untitled"] = "未命名对话",
        ["Assistant.Left.Delete"] = "删除",
        ["Assistant.Home.Title"] = "AI 助手",
        ["Assistant.Home.Description"] = "向 AI 助手提问，它可以读取当前实例的崩溃报告、模组列表，或查询 CurseForge / Modrinth 上的资源。",
        ["Assistant.Home.Start"] = "开始对话",
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
        ["Assistant.Chat.Copy"] = "复制",
        ["Assistant.Chat.Thinking"] = "正在思考",
        ["Assistant.Chat.Thinking.Next"] = "正在结合工具结果继续作答",
        ["Assistant.Tool.read_crash_report"] = "读取崩溃报告",
        ["Assistant.Tool.read_latest_log"] = "读取最新日志",
        ["Assistant.Tool.list_mods"] = "列出模组",
        ["Assistant.Tool.search_modrinth"] = "搜索 Modrinth",
        ["Assistant.Tool.search_curseforge"] = "搜索 CurseForge",
        ["Assistant.Tool.unknown"] = "调用工具",
        ["Assistant.Chat.System"] = "你是 PCL CE 启动器内置的 Minecraft 助手。你只能读取和分析，不能执行任何操作：不要承诺或声称你能新建/删除实例、安装或下载模组、修改文件。你可以调用给出的只读工具读取当前实例的崩溃报告、logs/latest.log、mods 列表，或在 Modrinth / CurseForge 搜索资源；工具没有提供的内容不要凭空编造。需要动手时，告诉用户在启动器里点哪几步。回答保持简洁。",
        ["Assistant.Chat.System.Instance"] = "当前选中的游戏实例：{0}",
        ["Assistant.Chat.Tools.Trace"] = "本次工具调用",
        ["Assistant.Chat.Tools.Rejected"] = "当前模型不接受工具调用，本次按普通对话回答。",
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
        ["Setup.Assistant.Card.Tools"] = "工具与隐私",
        ["Setup.Assistant.Tools"] = "允许读取本地文件与搜索资源",
        ["Setup.Assistant.Tools.Tip"] = "模型可调用只读工具：崩溃报告、latest.log、mods 列表、Modrinth / CurseForge 搜索。关闭后只能纯聊天。",
        ["Setup.Assistant.Redact"] = "发送前脱敏本地内容",
        ["Setup.Assistant.Redact.Tip"] = "刮掉 Windows 用户名与完整路径、IP、UUID、坐标、种子与账号名。关闭后本地内容原文发给服务商。",
        ["Setup.Assistant.Region"] = "放行被上游按区域禁用的功能",
        ["Setup.Assistant.Region.Tip"] = "上游自 2.15.1 起依据系统区域信息禁用下载、安装与整合包功能。本 fork 默认放行，关闭后恢复上游判定。"
    };

    internal static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>
    {
        ["Main.Tab.Assistant"] = "Chat",
        ["Assistant.Left.HomeCategory"] = "Home",
        ["Assistant.Left.Home"] = "Start",
        ["Assistant.Left.HistoryCategory"] = "History",
        ["Assistant.Left.Drafts"] = "Unsent drafts",
        ["Assistant.Left.Draft"] = "Untitled draft",
        ["Assistant.Left.NewChat"] = "New chat",
        ["Assistant.Left.Untitled"] = "Untitled",
        ["Assistant.Left.Delete"] = "Delete",
        ["Assistant.Home.Title"] = "AI Assistant",
        ["Assistant.Home.Description"] = "Ask the AI assistant. It can read crash reports and mod lists of the current instance, or query CurseForge / Modrinth.",
        ["Assistant.Home.Start"] = "Start a conversation",
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
        ["Assistant.Chat.Copy"] = "Copy",
        ["Assistant.Chat.Thinking"] = "Thinking",
        ["Assistant.Chat.Thinking.Next"] = "Composing the answer from tool results",
        ["Assistant.Tool.read_crash_report"] = "Reading crash reports",
        ["Assistant.Tool.read_latest_log"] = "Reading latest.log",
        ["Assistant.Tool.list_mods"] = "Listing mods",
        ["Assistant.Tool.search_modrinth"] = "Searching Modrinth",
        ["Assistant.Tool.search_curseforge"] = "Searching CurseForge",
        ["Assistant.Tool.unknown"] = "Calling a tool",
        ["Assistant.Chat.System"] = "You are the Minecraft assistant built into PCL CE. You can only read and analyse; you cannot act: never promise or claim that you created or deleted an instance, installed or downloaded a mod, or changed a file. Use the provided read-only tools to read the current instance's crash reports, logs/latest.log and mods list, or to search Modrinth / CurseForge; do not invent content the tools did not return. When something has to be done by hand, tell the user which clicks in the launcher. Keep answers short.",
        ["Assistant.Chat.System.Instance"] = "Currently selected instance: {0}",
        ["Assistant.Chat.Tools.Trace"] = "Tools used",
        ["Assistant.Chat.Tools.Rejected"] = "This model does not accept tool calls, so the answer came from plain chat.",
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
        ["Setup.Assistant.Card.Tools"] = "Tools & privacy",
        ["Setup.Assistant.Tools"] = "Allow reading local files and searching resources",
        ["Setup.Assistant.Tools.Tip"] = "The model may call read-only tools: crash reports, latest.log, mods list, Modrinth / CurseForge search.",
        ["Setup.Assistant.Redact"] = "Redact local content before sending",
        ["Setup.Assistant.Redact.Tip"] = "Strips Windows user name and full paths, IPs, UUIDs, coordinates, seeds and the account name. Turning this off sends local content as-is.",
        ["Setup.Assistant.Region"] = "Allow features upstream gates by region",
        ["Setup.Assistant.Region.Tip"] = "Since 2.15.1 upstream disables download, install and modpack features by system region. This fork allows them by default."
    };
}
