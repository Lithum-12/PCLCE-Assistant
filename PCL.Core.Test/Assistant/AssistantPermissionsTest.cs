using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Assistant;

namespace PCL.Core.Test;

[TestClass]
public class AssistantPermissionsTest
{
    [TestMethod]
    public void Parse_OnlyFullValueUnlocks_AndAnythingUnknownStaysStrict()
    {
        Assert.AreEqual(AssistantPermissionMode.Full, AssistantPermissions.Parse("full"));
        Assert.AreEqual(AssistantPermissionMode.Full, AssistantPermissions.Parse("  FULL "));
        Assert.AreEqual(AssistantPermissionMode.Ask, AssistantPermissions.Parse("ask"));
        Assert.AreEqual(AssistantPermissionMode.Ask, AssistantPermissions.Parse(null));
        Assert.AreEqual(AssistantPermissionMode.Ask, AssistantPermissions.Parse(""));
        Assert.AreEqual(AssistantPermissionMode.Ask, AssistantPermissions.Parse("yolo"));
    }

    [TestMethod]
    public void NeedsApproval_OnlyForToolsThatAskInAskMode()
    {
        Assert.IsTrue(AssistantPermissions.NeedsApproval(AssistantPermissionMode.Ask, true));
        Assert.IsFalse(AssistantPermissions.NeedsApproval(AssistantPermissionMode.Full, true));
        // 只读工具两档都不该弹确认
        Assert.IsFalse(AssistantPermissions.NeedsApproval(AssistantPermissionMode.Ask, false));
        Assert.IsFalse(AssistantPermissions.NeedsApproval(AssistantPermissionMode.Full, false));
    }

    [TestMethod]
    public void ToConfig_RoundTripsThroughParse()
    {
        foreach (var mode in new[] { AssistantPermissionMode.Ask, AssistantPermissionMode.Full })
            Assert.AreEqual(mode, AssistantPermissions.Parse(AssistantPermissions.ToConfig(mode)));
    }
}
