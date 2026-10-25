using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Assistant;

namespace PCL.Core.Test;

[TestClass]
public class AssistantPermissionsTest
{
    [TestMethod]
    public void Parse_KnowsAllThreeTiersAndFallsBackToAsk()
    {
        Assert.AreEqual(AssistantPermissionMode.Full, AssistantPermissions.Parse("full"));
        Assert.AreEqual(AssistantPermissionMode.Read, AssistantPermissions.Parse(" READ "));
        Assert.AreEqual(AssistantPermissionMode.Ask, AssistantPermissions.Parse("ask"));
        Assert.AreEqual(AssistantPermissionMode.Ask, AssistantPermissions.Parse(null));
        Assert.AreEqual(AssistantPermissionMode.Ask, AssistantPermissions.Parse(""));
        // 手改坏的配置既不该静默放开执行，也不该静默关掉他配好的能力
        Assert.AreEqual(AssistantPermissionMode.Ask, AssistantPermissions.Parse("yolo"));
    }

    [TestMethod]
    public void ReadTier_AttachesNoActionTools()
    {
        Assert.IsFalse(AssistantPermissions.AllowsActions(AssistantPermissionMode.Read));
        Assert.IsTrue(AssistantPermissions.AllowsActions(AssistantPermissionMode.Ask));
        Assert.IsTrue(AssistantPermissions.AllowsActions(AssistantPermissionMode.Full));
    }

    [TestMethod]
    public void OnlyAskTierInterruptsWithAConfirmation()
    {
        Assert.IsTrue(AssistantPermissions.NeedsApproval(AssistantPermissionMode.Ask, true));
        Assert.IsFalse(AssistantPermissions.NeedsApproval(AssistantPermissionMode.Full, true));
        Assert.IsFalse(AssistantPermissions.NeedsApproval(AssistantPermissionMode.Read, true));
        // 读取类工具三档都不该弹确认
        Assert.IsFalse(AssistantPermissions.NeedsApproval(AssistantPermissionMode.Ask, false));
    }

    [TestMethod]
    public void Next_CyclesThroughThreeTiers()
    {
        Assert.AreEqual(AssistantPermissionMode.Ask, AssistantPermissions.Next(AssistantPermissionMode.Read));
        Assert.AreEqual(AssistantPermissionMode.Full, AssistantPermissions.Next(AssistantPermissionMode.Ask));
        Assert.AreEqual(AssistantPermissionMode.Read, AssistantPermissions.Next(AssistantPermissionMode.Full));
    }

    [TestMethod]
    public void ToConfig_RoundTripsThroughParse()
    {
        foreach (var mode in new[] { AssistantPermissionMode.Read, AssistantPermissionMode.Ask, AssistantPermissionMode.Full })
            Assert.AreEqual(mode, AssistantPermissions.Parse(AssistantPermissions.ToConfig(mode)));
    }
}
