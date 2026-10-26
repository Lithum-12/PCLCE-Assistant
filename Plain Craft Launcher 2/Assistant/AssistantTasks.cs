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
    /// <summary>有整批任务结束了；真正的续轮由聊天页在合适的时机 TakeReady() 取走。</summary>
    public static event Action? BatchReady;

    private static readonly object Gate = new();
    private static readonly Dictionary<ModLoader.LoaderBase, string> Pending = new();
    private static readonly List<AssistantTaskResult> Done = [];
    private static readonly List<IReadOnlyList<AssistantTaskResult>> Ready = [];

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
            lock (Gate)
            {
                if (!Pending.TryGetValue(loader, out var name)) return;
                Pending.Remove(loader);
                Done.Add(new AssistantTaskResult(name, newState == ModBase.LoadState.Finished));
                if (Pending.Count > 0) return;
                // 下载常常比回答先结束，所以这里只排队：谁在忙不该由这里决定
                Ready.Add(Done.ToArray());
                Done.Clear();
            }

            ModBase.RunInUi(() => BatchReady?.Invoke());
        };
        loader.OnStateChangedUi += handler;
    }

    /// <summary>取走最早结束的那一批；没有排好的批次时返回 null。</summary>
    public static IReadOnlyList<AssistantTaskResult>? TakeReady()
    {
        lock (Gate)
        {
            if (Ready.Count == 0) return null;
            var batch = Ready[0];
            Ready.RemoveAt(0);
            return batch;
        }
    }

    /// <summary>玩家自己发言了：之前挂着的任务和排好队的批次都不再触发自动继续。</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Pending.Clear();
            Done.Clear();
            Ready.Clear();
        }
    }
}
