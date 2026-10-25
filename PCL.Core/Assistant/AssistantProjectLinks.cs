using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using PCL.Core.App;

namespace PCL.Core.Assistant;

public sealed record AssistantProjectLink(string Url, string Slug, bool Modrinth);

public sealed record AssistantProjectInfo(string Id, string Title, string Description, string IconUrl,
    string Source, string LoaderText, long Downloads, DateTime UpdatedAt);

/// <summary>回答正文里的一段：普通 Markdown 文字，或一条资源链接。</summary>
public readonly record struct AssistantSegment(bool IsCard, string Text);

/// <summary>
/// 识别回答里的 Modrinth / CurseForge 链接并查回工程信息。
/// 模型照常写链接就行，不依赖它学会某种自定义语法。
/// </summary>
public static class AssistantProjectLinks
{
    private const string ModrinthPattern =
        @"https?://modrinth\.com/(?:mod|modpack|resourcepack|datapack|world|plugin|customization)/[A-Za-z0-9\-_]+";

    // CurseForge 的分类段是 mc-mods / modpacks / texture-packs / worlds / shaders / addons / applications 等，
    // 逐个枚举很容易漏，所以只锁死 /minecraft/ 前缀，分类段放开
    private const string CurseForgePattern =
        @"https?://(?:www\.)?curseforge\.com/minecraft/[a-z][a-z\-]*/[A-Za-z0-9\-_]+";

    private static readonly Regex LinkRegex = new($"({ModrinthPattern}|{CurseForgePattern})",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Dictionary<string, AssistantProjectInfo?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 按出现顺序切分正文。命中链接时连同外层 <c>[说明](链接)</c> 一起吃掉，免得卡片和链接文字重复。
    /// </summary>
    public static List<AssistantSegment> Split(string text)
    {
        var segments = new List<AssistantSegment>();
        var cursor = 0;
        foreach (Match match in LinkRegex.Matches(text))
        {
            var start = match.Index;
            var end = match.Index + match.Length;
            ConsumeMarkdownLinkWrap(text, ref start, ref end);
            if (start < cursor) continue; // 上一条链接的收尾括号把这段吃掉了，跳过长在别人的壳里的匹配

            var before = text[cursor..start].Trim();
            if (before.Length > 0) segments.Add(new AssistantSegment(false, before));
            segments.Add(new AssistantSegment(true, match.Value));
            cursor = end;
        }

        var tail = text.Length > cursor ? text[cursor..].Trim() : "";
        if (tail.Length > 0) segments.Add(new AssistantSegment(false, tail));
        return segments;
    }

    private static void ConsumeMarkdownLinkWrap(string text, ref int start, ref int end)
    {
        if (start < 2 || text[start - 1] != '(' || text[start - 2] != ']') return;
        if (end >= text.Length || text[end] != ')') return;
        var open = text.LastIndexOf('[', start - 3);
        if (open < 0 || start - open > 130) return;
        start = open;
        end += 1;
    }

    public static AssistantProjectLink? Parse(string url)
    {
        var match = LinkRegex.Match(url);
        if (!match.Success) return null;
        var clean = match.Value.TrimEnd('.', ',', ';', '。', '，', '、', ')');
        var slug = clean[(clean.LastIndexOf('/') + 1)..];
        if (slug.Length == 0) return null;
        return new AssistantProjectLink(clean, slug, clean.Contains("modrinth.com", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<AssistantProjectInfo?> FetchAsync(AssistantProjectLink link)
    {
        if (Cache.TryGetValue(link.Url, out var cached)) return cached;
        var info = link.Modrinth ? await FetchModrinthAsync(link.Slug) : await FetchCurseForgeAsync(link.Slug);
        Cache[link.Url] = info;
        return info;
    }

    /// <summary>只读缓存，不发请求：卡片点击时先问一句，命中就省掉一个网络往返。</summary>
    public static bool TryPeek(AssistantProjectLink link, out AssistantProjectInfo? info) =>
        Cache.TryGetValue(link.Url, out info);

    /// <summary>给测试留的后门：同一 slug 不该为一张卡片发两次请求。</summary>
    internal static void ClearCache() => Cache.Clear();

    private static async Task<AssistantProjectInfo?> FetchModrinthAsync(string slug)
    {
        var body = await GetAsync($"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(slug)}");
        if (body is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            return new AssistantProjectInfo(
                Text(root, "id"),
                Text(root, "title"),
                Text(root, "description"),
                Text(root, "icon_url"),
                "Modrinth",
                BuildLoaderText(ReadArray(root, "loaders"), ReadArray(root, "game_versions")),
                root.TryGetProperty("downloads", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt64() : 0,
                ParseDate(Text(root, "updated")));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<AssistantProjectInfo?> FetchCurseForgeAsync(string slug)
    {
        // 没有 CurseForge 密钥时直接放弃卡片，聊天里保留普通链接。
        // 密钥走上游那套：CI 构建期注入，本地 Debug 可读 PCL_CURSEFORGE_API_KEY 环境变量
        if (string.IsNullOrWhiteSpace(Secrets.CurseForgeAPIKey)) return null;
        var body = await GetAsync($"https://api.curseforge.com/v1/mods/search?gameId=432&pageSize=1&slug={Uri.EscapeDataString(slug)}",
            Secrets.CurseForgeAPIKey);
        if (body is null) return null;
        return ParseCurseForgeHit(body);
    }

    /// <summary>给测试留的后门：响应形状改了至少能在这儿先发现。</summary>
    internal static AssistantProjectInfo? ParseCurseForgeHit(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
                return null;
            var item = data[0];
            return new AssistantProjectInfo(
                Text(item, "id"),
                Text(item, "name"),
                Text(item, "summary"),
                CurseForgeIcon(item),
                "CurseForge",
                "",
                item.TryGetProperty("downloadCount", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt64() : 0,
                ParseDate(FirstText(item, "dateReleased", "latestFilesDate")));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// /v1/mods/search 返回的是 FuzzyMod：图标在 logo.thumbnailUrl / logo.url，
    /// iconUrl 只有 /v1/mods/{id} 那种完整 Mod 对象才有，所以两个都认。
    /// 字段名与上游 ModComp._BuildFromCurseForge 读的保持一致。
    /// </summary>
    private static string CurseForgeIcon(JsonElement item)
    {
        if (item.TryGetProperty("logo", out var logo) && logo.ValueKind == JsonValueKind.Object)
        {
            var thumb = Text(logo, "thumbnailUrl");
            if (thumb.Length > 0) return thumb;
            var full = Text(logo, "url");
            if (full.Length > 0) return full;
        }

        return Text(item, "iconUrl");
    }

    private static string FirstText(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            var value = Text(item, name);
            if (value.Length > 0) return value;
        }

        return "";
    }

    private static async Task<string?> GetAsync(string url, string? apiKey = null) =>
        (await AssistantWeb.GetAsync(url, apiKey)).Body;

    private static string BuildLoaderText(List<string> loaders, List<string> versions)
    {
        var text = string.Join(" / ", loaders.Take(3));
        if (versions.Count > 0) text += (text.Length > 0 ? " " : "") + versions[0] + "+";
        return text;
    }

    private static List<string> ReadArray(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList()
            : [];

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText()
            : "";

    private static DateTime ParseDate(string text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value)
            ? value.ToLocalTime()
            : DateTime.MinValue;
}
