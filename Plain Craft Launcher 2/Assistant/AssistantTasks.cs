using System;
using System.Collections.Generic;

namespace PCL.Assistant;

/// <param name="Label">任务名，例如 "下载 embeddium-0.3.18.jar"。</param>
/// <param name="Success">是不是 Finished（Failed / Aborted 都算没成）。</param>
internal sealed record AssistantTaskResult(string Label, bool Success);

/// <summary>
/// 助手自己发起的长任务。只有从这里结束的任务才会触发自动继续——
/// 玩家自己在下载页点的东西不该让助手插话。
/// 一批 = 从上一个任务结束到下一个任务开始之间挂着的那些；整批结束才回调一次，
/// 免得"下载了四个 mod"就自动续四轮。
/// </summary>
internal static class AssistantTasks
{
    /// <summary>这一批任务全部结束。</summary>
    public static event Action<IReadOnlyList<AssistantTaskResult>>? AllFinished;

    private static readonly object Gate = new();
    private static readonly Dictionary<ModLoader.LoaderBase, string> Pending = new();
    private static readonly List<AssistantTaskResult> Done = [];

    public static int PendingCount
    {
        get
        {
            lock (Gate) return Pending.Count;
        }
    }

    public static void Watch(ModLoader.LoaderBase loader, string label)
    {
        lock (Gate)
        {
            if (Pending.Count == 0) Done.Clear();
            Pending[loader] = label;
        }

        var reported = false;
        ModLoader.LoaderBase.OnStateChangedUiEventHandler handler = null;
        handler = (_, newState, _) =>
        {
            if (newState is not (ModBase.LoadState.Finished or ModBase.LoadState.Failed or ModBase.LoadState.Aborted))
                return;
            if (reported) return;
            reported = true;
            loader.OnStateChangedUi -= handler;
            AssistantTaskResult[] batch = null;
            lock (Gate)
            {
                if (!Pending.TryGetValue(loader, out var name)) return;
                Pending.Remove(loader);
                Done.Add(new AssistantTaskResult(name, newState == ModBase.LoadState.Finished));
                if (Pending.Count > 0) return;
                batch = Done.ToArray();
            }

            ModBase.RunInUi(() => AllFinished?.Invoke(batch));
        };
        loader.OnStateChangedUi += handler;
    }

    /// <summary>玩家自己发言了：之前挂着的任务结束后不再触发自动继续。</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Pending.Clear();
            Done.Clear();
        }
    }
}
