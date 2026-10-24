using System.Collections.Generic;
using PCL.Core.App.Configuration;

namespace PCL.Core.App;

/// <summary>
/// AI 助手配置。声明在 fork 私有文件中，避免与上游 Config.cs 冲突。
/// </summary>
public static partial class Config
{
    [ConfigGroup("Assistant")] partial class AssistantConfigGroup
    {
        [ConfigItem<bool>("AssistantEnabled", true)] public partial bool Enabled { get; set; }

        [ConfigItem<string>("AssistantApiBaseUrl", "")] public partial string ApiBaseUrl { get; set; }

        [ConfigItem<string>("AssistantModelName", "")] public partial string ModelName { get; set; }

        [ConfigItem<string>("AssistantApiKey", "", ConfigSource.SharedEncrypt)] public partial string ApiKey { get; set; }

        // 上游 v2.15.1 起按区域禁用下载/安装，本 fork 面向该区域的用户，默认放行
        [ConfigItem<bool>("AssistantAllowRestrictedFeatures", true)] public partial bool AllowRestrictedFeatures { get; set; }

        [ConfigItem<bool>("AssistantToolsEnabled", true)] public partial bool ToolsEnabled { get; set; }

        // 本地文件内容发出去之前先刮掉用户名、IP、坐标等，关掉它等于允许原文外发
        [ConfigItem<bool>("AssistantRedactLocal", true)] public partial bool RedactLocalContent { get; set; }

        // AI 允许联网查阅哪些站点；空列表 = 没配置过（按默认全开），["none"] = 全部关掉
        [AnyConfigItem<List<string>>("AssistantAllowedSites")] public partial List<string> AllowedSites { get; set; }
    }
}
