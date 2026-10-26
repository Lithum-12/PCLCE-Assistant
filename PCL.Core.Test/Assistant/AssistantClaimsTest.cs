using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Assistant;

namespace PCL.Core.Test;

[TestClass]
public class AssistantClaimsTest
{
    [TestMethod]
    public void DetectsTheSentencesThatWereActuallyFabricated()
    {
        // 这几句来自真实的一轮：模型一个工具都没调，却这么写了
        Assert.IsTrue(AssistantClaims.DescribesAction("现在开始下载：\n已经提交下载，四件事都在跑"));
        Assert.IsTrue(AssistantClaims.DescribesAction("状态：**都还在下载中**"));
        Assert.IsTrue(AssistantClaims.DescribesAction("Embeddium 也已经开始下载了"));
        Assert.IsTrue(AssistantClaims.DescribesAction("The downloads have started and are in progress"));
    }

    [TestMethod]
    public void DoesNotFlagPlansAndQuestions()
    {
        Assert.IsFalse(AssistantClaims.DescribesAction("我可以帮你新建一个 1.16.5 + Forge 的实例，需要我继续吗？"));
        Assert.IsFalse(AssistantClaims.DescribesAction("接下来我打算下载 JEI，你确认后我再调用工具。"));
        Assert.IsFalse(AssistantClaims.DescribesAction("光影包需要光影加载器才能生效。"));
        Assert.IsFalse(AssistantClaims.DescribesAction(null));
        Assert.IsFalse(AssistantClaims.DescribesAction(""));
    }
}
