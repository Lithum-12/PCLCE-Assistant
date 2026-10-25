using System;
using System.Collections.Generic;
using System.IO;

namespace PCL.Core.Assistant;

/// <param name="File">可直接执行的文件名或绝对路径。</param>
/// <param name="Prefix">要加在脚本路径前面的参数，py 启动器需要 -3。</param>
public sealed record AssistantPythonCommand(string File, string Prefix);

/// <summary>
/// 解释器怎么找。放在 Core 里是因为这部分是纯逻辑，能被单测钉住：
/// 真正跑进程的代码在界面工程里，那边不好测。
/// </summary>
public static class AssistantPython
{
    /// <summary>Windows 上优先用官方的 py 启动器（多版本共存时它才知道该挑哪个），再退回 PATH 里的 python。</summary>
    public static IReadOnlyList<AssistantPythonCommand> Candidates(bool windows) => windows
        ? [new("py", "-3 "), new("python", ""), new("python3", "")]
        : [new("python3", ""), new("python", "")];

    /// <summary>在 PATH 里找可执行文件；找不到返回 null。测试里可以传自己的查找函数。</summary>
    public static AssistantPythonCommand? Resolve(bool windows, Func<string, bool> onPath)
    {
        foreach (var candidate in Candidates(windows))
            if (onPath(candidate.File)) return candidate;

        return null;
    }

    /// <summary>脚本路径一律加引号：玩家目录里带空格太常见了。</summary>
    public static string BuildArguments(AssistantPythonCommand command, string scriptPath) =>
        command.Prefix + "\"" + scriptPath + "\"";

    /// <summary>脚本落到系统临时目录里，不进实例目录，免得污染玩家的东西。</summary>
    public static string ScriptPath(string id) =>
        Path.Combine(Path.GetTempPath(), "pclce-assistant", $"snippet-{id}.py");
}
