using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.App;
using PCL.Core.Assistant;
using PCL.Core.IO.Net;
using PCL.Core.Minecraft.Profile;

namespace PCL.Assistant;

/// <summary>
/// 一次发送所绑定的只读工具集。实例在构造时就抓住，免得回答中途切换实例把两份数据混在一起。
/// 每次执行都会记一条痕迹，最后拼在回答末尾，让用户看得见到底发了什么出去。
/// </summary>
internal sealed class AssistantToolset
{
    private const int ContentBudgetChars = AssistantLocalReader.DefaultBudgetChars;
    private const int DefaultResults = 6;
    private const int MaxResults = 15;

    private const string NoInstance =
        "当前没有选中任何游戏实例。请在启动器的「实例」页选中一个实例后再问，或者先让用户选择实例。";

    private readonly McInstance? _instance;
    private readonly List<string> _trace = [];

    public AssistantToolset(McInstance? instance)
    {
        _instance = instance;
        Definitions = BuildDefinitions();
    }

    public IReadOnlyList<AssistantToolDef> Definitions { get; }

    public IReadOnlyList<string> Trace => _trace;

    private bool Redacts => Config.Assistant.RedactLocalContent;

    private static IReadOnlyDictionary<string, string> CurseForgeClassIds { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mod"] = "6",
            ["modpack"] = "4471",
            ["resourcepack"] = "12",
            ["datapack"] = "6945",
            ["world"] = "17"
        };

    public async Task<string> RunAsync(AssistantToolCall call, CancellationToken token)
    {
        var args = ReadArguments(call.ArgumentsJson);
        var content = call.Name switch
        {
            "read_crash_report" => ReadCrashReports(ToInt(Args(args, "max_files"), 2)),
            "read_latest_log" => ReadLatestLog(),
            "list_mods" => ListMods(),
            "search_modrinth" => await SearchModrinthAsync(args, token),
            "search_curseforge" => await SearchCurseForgeAsync(args, token),
            _ => $"没有名为 {call.Name} 的工具。"
        };

        return content.Length > ContentBudgetChars + 2000 ? content[..(ContentBudgetChars + 2000)] + "\n…" : content;
    }

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

        if (!string.IsNullOrWhiteSpace(Secrets.CurseForgeAPIKey))
            defs.Add(new("search_curseforge",
                "在 CurseForge 上搜索模组与整合包，返回名称、slug、下载量与简介。",
                [
                    new AssistantToolParam("query", "搜索关键词"),
                    new AssistantToolParam("project_type", "可选，mod / modpack / resourcepack / datapack / world"),
                    new AssistantToolParam("limit", "可选，返回条数，1 到 15，默认 6")
                ]));

        return defs;
    }

    private string ReadCrashReports(int maxFiles)
    {
        if (_instance is null) return NoInstance;

        var root = _instance.PathIndie;
        var reports = AssistantLocalReader.ReadCrashReports(root, Clamp(maxFiles, 1, 3));
        if (reports.Count == 0)
            return $"{root} 下的 crash-reports 目录没有可分析的崩溃报告（要么没崩溃过，要么目录不存在）。";

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
        return builder.ToString();
    }

    private string ReadLatestLog()
    {
        if (_instance is null) return NoInstance;

        var log = AssistantLocalReader.ReadLatestLog(_instance.PathIndie);
        if (log is null)
            return $"{_instance.PathIndie} 下的 logs/latest.log 不存在，这个实例可能还没成功启动过。";

        var text = Protect(log.Text);
        AddTrace($"latest.log 尾部 {FormatSize(text.Length)}{(log.Truncated ? "（已截断）" : "")}");
        return Header() + $"=== {log.Source}（读到 {log.RawChars} 字，送出 {text.Length} 字）===\n" + text;
    }

    private string ListMods()
    {
        if (_instance is null) return NoInstance;

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

        var version = AssistantLocalReader.ReadGameVersion(_instance.PathIndie);
        if (names.Count == 0)
        {
            AddTrace("模组列表 0 项");
            return $"{Header()}mods 目录是空的（或者不存在），这个实例没有安装任何模组文件。";
        }

        AddTrace($"模组列表 {names.Count} 项");
        var builder = new StringBuilder($"{Header()}mods 目录：{used}\n共 {names.Count} 个文件：\n");
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            builder.Append("- ").Append(name).Append('\n');
        return builder.ToString();
    }

    private async Task<string> SearchModrinthAsync(Dictionary<string, string> args, CancellationToken token)
    {
        var query = Args(args, "query");
        if (query.Length == 0) return "缺少 query 参数，无法搜索。";

        var limit = Clamp(ToInt(Args(args, "limit"), DefaultResults), 1, MaxResults);
        var facets = BuildModrinthFacets(Args(args, "version"), Args(args, "project_type"));
        var url = new StringBuilder($"search?limit={limit}&index=relevance&offset=0&query={WebUtility.UrlEncode(query)}");
        if (facets is not null) url.Append("&facets=").Append(WebUtility.UrlEncode(facets));

        var (body, error) = await GetAsync(NetworkService.ModrinthApi, url.ToString(), token);
        if (body is null) return $"Modrinth 搜索失败：{error}";

        try
        {
            using var doc = JsonDocument.Parse(body);
            var total = doc.RootElement.TryGetProperty("total_hits", out var t) && t.ValueKind == JsonValueKind.Number
                ? t.GetInt64()
                : -1L;
            if (doc.RootElement.TryGetProperty("hits", out var hits) && hits.ValueKind == JsonValueKind.Array)
            {
                var builder = new StringBuilder($"Modrinth 搜索 “{query}”，命中 {total} 条，返回前 {hits.GetArrayLength()} 条：\n");
                foreach (var hit in hits.EnumerateArray())
                    builder.Append("- ").Append(FormatModrinthHit(hit)).Append('\n');
                AddTrace($"Modrinth 搜索 “{query}”");
                return builder.ToString();
            }

            return "Modrinth 响应里没有 hits 数组。";
        }
        catch (JsonException)
        {
            return "Modrinth 返回的内容不是合法 json。";
        }
    }

    private async Task<string> SearchCurseForgeAsync(Dictionary<string, string> args, CancellationToken token)
    {
        var query = Args(args, "query");
        if (query.Length == 0) return "缺少 query 参数，无法搜索。";

        var limit = Clamp(ToInt(Args(args, "limit"), DefaultResults), 1, 50);
        var url = new StringBuilder($"mods/search?gameId=432&sortField=2&sortOrder=desc&pageSize={limit}");
        if (CurseForgeClassIds.TryGetValue(Args(args, "project_type"), out var classId))
            url.Append("&classId=").Append(classId);
        url.Append("&searchFilter=").Append(WebUtility.UrlEncode(query));

        var (body, error) = await GetAsync(NetworkService.CurseForgeApi, url.ToString(), token);
        if (body is null) return $"CurseForge 搜索失败：{error}";

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return "CurseForge 响应里没有 data 数组。";
            var builder = new StringBuilder($"CurseForge 搜索 “{query}”，返回 {data.GetArrayLength()} 条：\n");
            foreach (var item in data.EnumerateArray())
            {
                var name = Str(item, "name");
                var slug = Str(item, "slug");
                var summary = Shorten(Str(item, "summary"), 120);
                builder.Append("- ").Append(name).Append("（slug=").Append(slug)
                    .Append("，下载 ").Append(Str(item, "downloadCount")).Append("）")
                    .Append(string.IsNullOrEmpty(summary) ? "" : $"：{summary}").Append('\n');
            }

            AddTrace($"CurseForge 搜索 “{query}”");
            return builder.ToString();
        }
        catch (JsonException)
        {
            return "CurseForge 返回的内容不是合法 json。";
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
        return $"{Str(hit, "title")}（{Str(hit, "project_type")}，slug={Str(hit, "slug")}，下载 {Str(hit, "downloads")}，" +
               $"{versionText}{(categories.Length == 0 ? "" : "，分类 " + categories)}）：" + Shorten(Str(hit, "description"), 120);
    }

    private static string? BuildModrinthFacets(string version, string projectType)
    {
        List<string> groups = [];
        if (!string.IsNullOrWhiteSpace(version)) groups.Add($"[\"versions:{version.Trim()}\"]");
        if (!string.IsNullOrWhiteSpace(projectType)) groups.Add($"[\"project_types:{projectType.Trim().ToLowerInvariant()}\"]");
        return groups.Count == 0 ? null : "[" + string.Join(",", groups) + "]";
    }

    private async Task<(string? Body, string? Error)> GetAsync(string clientName, string relativeUri,
        CancellationToken token)
    {
        try
        {
            var client = NetworkService.GetClient(clientName);
            using var response = await client.GetAsync(new Uri(relativeUri, UriKind.Relative), token);
            var text = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode)
                return (null, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}：{Shorten(text, 160)}");
            return (text, null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw; // 用户按了停止，交给上层处理
        }
        catch (Exception e)
        {
            // 超时也会以 TaskCanceledException 抛出，这里必须兜住，否则整段对话被判定成"已停止"
            return (null, e.Message);
        }
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

    private static string Shorten(string text, int max) =>
        string.IsNullOrEmpty(text) || text.Length <= max ? text ?? "" : text[..max] + "…";

    private static string FormatSize(int chars) => $"{chars:N0} 字";
}
