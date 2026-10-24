using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Assistant;

namespace PCL.Core.Test;

[TestClass]
public class AssistantSessionStoreTest
{
    [TestInitialize]
    public void Reset()
    {
        AssistantSessionStore.StorePath =
            Path.Combine(Path.GetTempPath(), "pclce-assistant-test-" + Guid.NewGuid().ToString("N") + ".json");
        while (AssistantSessionStore.Current is { } current) AssistantSessionStore.DeleteDraft(current);
        AssistantSessionStore.Drafts.Clear();
        AssistantSessionStore.Sessions.Clear();
    }

    [TestCleanup]
    public void Cleanup()
    {
        var path = AssistantSessionStore.StorePath;
        if (File.Exists(path)) File.Delete(path);
    }

    [TestMethod]
    public void StartNewDraft_LeavesPreviousDraftSelectableAndDeletable()
    {
        var first = AssistantSessionStore.StartNewDraft();
        first.DraftText = "写了一半";

        AssistantSessionStore.StartNewDraft();

        CollectionAssert.Contains(AssistantSessionStore.Drafts, first);
        Assert.AreNotEqual(first, AssistantSessionStore.Current);
    }

    [TestMethod]
    public void Use_Draft_StaysInDraftListSoItCanBeDeleted()
    {
        var draft = AssistantSessionStore.StartNewDraft();
        draft.DraftText = "半句话";
        AssistantSessionStore.StartNewDraft();

        AssistantSessionStore.Use(draft);

        Assert.AreEqual(draft, AssistantSessionStore.Current);
        CollectionAssert.Contains(AssistantSessionStore.Drafts, draft);
    }

    [TestMethod]
    public void DeleteDraft_CurrentDraft_ClearsSelection()
    {
        var draft = AssistantSessionStore.StartNewDraft();
        draft.DraftText = "要删掉的草稿";

        AssistantSessionStore.DeleteDraft(draft);

        Assert.IsNull(AssistantSessionStore.Current);
        Assert.IsFalse(AssistantSessionStore.Drafts.Contains(draft));
    }

    [TestMethod]
    public void EmptyDraft_IsDroppedInsteadOfPilingUp()
    {
        var empty = AssistantSessionStore.StartNewDraft();

        AssistantSessionStore.StartNewDraft();

        Assert.AreEqual("", empty.DraftText);
        Assert.IsFalse(AssistantSessionStore.Drafts.Contains(empty));
    }

    [TestMethod]
    public void FirstUserMessage_PromotesDraftToHistoryWithTruncatedTitle()
    {
        var draft = AssistantSessionStore.StartNewDraft();

        AssistantSessionStore.AppendUserMessage(draft, new string('字', 40));

        CollectionAssert.Contains(AssistantSessionStore.Sessions, draft);
        Assert.AreEqual(24, draft.Title.Length);
        Assert.AreEqual(1, draft.Messages.Count);
    }

    [TestMethod]
    public void Delete_CurrentPersistedSession_ClearsSelectionAndFile()
    {
        var session = AssistantSessionStore.StartNewDraft();
        AssistantSessionStore.AppendUserMessage(session, "会话内容");

        AssistantSessionStore.Delete(session);

        Assert.IsNull(AssistantSessionStore.Current);
        Assert.IsFalse(AssistantSessionStore.Sessions.Contains(session));
        Assert.AreEqual("[]", File.ReadAllText(AssistantSessionStore.StorePath));
    }

    [TestMethod]
    public void AppendHintMessage_KeepsLevelAndDoesNotLookLikeAnAnswer()
    {
        var session = AssistantSessionStore.StartNewDraft();
        AssistantSessionStore.AppendUserMessage(session, "在吗");
        AssistantSessionStore.AppendHintMessage(session, "warn", "还没有配置 API 密钥");

        var hint = session.Messages[1];
        Assert.AreEqual("hint", hint.Role);
        Assert.AreEqual("warn", hint.Level);
    }

    [TestMethod]
    public void HintMessage_SurvivesSaveAndReload()
    {
        var session = AssistantSessionStore.StartNewDraft();
        AssistantSessionStore.AppendUserMessage(session, "在吗");
        AssistantSessionStore.AppendHintMessage(session, "error", "余额不足");

        var reloaded = AssistantSessionStore.Load();

        Assert.AreEqual("hint", reloaded[0].Messages[1].Role);
        Assert.AreEqual("error", reloaded[0].Messages[1].Level);
    }

    [TestMethod]
    public void RemoveMessage_DropsTheEmptyPlaceholderAfterAFailedRequest()
    {
        var session = AssistantSessionStore.StartNewDraft();
        AssistantSessionStore.AppendUserMessage(session, "在吗");
        var placeholder = AssistantSessionStore.AppendAssistantMessage(session, "");

        AssistantSessionStore.RemoveMessage(session, placeholder);
        AssistantSessionStore.RemoveMessage(session, 99); // 越界不动手

        Assert.AreEqual(1, session.Messages.Count);
    }

    [TestMethod]
    public void UpdateMessage_PreservesLevelSoHintsDoNotLoseTheirKind()
    {
        var session = AssistantSessionStore.StartNewDraft();
        AssistantSessionStore.AppendUserMessage(session, "在吗");
        AssistantSessionStore.AppendHintMessage(session, "warn", "旧文案");
        AssistantSessionStore.UpdateMessage(session, 1, "新文案");

        Assert.AreEqual("warn", session.Messages[1].Level);
        Assert.AreEqual("新文案", session.Messages[1].Content);
    }

    [TestMethod]
    public void Messages_AreStampedWithTimeAndKeepTraceAcrossReload()
    {
        var session = AssistantSessionStore.StartNewDraft();
        AssistantSessionStore.AppendUserMessage(session, "在吗");
        var answer = AssistantSessionStore.AppendAssistantMessage(session, "回答");
        AssistantSessionStore.CompleteAssistantMessage(session, answer, "回答", ["崩溃报告 2 份", "模组列表 132 项"]);

        Assert.IsTrue(session.Messages[0].At > 0);
        var reloaded = AssistantSessionStore.Load();

        Assert.AreEqual(2, reloaded[0].Messages[1].Trace!.Count);
        Assert.AreEqual(session.Messages[0].At, reloaded[0].Messages[0].At);
    }

    [TestMethod]
    public void CompleteAssistantMessage_WithoutTrace_LeavesFooterEmpty()
    {
        var session = AssistantSessionStore.StartNewDraft();
        AssistantSessionStore.AppendUserMessage(session, "在吗");
        var answer = AssistantSessionStore.AppendAssistantMessage(session, "");

        AssistantSessionStore.CompleteAssistantMessage(session, answer, "纯聊天回答");

        Assert.IsNull(session.Messages[answer].Trace);
    }
}
