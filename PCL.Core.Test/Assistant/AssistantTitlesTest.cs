using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Assistant;

namespace PCL.Core.Test;

[TestClass]
public class AssistantTitlesTest
{
    [TestMethod]
    public void Clean_StripsQuotesPrefixAndTrailingPunctuation()
    {
        Assert.AreEqual("NeoForge 优化方案", AssistantTitles.Clean("「NeoForge 优化方案」"));
        Assert.AreEqual("NeoForge 优化方案", AssistantTitles.Clean("标题：NeoForge 优化方案。"));
        Assert.AreEqual("NeoForge 优化方案", AssistantTitles.Clean("  \"NeoForge 优化方案\"\n"));
        Assert.AreEqual("Minecraft 1.7.10 下载教程", AssistantTitles.Clean("Minecraft 1.7.10 下载教程？"));
        Assert.AreEqual("Sodium 与 Iris 冲突", AssistantTitles.Clean("Sodium   与   Iris 冲突"));
    }

    [TestMethod]
    public void Clean_CapsLengthAndRejectsEmpty()
    {
        var clean = AssistantTitles.Clean(new string('字', 40));

        Assert.IsNotNull(clean);
        Assert.AreEqual(AssistantTitles.MaxChars + 1, clean!.Length); // 截断 + 省略号
        Assert.IsTrue(clean.EndsWith("…"));
        Assert.IsNull(AssistantTitles.Clean("   "));
        Assert.IsNull(AssistantTitles.Clean("「」"));
        Assert.IsNull(AssistantTitles.Clean(null));
    }
}
