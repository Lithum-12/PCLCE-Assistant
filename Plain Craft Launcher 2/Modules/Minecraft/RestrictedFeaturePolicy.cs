using PCL.Core.App.Region;
using PCL.Core.App.Localization;
using PCL.Core.UI;

namespace PCL;

internal static class RestrictedFeaturePolicy
{
    // [ASSISTANT] fork 注入点：返回 null 时沿用上游判定
    internal static Func<RegionAssessment?>? RegionOverride;

    internal static RegionAssessment Assess() => RegionOverride?.Invoke() ?? RegionPolicy.Evaluate(RegionPolicy.ReadWindowsSignals()); // [ASSISTANT]

    internal static bool IsRestricted => Assess().IsRestricted;

    internal static bool Check(string feature, bool showHint = true)
    {
        var assessment = Assess();
        if (!assessment.IsRestricted) return true;

        ModBase.Log($"[RegionPolicy] 已阻止功能 {feature}，判定原因：{assessment.Reason}");
        if (showHint)
        {
            HintService.Hint(Lang.Text(assessment.Classification == RegionClassification.Unknown
                ? "Region.Feature.Unknown"
                : "Region.Feature.Restricted"), HintType.Error);
        }
        return false;
    }

    internal static void ThrowIfRestricted(string feature)
    {
        if (Check(feature, false)) return;
        throw new ModBase.CancelledException();
    }
}
