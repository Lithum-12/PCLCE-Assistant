using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Assistant;

namespace PCL.Core.Test;

[TestClass]
public class AssistantChoicesTest
{
    [TestMethod]
    public void Parse_ReadsAJsonArrayOfOptions()
    {
        var set = AssistantChoices.Parse("要我现在装 Fabric API 吗？", """["装 Fabric API","把 Indium 移到回收站","先不装"]""");

        Assert.IsNotNull(set);
        Assert.AreEqual(3, set.Options.Count);
        Assert.AreEqual("装 Fabric API", set.Options[0]);
        Assert.AreEqual("先不装", set.Options[2]);
        Assert.AreEqual("要我现在装 Fabric API 吗？", set.Question);
    }

    [TestMethod]
    public void Parse_UnwrapsAnArrayPassedAsOneString()
    {
        // 模型有时把 options 整体当成一个字符串参数传过来："[\"a\",\"b\"]"
        var wrapped = "\"" + """["装 JEI","装 Jade"]""".Replace("\"", "\\\"") + "\"";

        var set = AssistantChoices.Parse("选哪个", wrapped);

        Assert.IsNotNull(set);
        Assert.AreEqual("装 JEI", set.Options[0]);
        Assert.AreEqual("装 Jade", set.Options[1]);
    }

    [TestMethod]
    public void Parse_FallsBackToLinesWhenTheJsonBreaksOff()
    {
        var set = AssistantChoices.Parse("选哪个", "[\"装 JEI\",\n\"装 Jade\"");

        Assert.IsNotNull(set);
        Assert.AreEqual(2, set.Options.Count);
        Assert.AreEqual("装 JEI", set.Options[0]);
        Assert.AreEqual("装 Jade", set.Options[1]);
    }

    [TestMethod]
    public void Parse_DropsBlanksAndDuplicatesAndCapsTheList()
    {
        var set = AssistantChoices.Parse("",
            """["装 JEI","","   ","装 jei","装 Jade","装 AppleSkin","装 OptiFine","装 JourneyMap"]""");

        Assert.IsNotNull(set);
        Assert.AreEqual(AssistantChoices.MaxOptions, set.Options.Count);
        Assert.AreEqual("装 JEI", set.Options[0]); // 去重不区分大小写，但保留第一次出现的写法
        Assert.IsFalse(set.Options.Any(s => s.Trim().Length == 0));
        Assert.IsFalse(set.Options.Contains("装 jei"));
    }

    [TestMethod]
    public void Parse_TrimsLongOptionsAndKeepsThemSingleLine()
    {
        var longOne = new string('相', AssistantChoices.MaxOptionChars + 12);

        var set = AssistantChoices.Parse("问题\n第二行", $"[\"{longOne}\",\"短的一条\"]");

        Assert.IsNotNull(set);
        Assert.AreEqual(AssistantChoices.MaxOptionChars + 1, set.Options[0].Length);
        Assert.IsTrue(set.Options[0].EndsWith("…"));
        Assert.AreEqual(2, set.Options.Count);
        Assert.IsFalse(set.Question.Contains('\n'));
        Assert.IsTrue(set.Question.Length <= AssistantChoices.MaxQuestionChars + 1);
    }

    [TestMethod]
    public void Parse_ReturnsNullWhenThereIsNothingToChoose()
    {
        Assert.IsNull(AssistantChoices.Parse("选哪个", null));
        Assert.IsNull(AssistantChoices.Parse("选哪个", ""));
        Assert.IsNull(AssistantChoices.Parse("选哪个", "[]"));
        Assert.IsNull(AssistantChoices.Parse("选哪个", """["只有一条，那就直接去做"]"""));
        Assert.IsNull(AssistantChoices.Parse("选哪个", "不是数组也没有换行"));
    }
}
