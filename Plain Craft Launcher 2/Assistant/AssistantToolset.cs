using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Assistant;
using PCL.Core.Minecraft.Profile;

namespace PCL.Assistant;

/// <param name="Content">回给模型的正文。</param>
/// <param name="Note">状态条上那半句短摘要（多少字、几条、是否脱敏）。</param>
internal sealed record AssistantToolOutcome(string Content, string Note, bool Failed);

/// <summary>
/// 一次发送所绑定的只读工具集。实例在构造时就抓住，免得回答中途切换实例把两份数据混在一起。
/// 每次执行都会记一条痕迹，最后拼在回答末尾，让用户看得见到底发了什么出去。
/// </summary>
internal sealed class AssistantToolset
{
    private const int ContentBudgetChars = AssistantLocalReader.DefaultBudgetChars;
    private const int DefaultResults = 6;
    private const int MaxResults = 15;

    internal const string NoInstance =
        "当前没有选中任何游戏实例。请在启动器的「实例」页选中一个实例后再问，或者先让用户选择实例。";

    // 上游只在 CI 构建期注入这枚 key，正式构建之外它永远是空串——缺它时要把"为什么、这次怎么办"讲清楚，
    // 否则模型只会看到一个 403 然后开始编
    private const string NoCurseForgeKey =
        "这台机器上的启动器没有 CurseForge API 密钥（上游只在正式构建里注入，本地调试可以设 PCL_CURSEFORGE_API_KEY 环境变量），" +
        "所以 search_curseforge 与 CurseForge 站点查阅都不可用。这轮先改用 search_modrinth，" +
        "确实只有 CurseForge 才有某项资源时，直说查不到。";

    private static bool HasCurseForgeKey => !string.IsNullOrWhiteSpace(Secrets.CurseForgeAPIKey);

    private readonly McInstance? _instance;
    private readonly List<string> _allowedSites;
    private readonly List<string> _trace = [];

    public AssistantToolset(McInstance? instance)
    {
        _instance = instance;
        _allowedSites = Config.Assistant.AllowedSites ?? [];
        PermissionMode = AssistantPermissions.Parse(Config.Assistant.PermissionMode);
        Definitions = BuildDefinitions();
    }

    public IReadOnlyList<AssistantToolDef> Definitions { get; }

    /// <summary>这一次发送用的是哪一档权限；工具集是按次构造的，中途改设置要下一轮才生效。</summary>
    public AssistantPermissionMode PermissionMode { get; }

    public IReadOnlyList<string> Trace => _trace;

    private bool Redacts => Config.Assistant.RedactLocalContent;

    private static readonly Regex ScriptStyleRegex = new(@"(?is)<(script|style)[^>]*>.*?</\1>",
        RegexOptions.CultureInvariant);

    private static readonly Regex TagRegex = new(@"<[^>]{0,400}>", RegexOptions.CultureInvariant);

    private static readonly Regex WhitespaceRegex = new(@"\s{2,}", RegexOptions.CultureInvariant);

    // classId 与 URL 段照搬上游 ModComp 里那份跑通的表 curseForgeCategoryClassIds：
    // 猜的 id 要么筛不到东西，把 modpack 链接写成 mc-mods 更是直接 404。
    // 上游没收录的类型（世界、数据包）就不筛，交给 searchFilter 全文命中
    private static IReadOnlyDictionary<string, (string ClassId, string Segment)> CurseForgeClasses { get; } =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["mod"] = ("6", "mc-mods"),
            ["modpack"] = ("4471", "modpacks"),
            ["resourcepack"] = ("12", "texture-packs"),
            ["shader"] = ("6552", "shaders")
        };

    public async Task<AssistantToolOutcome> RunAsync(AssistantToolCall call, CancellationToken token)
    {
        var args = ReadArguments(call.ArgumentsJson);
        var definition = Definitions.FirstOrDefault(d => d.Name == call.Name);
        var isAction = definition?.Kind == AssistantToolKind.Action;
        if (definition is not null && isAction && PermissionMode == AssistantPermissionMode.Read)
        {
            // 只读档这些工具根本没挂载，走到这里说明模型在凭空调用
            AddTrace($"拒绝 {definition.Name}（只读档）");
            return new AssistantToolOutcome(Refused(definition.Name, "当前权限档位是「只读」，助手不具备执行能力。"),
                "只读档", false);
        }

        if (definition is not null && AssistantPermissions.NeedsApproval(PermissionMode, isAction) &&
            !Approve(definition, args))
        {
            AddTrace($"拒绝 {definition.Name}");
            return new AssistantToolOutcome(Refused(definition.Name, "用户在权限确认里拒绝了这次操作。"), "已拒绝", false);
        }

        var (content, note, failed) = call.Name switch
        {
            "read_crash_report" => ReadCrashReports(ToInt(Args(args, "max_files"), 2)),
            "read_latest_log" => ReadLatestLog(),
            "list_mods" => ListMods(),
            "search_modrinth" => await SearchModrinthAsync(args, token),
            "search_curseforge" => await SearchCurseForgeAsync(args, token),
            "list_instance_files" => ListInstanceFiles(Args(args, "path")),
            "read_instance_file" => ReadInstanceFile(Args(args, "path")),
            "read_site" => await ReadSiteAsync(Args(args, "site"), Args(args, "path"), token),
            "download_resource" => await AssistantActions.DownloadResource(_instance, args, token),
            "write_instance_file" => AssistantActions.WriteInstanceFile(_instance, args),
            "set_launcher_config" => AssistantActions.SetLauncherConfig(args),
            "run_python" => await AssistantActions.RunPython(_instance, args, token),
            _ => ($"没有名为 {call.Name} 的工具。", "", true)
        };

        return new AssistantToolOutcome(
            content.Length > ContentBudgetChars + 2000 ? content[..(ContentBudgetChars + 2000)] + "\n…" : content,
            note, failed);
    }

    /// <summary>状态条上要显示的图标与文案。</summary>
    public (string Icon, string Label) Describe(string toolName) => toolName switch
    {
        "read_crash_report" => ("scroll-text", Lang.Text("Assistant.Tool.read_crash_report")),
        "read_latest_log" => ("list", Lang.Text("Assistant.Tool.read_latest_log")),
        "list_mods" => ("package", Lang.Text("Assistant.Tool.list_mods")),
        "search_modrinth" => ("search", Lang.Text("Assistant.Tool.search_modrinth")),
        "search_curseforge" => ("search", Lang.Text("Assistant.Tool.search_curseforge")),
        "list_instance_files" => ("folder-open", Lang.Text("Assistant.Tool.list_instance_files")),
        "read_instance_file" => ("file-input", Lang.Text("Assistant.Tool.read_instance_file")),
        "read_site" => ("globe", Lang.Text("Assistant.Tool.read_site")),
        "download_resource" => ("download", Lang.Text("Assistant.Tool.download_resource")),
        "write_instance_file" => ("save", Lang.Text("Assistant.Tool.write_instance_file")),
        "set_launcher_config" => ("settings", Lang.Text("Assistant.Tool.set_launcher_config")),
        "run_python" => ("flask-conical", Lang.Text("Assistant.Tool.run_python")),
        _ => ("settings", Lang.Text("Assistant.Tool.unknown"))
    };

    private IReadOnlyList<AssistantToolDef> BuildDefinitions()
    {
        if (!Config.Assistant.ToolsEnabled) return [];

        List<AssistantToolDef> defs =
        [
            new("read_crash_report",
                "读取当前实例最新的崩溃报告（已截断，默认 2 份）。不需要参数。",
                [new AssistantToolParam("max_files", "可选，读取份数，1 到 3，默认 2")]),
            new("read_latest_log",
                "读取当前实例 logs/latest.log 的结尾若干字符（已截断）。不需要参数。",
                []),
            new("list_mods",
                "列出当前实例 mods 目录里的文件名，并给出游戏版本。不需要参数。",
                []),
            new("search_modrinth",
                "在 Modrinth 上搜索模组、整合包等资源，返回标题、slug、下载量与支持的版本。",
                [
                    new AssistantToolParam("query", "搜索关键词，用英文原名效果更好，例如 sodium、iris"),
                    new AssistantToolParam("version", "可选，Minecraft 版本号，例如 1.20.1"),
                    new AssistantToolParam("project_type", "可选，mod / modpack / resourcepack / datapack / worldgen / plugin"),
                    new AssistantToolParam("limit", "可选，返回条数，1 到 15，默认 6")
                ])
        ];

        if (HasCurseForgeKey)
            defs.Add(new("search_curseforge",
                "在 CurseForge 上搜索模组与整合包，返回名称、slug、下载量与简介。",
                [
                    new AssistantToolParam("query", "搜索关键词"),
                    new AssistantToolParam("project_type", "可选，mod / modpack / resourcepack / shader"),
                    new AssistantToolParam("limit", "可选，返回条数，1 到 15，默认 6")
                ]));

        // 放开到整个实例目录：崩溃报告只是其中一小部分，配置、启动脚本、模组冲突线索都在别处
        defs.Add(new("list_instance_files",
            "列出当前实例目录下的某个子目录，用于先看清结构再读文件。",
            [new AssistantToolParam("path", "相对实例根目录的路径，留空表示根目录，例如 logs、config、saves/世界名")]));
        defs.Add(new("read_instance_file",
            "读取当前实例目录下的任意一个文本文件（已截断，敏感文件会被拒绝）。",
            [new AssistantToolParam("path", "相对实例根目录的文件路径，例如 options.txt、logs/latest.log、config/sodium-options.json")]));

        var allowed = AllowedIds();
        if (allowed.Count > 0)
            defs.Add(new("read_site",
                "查阅一个白名单站点的公开页面或 API，返回文本（已截断）。可用站点：" + string.Join("、", allowed),
                [
                    new AssistantToolParam("site", "站点短名，只能是：" + string.Join("、", allowed)),
                    new AssistantToolParam("path", "站内相对路径，可带查询串，例如 /v2/versions/loader/1.20.1、/s?key=钠")
                ]));

        // 执行类工具：只读档不挂载，需要审批档逐个弹确认
        if (AssistantPermissions.AllowsActions(PermissionMode))
        {
            defs.Add(new("download_resource",
                "用启动器自己的下载器，把一个资源下载到当前实例对应的目录里（模组→mods、材质→resourcepacks、光影→shaderpacks、世界→saves）。" +
                "任务会真的开始跑，进度在启动器的下载面板里；调用之后不要声称已经下载完成。整合包与数据包不支持。" +
                "默认按当前实例的加载器与游戏版本挑文件；玩家指定了版本或加载器就传 version / loader，不要靠猜。",
                [
                    new AssistantToolParam("project", "Modrinth 的 slug（如 sodium）或 CurseForge 的数字 id"),
                    new AssistantToolParam("version", "可选，Minecraft 版本号，例如 1.21.1；不填就用当前实例的版本"),
                    new AssistantToolParam("loader", "可选，fabric / neoforge / forge / quilt / liteloader；不填就用当前实例装的加载器"),
                    new AssistantToolParam("file", "可选，指定文件 id；填了就跳过挑选")
                ], AssistantToolKind.Action));
            defs.Add(new("write_instance_file",
                "把一段文本写进当前实例目录里的一个文件（覆盖前会自动备份到实例下的 " +
                AssistantLocalWriter.BackupFolder + " 目录）。只允许文本类扩展名，启动器自己的 PCL 目录与密钥库拒绝写入。",
                [
                    new AssistantToolParam("path", "相对实例根目录的路径，例如 options.txt、config/sodium-options.json"),
                    new AssistantToolParam("content", "要写入的完整文件内容")
                ], AssistantToolKind.Action));
            defs.Add(new("set_launcher_config",
                "修改启动器的一项设置。可改的键是白名单，改动前会先读出上一个值以便回滚。",
                [
                    new AssistantToolParam("key", "配置键名，只能是：" + string.Join("、", AssistantConfigKeys.Allowed)),
                    new AssistantToolParam("value", "新值，按该键的类型给（布尔给 true/false，数字给数字）")
                ], AssistantToolKind.Action));
            defs.Add(new("run_python",
                "在当前实例目录下执行一段 Python 脚本，返回 stdout/stderr 与退出码。" +
                "用来做批量分析或按方案改配置；解释器按 py → python → python3 顺序找。",
                [
                    new AssistantToolParam("script", "要执行的完整 Python 源码"),
                    new AssistantToolParam("timeout_seconds", "可选，超时秒数，5 到 300，默认 60")
                ], AssistantToolKind.Action));
        }

        return defs;
    }

    private List<string> AllowedIds() => AssistantSites.All
        .Where(s => AssistantSites.Allows(s.Id, _allowedSites))
        .Select(s => s.Id)
        .ToList();

    /// <summary>执行类工具的确认框：把要做什么、对哪儿做说清楚。拿不到主窗体时按拒绝处理，不默认放行。</summary>
    private bool Approve(AssistantToolDef definition, Dictionary<string, string> args)
    {
        if (ModMain.frmMain is null) return false;
        var (_, label) = Describe(definition.Name);
        var detail = string.Join("，", args.Where(p => !string.IsNullOrEmpty(p.Value))
            .Select(p => $"{p.Key}={Shorten(p.Value, 80)}"));
        var result = ModMain.MyMsgBox(
            Lang.Text("Assistant.Approval.Content", label, detail.Length == 0 ? "（无参数）" : detail),
            Lang.Text("Assistant.Approval.Title"),
            Lang.Text("Common.Action.Confirm"), Lang.Text("Common.Action.Cancel"), isWarn: true);
        if (result == 1) AddTrace($"经你同意才执行 {label}");
        return result == 1;
    }

    private static string Refused(string toolName, string reason) =>
        $"{reason} 不要重试同一个操作；基于已经拿到的信息继续回答，" +
        "或者把方案讲清楚、让用户自己在启动器里动手。";

    private (string Content, string Note, bool Failed) ReadCrashReports(int maxFiles)
    {
        if (_instance is null) return (NoInstance, "", false);

        var root = _instance.PathIndie;
        var reports = AssistantLocalReader.ReadCrashReports(root, Clamp(maxFiles, 1, 3));
        if (reports.Count == 0)
            return ($"{root} 下的 crash-reports 目录没有可分析的崩溃报告（要么没崩溃过，要么目录不存在）。", "无崩溃报告", false);

        var builder = new StringBuilder(Header());
        var sent = 0;
        foreach (var report in reports)
        {
            var text = Protect(report.Text);
            sent += text.Length;
            builder.Append($"=== {report.Source}（{(report.Truncated ? "已截断" : "完整")}，读到 {report.RawChars} 字，送出 {text.Length} 字）===\n")
                .Append(text).Append('\n');
        }

        AddTrace($"崩溃报告 {reports.Count} 份，送出 {FormatSize(sent)}");
        return (builder.ToString(), $"{FormatSize(sent)}{(Redacts ? "·已脱敏" : "·未脱敏")}", false);
    }

    private (string Content, string Note, bool Failed) ReadLatestLog()
    {
        if (_instance is null) return (NoInstance, "", false);

        var log = AssistantLocalReader.ReadLatestLog(_instance.PathIndie);
        if (log is null)
            return ($"{_instance.PathIndie} 下的 logs/latest.log 不存在，这个实例可能还没成功启动过。", "无日志", false);

        var text = Protect(log.Text);
        AddTrace($"latest.log 尾部 {FormatSize(text.Length)}{(log.Truncated ? "（已截断）" : "")}");
        return (Header() + $"=== {log.Source}（读到 {log.RawChars} 字，送出 {text.Length} 字）===\n" + text,
            $"{FormatSize(text.Length)}{(Redacts ? "·已脱敏" : "·未脱敏")}", false);
    }

    private (string Content, string Note, bool Failed) ListInstanceFiles(string relative)
    {
        if (_instance is null) return (NoInstance, "", false);

        var root = _instance.PathIndie;
        var path = string.IsNullOrWhiteSpace(relative)
            ? Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : AssistantLocalReader.ResolveInstancePath(root, relative);
        if (path is null) return ("只能访问实例目录内的路径，且不能包含 .. 或盘符。", "拒绝", true);

        var entries = AssistantLocalReader.ListDirectory(path);
        if (entries.Count == 0) return ($"{relative} 这个目录不存在或者是空的。", "0 项", false);

        AddTrace($"目录清单 {relative} 共 {entries.Count} 项");
        return ($"实例 {_instance.Name} 的目录 {path}（{entries.Count} 项）：\n" + string.Join("\n", entries),
            $"{entries.Count} 项", false);
    }

    private (string Content, string Note, bool Failed) ReadInstanceFile(string relative)
    {
        if (_instance is null) return (NoInstance, "", false);

        var root = _instance.PathIndie;
        var path = AssistantLocalReader.ResolveInstancePath(root, relative);
        if (path is null)
            return ($"{relative} 不在实例目录内，或属于被拒绝的敏感文件（启动器配置、密钥库等）。", "拒绝", true);
        if (Directory.Exists(path)) return ($"{relative} 是目录，请改用 list_instance_files。", "是目录", false);
        if (!File.Exists(path)) return ($"{relative} 不存在。", "无此文件", false);

        var file = AssistantLocalReader.ReadFile(root, path);
        if (file is null) return ($"{relative} 读取失败。", "失败", true);

        var text = Protect(file.Text);
        AddTrace($"文件 {file.Source} 送出 {FormatSize(text.Length)}");
        return ($"=== {file.Source}（{(file.Truncated ? "已截断" : "完整")}，读到 {file.RawChars} 字，送出 {text.Length} 字）===\n" +
                text, $"{FormatSize(text.Length)}{(Redacts ? "·已脱敏" : "·未脱敏")}", false);
    }

    private async Task<(string Content, string Note, bool Failed)> ReadSiteAsync(string siteId, string path,
        CancellationToken token)
    {
        var site = AssistantSites.Find(siteId);
        if (site is null) return ($"没有名为 {siteId} 的站点。可用：" + string.Join("、", AllowedIds()), "未知站点", true);

        if (!AssistantSites.Allows(site.Id, _allowedSites))
            return ($"{site.Label} 没有在 设置 → AI 助手 → 可查阅的站点 中开启。", "未开启", true);

        var url = AssistantSites.BuildUrl(site, path);
        if (url is null) return ($"只接受 {site.Label} 站内的相对路径，例如 /v2/versions/loader/1.20.1。", "路径非法", true);

        // CurseForge 的开放接口全都要 x-api-key，不带 key 只能拿到 403
        string? apiKey = null;
        if (site.Id.Equals("curseforge", StringComparison.OrdinalIgnoreCase))
        {
            if (!HasCurseForgeKey) return (NoCurseForgeKey, "无密钥", true);
            apiKey = Secrets.CurseForgeAPIKey;
        }

        var (body, error) = await AssistantWeb.GetAsync(url, apiKey, token);
        if (body is null) return ($"读取 {url} 失败：{error}", "失败", true);

        var text = Protect(CleanBody(body));
        AddTrace($"查阅 {site.Label} {new Uri(url).AbsolutePath}");
        return ($"GET {url}\n" + text, $"{FormatSize(text.Length)}{(Redacts ? "·已脱敏" : "·未脱敏")}", false);
    }

    /// <summary>JSON 原样给模型，HTML 剥掉标签再给，否则一半预算都花在标签上。</summary>
    private static string CleanBody(string body)
    {
        var head = body.TrimStart();
        if (head.StartsWith('{') || head.StartsWith('[')) return Shorten(body, ContentBudgetChars);

        var text = ScriptStyleRegex.Replace(body, " ");
        text = TagRegex.Replace(text, " ");
        text = text.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&quot;", "\"").Replace("&#39;", "'").Replace("&nbsp;", " ");
        return Shorten(WhitespaceRegex.Replace(text, " ").Trim(), ContentBudgetChars);
    }

    private (string Content, string Note, bool Failed) ListMods()
    {
        if (_instance is null) return (NoInstance, "", false);

        // 开了版本隔离时 mods 在实例目录，没开时在 Minecraft 根目录，两处都认
        var dirs = new[] { _instance.PathInstance, _instance.PathIndie }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        List<string> names = [];
        string? used = null;
        foreach (var dir in dirs)
        {
            var found = AssistantLocalReader.ReadModFileNames(dir);
            if (found.Count <= 0) continue;
            names.AddRange(found);
            used ??= Path.Combine(dir, "mods");
        }

        if (names.Count == 0)
        {
            AddTrace("模组列表 0 项");
            return ($"{Header()}mods 目录是空的（或者不存在），这个实例没有安装任何模组文件。", "0 个文件", false);
        }

        AddTrace($"模组列表 {names.Count} 项");
        var builder = new StringBuilder($"{Header()}mods 目录：{used}\n共 {names.Count} 个文件：\n");
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            builder.Append("- ").Append(name).Append('\n');
        return (builder.ToString(), $"{names.Count} 个文件", false);
    }

    private async Task<(string Content, string Note, bool Failed)> SearchModrinthAsync(
        Dictionary<string, string> args, CancellationToken token)
    {
        var query = Args(args, "query");
        if (query.Length == 0) return ("缺少 query 参数，无法搜索。", "", true);

        var limit = Clamp(ToInt(Args(args, "limit"), DefaultResults), 1, MaxResults);
        var facets = BuildModrinthFacets(Args(args, "version"), Args(args, "project_type"));
        var url = new StringBuilder($"https://api.modrinth.com/v2/search?limit={limit}&index=relevance&offset=0&query={WebUtility.UrlEncode(query)}");
        if (facets is not null) url.Append("&facets=").Append(WebUtility.UrlEncode(facets));

        var (body, error) = await AssistantWeb.GetAsync(url.ToString(), null, token);
        if (body is null) return ($"Modrinth 搜索失败：{error}", "失败", true);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var total = doc.RootElement.TryGetProperty("total_hits", out var t) && t.ValueKind == JsonValueKind.Number
                ? t.GetInt64()
                : -1L;
            if (doc.RootElement.TryGetProperty("hits", out var hits) && hits.ValueKind == JsonValueKind.Array)
            {
                var count = hits.GetArrayLength();
                var builder = new StringBuilder($"Modrinth 搜索 “{query}”，命中 {total} 条，返回前 {count} 条：\n");
                foreach (var hit in hits.EnumerateArray())
                    builder.Append("- ").Append(FormatModrinthHit(hit)).Append('\n');
                AddTrace($"Modrinth 搜索 “{query}”");
                return (builder.ToString(), $"{count} 条", false);
            }

            return ("Modrinth 响应里没有 hits 数组。", "空响应", true);
        }
        catch (JsonException)
        {
            return ("Modrinth 返回的内容不是合法 json。", "解析失败", true);
        }
    }

    private async Task<(string Content, string Note, bool Failed)> SearchCurseForgeAsync(
        Dictionary<string, string> args, CancellationToken token)
    {
        var query = Args(args, "query");
        if (query.Length == 0) return ("缺少 query 参数，无法搜索。", "", true);
        if (!HasCurseForgeKey) return (NoCurseForgeKey, "无密钥", true);

        var limit = Clamp(ToInt(Args(args, "limit"), DefaultResults), 1, 50);
        var type = Args(args, "project_type");
        var known = CurseForgeClasses.TryGetValue(type, out var cls);
        var url = new StringBuilder($"https://api.curseforge.com/v1/mods/search?gameId=432&sortField=2&sortOrder=desc&pageSize={limit}");
        if (known) url.Append("&classId=").Append(cls.ClassId);
        url.Append("&searchFilter=").Append(WebUtility.UrlEncode(query));

        var (body, error) = await AssistantWeb.GetAsync(url.ToString(), Secrets.CurseForgeAPIKey, token);
        if (body is null) return ($"CurseForge 搜索失败：{error}", "失败", true);

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return ("CurseForge 响应里没有 data 数组。", "空响应", true);
            var count = data.GetArrayLength();
            var builder = new StringBuilder($"CurseForge 搜索 “{query}”，返回 {count} 条：\n");
            foreach (var item in data.EnumerateArray())
            {
                var name = Str(item, "name");
                var slug = Str(item, "slug");
                var summary = Shorten(Str(item, "summary"), 120);
                // 页面地址优先用响应里的 links.websiteUrl：分类段是自己拼的，拼错就是 404
                var page = Nested(item, "links", "websiteUrl");
                if (page.Length == 0)
                    page = $"https://www.curseforge.com/minecraft/{(known ? cls.Segment : "mc-mods")}/{slug}";
                builder.Append("- ").Append(name)
                    .Append("（下载 ").Append(Str(item, "downloadCount")).Append("）")
                    .Append(string.IsNullOrEmpty(summary) ? "" : $"：{summary}")
                    .Append($"\n  页面 {page}").Append('\n');
            }

            AddTrace($"CurseForge 搜索 “{query}”");
            return (builder.ToString(), $"{count} 条", false);
        }
        catch (JsonException)
        {
            return ("CurseForge 返回的内容不是合法 json。", "解析失败", true);
        }
    }

    private static string FormatModrinthHit(JsonElement hit)
    {
        var versions = hit.TryGetProperty("versions", out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrEmpty(x)).Cast<string>().ToList()
            : [];
        var versionText = versions.Count == 0
            ? "版本未知"
            : string.Join(",", versions.Take(6)) + (versions.Count > 6 ? $"…（共 {versions.Count} 个）" : "");
        var categories = hit.TryGetProperty("categories", out var c) && c.ValueKind == JsonValueKind.Array
            ? string.Join("/", c.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrEmpty(x)).Take(4))
            : "";
        // 页面链接单独一行原样给出：聊天页会把它渲染成可点开的资源卡片
        var type = Str(hit, "project_type");
        return $"{Str(hit, "title")}（{type}，下载 {Str(hit, "downloads")}，{versionText}" +
               $"{(categories.Length == 0 ? "" : "，分类 " + categories)}）：" + Shorten(Str(hit, "description"), 120) +
               $"\n  页面 https://modrinth.com/{type}/{Str(hit, "slug")}";
    }

    private static string? BuildModrinthFacets(string version, string projectType)
    {
        List<string> groups = [];
        if (!string.IsNullOrWhiteSpace(version)) groups.Add($"[\"versions:{version.Trim()}\"]");
        if (!string.IsNullOrWhiteSpace(projectType)) groups.Add($"[\"project_types:{projectType.Trim().ToLowerInvariant()}\"]");
        return groups.Count == 0 ? null : "[" + string.Join(",", groups) + "]";
    }

    private string Header() => $"实例：{_instance?.Name}｜游戏版本：{AssistantLocalReader.ReadGameVersion(_instance?.PathIndie ?? "")}｜";

    private string Protect(string text)
    {
        if (!Redacts) return text;
        string[] terms = [Environment.UserName, ProfileService.Current?.UserName ?? ""];
        return AssistantRedactor.Redact(text, terms.Where(t => !string.IsNullOrWhiteSpace(t)));
    }

    private void AddTrace(string note) => _trace.Add(Redacts ? note + "（已脱敏）" : note + "（未脱敏）");

    private static Dictionary<string, string> ReadArguments(string? json)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var property in doc.RootElement.EnumerateObject())
                result[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? "",
                    JsonValueKind.Number => property.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => property.Value.GetRawText()
                };
        }
        catch (JsonException)
        {
            // 参数不是合法 json 时按无参数处理，让工具自己给模型解释
        }

        return result;
    }

    private static string Args(Dictionary<string, string> args, string key) =>
        args.TryGetValue(key, out var value) ? value.Trim() : "";

    private static int ToInt(string? text, int fallback) =>
        int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static int Clamp(int value, int min, int max) => Math.Min(Math.Max(value, min), max);

    private static string Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : value.GetRawText()
            : "";

    private static string Nested(JsonElement element, string outer, string inner) =>
        element.TryGetProperty(outer, out var o) && o.ValueKind == JsonValueKind.Object ? Str(o, inner) : "";

    private static string Shorten(string text, int max) =>
        string.IsNullOrEmpty(text) || text.Length <= max ? text ?? "" : text[..max] + "…";

    private static string FormatSize(int chars) => $"{chars:N0} 字";
}
