using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Assistant;

namespace PCL.Core.Test;

[TestClass]
public class AssistantSitesAndFilesTest
{
    [TestMethod]
    public void BuildUrl_KeepsRequestsInsideTheWhitelistedHost()
    {
        var fabric = AssistantSites.Find("fabric")!;

        Assert.AreEqual("https://meta.fabricmc.net/v2/versions/loader/1.20.1",
            AssistantSites.BuildUrl(fabric, "/v2/versions/loader/1.20.1"));
        Assert.AreEqual("https://meta.fabricmc.net/", AssistantSites.BuildUrl(fabric, ""));
        Assert.AreEqual("https://meta.fabricmc.net/x?q=a%20b", AssistantSites.BuildUrl(fabric, "x?q=a b"));
    }

    [TestMethod]
    public void BuildUrl_RejectsHostSmugglingAndTraversal()
    {
        var mcmod = AssistantSites.Find("mcmod")!;

        Assert.IsNull(AssistantSites.BuildUrl(mcmod, "https://evil.example.com/steal"));
        Assert.IsNull(AssistantSites.BuildUrl(mcmod, "//evil.example.com/steal"));
        Assert.IsNull(AssistantSites.BuildUrl(mcmod, "../etc/passwd"));
        Assert.IsNull(AssistantSites.BuildUrl(mcmod, "/evil.com/x"));
        Assert.IsNull(AssistantSites.BuildUrl(mcmod, @"C:\Windows\x"));
    }

    [TestMethod]
    public void Allows_EmptyMeansDefaultOnAndNoneMarkerTurnsEverythingOff()
    {
        Assert.IsTrue(AssistantSites.Allows("github", null));
        Assert.IsTrue(AssistantSites.Allows("github", []));
        Assert.IsFalse(AssistantSites.Allows("github", [AssistantSites.NoneMarker]));
        Assert.IsTrue(AssistantSites.Allows("github", ["fabric", "github"]));
        Assert.IsFalse(AssistantSites.Allows("forge", ["fabric", "github"]));
        Assert.IsFalse(AssistantSites.Allows("nope", ["fabric"]));
    }

    [TestMethod]
    public void EveryRegisteredSiteHasAHostAndHint()
    {
        CollectionAssert.AllItemsAreNotNull(AssistantSites.All.ToList());
        foreach (var site in AssistantSites.All)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(site.Host), site.Id);
            Assert.IsFalse(string.IsNullOrWhiteSpace(site.Label), site.Id);
            Assert.IsFalse(string.IsNullOrWhiteSpace(site.Hint), site.Id);
        }
    }

    [TestMethod]
    public void ResolveInstancePath_BlocksEscapingTheInstance()
    {
        var root = Path.Combine(Path.GetTempPath(), "pclce-assistant-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var inside = AssistantLocalReader.ResolveInstancePath(root, "logs/latest.log");
            Assert.IsNotNull(inside);
            Assert.IsTrue(inside!.StartsWith(Path.GetFullPath(root).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase));

            Assert.IsNull(AssistantLocalReader.ResolveInstancePath(root, "../../Windows/win.ini"));
            Assert.IsNull(AssistantLocalReader.ResolveInstancePath(root, "C:\\Windows\\win.ini"));
            Assert.IsNull(AssistantLocalReader.ResolveInstancePath(root, ""));
            // 开头的斜杠只是被当成相对路径，不能借它跳出实例目录
            var leadingSlash = AssistantLocalReader.ResolveInstancePath(root, "/etc/passwd");
            Assert.IsTrue(leadingSlash!.StartsWith(Path.GetFullPath(root).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase));
            // 启动器在实例目录下写的 PCL/ 里有 LatestLaunch.bat（含 accessToken）与配置，全部拒绝
            Assert.IsNull(AssistantLocalReader.ResolveInstancePath(root, "PCL/config.v1.yml"));
            Assert.IsNull(AssistantLocalReader.ResolveInstancePath(root, "PCL/LatestLaunch.bat"));
            Assert.IsNull(AssistantLocalReader.ResolveInstancePath(root, "server.keystore"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ListDirectory_ShowsFoldersWithSlashAndFileSizes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pclce-assistant-list-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "mods"));
        File.WriteAllText(Path.Combine(root, "options.txt"), "lang:zh_cn");
        try
        {
            var entries = AssistantLocalReader.ListDirectory(root);
            Assert.IsTrue(entries.Any(e => e == "mods/"), string.Join("|", entries));
            Assert.IsTrue(entries.Any(e => e.StartsWith("options.txt（")), string.Join("|", entries));
            Assert.AreEqual(0, AssistantLocalReader.ListDirectory(Path.Combine(root, "不存在")).Count);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ReadFile_KeepsHeadAndTailOfAnArbitraryInstanceFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "pclce-assistant-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.WriteAllText(Path.Combine(root, "config", "sodium-options.json"),
            "HEAD-MARKER" + new string('x', 20000) + "TAIL-MARKER");
        try
        {
            var path = AssistantLocalReader.ResolveInstancePath(root, "config/sodium-options.json")!;
            var file = AssistantLocalReader.ReadFile(root, path, 1200);

            Assert.IsNotNull(file);
            Assert.IsTrue(file.Text.StartsWith("HEAD-MARKER"));
            Assert.IsTrue(file.Text.EndsWith("TAIL-MARKER"));
            Assert.IsTrue(file.Truncated);
            Assert.AreEqual("config/sodium-options.json", file.Source.Replace('\\', '/'));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
