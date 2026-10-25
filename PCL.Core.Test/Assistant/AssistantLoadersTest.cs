using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Assistant;

namespace PCL.Core.Test;

[TestClass]
public class AssistantLoadersTest
{
    [TestMethod]
    public void Normalize_AcceptsTheWaysModelsWriteThem()
    {
        Assert.AreEqual("neoforge", AssistantLoaders.Normalize("NeoForge"));
        Assert.AreEqual("neoforge", AssistantLoaders.Normalize("Neo Forge"));
        Assert.AreEqual("neoforge", AssistantLoaders.Normalize("neo"));
        Assert.AreEqual("fabric", AssistantLoaders.Normalize("FabricMC"));
        Assert.AreEqual("fabric", AssistantLoaders.Normalize(" fabric-loader "));
        Assert.AreEqual("forge", AssistantLoaders.Normalize("Minecraft Forge"));
        Assert.AreEqual("quilt", AssistantLoaders.Normalize("Quilt"));
        Assert.AreEqual("liteloader", AssistantLoaders.Normalize("LiteLoader"));
    }

    [TestMethod]
    public void Normalize_UnknownOrEmptyMeansNoFilter()
    {
        Assert.IsNull(AssistantLoaders.Normalize(null));
        Assert.IsNull(AssistantLoaders.Normalize(""));
        Assert.IsNull(AssistantLoaders.Normalize("any"));
        Assert.IsNull(AssistantLoaders.Normalize("sodium"));
    }
}
