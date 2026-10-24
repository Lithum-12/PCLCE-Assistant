using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PCL.Core.Assistant;

/// <param name="Source">相对实例目录的来源，同时用作发送痕迹的说明。</param>
/// <param name="Text">窗口化截取后的正文，脱敏由调用方再做一次。</param>
/// <param name="RawChars">本次实际读到的字符数，可能小于整份文件。</param>
/// <param name="Truncated">文件比读取窗口大，或正文被截掉过。</param>
public sealed record AssistantExcerpt(string Source, string Text, int RawChars, bool Truncated);

/// <summary>
/// 只读的实例文件提取器。全部按目录路径入参、不碰启动器全局状态，所以能直接进单元测试。
/// </summary>
public static class AssistantLocalReader
{
    public const int DefaultBudgetChars = 6000;

    private static readonly string[] CrashExtensions = [".txt", ".json", ".log"];

    public static IReadOnlyList<AssistantExcerpt> ReadCrashReports(string instancePath, int maxFiles = 2,
        int budgetChars = DefaultBudgetChars)
    {
        var folder = new DirectoryInfo(Path.Combine(instancePath, "crash-reports"));
        if (!folder.Exists) return [];

        List<FileInfo> picked;
        try
        {
            picked = folder.EnumerateFiles()
                .Where(f => CrashExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
                .Where(f => f.Length > 0)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(Math.Max(1, maxFiles))
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }

        var headChars = Math.Max(400, budgetChars * 3 / 5);
        var tailChars = Math.Max(400, budgetChars - headChars);
        List<AssistantExcerpt> result = [];
        foreach (var file in picked)
        {
            var head = ReadWindow(file.FullName, fromEnd: false, headChars);
            if (head is null) continue;
            if (!head.Value.Partial)
            {
                result.Add(Excerpt(instancePath, file, head.Value.Text, head.Value.ReadChars, false));
                continue;
            }

            // 崩溃报告的开头是异常链、结尾是 System Details，中间大段是重复清单，两头都比中部值钱
            var tail = ReadWindow(file.FullName, fromEnd: true, tailChars);
            var text = tail is null || tail.Value.Text.Length == 0
                ? head.Value.Text
                : head.Value.Text + $"\n…（中间省略约 {EstimateOmittedChars(file) - headChars - tail.Value.Text.Length} 字）…\n" +
                  tail.Value.Text;
            result.Add(Excerpt(instancePath, file, text, head.Value.ReadChars + (tail?.ReadChars ?? 0), true));
        }

        return result;
    }

    public static AssistantExcerpt? ReadLatestLog(string instancePath, int budgetChars = DefaultBudgetChars)
    {
        var path = Path.Combine(instancePath, "logs", "latest.log");
        if (!new FileInfo(path).Exists) return null;
        // 日志里有用的部分几乎都在末尾，整份读进来只会撑爆请求体
        var window = ReadWindow(path, fromEnd: true, budgetChars);
        if (window is null) return null;
        return new AssistantExcerpt(Relative(instancePath, path), window.Value.Text, window.Value.ReadChars,
            window.Value.Partial);
    }

    public static IReadOnlyList<string> ReadModFileNames(string instancePath, int maxItems = 300)
    {
        var folder = new DirectoryInfo(Path.Combine(instancePath, "mods"));
        if (!folder.Exists) return [];
        try
        {
            return folder.EnumerateFiles()
                .Where(f => f.Length > 0)
                .Select(f => f.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, maxItems))
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>读 version.json 的 id；拿不到返回空串，让模型自己从日志里找版本号。</summary>
    public static string ReadGameVersion(string instancePath)
    {
        foreach (var path in new[]
                 {
                     Path.Combine(instancePath, "version.json"),
                     Path.Combine(instancePath, "..", "version.json")
                 })
            try
            {
                if (!File.Exists(path)) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    return id.GetString() ?? "";
            }
            catch (Exception)
            {
                // 坏 json 不算错误，只是没有这个信息
            }

        return "";
    }

    private static AssistantExcerpt Excerpt(string root, FileInfo file, string text, int readChars, bool truncated) =>
        new(Relative(root, file.FullName), text, readChars, truncated);

    private static long EstimateOmittedChars(FileInfo file) => file.Length; // ASCII 日志里字节数≈字符数，估个量级就够

    /// <summary>把相对路径锁在实例目录内；越界、绝对路径和敏感文件一律返回 null。</summary>
    public static string? ResolveInstancePath(string root, string? relative)
    {
        if (string.IsNullOrWhiteSpace(root)) return null;
        var value = (relative ?? "").Trim().Replace('\\', '/').TrimStart('/');
        if (value.Length == 0 || value.Contains(':') || value.Contains("..")) return null;
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(rootFull, value));
        if (!candidate.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return null;
        return IsSensitive(candidate) ? null : candidate;
    }

    // 实例目录下的 PCL 子目录里有 LatestLaunch.bat（带 accessToken 与玩家身份）和启动器配置，
    // 一律不给模型读；密钥库类文件同理
    private static readonly string[] SensitiveHints =
    [
        "pcl" + Path.DirectorySeparatorChar, ".jks", ".p12", ".pem", ".key", ".env", "instance.lock"
    ];

    private static bool IsSensitive(string path)
    {
        var lower = path.ToLowerInvariant();
        foreach (var hint in SensitiveHints)
            if (lower.Contains(hint.ToLowerInvariant(), StringComparison.Ordinal))
                return true;

        return false;
    }

    /// <summary>列目录；文件夹带结尾斜杠，方便模型继续往下钻。</summary>
    public static IReadOnlyList<string> ListDirectory(string dir, int maxItems = 300)
    {
        try
        {
            var info = new DirectoryInfo(dir);
            if (!info.Exists) return [];
            List<string> names = [];
            foreach (var child in info.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                names.Add(child.Name + "/");
            foreach (var file in info.EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                names.Add($"{file.Name}（{FormatLength(file.Length)}）");
            return names.Take(Math.Max(1, maxItems)).ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>读实例内的任意文件（同样只取头尾窗口）。</summary>
    public static AssistantExcerpt? ReadFile(string root, string path, int budgetChars = DefaultBudgetChars)
    {
        if (!File.Exists(path)) return null;
        var headChars = Math.Max(400, budgetChars * 3 / 5);
        var head = ReadWindow(path, fromEnd: false, headChars);
        if (head is null) return null;
        var text = head.Value.Text;
        if (head.Value.Partial)
        {
            var tail = ReadWindow(path, fromEnd: true, Math.Max(400, budgetChars - headChars));
            if (tail is not null && tail.Value.Text.Length > 0)
                text += $"\n…（中间省略约 {new FileInfo(path).Length - headChars - tail.Value.Text.Length} 字）…\n" +
                        tail.Value.Text;
        }

        return new AssistantExcerpt(Relative(root, path), text, head.Value.ReadChars, head.Value.Partial);
    }

    private static string FormatLength(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };

    private static (string Text, int ReadChars, bool Partial)? ReadWindow(string path, bool fromEnd, int maxChars)    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var total = stream.Length;
            var byteCap = (long)maxChars * 4;
            var partial = total > byteCap;
            if (partial && fromEnd) stream.Seek(total - byteCap, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: !fromEnd);
            var text = reader.ReadToEnd();
            // 从文件中部开始解码时，开头可能残留半个字符
            if (fromEnd) text = text.TrimStart('\uFFFD');
            var readChars = text.Length;
            partial |= readChars > maxChars;
            if (readChars > maxChars) text = fromEnd ? text[^maxChars..] : text[..maxChars];
            return (text, readChars, partial);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Relative(string root, string path)
    {
        try
        {
            return Path.GetRelativePath(root, path).Replace('\\', '/');
        }
        catch (Exception)
        {
            return Path.GetFileName(path);
        }
    }
}
