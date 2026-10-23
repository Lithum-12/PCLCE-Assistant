using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PCL.Core.App;

namespace PCL.Core.Assistant;

public sealed class AssistantSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public List<AssistantMessage> Messages { get; set; } = new();
}

/// <summary>
/// 会话落盘在实例数据目录，跟随实例隔离；损坏时当作空列表，不让聊天页起不来。
/// </summary>
public static class AssistantSessionStore
{
    private static readonly string StorePath = Path.Combine(Paths.Data, "assistant-sessions.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static List<AssistantSession> Sessions { get; } = Load();
    public static AssistantSession? Current { get; private set; }

    public static event Action? Changed;

    public static AssistantSession New()
    {
        var session = new AssistantSession();
        Sessions.Insert(0, session);
        Current = session;
        Save();
        Changed?.Invoke();
        return session;
    }

    public static void Use(AssistantSession? session)
    {
        if (ReferenceEquals(Current, session)) return;
        Current = session;
        Changed?.Invoke();
    }

    public static void Delete(AssistantSession session)
    {
        Sessions.Remove(session);
        if (ReferenceEquals(Current, session)) Current = null;
        Save();
        Changed?.Invoke();
    }

    public static void Save()
    {
        try
        {
            File.WriteAllText(StorePath, JsonSerializer.Serialize(Sessions, Options));
        }
        catch (IOException)
        {
            // 写盘失败不应打断对话
        }
    }

    private static List<AssistantSession> Load()
    {
        try
        {
            if (File.Exists(StorePath))
                return JsonSerializer.Deserialize<List<AssistantSession>>(File.ReadAllText(StorePath)) ?? [];
        }
        catch (Exception)
        {
            // 会话文件损坏时从空开始
        }

        return [];
    }
}
