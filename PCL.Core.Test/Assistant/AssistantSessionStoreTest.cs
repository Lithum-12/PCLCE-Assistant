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
}
