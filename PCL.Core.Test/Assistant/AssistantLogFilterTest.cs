using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Assistant;

namespace PCL.Core.Test;

[TestClass]
public class AssistantLogFilterTest
{
    // 真实 log4j 控制台格式：ERROR 后面跟着没有级别前缀的堆栈续行，
    // 只按级别过滤会把整段堆栈丢掉，那才是诊断最有用的部分
    private static readonly string[] Sample =
    [
        "[12:00:01] [main/INFO]: Environment: authHost='https://authserver.mojang.com'",
        "[12:00:02] [Render thread/DEBUG]: chatty debug line that nobody reads",
        "[12:00:03] [Render thread/ERROR]: Couldn't load model for block",
        "java.lang.IllegalStateException: Missing texture",
        "\tat net.minecraft.client.render.ModelLoader.load(ModelLoader.java:88)",
        "Caused by: java.io.FileNotFoundException: assets/minecraft/textures/block/missing.png",
        "\t... 42 more",
        "[12:00:04] [Worker-Main-3/WARN]: Failed to fetch profile that contains unsaturation!",
        "[12:00:05] [main/INFO]: Sound engine started"
    ];

    [TestMethod]
    public void RankOfLine_RecognizesTheLog4jHeaderAndTreatsStackLinesAsContinuations()
    {
        Assert.AreEqual(2, AssistantLogFilter.RankOfLine("[12:00:01] [main/INFO]: ok"));
        Assert.AreEqual(4, AssistantLogFilter.RankOfLine("[12:00:03] [Render thread/ERROR]: boom"));
        Assert.AreEqual(5, AssistantLogFilter.RankOfLine("[12:00:03.123] [main/FATAL]: dead"));
        Assert.AreEqual(-1, AssistantLogFilter.RankOfLine("\tat com.example.Thing(Thing.java:1)"));
        Assert.AreEqual(-1, AssistantLogFilter.RankOfLine("no header at all"));
    }

    [TestMethod]
    public void Select_KeepsTheWholeStackTraceOfAnError()
    {
        var result = AssistantLogFilter.Select(Sample, "error", 0, 4000);

        StringAssert.Contains(result.Text, "Couldn't load model for block");
        StringAssert.Contains(result.Text, "Caused by: java.io.FileNotFoundException");
        StringAssert.Contains(result.Text, "... 42 more");
        Assert.IsFalse(result.Text.Contains("Sound engine started"));
        Assert.IsFalse(result.Text.Contains("chatty debug line"));
        // WARN 不该出现在 error 阈值之上
        Assert.IsFalse(result.Text.Contains("unsaturation"));
        Assert.AreEqual(1, result.Matched);
    }

    [TestMethod]
    public void Select_WarnThresholdAlsoKeepsErrors()
    {
        var result = AssistantLogFilter.Select(Sample, "warn", 0, 4000);

        StringAssert.Contains(result.Text, "unsaturation");
        StringAssert.Contains(result.Text, "Couldn't load model for block");
        Assert.AreEqual(2, result.Matched);
    }

    [TestMethod]
    public void Select_ContextLinesComeBeforeTheMatch()
    {
        var result = AssistantLogFilter.Select(Sample, "error", 1, 4000);

        StringAssert.Contains(result.Text, "chatty debug line"); // 命中行前一行
    }

    [TestMethod]
    public void Select_WhenBudgetIsTightItKeepsTheMostRecentErrors()
    {
        var lines = new List<string>();
        for (var i = 0; i < 40; i++) lines.Add($"[12:{i / 60:00}:0{i % 60}] [main/INFO]: filler line {i}");
        lines.Add("[13:00:00] [main/ERROR]: the first failure");
        lines.Add("[13:00:05] [main/ERROR]: the newest failure");

        // 两条错误各约 44 字，预算 60 只够最近那条
        var result = AssistantLogFilter.Select(lines, "error", 0, 60);

        StringAssert.Contains(result.Text, "the newest failure");
        Assert.IsFalse(result.Text.Contains("the first failure"));
        Assert.IsTrue(result.CutByBudget);
    }

    [TestMethod]
    public void Select_WhenOneLineIsHugeItStillReturnsAPieceOfIt()
    {
        var blob = "[13:00:00] [main/ERROR]: " + new string('x', 5000);

        var result = AssistantLogFilter.Select([blob], "error", 0, 200);

        Assert.AreEqual(1, result.Matched);
        Assert.IsTrue(result.Text.StartsWith("[13:00:00] [main/ERROR]: xxx"));
        Assert.IsTrue(result.Text.Length <= 200);
        Assert.IsTrue(result.CutByBudget);
    }

    [TestMethod]
    public void Select_ReportsNoMatchInsteadOfLyingWithAnEmptyBody()
    {
        var clean = new[] { "[12:00:01] [main/INFO]: fine", "[12:00:02] [main/INFO]: still fine" };

        var result = AssistantLogFilter.Select(clean, "error", 1, 4000);

        Assert.AreEqual(0, result.Matched);
        Assert.AreEqual("", result.Text);
    }

    [TestMethod]
    public void Threshold_UnknownLevelMeansNoFilter()
    {
        Assert.AreEqual(4, AssistantLogFilter.Threshold("error"));
        Assert.AreEqual(3, AssistantLogFilter.Threshold("WARN"));
        Assert.AreEqual(0, AssistantLogFilter.Threshold("all"));
        Assert.AreEqual(0, AssistantLogFilter.Threshold(null));
        Assert.AreEqual(0, AssistantLogFilter.Threshold("nonsense"));
    }

    // 真机日志（Forge 1.20.1 / NeoForge）的日期段是本地化的"[157月2026 10:11:18.196]"，
    // 级别在第二组，第三组还带一个像级别的大写标签；按时间格式认行首会把整份日志判成续行
    [TestMethod]
    public void RankOfLine_RecognizesTheForgeLocalizedDateFormat()
    {
        Assert.AreEqual(4, AssistantLogFilter.RankOfLine(
            "[157月2026 10:11:36.905] [Render thread/ERROR] [net.minecraftforge.fml.loading.FMLLoader/CORE]: Failed"));
        Assert.AreEqual(3, AssistantLogFilter.RankOfLine(
            "[157月2026 10:11:18.196] [main/WARN] [net.minecraftforge.fml.loading.FMLConfig/CORE]: Configuration file is not correct"));
        Assert.AreEqual(2, AssistantLogFilter.RankOfLine(
            "[128月2026 23:49:00.147] [main/INFO] [cpw.mods.modlauncher.Launcher/MODLAUNCHER]: ModLauncher running"));
        Assert.AreEqual(2, AssistantLogFilter.RankOfLine("[main/INFO:jei] Fabric style"));

        // 有时间没级别：算新条目，不算命中
        Assert.AreEqual(-1, AssistantLogFilter.RankOfLine("[12:00:07] a bare timestamped line"));
        Assert.IsTrue(AssistantLogFilter.IsHeaderLine("[12:00:07] a bare timestamped line"));
        Assert.IsFalse(AssistantLogFilter.IsHeaderLine("\tat net.minecraft.server.MinecraftServer.run(MinecraftServer.java:842)"));
        Assert.IsFalse(AssistantLogFilter.IsHeaderLine("]] not a header either"));
    }

    [TestMethod]
    public void Select_FindsErrorsInAForgeStyleLog()
    {
        var lines = new[]
        {
            "[157月2026 10:11:18.196] [main/INFO] [cpw.mods.modlauncher.Launcher/MODLAUNCHER]: ModLauncher running",
            "[157月2026 10:11:36.905] [Render thread/ERROR] [net.minecraftforge.fml.loading.FMLLoader/CORE]: Failed to load a thing",
            "java.lang.NullPointerException: Cannot invoke \"Object.length()\" because \"x\" is null",
            "\tat com.example.Thing.run(Thing.java:9)",
            "[157月2026 10:11:37.000] [Render thread/WARN] [net.minecraftforge.foo/]: A later entry"
        };

        var result = AssistantLogFilter.Select(lines, "error", 0, 4000);

        Assert.AreEqual(1, result.Matched);
        StringAssert.Contains(result.Text, "Failed to load a thing");
        StringAssert.Contains(result.Text, "java.lang.NullPointerException");
        StringAssert.Contains(result.Text, "at com.example.Thing.run");
        Assert.IsFalse(result.Text.Contains("A later entry"));
    }
}
