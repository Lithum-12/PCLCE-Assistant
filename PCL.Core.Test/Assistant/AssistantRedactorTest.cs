using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Assistant;

namespace PCL.Core.Test;

[TestClass]
public class AssistantRedactorTest
{
    [TestMethod]
    public void Redact_WindowsPath_HidesUserFolder()
    {
        var text = AssistantRedactor.Redact(@"Caused: C:\Users\35320\.minecraft\mods\sodium-0.4.2.jar");
        Assert.IsTrue(text.Contains(@"C:\Users\<用户>"), text);
        Assert.IsFalse(text.Contains("35320"), text);
    }

    [TestMethod]
    public void Redact_ForwardSlashAndUnixHome_HideUserFolder()
    {
        Assert.IsTrue(AssistantRedactor.Redact(@"C:/Users/Lithum/mods/a.jar").Contains(@"C:/Users/<用户>"));
        Assert.IsTrue(AssistantRedactor.Redact("/home/lithum/.minecraft/logs").Contains("/home/<用户>"));
    }

    [TestMethod]
    public void Redact_IpAddresses_HideButKeepBuildNumbers()
    {
        Assert.IsTrue(AssistantRedactor.Redact("Connecting to 203.0.113.9:25565").Contains("<IP>:25565"));
        Assert.IsTrue(AssistantRedactor.Redact("server address = 198.51.100.7").Contains("<IP>"));
        Assert.IsTrue(AssistantRedactor.Redact("lan peer 10.1.2.3 joined").Contains("<IP>"));
        Assert.IsTrue(AssistantRedactor.Redact("loopback ::1 refused").Contains("<IPv6>"));
        // 四段数字同时也是构建号的写法，没有上下文时不该被当成地址
        Assert.IsTrue(AssistantRedactor.Redact("forge build 1.20.1.40 loaded").Contains("1.20.1.40"));
    }

    [TestMethod]
    public void Redact_UuidCoordsAndSeed_AreMasked()
    {
        var text = AssistantRedactor.Redact(
            "player 7890abcd-1234-5678-9abc-def012345678 moved to x=128, y=64, z=-256 / seed = -89234234");
        StringAssert.Contains(text, "<UUID>");
        StringAssert.Contains(text, "x=<数值>");
        StringAssert.Contains(text, "<已隐藏>");
        Assert.IsFalse(text.Contains("128"), text);
        Assert.IsFalse(text.Contains("89234234"), text);

        Assert.IsTrue(AssistantRedactor.Redact("Pos: [100.5, 64.0, -32.25]").Contains("<坐标>"));
    }

    [TestMethod]
    public void Redact_CredentialAndEmail_AreMasked()
    {
        Assert.IsTrue(AssistantRedactor.Redact("api_key=sk-abcdefghijklmnop1234").Contains("<已隐藏>"));
        Assert.IsTrue(AssistantRedactor.Redact("contact me@lithum.dev please").Contains("<邮箱>"));
    }

    [TestMethod]
    public void Redact_ExtraTerms_AreCaseInsensitivelyMasked()
    {
        var text = AssistantRedactor.Redact("Steve_07 broke block, then steve_07 left", ["Steve_07"]);
        Assert.IsFalse(text.ToLowerInvariant().Contains("steve_07"), text);
    }

    [TestMethod]
    public void Redact_EmptyInput_ReturnsEmpty()
    {
        Assert.AreEqual("", AssistantRedactor.Redact(null));
        Assert.AreEqual("", AssistantRedactor.Redact(""));
    }
}
