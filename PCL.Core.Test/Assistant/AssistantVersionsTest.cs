using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Assistant;

namespace PCL.Core.Test;

[TestClass]
public class AssistantVersionsTest
{
    [TestMethod]
    public void Matches_DoesNotConfuse1_21_1With1_21_11()
    {
        // 真实事故：玩家要 1.21.1，用"包含"判断会命中 1.21.11
        Assert.IsFalse(AssistantVersions.Matches("1.21.11", "1.21.1"));
        Assert.IsFalse(AssistantVersions.Matches("1.21.1.2", "1.21.1"));
        Assert.IsTrue(AssistantVersions.Matches("1.21.1", "1.21.1"));
        Assert.IsTrue(AssistantVersions.Matches("1.21.1-pre1", "1.21.1"));
        Assert.IsTrue(AssistantVersions.Matches("1.21.1-rc1", "1.21.1"));
        Assert.IsFalse(AssistantVersions.Matches("1.21", "1.21.1"), "只写到大版本的不算匹配");
        Assert.IsFalse(AssistantVersions.Matches("1.20.1", "1.21.1"));
    }

    [TestMethod]
    public void Matches_EmptyWantedMeansNoFilter()
    {
        Assert.IsTrue(AssistantVersions.Matches("1.20.4", ""));
        Assert.IsTrue(AssistantVersions.Matches("1.20.4", null));
        Assert.IsFalse(AssistantVersions.Matches("", "1.21.1"));
        Assert.IsFalse(AssistantVersions.Matches(null, "1.21.1"));
    }

    [TestMethod]
    public void AnyMatches_ChecksEveryDeclaredVersion()
    {
        var declared = new List<string> { "1.21", "1.21.1", "1.21.2" };
        Assert.IsTrue(AssistantVersions.AnyMatches(declared, "1.21.1"));
        Assert.IsFalse(AssistantVersions.AnyMatches(new List<string> { "1.21.11" }, "1.21.1"));
        Assert.IsTrue(AssistantVersions.AnyMatches(new List<string>(), ""));
        Assert.IsFalse(AssistantVersions.AnyMatches(new List<string>(), "1.21.1"));
    }

    [TestMethod]
    public void Approval_OnlyButtonsOneAndTwoAllow()
    {
        Assert.AreEqual(AssistantApprovalDecision.Once, AssistantApproval.FromDialogResult(1));
        Assert.AreEqual(AssistantApprovalDecision.All, AssistantApproval.FromDialogResult(2));
        Assert.AreEqual(AssistantApprovalDecision.Deny, AssistantApproval.FromDialogResult(3));
        // 关窗口、ESC 之类的 0 一律按拒绝
        Assert.AreEqual(AssistantApprovalDecision.Deny, AssistantApproval.FromDialogResult(0));
        Assert.AreEqual(AssistantApprovalDecision.Deny, AssistantApproval.FromDialogResult(-1));
    }
}
