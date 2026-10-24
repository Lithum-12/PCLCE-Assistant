using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Assistant;

namespace PCL.Core.Test;

[TestClass]
public class AssistantLocalReaderTest
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "pclce-assistant-reader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    [TestMethod]
    public void ReadCrashReports_NewestFirst_KeepsHeadAndTail()
    {
        Write(Path.Combine(_root, "crash-reports", "crash-old.txt"), 'O', 20000);
        var newest = Path.Combine(_root, "crash-reports", "crash-new.txt");
        Write(newest, 'N', 20000);
        File.SetLastWriteTimeUtc(newest, DateTime.UtcNow);
        File.SetLastWriteTimeUtc(Path.Combine(_root, "crash-reports", "crash-old.txt"), DateTime.UtcNow.AddHours(-1));

        var reports = AssistantLocalReader.ReadCrashReports(_root, maxFiles: 2, budgetChars: 2000);

        Assert.AreEqual(2, reports.Count);
        StringAssert.EndsWith(reports[0].Source, "crash-new.txt");
        var text = reports[0].Text;
        Assert.IsTrue(text.StartsWith("HEAD-MARKER"), text[..40]);
        Assert.IsTrue(text.EndsWith("TAIL-MARKER"), text[^40..]);
        Assert.IsTrue(reports[0].Truncated);
        Assert.IsTrue(text.Length < 2100, text.Length.ToString());
    }

    [TestMethod]
    public void ReadLatestLog_ReadsOnlyTail()
    {
        Write(Path.Combine(_root, "logs", "latest.log"), 'L', 20000);

        var log = AssistantLocalReader.ReadLatestLog(_root, budgetChars: 1500);

        Assert.IsNotNull(log);
        Assert.IsTrue(log.Text.EndsWith("TAIL-MARKER"), log.Text[^40..]);
        Assert.IsFalse(log.Text.Contains("HEAD-MARKER"));
        Assert.IsTrue(log.Truncated);
        Assert.IsTrue(log.Text.Length <= 1500);
    }

    [TestMethod]
    public void ReadModFileNames_SortedAndLimited()
    {
        Write(Path.Combine(_root, "mods", "b.jar"), 'x', 10);
        Write(Path.Combine(_root, "mods", "a.jar"), 'x', 10);
        Write(Path.Combine(_root, "mods", "README.txt"), 'x', 10);

        var all = AssistantLocalReader.ReadModFileNames(_root);
        Assert.AreEqual(3, all.Count);
        Assert.AreEqual("a.jar", all[0]);
        Assert.AreEqual(2, AssistantLocalReader.ReadModFileNames(_root, maxItems: 2).Count);
    }

    [TestMethod]
    public void ReadGameVersion_ReadsVersionJsonId()
    {
        File.WriteAllText(Path.Combine(_root, "version.json"), "{\"id\":\"1.20.1\",\"inheritsFrom\":\"1.20.1\"}");
        Assert.AreEqual("1.20.1", AssistantLocalReader.ReadGameVersion(_root));
    }

    [TestMethod]
    public void MissingFolders_ReturnEmptyInsteadOfThrowing()
    {
        Assert.AreEqual(0, AssistantLocalReader.ReadCrashReports(_root).Count);
        Assert.IsNull(AssistantLocalReader.ReadLatestLog(_root));
        Assert.AreEqual(0, AssistantLocalReader.ReadModFileNames(_root).Count);
        Assert.AreEqual("", AssistantLocalReader.ReadGameVersion(_root));
    }

    [TestMethod]
    public void ReadCrashReports_IgnoresEmptyAndForeignFiles()
    {
        var dir = Path.Combine(_root, "crash-reports");
        Write(Path.Combine(dir, "keep.txt"), 'K', 200);
        File.WriteAllText(Path.Combine(dir, "zero.txt"), "");
        Write(Path.Combine(dir, "notes.md"), 'M', 200);

        var names = AssistantLocalReader.ReadCrashReports(_root).Select(r => r.Source).ToList();
        Assert.AreEqual(1, names.Count);
        StringAssert.EndsWith(names[0], "keep.txt");
    }

    private void Write(string path, char filler, int size)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var body = new string(filler, Math.Max(0, size - "HEAD-MARKER".Length - "TAIL-MARKER".Length));
        File.WriteAllText(path, "HEAD-MARKER" + body + "TAIL-MARKER");
    }
}
