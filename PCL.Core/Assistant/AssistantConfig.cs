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
    }
}
