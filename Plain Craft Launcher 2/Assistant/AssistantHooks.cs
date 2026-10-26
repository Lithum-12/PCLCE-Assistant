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
        ["Assistant.Left.Export"] = "导出",
        ["Assistant.Left.Export.Filter"] = "Markdown 文档|*.md",
        ["Assistant.Left.Export.Done"] = "已导出到 {0}",
        ["Assistant.Left.Export.Failed"] = "导出失败：{0}",
        ["Assistant.Left.Delete"] = "删除",
        ["Assistant.Left.Open"] = "打开所在位置",
        ["Assistant.Left.Rename"] = "修改名称",
        ["Assistant.Left.Rename.Tip"] = "给这段对话起个好认的名字（不超过 30 字）",
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
        ["Assistant.Tool.download_status"] = "查看下载进度",
        ["Assistant.Tool.download_resource"] = "下载资源",
        ["Assistant.Tool.write_instance_file"] = "写入实例文件",
        ["Assistant.Tool.delete_instance_file"] = "把实例文件放进回收站",
        ["Assistant.Tool.create_instance"] = "新建实例",
        ["Assistant.Tool.set_launcher_config"] = "修改启动器设置",
        ["Assistant.Tool.run_python"] = "执行 Python 脚本",
        ["Assistant.Chat.System"] = """
            你是 PCL CE 启动器内置的 Minecraft 助手，用界面语言回答。

            【能力边界】你能做什么由后面那段"当前权限档位"决定，不要超出它：没有挂载给你的工具就不要声称调用过，也不要承诺还没开放的能力（新建/删除实例、安装 Java 目前都做不到）。需要玩家自己动手时，写清在启动器里点哪几步。

            【可用工具】读取当前实例的崩溃报告、logs/latest.log、mods 清单，浏览并读取实例目录下的文本文件；在 Modrinth / CurseForge 搜索资源；查阅白名单站点（MC 百科、ModpackIndex、BMCLAPI、Mojang 版本库、Fabric、Forge、NeoForge、Quilt、LiteLoader、OptiFine、GitHub）。信息不足时先调用工具查证，不要凭记忆作答。

            【推荐资源】必须与当前实例的游戏版本和加载器匹配，不匹配的要说明；同时给出中文常用名与英文原名（例如「钠 / Sodium」）；把资源页面链接单独一行原样给出（如 https://modrinth.com/mod/sodium），启动器会渲染成可点击卡片，不要只写版本号。已经确定玩家的 Minecraft 版本与加载器时，在链接后面带上筛选参数（如 https://modrinth.com/mod/sodium?mc=1.21.1&loader=fabric），玩家点开卡片就直接落在能用的那个版本；没把握就别带。优先推荐大多数人能直接访问的来源（Modrinth、BMCLAPI 镜像、MC 百科）；只有 CurseForge 或其他受地区限制的站点才有某项资源时，明确说明可能需要代理。版本号、下载量、更新时间只能来自工具返回的内容，查不到就直说查不到。

            回答保持简洁。
            """,
        ["Assistant.Chat.System.Intent"] = """
            【玩家的说法对应哪个工具】"装一个 / 下载 / 加个" mod、模组、光影包、材质包、世界 → download_resource：光影包和材质包一样能装，它们只是落到 shaderpacks、resourcepacks 目录里的文件；"删掉 / 移除 / 卸载" → delete_instance_file；"改配置 / 调选项" → write_instance_file；"改启动器设置（内存、JVM 参数、窗口）" → set_launcher_config；"算一下 / 批量处理 / 分析文件" → run_python；"建个新实例 / 装个 1.21.1 的游戏 / 顺便加个 Fabric" → create_instance。这些都算在你当前的能力范围内，不要再说自己做不到。

            【不许凭空描述动作】只有在这一轮真的调用过工具、并且工具返回成功之后，才能用"已经开始 / 正在下载 / 已经装好"这种说法；没调用工具就只用将来时写你打算做什么。想知道之前那些任务到底下完没有，用 download_status 查，别凭上一轮的记忆猜。
            """,
        ["Assistant.Chat.System.Env"] = """
            【当前环境】
            {0}
            """,
        ["Assistant.Chat.System.Env.NoInstance"] = "当前没有选中任何实例：需要实例信息时，先让玩家在「实例」页选一个。",
        ["Assistant.Chat.System.Env.Instance"] = "当前实例：{0}",
        ["Assistant.Chat.System.Env.Tier"] = "权限档位：{0}",
        ["Assistant.Chat.System.Env.Tools"] = "这一轮真的挂载给你的工具：{0}。清单里没有的别说自己能做，清单里的也别推辞。",
        ["Assistant.Chat.System.Env.NoTools"] = "这一轮没有挂载任何工具（工具开关已关闭），只能凭对话内容回答。",
        ["Assistant.Chat.System.Env.Sites"] = "可联网查阅的站点：{0}",
        ["Assistant.Chat.System.Env.NoSites"] = "没有开启任何可查阅站点，无法联网查证。",
        ["Assistant.Chat.System.NoCurseForge"] =
            "【本机没有 CurseForge 密钥】search_curseforge 与 CurseForge 站点查阅这次没有挂载，CurseForge 链接也不会变成卡片。" +
            "请一律优先给 Modrinth、MC 百科、ModpackIndex 这些不需要代理的来源；确实只有 CurseForge 才有某项资源时，" +
            "直说这台机器上的启动器查不到 CurseForge，不要编造下载量、版本或更新时间。",
        ["Assistant.Chat.Tools.Rejected"] = "当前模型不接受工具调用，本次按普通对话回答。",
        ["Assistant.Chat.System.Tier.Read"] = """
            【当前权限档位：只读】你现在只能读取与分析：崩溃报告、latest.log、mods 清单、实例里的文本文件、资源站与白名单站点。你没有下载、写入、删除、改启动器设置、执行脚本的能力，这些工具此刻根本没有挂载给你。

            需要动手时不要笼统说"我没有这个能力"，要说清楚"当前是只读模式，我无法操作文件"，并告诉玩家可以在聊天底部把权限切到「需要审批」或「完全控制」，或者自己在启动器里点哪几步。
            """,
        ["Assistant.Chat.System.Tier.Ask"] = """
            【当前权限档位：需要审批】除了读取，你可以执行：download_resource（用启动器自己的下载器把资源下到当前实例的对应目录）、write_instance_file（写实例里的文本文件，覆盖前会备份到实例下的 .pclce-assistant-backup）、delete_instance_file（把实例里的单个文件放进回收站，玩家还能还原，删不了目录）、set_launcher_config（改白名单里的启动器设置）、run_python（在当前实例目录下执行 Python）。每执行一个都会弹确认框，由玩家批准。

            【动手的规矩】先给诊断和完整方案，再明确问一句"需要我帮你改吗"；玩家同意了才调用这些工具，不要替他做决定。下载前先确认游戏版本与加载器：跟当前实例一致就不传，玩家另指定了就传 version 和 loader，挑不到匹配文件时如实说明、不要硬下不相干的版本。工具返回失败或被拒绝时如实说出来，绝对不要假装已经改好。改完要讲清：改了哪个文件/哪个设置、改成了什么、上一个值或备份在哪里、怎么撤销。下载类任务只是"已经开始"，不要声称下载完成。
            """,
        ["Assistant.Chat.System.Tier.Full"] = """
            【当前权限档位：完全控制】你可以执行：download_resource、write_instance_file、delete_instance_file（放进回收站，还能还原）、set_launcher_config、run_python，并且这些操作不会弹确认框，直接生效——没有人再替你把关。

            【动手的规矩】正因为不会有人工确认，更要先给诊断和完整方案、征得玩家同意再调用，一次只做玩家要的那一件事，不要顺手"优化"别的东西。下载前先确认游戏版本与加载器，挑不到匹配文件就如实说明。改完要讲清：改了哪个文件/哪个设置、改成了什么、上一个值或备份在哪里、怎么撤销。下载类任务只是"已经开始"，不要声称下载完成。
            """,
        ["Assistant.Chat.DeleteConfirm.Title"] = "删除对话",
        ["Assistant.Chat.DeleteConfirm"] = "确定要删除「{0}」吗？删除后无法恢复。",
        ["Assistant.Chat.LoadEarlier"] = "显示更早的 {0} 条消息",
        ["Assistant.Chat.Permission.Read"] = "只读",
        ["Assistant.Chat.Permission.Read.Tip"] = "只能读取和分析：看崩溃报告、日志、mods 清单、查资源站与白名单站点。下载资源、写文件、改设置、跑脚本这类执行工具根本不会挂载。点一下切到「需要审批」。",
        ["Assistant.Chat.Permission.Ask"] = "需要审批",
        ["Assistant.Chat.Permission.Ask.Tip"] = "读取与查询直接执行；下载资源、写文件、改启动器设置、执行脚本这些操作每一下都会先弹窗问你。点一下切到「完全控制」。",
        ["Assistant.Chat.Permission.Full"] = "完全控制",
        ["Assistant.Chat.Permission.Full.Tip"] = "执行类工具不再询问，直接动手。改过什么只记在每条回答左下角的痕迹里，不会再提醒。点一下切回「只读」。",
        ["Assistant.Approval.Title"] = "需要你的确认",
        ["Assistant.Approval.Content"] = "AI 请求执行：{0}\n参数：{1}\n当前权限档位是「需要审批」，允许吗？",
        ["Assistant.Approval.AllowAll"] = "本轮全部允许",
        ["Assistant.Chat.RoundsExhausted"] = "这一轮的工具调用次数已经用完，剩下的步骤没有执行。让它继续做，直接回一句\"继续\"即可。",
        ["Assistant.Chat.ClaimCorrection"] = "（系统提示）你刚才的回答说已经在下载或已经安装，但这一轮你没有调用过任何工具，也没有任何东西真的被执行。请重新回答：要么现在就去调用对应的工具，要么明确说还没开始、需要玩家点头后再做。不要重复刚才那段说法。",
        ["Assistant.Chat.AutoContinue"] = "（自动继续）你发起的任务已经全部结束：{0}。先用 list_instance_files 等只读工具核对实际结果，再继续下一步；没有可做的就明确说明并停下，不要凭空描述进度，也不要再发起新的下载。这一轮之后最多还能自动继续 {1} 次。",
        ["Assistant.Chat.AutoContinue.Limited"] = "自动继续的次数已经用完，剩下的步骤要你再开口的才会做。",
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
        ["Assistant.Left.Export"] = "Export",
        ["Assistant.Left.Export.Filter"] = "Markdown document|*.md",
        ["Assistant.Left.Export.Done"] = "Exported to {0}",
        ["Assistant.Left.Export.Failed"] = "Export failed: {0}",
        ["Assistant.Left.Delete"] = "Delete",
        ["Assistant.Left.Open"] = "Open folder",
        ["Assistant.Left.Rename"] = "Rename",
        ["Assistant.Left.Rename.Tip"] = "Give this conversation a name you will recognise (up to 30 characters)",
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
        ["Assistant.Tool.download_status"] = "Checking download progress",
        ["Assistant.Tool.download_resource"] = "Downloading a resource",
        ["Assistant.Tool.write_instance_file"] = "Writing an instance file",
        ["Assistant.Tool.delete_instance_file"] = "Recycling an instance file",
        ["Assistant.Tool.create_instance"] = "Creating an instance",
        ["Assistant.Tool.set_launcher_config"] = "Changing a launcher setting",
        ["Assistant.Tool.run_python"] = "Running a Python script",
        ["Assistant.Chat.System"] = """
            You are the Minecraft assistant built into PCL CE. Answer in the interface language.

            [Scope] What you are allowed to do is decided by the "permission tier" paragraph below - never go past it: do not claim to have used a tool that is not attached to you, and do not promise capabilities that do not exist yet (creating or deleting instances, and installing Java, are not possible today). When the player has to do something by hand, name the exact clicks in the launcher.

            [Tools] Read the current instance's crash reports, logs/latest.log and mods list, browse and read text files inside the instance folder, search Modrinth / CurseForge, and consult the allowlisted sites (MC 百科, ModpackIndex, BMCLAPI, Mojang version manifest, Fabric, Forge, NeoForge, Quilt, LiteLoader, OptiFine, GitHub). When information is missing, look it up with a tool instead of answering from memory.

            [Recommending] Match the current instance's game version and loader, and say so when something does not match; give both the localised and the original name (e.g. 钠 / Sodium); put the resource page link on its own line verbatim (e.g. https://modrinth.com/mod/sodium) so the launcher can render a clickable card, instead of bare version numbers. Once you know the player's Minecraft version and loader, append the filters to that link (e.g. https://modrinth.com/mod/sodium?mc=1.21.1&loader=fabric) so the card opens straight on a usable version; leave them off when you are not sure. Prefer sources most users can reach directly (Modrinth, BMCLAPI mirrors, MC 百科); when a resource only exists on CurseForge or another region-limited host, say a proxy may be needed. Version numbers, download counts and update times must come from tool output - if it cannot be found, say so.

            Keep answers short.
            """,
        ["Assistant.Chat.System.Intent"] = """
            [Mapping what the player says to a tool] "install / download / add" a mod, shader pack, texture pack or world -> download_resource: shader and resource packs install just as well, they are only files dropped into shaderpacks and resourcepacks; "remove / delete / uninstall" -> delete_instance_file; "change a config or option" -> write_instance_file; "change a launcher setting (memory, JVM args, window)" -> set_launcher_config; "compute / batch-process / analyse files" -> run_python; "make a new instance / install 1.21.1 / add Fabric to it" -> create_instance. All of these are within what you can actually do - do not claim otherwise.

            [Never narrate an action you did not take] Only say "started / downloading / installed" after you actually called a tool this round and it returned success; without a call, describe what you intend to do in the future tense. To report on tasks from earlier rounds, query download_status instead of guessing from memory.
            """,
        ["Assistant.Chat.System.Env"] = """
            [Current situation]
            {0}
            """,
        ["Assistant.Chat.System.Env.NoInstance"] = "No instance is selected right now: ask the player to pick one on the Instances page first.",
        ["Assistant.Chat.System.Env.Instance"] = "Current instance: {0}",
        ["Assistant.Chat.System.Env.Tier"] = "Permission tier: {0}",
        ["Assistant.Chat.System.Env.Tools"] = "Tools actually attached to you this round: {0}. Do not claim anything that is not on this list, and do not refuse something that is.",
        ["Assistant.Chat.System.Env.NoTools"] = "No tools are attached this round (the tool switch is off); answer from the conversation only.",
        ["Assistant.Chat.System.Env.Sites"] = "Sites you may read online: {0}",
        ["Assistant.Chat.System.Env.NoSites"] = "No sites are enabled, so you cannot verify anything online.",
        ["Assistant.Chat.System.NoCurseForge"] =
            "[No CurseForge key on this machine] search_curseforge and CurseForge site reads are not attached this run, " +
            "and CurseForge links will not render as cards. Prefer Modrinth, mcmod.cn and ModpackIndex, which need no proxy; " +
            "when something really is only on CurseForge, say plainly that this launcher cannot query CurseForge " +
            "instead of inventing download counts, versions or dates.",
        ["Assistant.Chat.Tools.Rejected"] = "This model does not accept tool calls, so the answer came from plain chat.",
        ["Assistant.Chat.System.Tier.Read"] = """
            [Permission tier: read only] You can currently only read and analyse: crash reports, latest.log, the mods list, text files inside the instance, resource sites and whitelisted pages. You have no ability to download, write, delete, change launcher settings or run scripts - those tools are not attached to you at all right now.

            When something needs doing, do not say a vague "I can't do that". Say plainly that this is read-only mode so you cannot touch files, and tell the player they can switch the tier at the bottom of the chat to "Ask first" or "Full control", or do the steps themselves in the launcher.
            """,
        ["Assistant.Chat.System.Tier.Ask"] = """
            [Permission tier: ask first] Besides reading, you can act: download_resource (hand the file to the launcher's own downloader, into the right folder of the current instance), write_instance_file (write a text file inside the instance; the previous version is backed up under .pclce-assistant-backup), delete_instance_file (move one file inside the instance to the recycle bin, where the player can restore it - directories cannot be deleted), set_launcher_config (change one whitelisted launcher setting), run_python (run Python in the current instance folder). Every one of these shows the player a confirmation dialog.

            [Rules for acting] Give the diagnosis and the full plan first, then explicitly ask "want me to apply this?" - only call these tools after the player agrees, never decide for them. Before downloading, settle the game version and loader: pass nothing when they match the current instance, pass version and loader when the player asked for something else, and if no file matches say so instead of downloading an unrelated build. If a tool fails or is refused, say so plainly; never pretend something was already changed. After a change, state what was touched, what it is now, where the old value or backup lives, and how to undo it. A download call only means it started - do not claim it finished.
            """,
        ["Assistant.Chat.System.Tier.Full"] = """
            [Permission tier: full control] You can act: download_resource, write_instance_file, delete_instance_file (to the recycle bin, still recoverable), set_launcher_config, run_python - and none of these show a confirmation dialog, they take effect immediately. Nobody is checking your work for you.

            [Rules for acting] Precisely because nothing asks for confirmation: give the diagnosis and the plan first, get the player's agreement in chat before calling a tool, do only the one thing they asked for, and never "improve" anything on the side. Before downloading, settle the game version and loader, and if no file matches, say so. After a change, state what was touched, what it is now, where the old value or backup lives, and how to undo it. A download call only means it started - do not claim it finished.
            """,
        ["Assistant.Chat.DeleteConfirm.Title"] = "Delete conversation",
        ["Assistant.Chat.DeleteConfirm"] = "Delete “{0}”? This cannot be undone.",
        ["Assistant.Chat.LoadEarlier"] = "Show {0} earlier messages",
        ["Assistant.Chat.Permission.Read"] = "Read only",
        ["Assistant.Chat.Permission.Read.Tip"] = "Analysis only: crash reports, logs, the mods list, resource sites and whitelisted pages. Action tools (downloads, writes, settings, scripts) are not attached at all. Click to switch to Ask first.",
        ["Assistant.Chat.Permission.Ask"] = "Ask first",
        ["Assistant.Chat.Permission.Ask.Tip"] = "Reading and searching run straight away; downloading, writing files, changing launcher settings and running scripts each ask you first. Click to switch to Full control.",
        ["Assistant.Chat.Permission.Full"] = "Full control",
        ["Assistant.Chat.Permission.Full.Tip"] = "Action tools run without asking. What changed is only recorded in the per-answer trace at the bottom-left of the card, with no further prompts. Click to go back to Read only.",
        ["Assistant.Approval.Title"] = "Approval needed",
        ["Assistant.Approval.Content"] = "The assistant wants to run: {0}\nArguments: {1}\nYour permission tier is \"Ask first\" - allow it?",
        ["Assistant.Approval.AllowAll"] = "Allow all this round",
        ["Assistant.Chat.RoundsExhausted"] = "This answer ran out of tool-call rounds, so the remaining steps were not executed. Reply \"go on\" and it will continue.",
        ["Assistant.Chat.ClaimCorrection"] = "(system note) Your previous answer claimed something was downloading or installed, but you called no tool this round and nothing actually ran. Answer again: either call the tool now, or say plainly that nothing has started yet and you need the player's go-ahead. Do not repeat the earlier claim.",
        ["Assistant.Chat.AutoContinue"] = "(auto continue) The tasks you started have all finished: {0}. Verify the real result with a read-only tool such as list_instance_files, then take the next step; if there is nothing left to do, say so and stop. Do not narrate progress you have not checked, and do not queue new downloads. Up to {1} more automatic continuations remain for this request.",
        ["Assistant.Chat.AutoContinue.Limited"] = "The automatic continuations for this request are used up; it will wait for you to speak again.",
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
