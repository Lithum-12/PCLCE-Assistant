using System;
using System.Net.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Assistant;

namespace PCL.Core.Test;

[TestClass]
public class AssistantErrorsTest
{
    [TestMethod]
    public void Classify_StatusCodes_MapToTheirOwnHints()
    {
        Assert.AreEqual("Assistant.Error.Balance",
            AssistantErrors.Classify(new AssistantException("服务返回 402", null, 402)).Key);
        Assert.AreEqual("Assistant.Error.Auth",
            AssistantErrors.Classify(new AssistantException("服务返回 401", null, 401)).Key);
        Assert.AreEqual("Assistant.Error.Auth",
            AssistantErrors.Classify(new AssistantException("服务返回 403", null, 403)).Key);
        Assert.AreEqual("Assistant.Error.RateLimited",
            AssistantErrors.Classify(new AssistantException("服务返回 429", null, 429)).Key);
        Assert.AreEqual("Assistant.Error.Server",
            AssistantErrors.Classify(new AssistantException("服务返回 503", null, 503)).Key);
        Assert.AreEqual("Assistant.Error.Rejected",
            AssistantErrors.Classify(new AssistantException("服务返回 400", null, 400)).Key);
    }

    [TestMethod]
    public void Classify_BalanceWordsInBody_WinOverGenericStatus()
    {
        // 不少国产兼容层余额不足也回 400/403，只能看正文
        Assert.AreEqual("Assistant.Error.Balance",
            AssistantErrors.Classify(new AssistantException("服务返回 400",
                "{\"error\":{\"message\":\"Insufficient Balance\"}}", 400)).Key);
        Assert.AreEqual("Assistant.Error.Balance",
            AssistantErrors.Classify(new AssistantException("服务返回 403",
                "{\"error\":{\"message\":\"exceeded your current quota\"}}", 403)).Key);
        Assert.AreEqual("Assistant.Error.Balance",
            AssistantErrors.Classify(new AssistantException("服务返回 400", "账户余额不足", 400)).Key);
    }

    [TestMethod]
    public void Classify_TransportFailures_MapToNetwork()
    {
        Assert.AreEqual("Assistant.Error.Network",
            AssistantErrors.Classify(new HttpRequestException("发送请求时出错")).Key);
        Assert.AreEqual("Assistant.Error.Network",
            AssistantErrors.Classify(new AssistantException("失败",
                "Failed to resolve DNS for api.modrinth.com", 0)).Key);
    }

    [TestMethod]
    public void Classify_UnknownError_KeepsOriginalMessageAsDetail()
    {
        var issue = AssistantErrors.Classify(new InvalidOperationException("模型名称为空"));

        Assert.AreEqual("Assistant.Error.Unknown", issue.Key);
        Assert.AreEqual("模型名称为空", issue.Detail);
        Assert.AreEqual("error", issue.Level);
    }

    [TestMethod]
    public void Classify_StatusDetail_IsCarriedForContext()
    {
        var issue = AssistantErrors.Classify(new AssistantException("服务返回 401", null, 401));

        Assert.AreEqual("401", issue.Detail);
        Assert.AreEqual("error", issue.Level);
    }
}
