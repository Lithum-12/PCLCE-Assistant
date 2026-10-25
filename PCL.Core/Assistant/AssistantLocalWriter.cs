using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PCL.Core.Assistant;

/// <param name="Source">相对实例根目录的路径，给模型和痕迹看。</param>
/// <param name="Backup">覆盖前备份出去的位置；新建文件时为 null。</param>
/// <param name="Chars">写出去的字符数。</param>
/// <param name="Created">是不是新建（false 表示覆盖了已有文件）。</param>
public sealed record AssistantWriteResult(string Source, string? Backup, int Chars, bool Created);

/// <summary>
/// 往实例目录里写文件的唯一入口。比读更严：只认文本类扩展名，
/// 覆盖前先把原件挪进 .pclce-assistant-backup 按时间戳留档，
/// 这样即使 AI 改坏了，玩家也能在实例目录里自己找回上一个版本。
/// </summary>
public static class AssistantLocalWriter
{
    public const string BackupFolder = ".pclce-assistant-backup";
    public const int MaxWriteChars = 200_000;

    private static readonly string[] TextExtensions =
    [
        ".txt", ".cfg", ".conf", ".properties", ".json", ".json5", ".toml",
        ".yaml", ".yml", ".xml", ".ini", ".md", ".lang", ".csv", ".list",
        ".mcfunction", ".snbt", ".bytes"
    ];

    public static bool IsWritableTextFile(string path) =>
        TextExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 删除用的路径守卫：跟写一样不许越界、不许碰启动器自己的目录与密钥库，
    /// 但不限扩展名（删模组 jar 正是主要用途）；只允许删文件，目录一律拒绝。
    /// </summary>
    public static string? ResolveDeletablePath(string root, string? relative)
    {
        var path = AssistantLocalReader.ResolveInstancePath(root, relative);
        if (path is null) return null;
        if (IsInsideBackupFolder(root, path)) return null;
        if (path.EndsWith(Path.DirectorySeparatorChar.ToString()) || Directory.Exists(path)) return null;
        return path;
    }

    /// <summary>沿用读的那道守卫（越界、盘符、PCL 子目录、密钥库一律拒绝），再叠一层扩展名白名单。</summary>
    public static string? ResolveWritablePath(string root, string? relative)
    {
        var path = AssistantLocalReader.ResolveInstancePath(root, relative);
        if (path is null) return null;
        if (IsInsideBackupFolder(root, path)) return null;
        return IsWritableTextFile(path) ? path : null;
    }

    private static bool IsInsideBackupFolder(string root, string path)
    {
        var prefix = Path.Combine(Path.GetFullPath(root), BackupFolder) + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static AssistantWriteResult WriteText(string root, string path, string content)
    {
        if (content.Length > MaxWriteChars)
            throw new ArgumentException($"单次写入不能超过 {MaxWriteChars} 字。", nameof(content));

        var existed = File.Exists(path);
        string? backup = null;
        if (existed)
        {
            backup = Path.Combine(Path.GetFullPath(root), BackupFolder,
                DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"),
                path[(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Length + 1)..]);
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.Copy(path, backup, true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // 不带 BOM：Minecraft 与多数模组配置读带 BOM 的文件会直接解析失败
        File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
        return new AssistantWriteResult(
            Path.GetRelativePath(Path.GetFullPath(root), path).Replace(Path.DirectorySeparatorChar, '/'),
            backup, content.Length, !existed);
    }
}
