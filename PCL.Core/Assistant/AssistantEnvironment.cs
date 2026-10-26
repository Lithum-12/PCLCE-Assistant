using System.Collections.Generic;
using System.Linq;

namespace PCL.Core.Assistant;

/// <summary>
/// 给模型的一段"你现在面对的是什么情况"。它此前会说自己装不了光影，
/// 就是因为只能凭训练记忆猜自己有什么工具、实例是什么版本；
/// 这里把实况摊开写出来，版本与加载器也一并给出，挑文件时就不用再猜。
/// </summary>
public static class AssistantEnvironment
{
    /// <summary>实例装了哪些加载器；一个都没有时明确说原版，免得模型以为可以随便下 mod。</summary>
    public static string DescribeLoaders(bool fabric, bool legacyFabric, bool quilt, bool neoForge, bool forge,
        bool liteLoader)
    {
        List<string> names = [];
        if (fabric) names.Add("Fabric");
        if (legacyFabric) names.Add("Legacy Fabric");
        if (quilt) names.Add("Quilt");
        if (neoForge) names.Add("NeoForge");
        if (forge) names.Add("Forge");
        if (liteLoader) names.Add("LiteLoader");
        return names.Count == 0 ? "原版（无加载器）" : string.Join(" + ", names);
    }

    /// <summary>一行实例概况：名字、游戏版本、加载器、是否版本隔离、模组文件数。</summary>
    public static string DescribeInstance(string instanceName, string gameVersion, string loaders, bool isolated,
        int modCount)
    {
        if (string.IsNullOrWhiteSpace(instanceName)) return "没有选中任何实例";
        var parts = new List<string>
        {
            instanceName,
            string.IsNullOrWhiteSpace(gameVersion) ? "游戏版本未知" : "Minecraft " + gameVersion,
            loaders,
            isolated ? "已开启版本隔离" : "未开启版本隔离",
            modCount + " 个模组文件"
        };
        return string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));
    }
}
