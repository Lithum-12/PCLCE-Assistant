using System;
using System.Collections.Generic;
using System.Linq;

namespace PCL.Core.Assistant;

/// <param name="Id">工具参数里用的短名。</param>
/// <param name="Host">唯一允许的域名，路径再自由也只在这个域里。</param>
/// <param name="Hint">给模型看的用法提示（接口形状它本来就会，这里只兜住入口）。</param>
public sealed record AssistantSite(string Id, string Label, string Host, string Hint);

/// <summary>
/// AI 可以联网查阅的站点白名单。工具本身只有一个 read_site，
/// 能不能访问某个站由这里的允许列表决定，设置页开关的就是它。
/// </summary>
public static class AssistantSites
{
    public const string NoneMarker = "none";

    public static readonly IReadOnlyList<AssistantSite> All =
    [
        new("mcmod", "MC 百科", "www.mcmod.cn", "MC 百科。搜索 /s?key=关键词，条目页 /Item/123.html、/Mod/123.html。中文资料首选。"),
        new("github", "GitHub", "api.github.com", "GitHub REST API（无需登录的公开接口）。如 /search/repositories?q=sodium、/repos/owner/name/releases/latest。"),
        new("modpackindex", "ModpackIndex", "api.modpacks.ch", "ModpackIndex（modpacks.ch）公开 API。/public/modpack/search/?term=skyfactory、/public/modpack/{id} 可查整合包与文件。"),
        new("mojang", "Mojang 版本库", "piston-meta.mojang.com", "Mojang 官方版本库。/mc/game/version_manifest_v2.json 列出全部版本，再取对应版本 json。"),
        new("bmclapi", "BMC API", "bmclapi2.bangbang93.com", "BMC 镜像 API，国内下载源与版本列表。/mc/game/version_manifest.json、/v3/maven/{坐标}。"),
        new("fabric", "Fabric", "meta.fabricmc.net", "Fabric 官方元数据。/v2/versions/loader/1.20.1 列出该游戏版本的 loader。"),
        new("forge", "Forge", "files.minecraftforge.net", "Forge 发布元数据。/net/minecraftforge/forge/promotions_slim.json 查各版本推荐构建。"),
        new("neoforge", "NeoForge", "maven.neoforged.net", "NeoForge Maven API。/api/maven/versions/releases/net/neoforged/neoforge 列出版本。"),
        new("quilt", "Quilt", "meta.quiltmc.org", "Quilt 官方元数据。/v3/versions/loader/1.20.1。"),
        new("liteloader", "LiteLoader", "dl.liteloader.com", "LiteLoader 版本表 /versions/versions.json。"),
        new("optifine", "OptiFine", "www.optifine.net", "OptiFine 官网（无公开 API，只能抓页面文本）。/downloads"),
        new("modrinth", "Modrinth", "api.modrinth.com", "Modrinth API。/v2/search?query=... 与 /v2/project/{slug}。"),
        new("curseforge", "CurseForge", "api.curseforge.com", "CurseForge 核心 API。/v1/mods/search?gameId=432&searchFilter=...（需要上游那枚构建期密钥，本地没有时这个站点会直接返回不可用）")
    ];

    public static AssistantSite? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(s => s.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 空列表表示"没配置过"，此时按默认全开；显式写入 ["none"] 才是全部关闭。
    /// </summary>
    public static bool Allows(string? id, IReadOnlyList<string>? allowed)
    {
        var site = Find(id);
        if (site is null) return false;
        if (allowed is null || allowed.Count == 0) return true;
        if (allowed.Contains(NoneMarker, StringComparer.OrdinalIgnoreCase)) return false;
        return allowed.Any(a => a.Equals(site.Id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>只允许站内相对路径；绝对地址、协议相对地址和越界路径一律拒绝。</summary>
    public static string? BuildUrl(AssistantSite site, string? path)
    {
        var value = (path ?? "").Trim();
        if (value.Length == 0) value = "/";
        if (!value.StartsWith('/')) value = "/" + value;
        if (value.StartsWith("//", StringComparison.Ordinal)) return null;
        if (value.Contains("://", StringComparison.Ordinal)) return null;
        if (value.Contains("..", StringComparison.Ordinal)) return null;
        if (value.Contains('\\')) return null;
        value = value.Replace(" ", "%20");
        // 路径里塞域名是最常见的绕法，直接看第一段是不是 host 形状
        var firstSegment = value.TrimStart('/').Split('/')[0];
        if (firstSegment.Contains(':') || firstSegment.EndsWith(".cn", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.EndsWith(".com", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.EndsWith(".net", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.EndsWith(".org", StringComparison.OrdinalIgnoreCase))
            return null;
        return $"https://{site.Host}{value}";
    }

    public static IReadOnlyList<string> DefaultAllowed() => All.Select(s => s.Id).ToList();
}
