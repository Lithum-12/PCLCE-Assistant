using System;
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

    [TestMethod]
    public void Parse_KeepsLoaderAndVersionFilters()
    {
        var link = AssistantProjectLinks.Parse("看 https://modrinth.com/mod/sodium?mc=1.21.1&loader=fabric 吧");

        Assert.IsNotNull(link);
        Assert.AreEqual("sodium", link!.Slug);
        Assert.AreEqual("fabric", link.Loader);
        Assert.AreEqual("1.21.1", link.Version);
        Assert.AreEqual("https://modrinth.com/mod/sodium?mc=1.21.1&loader=fabric", link.Url);
    }

    [TestMethod]
    public void Parse_AcceptsAliasesAndBareLinks()
    {
        var alias = AssistantProjectLinks.Parse("https://modrinth.com/mod/iris?version=1.20.1&modloader=NeoForge");
        Assert.AreEqual("1.20.1", alias!.Version);
        Assert.AreEqual("NeoForge", alias.Loader);

        var plain = AssistantProjectLinks.Parse("https://modrinth.com/mod/sodium");
        Assert.AreEqual("", plain!.Loader);
        Assert.AreEqual("", plain.Version);

        var wrapped = AssistantProjectLinks.Parse("[Sodium](https://modrinth.com/mod/sodium?mc=1.21.1)");
        Assert.AreEqual("1.21.1", wrapped!.Version);
    }

    [TestMethod]
    public void Split_CardSegmentKeepsTheQueryString()
    {
        var parts = AssistantProjectLinks.Split("用 https://modrinth.com/mod/sodium?mc=1.21.1&loader=fabric 就行");

        Assert.AreEqual(1, parts.Count(p => p.IsCard));
        Assert.AreEqual("https://modrinth.com/mod/sodium?mc=1.21.1&loader=fabric", parts.First(p => p.IsCard).Text);
        Assert.AreEqual("就行", parts.Last(p => !p.IsCard).Text);
    }

    // 字段名照上游 ModComp._BuildFromCurseForge 读的 FuzzyMod 形状手写的一份，
    // 他贴上真 key 之后应该换成从 api.curseforge.com 抓下来的原始响应
    [TestMethod]
    public void ParseCurseForgeHit_ReadsFuzzyModFields()
    {
        const string body = """
            {"data":[{"id":328085,"name":"Sodium","slug":"sodium",
              "summary":"A free and open-source Fabric mod",
              "downloadCount":11300000,"dateReleased":"2026-10-02T09:00:00Z",
              "logo":{"thumbnailUrl":"https://media.curseforge.com/t.png","url":"https://media.curseforge.com/f.png"},
              "links":{"websiteUrl":"https://www.curseforge.com/minecraft/mc-mods/sodium/"}}]}
            """;

        var info = AssistantProjectLinks.ParseCurseForgeHit(body);

        Assert.IsNotNull(info);
        Assert.AreEqual("Sodium", info!.Title);
        Assert.AreEqual("https://media.curseforge.com/t.png", info.IconUrl);
        Assert.AreEqual(11300000, info.Downloads);
        Assert.AreEqual("CurseForge", info.Source);
        Assert.IsTrue(info.UpdatedAt > DateTime.MinValue);
    }

    [TestMethod]
    public void ParseCurseForgeHit_FallsBackAndRejectsEmptyShapes()
    {
        var info = AssistantProjectLinks.ParseCurseForgeHit("""{"data":[{"name":"X","iconUrl":"https://c/i.png"}]}""");
        Assert.AreEqual("https://c/i.png", info!.IconUrl);
        Assert.AreEqual(0, info.Downloads);
        Assert.AreEqual(DateTime.MinValue, info.UpdatedAt);

        Assert.IsNull(AssistantProjectLinks.ParseCurseForgeHit("""{"data":[]}"""));
        Assert.IsNull(AssistantProjectLinks.ParseCurseForgeHit("not json"));
    }
}
