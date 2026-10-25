namespace PCL.Core.Assistant;

/// <summary>
/// 加载器名字的归一化。模型会写 NeoForge、neoforge、Neo Forge、fabricmc，
/// 这里统一成一小撮标准名，界面层再翻成上游的 CompLoaderType。
/// </summary>
public static class AssistantLoaders
{
    /// <returns>标准名；认不出来或空值返回 null，表示"不做加载器筛选"。</returns>
    public static string? Normalize(string? raw)
    {
        var text = (raw ?? "").Trim().Replace(" ", "").Replace("-", "").ToLowerInvariant();
        return text switch
        {
            "" or "any" or "none" => null,
            "fabric" or "fabricmc" or "fabricloader" => "fabric",
            "legacyfabric" => "legacyfabric",
            "neoforge" or "neo" => "neoforge",
            "forge" or "minecraftforge" => "forge",
            "quilt" or "quiltloader" => "quilt",
            "liteloader" or "lite" => "liteloader",
            "cleanroom" => "cleanroom",
            _ => null
        };
    }
}
