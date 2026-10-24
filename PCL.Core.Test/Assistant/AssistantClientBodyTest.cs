using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Assistant;

namespace PCL.Core.Test;

[TestClass]
public class AssistantClientBodyTest
{
    [TestMethod]
    public void BuildBody_WithTools_EmitsOpenAiFunctionSchema()
    {
        var body = AssistantClient.BuildBody("deepseek-chat",
            [new AssistantMessage("system", "你是助手"), new AssistantMessage("user", "有哪些优化模组？")],
            [new AssistantToolDef("search_modrinth", "在 Modrinth 搜索",
                [new AssistantToolParam("query", "搜索关键词")])]);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.IsTrue(root.GetProperty("stream").GetBoolean());
        Assert.AreEqual("auto", root.GetProperty("tool_choice").GetString());

        var tool = root.GetProperty("tools")[0];
        Assert.AreEqual("function", tool.GetProperty("type").GetString());
        Assert.AreEqual("search_modrinth", tool.GetProperty("function").GetProperty("name").GetString());

        var parameters = tool.GetProperty("function").GetProperty("parameters");
        Assert.AreEqual("object", parameters.GetProperty("type").GetString());
        Assert.AreEqual("string",
            parameters.GetProperty("properties").GetProperty("query").GetProperty("type").GetString());
        Assert.AreEqual("搜索关键词",
            parameters.GetProperty("properties").GetProperty("query").GetProperty("description").GetString());
        Assert.AreEqual("query", parameters.GetProperty("required")[0].GetString());
        Assert.AreEqual(2, root.GetProperty("messages").GetArrayLength());
    }

    [TestMethod]
    public void BuildBody_WithoutTools_OmitsToolFields()
    {
        using var doc = JsonDocument.Parse(AssistantClient.BuildBody("m", [new AssistantMessage("user", "hi")], null));
        Assert.IsFalse(doc.RootElement.TryGetProperty("tools", out _));
        Assert.IsFalse(doc.RootElement.TryGetProperty("tool_choice", out _));
    }

    [TestMethod]
    public void ToJson_AssistantToolRound_KeepsArgumentsAsJsonString()
    {
        var call = new AssistantToolCall("call_1", "search_modrinth", """{"query":"sodium"}""");
        using var doc = JsonDocument.Parse(AssistantClient.ToJson(AssistantMessage.AssistantWithTools([call], "我先查一下")).ToJsonString());
        var root = doc.RootElement;

        Assert.AreEqual("assistant", root.GetProperty("role").GetString());
        // arguments 必须是字符串；写成对象会被不少兼容层判成非法请求
        var arguments = root.GetProperty("tool_calls")[0].GetProperty("function").GetProperty("arguments");
        Assert.AreEqual(JsonValueKind.String, arguments.ValueKind);
        Assert.AreEqual("""{"query":"sodium"}""", arguments.GetString());
        Assert.AreEqual("我先查一下", root.GetProperty("content").GetString());
    }

    [TestMethod]
    public void ToJson_EmptyArguments_BecomeEmptyObject()
    {
        var call = new AssistantToolCall("call_2", "list_mods", "");
        using var doc = JsonDocument.Parse(AssistantClient.ToJson(AssistantMessage.AssistantWithTools([call])).ToJsonString());
        Assert.AreEqual("{}", doc.RootElement.GetProperty("tool_calls")[0].GetProperty("function").GetProperty("arguments").GetString());
        Assert.AreEqual("", doc.RootElement.GetProperty("content").GetString());
    }

    [TestMethod]
    public void ToJson_ToolMessage_CarriesToolCallId()
    {
        using var doc = JsonDocument.Parse(AssistantClient.ToJson(AssistantMessage.Tool("call_9", "共 12 个模组")).ToJsonString());
        Assert.AreEqual("tool", doc.RootElement.GetProperty("role").GetString());
        Assert.AreEqual("call_9", doc.RootElement.GetProperty("tool_call_id").GetString());
        Assert.AreEqual("共 12 个模组", doc.RootElement.GetProperty("content").GetString());
    }
}
