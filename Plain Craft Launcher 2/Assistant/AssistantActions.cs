using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Assistant;
using PCL.Core.App.Localization;
using PCL.Core.Minecraft.Profile;
using PCL.Network.Loaders;

namespace PCL.Assistant;

/// <summary>
/// 执行类动作的实现。刻意跟 AssistantToolset 分开：那边管"挂哪些工具、要不要审批、记什么痕迹"，
/// 这边只负责真的动手，出错一律回一段给模型看得懂的话，不把异常抛回对话循环。
/// </summary>
internal static class AssistantActions
{
    private const int OutputBudgetChars = 8000;
    private const int DefaultTimeoutSeconds = 60;
    private const int MaxTimeoutSeconds = 300;

    public static (string Content, string Note, bool Failed) WriteInstanceFile(McInstance? instance,
        Dictionary<string, string> args)
    {
        if (instance is null) return (AssistantToolset.NoInstance, "", false);

        var relative = args.GetValueOrDefault("path", "").Trim();
        var content = args.GetValueOrDefault("content", "");
        if (relative.Length == 0) return ("缺少 path 参数。", "", true);
        if (content.Length > AssistantLocalWriter.MaxWriteChars)
            return ($"一次最多写 {AssistantLocalWriter.MaxWriteChars} 字，这次有 {content.Length} 字。请分段写。", "太长", true);

        var root = instance.PathIndie;
        var path = AssistantLocalWriter.ResolveWritablePath(root, relative);
        if (path is null)
            return ($"{relative} 不能写：只允许实例目录内的文本文件（配置、options.txt、json/toml/properties 等），" +
                    "启动器自己的 PCL 目录、密钥库、二进制文件与备份目录都在拒绝之列。", "拒绝", true);

        try
        {
            var result = AssistantLocalWriter.WriteText(root, path, content);
            var note = result.Created ? $"新建 {result.Chars:N0} 字" : $"覆盖 {result.Chars:N0} 字";
            return ($"已写入 {result.Source}（{(result.Created ? "新建" : "覆盖")}，{result.Chars} 字）。" +
                    (result.Backup is null ? "" : $"\n原文件已备份到 {result.Backup}，改坏了可以从那里拿回去。"),
                note, false);
        }
        catch (Exception e)
        {
            ModBase.Log(e, "[Assistant] 写入实例文件失败");
            return ($"写入 {relative} 失败：{e.Message}", "失败", true);
        }
    }

    public static async Task<(string Content, string Note, bool Failed)> RunPython(McInstance? instance,
        Dictionary<string, string> args, CancellationToken token)
    {
        var script = args.GetValueOrDefault("script", "");
        if (string.IsNullOrWhiteSpace(script)) return ("缺少 script 参数，没有脚本可执行。", "", true);

        var command = AssistantPython.Resolve(OperatingSystem.IsWindows(), IsOnPath);
        if (command is null)
            return ("这台机器上没有找到 Python（试过 py、python、python3）。" +
                    "请让用户安装 Python，或者改用只读手段把结论讲清楚。", "无 Python", true);

        var timeout = ClampTimeout(args.GetValueOrDefault("timeout_seconds", ""));
        var dir = Path.GetDirectoryName(AssistantPython.ScriptPath(Guid.NewGuid().ToString("N")))!;
        Directory.CreateDirectory(dir);
        var scriptPath = Path.Combine(dir, $"snippet-{Guid.NewGuid():N}.py");
        try
        {
            File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
            var working = string.IsNullOrWhiteSpace(instance?.PathIndie) ? AppDomain.CurrentDomain.BaseDirectory : instance!.PathIndie;
            return await RunAsync(command, scriptPath, working, timeout, token);
        }
        catch (Exception e)
        {
            ModBase.Log(e, "[Assistant] 执行 Python 脚本失败");
            return ($"执行失败：{e.Message}", "失败", true);
        }
        finally
        {
            TryDelete(scriptPath);
        }
    }

    private static async Task<(string Content, string Note, bool Failed)> RunAsync(AssistantPythonCommand command,
        string scriptPath, string working, int timeout, CancellationToken token)
    {
        var info = new ProcessStartInfo
        {
            FileName = command.File,
            Arguments = AssistantPython.BuildArguments(command, scriptPath),
            WorkingDirectory = working,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        // 中文 Windows 上 Python 默认按 GBK 往管道里写，不显式指定就会拿到乱码
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        info.Environment["PYTHONUNBUFFERED"] = "1";

        using var process = new Process { StartInfo = info };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!process.Start()) return ("进程启动失败。", "失败", true);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeout));
        var killed = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            killed = true;
            try { process.Kill(entireProcessTree: true); } catch (Exception) { }
        }

        var builder = new StringBuilder();
        builder.Append($"退出码 {(killed ? "（超时被终止）" : process.HasExited ? process.ExitCode.ToString() : "未知")}");
        if (stdout.Length > 0) builder.Append("\n--- stdout ---\n").Append(Trim(stdout.ToString()));
        if (stderr.Length > 0) builder.Append("\n--- stderr ---\n").Append(Trim(stderr.ToString()));
        if (stdout.Length == 0 && stderr.Length == 0) builder.Append("\n（没有任何输出）");
        if (killed) builder.Append($"\n超过 {timeout} 秒被终止。");
        return (builder.ToString(), killed ? "超时终止" : $"退出码 {(process.HasExited ? process.ExitCode : -1)}", killed);
    }

    public static (string Content, string Note, bool Failed) DeleteInstanceFile(McInstance? instance,
        Dictionary<string, string> args)
    {
        if (instance is null) return (AssistantToolset.NoInstance, "", false);

        var relative = args.GetValueOrDefault("path", "").Trim();
        if (relative.Length == 0) return ("缺少 path 参数。", "", true);

        var root = instance.PathIndie;
        var path = AssistantLocalWriter.ResolveDeletablePath(root, relative);
        if (path is null)
            return ($"{relative} 不能删：只能删实例目录里的单个文件，" +
                    "启动器自己的 PCL 目录、密钥库、备份目录与整个文件夹都在拒绝之列。", "拒绝", true);
        if (!File.Exists(path)) return ($"{relative} 不存在，可能已经被删掉了。", "无此文件", false);

        var size = new FileInfo(path).Length;
        try
        {
            // 走回收站而不是 File.Delete：这是玩家的东西，删模组删错了还能自己捞回来。
            // 用 OnlyErrorDialogs 而不是上游部分调用点的 AllDialogs——确认框我们已经弹过了，不该再让系统弹一层。
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
        catch (Exception e)
        {
            ModBase.Log(e, "[Assistant] 删除文件失败");
            return ($"删除 {relative} 失败：{e.Message}", "失败", true);
        }

        return ($"已把 {relative}（{size:N0} 字节）放进回收站，需要的话玩家还能从回收站还原。",
            size > 1024 * 1024 ? $"{size / 1024 / 1024:N1} MB" : $"{size / 1024:N0} KB", false);
    }

    public static (string Content, string Note, bool Failed) SetLauncherConfig(Dictionary<string, string> args)
    {
        var key = args.GetValueOrDefault("key", "").Trim();
        var raw = args.GetValueOrDefault("value", "");
        if (!AssistantConfigKeys.IsAllowed(key))
            return ($"{key} 不在允许修改的设置清单里。可以改的只有：{string.Join("、", AssistantConfigKeys.Allowed)}。" +
                    "助手不允许改动自己的权限、站点白名单、脱敏开关与账号相关设置。", "不在白名单", true);

        if (!PCL.Core.App.Configuration.ConfigService.TryGetConfigItemNoType(key, out var item))
            return ($"没有名为 {key} 的配置项。", "无此配置", true);

        if (!AssistantConfigKeys.TryCoerce(item.Type, raw, out var value))
            return ($"{key} 需要 {item.Type.Name} 类型的值，收到的是 \"{raw}\"。", "值类型不对", true);

        var previous = item.GetValueNoType()?.ToString() ?? "";
        try
        {
            PCL.Core.App.Configuration.ConfigService.TrySetValue(key, value);
        }
        catch (Exception e)
        {
            ModBase.Log(e, "[Assistant] 写配置失败");
            return ($"写入 {key} 失败：{e.Message}", "失败", true);
        }

        // 回滚提示要说人话：万一改坏了，玩家知道上一个值是什么、在哪个页面能改回去
        return ($"已把启动器设置 {key} 从 \"{previous}\" 改成 \"{value}\"。" +
                "如果改坏了，可以在 设置 里手动改回上一个值。", $"设置 {key}", false);
    }

    /// <summary>
    /// 走启动器自己的下载器：CompFilesGet 是同步 HTTP，整段都必须在后台线程上跑；
    /// 只有把任务挂到速度面板那两行需要回 UI 线程。
    /// </summary>
    public static async Task<(string Content, string Note, bool Failed)> DownloadResource(McInstance? instance,
        Dictionary<string, string> args, CancellationToken token)
    {
        if (instance is null) return (AssistantToolset.NoInstance, "", false);
        var projectId = args.GetValueOrDefault("project", "").Trim();
        if (projectId.Length == 0) return ("缺少 project 参数（Modrinth 的 slug 或 CurseForge 的数字 id）。", "", true);

        List<ModComp.CompProject> projects;
        try
        {
            projects = await ModComp.CompRequest.GetCompProjectsByIdsAsync([projectId]);
        }
        catch (Exception e)
        {
            ModBase.Log(e, "[Assistant] 解析资源失败");
            return ($"找不到工程 {projectId}：{e.Message}", "解析失败", true);
        }

        token.ThrowIfCancellationRequested();
        if (projects.Count == 0) return ($"资源站上找不到 {projectId}。", "无此工程", true);
        var project = projects[0];
        if (project.Type is ModComp.CompType.ModPack or ModComp.CompType.DataPack)
            return ($"{ModComp.GetCompTypeName(project.Type)} 不是把文件丢进目录就算装好的，" +
                    "请让用户在启动器的下载页里操作。", "暂不支持", true);

        List<ModComp.CompFile> files;
        try
        {
            files = ModComp.CompFilesGet(project.Id, project.FromCurseForge);
        }
        catch (Exception e)
        {
            ModBase.Log(e, "[Assistant] 获取文件列表失败");
            return ($"取 {projectId} 的文件列表失败：{e.Message}", "失败", true);
        }

        if (files.Count == 0) return ($"{project.RawName} 没有可下载的文件。", "无文件", true);

        // 玩家或模型指定了加载器就用指定的，没指定就用实例自己装的那些。
        // 传空列表给上游的判定等于"不筛加载器"——上次就是因此把 NeoForge 版下载进了 Fabric 实例。
        var wantedLoader = AssistantLoaders.Normalize(args.GetValueOrDefault("loader", ""));
        var loaders = wantedLoader is null ? LoadersOfInstance(instance) : ToLoaderTypes(wantedLoader);
        if (wantedLoader is null && loaders.Count == 0)
            return ($"实例「{instance.Name}」没有装任何模组加载器，不知道该下哪种版本。" +
                    "先问用户要用 Fabric / NeoForge / Forge / Quilt，再带上 loader 参数重新调用。", "无加载器", true);

        var wantedVersion = args.GetValueOrDefault("version", "").Trim();
        if (wantedVersion.Length == 0) wantedVersion = instance.Info.VanillaName ?? "";

        var wantedFileId = args.GetValueOrDefault("file", "").Trim();
        var file = wantedFileId.Length > 0 ? files.Find(f => f.Id == wantedFileId) : null;
        if (file is null)
            file = files.Where(f => Fits(f, wantedVersion, loaders))
                .OrderByDescending(f => f.Status == ModComp.CompFileStatus.Release)
                .ThenByDescending(f => f.ReleaseDate)
                .FirstOrDefault();
        if (file is null)
            return ($"{project.RawName} 里没有同时满足「{wantedVersion} + {DescribeLoaders(loaders)}」的文件。" +
                    $"可选的版本有：{string.Join("、", files.SelectMany(f => f.GameVersions).Distinct().Take(12))}。" +
                    "把这件事如实告诉用户，让他换个版本号或加载器，不要硬下不匹配的文件。", "无匹配文件", true);

        var target = Path.Combine(instance.PathIndie, SubFolderFor(file.Type), ModComp.CompFileNameGet(project, file));
        try
        {
            StartDownload(project, file, target);
        }
        catch (Exception e)
        {
            ModBase.Log(e, "[Assistant] 启动下载失败");
            return ($"启动下载失败：{e.Message}", "失败", true);
        }

        return ($"已让启动器开始下载 {project.RawName} 的 {file.FileName}" +
                $"（{string.Join("/", file.GameVersions.Take(3))} · {DescribeLoaders(file.ModLoaders)}）到 {target}。" +
                "进度在启动器右下角的下载面板里，任务还在跑；不要假设它已经下载完成。",
            Path.GetFileName(target), false);
    }

    /// <summary>实例装了哪些加载器。原版实例返回空列表，交给调用方去问玩家。</summary>
    private static List<ModComp.CompLoaderType> LoadersOfInstance(McInstance instance)
    {
        // 跟上游 IsInstanceSuitableForFile 一样：没加载过的实例先 Load()，否则 Has* 全是 false
        if (!instance.IsLoaded) instance.Load();
        var info = instance.Info;
        List<ModComp.CompLoaderType> result = [];
        if (info.HasFabric) result.Add(ModComp.CompLoaderType.Fabric);
        if (info.HasLegacyFabric) result.Add(ModComp.CompLoaderType.Fabric);
        if (info.HasQuilt) result.Add(ModComp.CompLoaderType.Quilt);
        if (info.HasNeoForge) result.Add(ModComp.CompLoaderType.NeoForge);
        if (info.HasForge) result.Add(ModComp.CompLoaderType.Forge);
        if (info.HasLiteLoader) result.Add(ModComp.CompLoaderType.LiteLoader);
        return result.Distinct().ToList();
    }

    private static List<ModComp.CompLoaderType> ToLoaderTypes(string normalized) => normalized switch
    {
        "fabric" or "legacyfabric" => [ModComp.CompLoaderType.Fabric],
        "neoforge" => [ModComp.CompLoaderType.NeoForge],
        "forge" => [ModComp.CompLoaderType.Forge],
        "quilt" => [ModComp.CompLoaderType.Quilt],
        "liteloader" => [ModComp.CompLoaderType.LiteLoader],
        _ => []
    };

    /// <summary>版本要落在文件声明的版本列表里；加载器要有交集。文件本身没声明加载器（材质、光影、世界）时不按加载器卡。</summary>
    private static bool Fits(ModComp.CompFile file, string version, List<ModComp.CompLoaderType> loaders)
    {
        if (version.Length > 0 && file.GameVersions.Count > 0 &&
            !file.GameVersions.Any(v => v.Contains(version, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (loaders.Count > 0 && file.ModLoaders.Count > 0 && !file.ModLoaders.Intersect(loaders).Any())
            return false;
        return true;
    }

    private static string DescribeLoaders(List<ModComp.CompLoaderType> loaders) =>
        loaders.Count == 0 ? "不限加载器" : string.Join("/", loaders);

    private static void StartDownload(ModComp.CompProject project, ModComp.CompFile file, string target)
    {
        var loaders = new List<ModLoader.LoaderBase>
        {
            new LoaderDownload(Lang.Text("Download.Comp.Detail.DownloadFile"),
                new List<PCL.Network.DownloadFile> { file.ToNetFile(target) })
            {
                ProgressWeight = 6,
                block = true
            }
        };
        var name = Lang.Text("Download.Comp.Detail.DownloadResource", ModComp.GetCompTypeName(project.Type),
            ModBase.GetFileNameWithoutExtentionFromPath(target));
        var loader = new ModLoader.LoaderCombo<int>(name, loaders)
        {
            OnStateChanged = ModDownloadLib.LoaderStateChangedHintOnly
        };
        loader.Start(1);
        // 挂到速度面板与右下角按钮上：这两个都是 WPF 依赖属性，必须回 UI 线程
        ModBase.RunInUi(() =>
        {
            ModLoader.LoaderTaskbarAdd(loader);
            if (ModMain.frmMain is null) return;
            ModMain.frmMain.BtnExtraDownload.ShowRefresh();
            ModMain.frmMain.BtnExtraDownload.Ribble();
        });
    }

    /// <summary>与上游 _GetSubFolder 一致的实例内目标目录。</summary>
    private static string SubFolderFor(ModComp.CompType type) => type switch
    {
        ModComp.CompType.Mod => "mods",
        ModComp.CompType.ResourcePack => "resourcepacks",
        ModComp.CompType.Shader => "shaderpacks",
        ModComp.CompType.World => "saves",
        _ => "mods"
    };

    private static int ClampTimeout(string raw) =>
        int.TryParse(raw.Trim(), out var value) ? Math.Min(Math.Max(value, 5), MaxTimeoutSeconds) : DefaultTimeoutSeconds;

    private static string Trim(string text) =>
        text.Length <= OutputBudgetChars ? text : text[..OutputBudgetChars] + "\n…（输出已截断）";

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
    }

    /// <summary>PATH 里逐个目录试；py.exe 在 Windows 目录里，不靠 PATH 也能见到。</summary>
    private static bool IsOnPath(string file)
    {
        if (Path.IsPathRooted(file) && File.Exists(file)) return true;
        var windows = OperatingSystem.IsWindows();
        if (windows)
        {
            var system = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            foreach (var ext in new[] { ".exe", ".bat", ".cmd" })
                if (File.Exists(Path.Combine(system, file + ext))) return true;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => File.Exists(Path.Combine(dir.Trim(), windows ? file + ".exe" : file)));
    }
}
