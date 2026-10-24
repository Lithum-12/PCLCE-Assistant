using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Assistant;

namespace PCL.Core.Test;

[TestClass]
public class AssistantProjectLinksTest
{
    [TestMethod]
    public void Split_BareUrl_BecomesCardBetweenText()
    {
        var parts = AssistantProjectLinks.Split("试试 Sodium：https://modrinth.com/mod/sodium 就能提速");

        Assert.AreEqual(3, parts.Count);
        Assert.IsFalse(parts[0].IsCard);
        Assert.AreEqual("试试 Sodium：", parts[0].Text);
        Assert.IsTrue(parts[1].IsCard);
        Assert.AreEqual("https://modrinth.com/mod/sodium", parts[1].Text);
        Assert.AreEqual("就能提速", parts[2].Text);
    }

    [TestMethod]
    public void Split_MarkdownLink_IsConsumedWhole()
    {
        var parts = AssistantProjectLinks.Split("看 [Sodium 优化模组](https://modrinth.com/mod/sodium) 吧");

        Assert.AreEqual(3, parts.Count);
        Assert.AreEqual("看", parts[0].Text);
        Assert.IsTrue(parts[1].IsCard);
        Assert.AreEqual("吧", parts[2].Text);
        Assert.IsFalse(string.Concat(parts.Select(p => p.Text)).Contains("]("));
    }

    [TestMethod]
    public void Split_TwoLinks_ProduceTwoCards()
    {
        var parts = AssistantProjectLinks.Split(
            "两个都要：https://modrinth.com/mod/sodium 和 https://modrinth.com/mod/iris\n结束");

        Assert.AreEqual(2, parts.Count(p => p.IsCard));
        Assert.AreEqual("https://modrinth.com/mod/sodium", parts.First(p => p.IsCard).Text);
        Assert.AreEqual("https://modrinth.com/mod/iris", parts.Last(p => p.IsCard).Text);
    }

    [TestMethod]
    public void Split_OtherHosts_StayPlainText()
    {
        var parts = AssistantProjectLinks.Split("看 https://example.com/mod/sodium 或 https://github.com/x/y");

        Assert.AreEqual(1, parts.Count);
        Assert.IsFalse(parts[0].IsCard);
    }

    [TestMethod]
    public void Parse_ModrinthUrl_TrimsTrailingPunctuation()
    {
        var link = AssistantProjectLinks.Parse("https://modrinth.com/mod/sodium。");

        Assert.IsNotNull(link);
        Assert.AreEqual("sodium", link!.Slug);
        Assert.IsTrue(link.Modrinth);
        Assert.AreEqual("https://modrinth.com/mod/sodium", link.Url);
    }

    [TestMethod]
    public void Parse_CurseForgeUrl_IsFlaggedAsCurseForge()
    {
        var link = AssistantProjectLinks.Parse("https://www.curseforge.com/minecraft/mc-mods/optifine");

        Assert.IsNotNull(link);
        Assert.AreEqual("optifine", link!.Slug);
        Assert.IsFalse(link.Modrinth);

        var pack = AssistantProjectLinks.Parse("https://curseforge.com/minecraft/modpacks/fabric-example");
        Assert.AreEqual("fabric-example", pack!.Slug);
    }

    [TestMethod]
    public void Parse_UnrelatedUrl_ReturnsNull()
    {
        Assert.IsNull(AssistantProjectLinks.Parse("https://modrinth.com/user/x"));
        Assert.IsNull(AssistantProjectLinks.Parse("https://mcapi.org/mod/sodium"));
        Assert.IsNull(AssistantProjectLinks.Parse(""));
    }
}
