using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCL.Core.App;

namespace PCL.Core.Assistant;

public sealed class AssistantSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    // Zero means this is a legacy session which predates UpdatedAt; order it by CreatedAt.
    public long UpdatedAt { get; set; }
    public List<AssistantMessage> Messages { get; set; } = new();

    [JsonIgnore]
    public string DraftText { get; set; } = "";

    [JsonIgnore]
    public long LastActivityAt => UpdatedAt > 0 ? UpdatedAt : CreatedAt * 1000L;
}

/// <summary>
/// Owns conversation selection and persistence. A new conversation is a draft and is not
/// added to history until the first user message is sent. The chat UI has one view; changing
/// Current changes its data, never the page instance.
/// </summary>
public static class AssistantSessionStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };
    private static string? _storePath;
    private static List<AssistantSession>? _sessions;
    private static AssistantSession? _current;
    private static bool _currentResolved;

    /// <summary>
    /// 路径与内容都惰性求值：静态构造里读 Paths 会让这段逻辑在测试宿主里不可用
    /// （Basics 从入口程序集取 metadata.json，测试入口不是启动器）。
    /// </summary>
    public static string StorePath
    {
        get => _storePath ??= Path.Combine(Paths.Data, "assistant-sessions.json");
        set => _storePath = value;
    }

    public static List<AssistantSession> Sessions => _sessions ??= Load();
    public static List<AssistantSession> Drafts { get; } = new();

    public static AssistantSession? Current
    {
        get
        {
            if (_currentResolved) return _current;
            _currentResolved = true;
            _current ??= Sessions.Count > 0 ? Sessions[0] : null;
            return _current;
        }
        private set
        {
            _current = value;
            _currentResolved = true;
        }
    }

    /// <summary>Raised when the selected conversation (including an unpersisted draft) changes.</summary>
    public static event Action? CurrentChanged;

    /// <summary>Raised when persisted history or messages change.</summary>
    public static event Action? SessionsChanged;

    /// <summary>Raised when the list of unsent, in-memory drafts changes.</summary>
    public static event Action? DraftsChanged;

    public static bool IsPersisted(AssistantSession session) => Sessions.Contains(session);

    public static AssistantSession StartNewDraft()
    {
        ParkCurrentDraft();
        var draft = new AssistantSession();
        Current = draft;
        CurrentChanged?.Invoke();
        return draft;
    }

    public static AssistantSession GetCurrentOrCreateDraft()
    {
        if (Current is not null) return Current;
        return StartNewDraft();
    }

    public static void Use(AssistantSession session)
    {
        if (ReferenceEquals(Current, session)) return;
        if (!Sessions.Contains(session) && !Drafts.Contains(session)) return;
        ParkCurrentDraft();
        // 草稿被选中时仍留在 Drafts 中，否则它离开列表就再也删不掉了
        Current = session;
        CurrentChanged?.Invoke();
    }

    /// <summary>
    /// 离开的草稿：有未发送文字就留在草稿区，空的就直接丢掉，避免堆积删不掉的幽灵条目。
    /// </summary>
    private static void ParkCurrentDraft()
    {
        var previous = Current;
        if (previous is null || Sessions.Contains(previous) || previous.Messages.Count > 0) return;
        bool changed;
        if (string.IsNullOrWhiteSpace(previous.DraftText))
            changed = Drafts.Remove(previous);
        else if (Drafts.Contains(previous))
            changed = false;
        else
        {
            Drafts.Insert(0, previous);
            changed = true;
        }

        if (changed) DraftsChanged?.Invoke();
    }

    public static void ParkDraft(AssistantSession session)
    {
        if (Sessions.Contains(session) || session.Messages.Count > 0 ||
            ReferenceEquals(Current, session) || string.IsNullOrWhiteSpace(session.DraftText) ||
            Drafts.Contains(session)) return;
        Drafts.Insert(0, session);
        DraftsChanged?.Invoke();
    }

    public static void DeleteDraft(AssistantSession session)
    {
        var removed = Drafts.Remove(session);
        var wasCurrent = ReferenceEquals(Current, session);
        if (wasCurrent) Current = null;
        if (removed) DraftsChanged?.Invoke();
        if (wasCurrent) CurrentChanged?.Invoke();
    }

    /// <summary>Stores the first prompt, which promotes a draft into a real history item.</summary>
    public static void AppendUserMessage(AssistantSession session, string content)
    {
        Track(session);
        if (string.IsNullOrWhiteSpace(session.Title))
        {
            var title = content.Trim().Replace('\r', ' ').Replace('\n', ' ');
            session.Title = title.Length > 24 ? title[..24] : title;
        }

        session.Messages.Add(new AssistantMessage("user", content));
        Touch(session);
        Save();
    }

    public static int AppendAssistantMessage(AssistantSession session, string content)
    {
        Track(session);
        session.Messages.Add(new AssistantMessage("assistant", content));
        Touch(session);
        Save();
        return session.Messages.Count - 1;
    }

    /// <summary>诊断提示走独立 role：只给人看，不会被拼进模型上下文。</summary>
    public static void AppendHintMessage(AssistantSession session, string level, string content)
    {
        Track(session);
        session.Messages.Add(new AssistantMessage("hint", content) { Level = level });
        Touch(session);
        Save();
    }

    /// <summary>请求在吐出任何内容之前就失败了，把空占位气泡收回去。</summary>
    public static void RemoveMessage(AssistantSession session, int index)
    {
        if (index < 0 || index >= session.Messages.Count) return;
        session.Messages.RemoveAt(index);
        Touch(session);
        Save();
    }

    /// <summary>Updates a streaming assistant message in memory; final persistence is explicit.</summary>
    public static void UpdateMessage(AssistantSession session, int index, string content)
    {
        if (index < 0 || index >= session.Messages.Count) return;
        var previous = session.Messages[index];
        session.Messages[index] = previous with { Content = content };
    }

    public static void CompleteAssistantMessage(AssistantSession session, int index, string content)
    {
        UpdateMessage(session, index, content);
        Touch(session);
        Save();
    }

    public static void Delete(AssistantSession session)
    {
        if (!Sessions.Remove(session)) return;
        var deletedCurrent = ReferenceEquals(Current, session);
        if (deletedCurrent) Current = null;
        Save();
        if (deletedCurrent) CurrentChanged?.Invoke();
    }

    public static void Save()
    {
        try
        {
            Sessions.Sort((a, b) => b.LastActivityAt.CompareTo(a.LastActivityAt));
            var directory = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temporaryPath = StorePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Sessions, Options));
            File.Move(temporaryPath, StorePath, true);
        }
        catch (IOException)
        {
            // A failed disk write must not interrupt a conversation; in-memory state remains usable.
        }
        finally
        {
            SessionsChanged?.Invoke();
        }
    }

    private static void Track(AssistantSession session)
    {
        if (Sessions.Contains(session)) return;
        if (Drafts.Remove(session)) DraftsChanged?.Invoke();
        Sessions.Insert(0, session);
    }

    private static void Touch(AssistantSession session) =>
        session.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    internal static List<AssistantSession> Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return [];
            var sessions = JsonSerializer.Deserialize<List<AssistantSession>>(File.ReadAllText(StorePath)) ?? [];
            // Older versions persisted empty sessions. They are drafts, not useful history entries.
            return sessions
                .Where(session => session is not null && session.Messages is { Count: > 0 })
                .OrderByDescending(session => session.LastActivityAt)
                .ToList();
        }
        catch (Exception)
        {
            // A malformed history file must not prevent the launcher from opening.
            return [];
        }
    }
}
