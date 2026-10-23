using PCL.Core.App;
using PCL.Core.App.Region;

namespace PCL.Core.Assistant;

/// <summary>
/// 区域判定覆盖。返回 null 表示交回上游判定逻辑。
/// </summary>
public static class AssistantRegionBypass
{
    public static RegionAssessment? Evaluate()
    {
        if (!Config.Assistant.AllowRestrictedFeatures) return null;
        return new RegionAssessment(RegionClassification.Other, "assistant-fork");
    }
}
