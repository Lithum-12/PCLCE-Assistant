using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Assistant;

namespace PCL.Core.Test;

[TestClass]
public class AssistantExportTest
{
    [TestMethod]
    public void ToMarkdown_WritesRolesHintsAndTrace()
    {
        // 直接拼消息，不走 AssistantSessionStore：那会牵扯到落盘路径
        var session = new AssistantSession { Title = "游戏无法启动" };
        session.Messages.Add(new AssistantMessage("user", "帮我看看为什么进不去") { At = 1 });
        session.Messages.Add(new AssistantMessage("assistant", "缺少 Sodium 前置。")
        {
            At = 2,
            Trace = new List<string> { "崩溃报告 1 份（已脱敏）" }
        });
        session.Messages.Add(new AssistantMessage("hint", "余额不足") { Level = "warn", At = 3 });

        var text = AssistantExport.ToMarkdown(session);

        StringAssert.Contains(text, "# 游戏无法启动");
        StringAssert.Contains(text, "## 玩家");
        StringAssert.Contains(text, "帮我看看为什么进不去");
        StringAssert.Contains(text, "## 助手");
        StringAssert.Contains(text, "> 本次工具调用：崩溃报告 1 份（已脱敏）");
        StringAssert.Contains(text, "> ⚠ 余额不足");
        // 提示框只是给人看的诊断，保留内容，但不该冒充某一方的发言
        Assert.IsFalse(text.Contains("## hint"));
    }

    [TestMethod]
    public void ToMarkdown_FallsBackWhenTheConversationHasNoName()
    {
        var session = new AssistantSession();
        session.Messages.Add(new AssistantMessage("user", "在吗"));

        var text = AssistantExport.ToMarkdown(session);

        StringAssert.Contains(text, "# 未命名对话");
        StringAssert.Contains(text, "消息条数：1");
        // 没有时间点时不该写出 "08:00 · " 这种假时间
        Assert.IsFalse(text.Contains("1970"));
    }

    [TestMethod]
    public void SafeFileName_StripsIllegalCharsAndCapsLength()
    {
        Assert.AreEqual("a_b_c", AssistantExport.SafeFileName("a/b:c"));
        Assert.AreEqual("conversation", AssistantExport.SafeFileName("   "));
        Assert.AreEqual("conversation", AssistantExport.SafeFileName(null));
        Assert.AreEqual(60, AssistantExport.SafeFileName(new string('字', 200)).Length);
    }
}
