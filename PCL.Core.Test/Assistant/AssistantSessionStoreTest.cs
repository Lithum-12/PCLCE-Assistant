using System;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        var temp = path + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);
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

    [TestMethod]
    public void Save_WhenRenameIsBlocked_RewritesInPlaceAndNeverThrows()
    {
        var keep = AssistantSessionStore.StartNewDraft();
        AssistantSessionStore.AppendUserMessage(keep, "留着的内容");

        // 复现 2026-10-25 03:27 连点删除时的现场：目标文件被一个不给 FILE_SHARE_DELETE 的读句柄
        // 开着（索引器/杀软/编辑器都算），File.Move 直接抛 UnauthorizedAccessException。
        // 那时候 Save 只 catch IOException，异常一路穿到删除按钮的 Click 上，UI 线程就停在那了。
        using (new FileStream(AssistantSessionStore.StorePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            // 先确认这个句柄真的挡住了改名，否则整条测试只是走运通过，测不到退回写
            var probe = AssistantSessionStore.StorePath + ".probe";
            var renameBlocked = false;
            try
            {
                File.WriteAllText(probe, "probe");
                File.Move(probe, AssistantSessionStore.StorePath, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                renameBlocked = true;
            }

            Assert.IsTrue(renameBlocked, "File.Move 没有被挡住，测试前提不成立");
            if (File.Exists(probe)) File.Delete(probe);

            var doomed = AssistantSessionStore.StartNewDraft();
            AssistantSessionStore.AppendUserMessage(doomed, "被删掉的那条");
            AssistantSessionStore.Delete(doomed);
        }

        // 序列化默认转义非 ASCII，所以按 Title 比对而不是按原文子串
        using var doc = JsonDocument.Parse(File.ReadAllText(AssistantSessionStore.StorePath));
        var titles = doc.RootElement.EnumerateArray().Select(s => s.GetProperty("Title").GetString()).ToList();
        CollectionAssert.Contains(titles, "留着的内容");
        Assert.IsFalse(titles.Contains("被删掉的那条"));
        Assert.IsFalse(File.Exists(AssistantSessionStore.StorePath + ".tmp"));
    }

    [TestMethod]
    public void Load_PromotesTheNewerTempFileLeftByABlockedRename()
    {
        var keep = AssistantSessionStore.StartNewDraft();
        AssistantSessionStore.AppendUserMessage(keep, "留着的内容");
        var doomed = AssistantSessionStore.StartNewDraft();
        AssistantSessionStore.AppendUserMessage(doomed, "被删掉的那条");
        var beforeDelete = File.ReadAllText(AssistantSessionStore.StorePath);

        AssistantSessionStore.Delete(doomed);
        Assert.AreEqual(1, AssistantSessionStore.Load().Count);

        // .tmp 里存的是"删除之前"那份，且时间戳更新 —— 就是改名被挡时的现场
        // （必须在最后一次 Save 之后再摆，否则会被 Save 自己写的 .tmp 覆盖掉）
        var tempPath = AssistantSessionStore.StorePath + ".tmp";
        File.WriteAllText(tempPath, beforeDelete);
        File.SetLastWriteTimeUtc(tempPath, File.GetLastWriteTimeUtc(AssistantSessionStore.StorePath).AddSeconds(5));

        var reloaded = AssistantSessionStore.Load();

        Assert.AreEqual(2, reloaded.Count);
        Assert.IsFalse(File.Exists(tempPath));
    }

    [TestMethod]
    public void Load_DropsATempFileOlderThanTheStore()
    {
        var keep = AssistantSessionStore.StartNewDraft();
        AssistantSessionStore.AppendUserMessage(keep, "留着的内容");

        var tempPath = AssistantSessionStore.StorePath + ".tmp";
        File.WriteAllText(tempPath, "[]");
        File.SetLastWriteTimeUtc(tempPath, File.GetLastWriteTimeUtc(AssistantSessionStore.StorePath).AddSeconds(-30));

        var reloaded = AssistantSessionStore.Load();

        Assert.AreEqual(1, reloaded.Count);
        Assert.IsFalse(File.Exists(tempPath));
    }

    [TestMethod]
    public void RenameSession_WritesThroughWithoutBumpingActivity()
    {
        var session = AssistantSessionStore.StartNewDraft();
        AssistantSessionStore.AppendUserMessage(session, "请帮我找一下 Neoforge 可用的优化方案");
        var stamp = session.UpdatedAt;

        AssistantSessionStore.RenameSession(session, "NeoForge 优化方案");

        Assert.AreEqual("NeoForge 优化方案", session.Title);
        Assert.IsTrue(session.TitleIsAi);
        Assert.AreEqual(stamp, session.UpdatedAt); // 改标题不该把这条顶到列表最前
        Assert.AreEqual("NeoForge 优化方案", AssistantSessionStore.Load()[0].Title);
    }

    [TestMethod]
    public void RenameSession_IgnoresDeletedSessionsAndBlankTitles()
    {
        var session = AssistantSessionStore.StartNewDraft();
        AssistantSessionStore.AppendUserMessage(session, "在吗");

        AssistantSessionStore.Delete(session);
        AssistantSessionStore.RenameSession(session, "不该生效");
        AssistantSessionStore.RenameSession(session, "   ");

        Assert.AreEqual("在吗", session.Title);
        Assert.IsFalse(session.TitleIsAi);
    }
}
