using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Assistant;

namespace PCL.Core.Test;

[TestClass]
public class AssistantActionsGuardTest
{
    private static string _root = "";

    [TestInitialize]
    public void MakeInstance()
    {
        _root = Path.Combine(Path.GetTempPath(), "pclce-actions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "config"));
        Directory.CreateDirectory(Path.Combine(_root, "PCL"));
        File.WriteAllText(Path.Combine(_root, "options.txt"), "fov:1.0\n");
        File.WriteAllText(Path.Combine(_root, "PCL", "LatestLaunch.bat"), "accessToken=secret\n");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [TestMethod]
    public void ConfigAllowlist_NeverReachesTheAssistantsOwnSwitches()
    {
        foreach (var key in AssistantConfigKeys.Allowed)
        {
            Assert.IsFalse(key.StartsWith("Assistant", StringComparison.OrdinalIgnoreCase), key);
            foreach (var hint in new[] { "password", "token", "secret", "username", "profile", "apikey" })
                Assert.IsFalse(key.Contains(hint, StringComparison.OrdinalIgnoreCase), $"{key} 含 {hint}");
        }

        Assert.IsTrue(AssistantConfigKeys.IsAllowed("LaunchRamCustom"));
        Assert.IsTrue(AssistantConfigKeys.IsAllowed("  launchramcustom "), "大小写与空格不该影响判断");
        Assert.IsFalse(AssistantConfigKeys.IsAllowed("AssistantPermissionMode"));
        Assert.IsFalse(AssistantConfigKeys.IsAllowed("SystemHttpProxyCustomPassword"));
        Assert.IsFalse(AssistantConfigKeys.IsAllowed(""));
        Assert.IsFalse(AssistantConfigKeys.IsAllowed(null));
    }

    private enum SampleMode
    {
        Small,
        Big
    }

    [TestMethod]
    public void ConfigValues_MustMatchTheDeclaredType()
    {
        Assert.IsTrue(AssistantConfigKeys.TryCoerce(typeof(bool), "true", out var on));
        Assert.AreEqual(true, on);
        Assert.IsTrue(AssistantConfigKeys.TryCoerce(typeof(bool), "关", out var off));
        Assert.AreEqual(false, off);
        Assert.IsFalse(AssistantConfigKeys.TryCoerce(typeof(bool), "maybe", out _));

        Assert.IsTrue(AssistantConfigKeys.TryCoerce(typeof(int), "12", out var twelve));
        Assert.AreEqual(12, twelve);
        Assert.IsFalse(AssistantConfigKeys.TryCoerce(typeof(int), "abc", out _));
        // 枚举名要认，数字不认：那是模型在瞎猜
        Assert.IsTrue(AssistantConfigKeys.TryCoerce(typeof(SampleMode), "big", out var big));
        Assert.AreEqual(SampleMode.Big, big);
        Assert.IsFalse(AssistantConfigKeys.TryCoerce(typeof(SampleMode), "1", out _));
        Assert.IsTrue(AssistantConfigKeys.TryCoerce(typeof(string), "  -Xmx4G ", out var jvm));
        Assert.AreEqual("-Xmx4G", jvm);
    }

    [TestMethod]
    public void WritePath_OnlyAcceptsTextFilesInsideTheInstance()
    {
        Assert.IsNotNull(AssistantLocalWriter.ResolveWritablePath(_root, "options.txt"));
        Assert.IsNotNull(AssistantLocalWriter.ResolveWritablePath(_root, "config/sodium-options.json"));
        Assert.IsNull(AssistantLocalWriter.ResolveWritablePath(_root, "../outside.txt"), "越出实例目录要拒绝");
        Assert.IsNull(AssistantLocalWriter.ResolveWritablePath(_root, "PCL/LatestLaunch.bat"), "启动器自己的目录要拒绝");
        Assert.IsNull(AssistantLocalWriter.ResolveWritablePath(_root, "mods/sodium.jar"), "二进制文件不该让 AI 写");
        Assert.IsNull(AssistantLocalWriter.ResolveWritablePath(_root, "keystore.jks"));
        Assert.IsNull(AssistantLocalWriter.ResolveWritablePath(_root,
            AssistantLocalWriter.BackupFolder + "/options.txt"), "备份目录不该被写");
        Assert.IsNull(AssistantLocalWriter.ResolveWritablePath(_root, "C:/Windows/host"));
    }

    [TestMethod]
    public void Overwrite_KeepsThePreviousVersionInABackup()
    {
        var path = AssistantLocalWriter.ResolveWritablePath(_root, "options.txt")!;

        var result = AssistantLocalWriter.WriteText(_root, path, "fov:70.0\n");

        Assert.IsFalse(result.Created, "原来就有文件，不该报成新建");
        Assert.IsNotNull(result.Backup);
        Assert.IsTrue(File.Exists(result.Backup));
        Assert.AreEqual("fov:1.0\n", File.ReadAllText(result.Backup));
        Assert.AreEqual("fov:70.0\n", File.ReadAllText(path));
        Assert.AreEqual("options.txt", result.Source);
    }

    [TestMethod]
    public void NewFile_IsWrittenWithoutABackupAndReportsForwardSlashes()
    {
        var path = AssistantLocalWriter.ResolveWritablePath(_root, "config/assistant-demo.json")!;

        var result = AssistantLocalWriter.WriteText(_root, path, "{\"a\":1}");

        Assert.IsTrue(result.Created);
        Assert.IsNull(result.Backup);
        Assert.AreEqual("config/assistant-demo.json", result.Source);
        // 带 BOM 的配置文件 Minecraft 读不了
        Assert.IsFalse(File.ReadAllBytes(path)[0] == 0xEF);
    }

    [TestMethod]
    public void PythonCandidates_PreferTheWindowsLauncher()
    {
        var windows = AssistantPython.Candidates(true).Select(c => c.File).ToList();
        CollectionAssert.AreEqual(new List<string> { "py", "python", "python3" }, windows);
        Assert.AreEqual("-3 ", AssistantPython.Candidates(true)[0].Prefix);
        CollectionAssert.AreEqual(new List<string> { "python3", "python" },
            AssistantPython.Candidates(false).Select(c => c.File).ToList());
    }

    [TestMethod]
    public void PythonArguments_QuoteTheScriptPath()
    {
        var py = AssistantPython.Candidates(true)[0];
        Assert.AreEqual("-3 \"C:\\My Games\\a b.py\"", AssistantPython.BuildArguments(py, @"C:\My Games\a b.py"));

        var plain = AssistantPython.Candidates(false)[0];
        Assert.AreEqual("\"/tmp/a b.py\"", AssistantPython.BuildArguments(plain, "/tmp/a b.py"));
    }

    [TestMethod]
    public void PythonResolve_TakesTheFirstThingOnPath()
    {
        var found = AssistantPython.Resolve(true, file => file == "python");
        Assert.AreEqual("python", found?.File);
        Assert.AreEqual("", found?.Prefix);
        Assert.IsNull(AssistantPython.Resolve(true, _ => false));
    }
}
