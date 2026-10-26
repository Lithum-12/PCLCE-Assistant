using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Assistant;

namespace PCL.Core.Test;

[TestClass]
public class AssistantEnvironmentTest
{
    [TestMethod]
    public void DescribeLoaders_NamesEveryLoaderTheInstanceHas()
    {
        Assert.AreEqual("Fabric", AssistantEnvironment.DescribeLoaders(true, false, false, false, false, false));
        Assert.AreEqual("Fabric + LiteLoader",
            AssistantEnvironment.DescribeLoaders(true, false, false, false, false, true));
        Assert.AreEqual("NeoForge", AssistantEnvironment.DescribeLoaders(false, false, false, true, false, false));
        // 一个加载器都没有时必须说清楚是原版，别让模型以为随便下 mod 都能用
        Assert.AreEqual("原版（无加载器）",
            AssistantEnvironment.DescribeLoaders(false, false, false, false, false, false));
    }

    [TestMethod]
    public void DescribeInstance_PutsVersionAndModCountOnOneLine()
    {
        var line = AssistantEnvironment.DescribeInstance("1.21.1 Tester", "1.21.1", "Fabric", true, 132);

        StringAssert.Contains(line, "1.21.1 Tester");
        StringAssert.Contains(line, "Minecraft 1.21.1");
        StringAssert.Contains(line, "Fabric");
        StringAssert.Contains(line, "已开启版本隔离");
        StringAssert.Contains(line, "132 个模组文件");
    }

    [TestMethod]
    public void DescribeInstance_SaysSoWhenNothingIsSelected()
    {
        Assert.AreEqual("没有选中任何实例", AssistantEnvironment.DescribeInstance("", "1.21.1", "Fabric", true, 0));
        Assert.IsTrue(AssistantEnvironment.DescribeInstance("A", "", "Fabric", false, 0).Contains("游戏版本未知"));
    }
}
