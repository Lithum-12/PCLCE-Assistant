using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Assistant;

namespace PCL.Core.Test;

[TestClass]
public class AssistantStreamAccumulatorTest
{
    [TestMethod]
    public void FeedData_ContentDeltas_AppendInOrderAndReportSlice()
    {
        var acc = new AssistantStreamAccumulator();
        Assert.AreEqual("你好", acc.FeedData("""{"choices":[{"index":0,"delta":{"role":"assistant","content":"你好"},"finish_reason":null}]}"""));
        Assert.AreEqual("，世界", acc.FeedData("""{"choices":[{"index":0,"delta":{"content":"，世界"}}]}"""));
        Assert.AreEqual("", acc.FeedData("""{"choices":[{"index":0,"delta":{"role":"assistant"}}]}"""));
        Assert.AreEqual("你好，世界", acc.Build().Content);
    }

    [TestMethod]
    public void FeedData_ToolArguments_SplitAcrossFragmentsAndKeptInIndexOrder()
    {
        var acc = new AssistantStreamAccumulator();
        acc.FeedData("""{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_a","type":"function","function":{"name":"read_crash_report","arguments":""}}]}}]}""");
        acc.FeedData("""{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"maxFiles\""}}]}}]}""");
        acc.FeedData("""{"choices":[{"delta":{"tool_calls":[{"index":1,"id":"call_b","function":{"name":"list_mods"}}]}}]}""");
        acc.FeedData("""{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":": \"2\"}"}}]}}]}""");
        acc.FeedData("""{"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""");

        var reply = acc.Build();
        Assert.AreEqual(2, reply.ToolCalls.Count);
        Assert.AreEqual("call_a", reply.ToolCalls[0].Id);
        Assert.AreEqual("read_crash_report", reply.ToolCalls[0].Name);
        Assert.AreEqual("""{"maxFiles": "2"}""", reply.ToolCalls[0].ArgumentsJson);
        Assert.AreEqual("call_b", reply.ToolCalls[1].Id);
        Assert.AreEqual("list_mods", reply.ToolCalls[1].Name);
        Assert.AreEqual("tool_calls", reply.FinishReason);
    }

    [TestMethod]
    public void FeedData_FullResendOfArguments_DoesNotDoubleThem()
    {
        var acc = new AssistantStreamAccumulator();
        acc.FeedData("""{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"c1","function":{"name":"search_modrinth","arguments":"{\"q\":\"sodium\"}"}}]}}]}""");
        acc.FeedData("""{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"q\":\"sodium\"}"}}]}}]}""");

        Assert.AreEqual("""{"q":"sodium"}""", acc.Build().ToolCalls[0].ArgumentsJson);
    }

    [TestMethod]
    public void FeedData_MissingChoicesOrBrokenJson_IsSkipped()
    {
        var acc = new AssistantStreamAccumulator();
        Assert.AreEqual("", acc.FeedData("""{"error":"rate limited"}"""));
        Assert.AreEqual("", acc.FeedData("""{"choices":[]}"""));
        Assert.AreEqual("", acc.FeedData("{ this is not json "));
        Assert.AreEqual(0, acc.Build().ToolCalls.Count);
        Assert.AreEqual("", acc.Build().Content);
    }

    [TestMethod]
    public void FeedData_SegmentArrayContent_IsJoined()
    {
        var acc = new AssistantStreamAccumulator();
        acc.FeedData("""{"choices":[{"delta":{"content":[{"type":"text","text":"甲"},{"type":"text","text":"乙"}]}}]}""");
        Assert.AreEqual("甲乙", acc.Build().Content);
    }

    [TestMethod]
    public void FeedWhole_ParsesNonStreamingMessage()
    {
        var acc = new AssistantStreamAccumulator();
        var added = acc.FeedWhole(
            """{"choices":[{"index":0,"message":{"role":"assistant","content":"整段回复","tool_calls":[{"index":0,"id":"w1","function":{"name":"list_mods","arguments":"{}"}}]},"finish_reason":"tool_calls"}]}""");

        Assert.AreEqual("整段回复", added);
        var reply = acc.Build();
        Assert.AreEqual("整段回复", reply.Content);
        Assert.AreEqual(1, reply.ToolCalls.Count);
        Assert.AreEqual("w1", reply.ToolCalls[0].Id);
        Assert.AreEqual("tool_calls", reply.FinishReason);
    }
}
