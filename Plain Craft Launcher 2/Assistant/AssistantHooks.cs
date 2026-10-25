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
        ["Assistant.Chat.NotConfigured"] = "还没有配置 API 密钥。请到 设置 → AI 助手 填写服务地址、模型名称与 API 密钥，然后回来继续问。",
        ["Assistant.Chat.Disabled"] = "AI 助手已在设置中关闭。请到 设置 → AI 助手 重新启用。",
        ["Assistant.Error.Auth"] = "API 密钥无效或没有权限。请检查 设置 → AI 助手 里的密钥是否填错、是否已过期。",
        ["Assistant.Error.Balance"] = "账户余额或额度不足。请到服务商后台充值或查看用量后再试。",
        ["Assistant.Error.RateLimited"] = "请求过于频繁或额度已用尽。稍等一会儿再试。",
        ["Assistant.Error.Server"] = "服务商那边出了错，这次没有完成。稍后重试。",
        ["Assistant.Error.Network"] = "网络连接失败。请检查网络、代理或 DNS 设置后重试。",
        ["Assistant.Error.Rejected"] = "请求被服务商拒绝。请检查模型名称与服务地址是否匹配。",
        ["Assistant.Error.Unknown"] = "这次对话失败了。",
        ["Assistant.Chat.SelectInstance"] = "选择实例",
        ["Assistant.Chat.Model.None"] = "未配置模型",
        ["Assistant.Chat.Stop"] = "停止",
        ["Assistant.Chat.Stopped"] = "（已停止）",
        ["Assistant.Chat.Copy"] = "复制",
        ["Assistant.Chat.UsedTools"] = "使用 {0} 个工具",
        ["Assistant.Chat.Thinking"] = "正在思考",
        ["Assistant.Chat.Thinking.Next"] = "正在结合工具结果继续作答",
        ["Assistant.Tool.read_crash_report"] = "读取崩溃报告",
        ["Assistant.Tool.read_latest_log"] = "读取最新日志",
        ["Assistant.Tool.list_mods"] = "列出模组",
        ["Assistant.Tool.search_modrinth"] = "搜索 Modrinth",
        ["Assistant.Tool.search_curseforge"] = "搜索 CurseForge",
        ["Assistant.Tool.unknown"] = "调用工具",
        ["Assistant.Tool.list_instance_files"] = "查看实例目录",
        ["Assistant.Tool.read_instance_file"] = "读取实例文件",
        ["Assistant.Tool.read_site"] = "查阅站点",
        ["Assistant.Chat.System"] = """
            你是 PCL CE 启动器内置的 Minecraft 助手，用界面语言回答。

            【能力边界】只能读取和分析，不能执行任何操作：不要承诺或声称你能新建/删除实例、安装或下载模组、修改文件。需要动手时，告诉用户在启动器里点哪几步。

            【可用工具】读取当前实例的崩溃报告、logs/latest.log、mods 清单，浏览并读取实例目录下的文本文件；在 Modrinth / CurseForge 搜索资源；查阅白名单站点（MC 百科、ModpackIndex、BMCLAPI、Mojang 版本库、Fabric、Forge、NeoForge、Quilt、LiteLoader、OptiFine、GitHub）。信息不足时先调用工具查证，不要凭记忆作答。

            【推荐资源】必须与当前实例的游戏版本和加载器匹配，不匹配的要说明；同时给出中文常用名与英文原名（例如「钠 / Sodium」）；把资源页面链接单独一行原样给出（如 https://modrinth.com/mod/sodium），启动器会渲染成可点击卡片，不要只写版本号。优先推荐大多数人能直接访问的来源（Modrinth、BMCLAPI 镜像、MC 百科）；只有 CurseForge 或其他受地区限制的站点才有某项资源时，明确说明可能需要代理。版本号、下载量、更新时间只能来自工具返回的内容，查不到就直说查不到。

            回答保持简洁。
            """,
        ["Assistant.Chat.System.Instance"] = "当前选中的游戏实例：{0}",
        ["Assistant.Chat.System.NoCurseForge"] =
            "【本机没有 CurseForge 密钥】search_curseforge 与 CurseForge 站点查阅这次没有挂载，CurseForge 链接也不会变成卡片。" +
            "请一律优先给 Modrinth、MC 百科、ModpackIndex 这些不需要代理的来源；确实只有 CurseForge 才有某项资源时，" +
            "直说这台机器上的启动器查不到 CurseForge，不要编造下载量、版本或更新时间。",
        ["Assistant.Chat.Tools.Rejected"] = "当前模型不接受工具调用，本次按普通对话回答。",
        ["Assistant.Chat.DeleteConfirm.Title"] = "删除对话",
        ["Assistant.Chat.DeleteConfirm"] = "确定要删除「{0}」吗？删除后无法恢复。",
        ["Assistant.Chat.LoadEarlier"] = "显示更早的 {0} 条消息",
        ["Assistant.Chat.Permission.Ask"] = "需要审批",
        ["Assistant.Chat.Permission.Ask.Tip"] = "只读操作（看崩溃报告、日志、mods 清单、查资源站与白名单站点）直接执行；会改动东西的操作会先弹窗问你。点一下切到「完全控制」。",
        ["Assistant.Chat.Permission.Full"] = "完全控制",
        ["Assistant.Chat.Permission.Full.Tip"] = "AI 请求的操作不再逐个确认，直接执行。当前版本它仍然只有只读能力，等执行类工具上线后这一档才会真正放开。点一下切回「需要审批」。",
        ["Assistant.Approval.Title"] = "需要你的确认",
        ["Assistant.Approval.Content"] = "AI 请求执行：{0}\n参数：{1}\n当前权限档位是「需要审批」，允许吗？",
        ["Assistant.Title.Instruction"] = """
            把下面这条玩家请求改写成不超过 12 个词的短标题，用来显示在会话列表里。
            与请求保持同一种语言（中文请求就给中文标题）；把专有名词写成常见写法与大小写（例如 neoforge → NeoForge、我的世界 → Minecraft、钠 → Sodium）；
            不要引号、书名号或句号，不要加"标题："前缀，不要解释，只输出标题本身。
            """,
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
        ["Setup.Assistant.Card.Sites"] = "可查阅的站点",
        ["Setup.Assistant.Sites.Tip"] = "开关只影响 AI 能否联网查阅该站点；关闭后模型调用会被直接拒绝。本地文件与资源搜索不受这里控制。",
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
        ["Assistant.Chat.NotConfigured"] = "No API key is configured yet. Open Settings → AI Assistant and fill in the base URL, model name and API key, then ask again.",
        ["Assistant.Chat.Disabled"] = "The AI assistant is turned off. Enable it in Settings → AI Assistant.",
        ["Assistant.Error.Auth"] = "The API key is invalid or lacks permission. Check the key in Settings → AI Assistant.",
        ["Assistant.Error.Balance"] = "Not enough balance or quota on that account. Top up or check usage at the provider, then retry.",
        ["Assistant.Error.RateLimited"] = "Too many requests or the quota is used up. Wait a moment and try again.",
        ["Assistant.Error.Server"] = "The provider hit a server error, so this answer did not finish. Try again later.",
        ["Assistant.Error.Network"] = "The network request failed. Check your connection, proxy or DNS settings and retry.",
        ["Assistant.Error.Rejected"] = "The provider rejected the request. Check that the model name matches the base URL.",
        ["Assistant.Error.Unknown"] = "This request failed.",
        ["Assistant.Chat.SelectInstance"] = "Select instance",
        ["Assistant.Chat.Model.None"] = "No model configured",
        ["Assistant.Chat.Stop"] = "Stop",
        ["Assistant.Chat.Stopped"] = "(stopped)",
        ["Assistant.Chat.Copy"] = "Copy",
        ["Assistant.Chat.UsedTools"] = "used {0} tools",
        ["Assistant.Chat.Thinking"] = "Thinking",
        ["Assistant.Chat.Thinking.Next"] = "Composing the answer from tool results",
        ["Assistant.Tool.read_crash_report"] = "Reading crash reports",
        ["Assistant.Tool.read_latest_log"] = "Reading latest.log",
        ["Assistant.Tool.list_mods"] = "Listing mods",
        ["Assistant.Tool.search_modrinth"] = "Searching Modrinth",
        ["Assistant.Tool.search_curseforge"] = "Searching CurseForge",
        ["Assistant.Tool.unknown"] = "Calling a tool",
        ["Assistant.Tool.list_instance_files"] = "Browsing the instance folder",
        ["Assistant.Tool.read_instance_file"] = "Reading an instance file",
        ["Assistant.Tool.read_site"] = "Reading a website",
        ["Assistant.Chat.System"] = """
            You are the Minecraft assistant built into PCL CE. Answer in the interface language.

            [Scope] You can only read and analyse; you cannot act: never claim to have created or deleted instances, installed or downloaded mods, or changed files. When something needs doing by hand, name the exact clicks in the launcher.

            [Tools] Read the current instance's crash reports, logs/latest.log and mods list, browse and read text files inside the instance folder, search Modrinth / CurseForge, and consult the allowlisted sites (MC 百科, ModpackIndex, BMCLAPI, Mojang version manifest, Fabric, Forge, NeoForge, Quilt, LiteLoader, OptiFine, GitHub). When information is missing, look it up with a tool instead of answering from memory.

            [Recommending] Match the current instance's game version and loader, and say so when something does not match; give both the localised and the original name (e.g. 钠 / Sodium); put the resource page link on its own line verbatim (e.g. https://modrinth.com/mod/sodium) so the launcher can render a clickable card, instead of bare version numbers. Prefer sources most users can reach directly (Modrinth, BMCLAPI mirrors, MC 百科); when a resource only exists on CurseForge or another region-limited host, say a proxy may be needed. Version numbers, download counts and update times must come from tool output - if it cannot be found, say so.

            Keep answers short.
            """,
        ["Assistant.Chat.System.Instance"] = "Currently selected instance: {0}",
        ["Assistant.Chat.System.NoCurseForge"] =
            "[No CurseForge key on this machine] search_curseforge and CurseForge site reads are not attached this run, " +
            "and CurseForge links will not render as cards. Prefer Modrinth, mcmod.cn and ModpackIndex, which need no proxy; " +
            "when something really is only on CurseForge, say plainly that this launcher cannot query CurseForge " +
            "instead of inventing download counts, versions or dates.",
        ["Assistant.Chat.Tools.Rejected"] = "This model does not accept tool calls, so the answer came from plain chat.",
        ["Assistant.Chat.DeleteConfirm.Title"] = "Delete conversation",
        ["Assistant.Chat.DeleteConfirm"] = "Delete “{0}”? This cannot be undone.",
        ["Assistant.Chat.LoadEarlier"] = "Show {0} earlier messages",
        ["Assistant.Chat.Permission.Ask"] = "Ask first",
        ["Assistant.Chat.Permission.Ask.Tip"] = "Read-only steps (crash reports, logs, the mods list, resource sites and whitelisted pages) run straight away; anything that would change something pops a confirmation first. Click to switch to Full control.",
        ["Assistant.Chat.Permission.Full"] = "Full control",
        ["Assistant.Chat.Permission.Full.Tip"] = "The assistant acts without asking each time. It still only has read-only tools in this build, so this tier starts to matter once the action tools land. Click to go back to Ask first.",
        ["Assistant.Approval.Title"] = "Approval needed",
        ["Assistant.Approval.Content"] = "The assistant wants to run: {0}\nArguments: {1}\nYour permission tier is \"Ask first\" - allow it?",
        ["Assistant.Title.Instruction"] = """
            Rewrite the player's request below as a conversation-list title of at most 8 words.
            Keep the language of the request; write proper nouns the usual way (e.g. neoforge → NeoForge, 我的世界 → Minecraft, 钠 → Sodium);
            no quotes, no brackets, no trailing period, no "Title:" prefix, no explanation - output the title only.
            """,
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
        ["Setup.Assistant.Card.Sites"] = "Sites the assistant may read",
        ["Setup.Assistant.Sites.Tip"] = "These switches only gate web reading; local files and Modrinth/CurseForge search are controlled by the tool switch above.",
        ["Setup.Assistant.Tools"] = "Allow reading local files and searching resources",
        ["Setup.Assistant.Tools.Tip"] = "The model may call read-only tools: crash reports, latest.log, mods list, Modrinth / CurseForge search.",
        ["Setup.Assistant.Redact"] = "Redact local content before sending",
        ["Setup.Assistant.Redact.Tip"] = "Strips Windows user name and full paths, IPs, UUIDs, coordinates, seeds and the account name. Turning this off sends local content as-is.",
        ["Setup.Assistant.Region"] = "Allow features upstream gates by region",
        ["Setup.Assistant.Region.Tip"] = "Since 2.15.1 upstream disables download, install and modpack features by system region. This fork allows them by default."
    };
}
