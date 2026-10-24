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
    private static readonly string StorePath = Path.Combine(Paths.Data, "assistant-sessions.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static List<AssistantSession> Sessions { get; } = Load();
    public static List<AssistantSession> Drafts { get; } = new();
    public static AssistantSession? Current { get; private set; } = Sessions.FirstOrDefault();

    /// <summary>Raised when the selected conversation (including an unpersisted draft) changes.</summary>
    public static event Action? CurrentChanged;

    /// <summary>Raised when persisted history or messages change.</summary>
    public static event Action? SessionsChanged;

    /// <summary>Raised when the list of unsent, in-memory drafts changes.</summary>
    public static event Action? DraftsChanged;

    public static bool IsPersisted(AssistantSession session) => Sessions.Contains(session);

    public static AssistantSession StartNewDraft()
    {
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
        var isHistory = Sessions.Contains(session);
        var isDraft = Drafts.Remove(session);
        if (!isHistory && !isDraft) return;
        Current = session;
        if (isDraft) DraftsChanged?.Invoke();
        CurrentChanged?.Invoke();
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
        if (Drafts.Remove(session)) DraftsChanged?.Invoke();
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

    /// <summary>Updates a streaming assistant message in memory; final persistence is explicit.</summary>
    public static void UpdateMessage(AssistantSession session, int index, string content)
    {
        if (index < 0 || index >= session.Messages.Count) return;
        session.Messages[index] = new AssistantMessage(session.Messages[index].Role, content);
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

    private static List<AssistantSession> Load()
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
