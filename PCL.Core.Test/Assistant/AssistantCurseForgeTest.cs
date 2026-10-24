using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Assistant;

namespace PCL.Core.Test;

[TestClass]
public class AssistantCurseForgeTest
{
    [TestMethod]
    public void Resolve_PrefersTheKeyTheUserFilledIn()
    {
        Assert.AreEqual("mine", AssistantCurseForge.Resolve(" mine ", "injected"));
        Assert.AreEqual("injected", AssistantCurseForge.Resolve("", "injected"));
        Assert.AreEqual("injected", AssistantCurseForge.Resolve(null, "injected"));
    }

    [TestMethod]
    public void Resolve_EmptyOnBothSidesMeansNoKey()
    {
        Assert.AreEqual("", AssistantCurseForge.Resolve("   ", null));
        Assert.AreEqual("", AssistantCurseForge.Resolve(null, null));
    }
}
