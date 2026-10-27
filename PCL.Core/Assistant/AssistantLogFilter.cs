using System;
using System.Collections.Generic;

namespace PCL.Core.Assistant;

/// <param name="Text">筛出来的日志正文，块与块之间用省略号分隔。</param>
/// <param name="Matched">命中阈值的行数（不含续行与上下文）。</param>
/// <param name="Total">参与筛选的总行数。</param>
/// <param name="CutByBudget">是不是因为超出预算而只留下了最近的若干块。</param>
public readonly record struct LogFilterResult(string Text, int Matched, int Total, bool CutByBudget);

/// <summary>
/// 按日志级别筛游戏日志。崩溃诊断时 latest.log 动辄几 MB，全量塞进请求既贵又没用：
/// 有用的几乎只有 WARN/ERROR 那几十行和它们后面的堆栈。
/// 关键点是堆栈续行（"at ..."、"Caused by:"、缩进的包装信息）本身没有级别前缀，
/// 只按级别过滤会把最有用的部分筛掉。
/// </summary>
public static class AssistantLogFilter
{
    private const string Separator = "…";

    /// <summary>行首最多看几组方括号。原版两组、Forge/NeoForge 三组，多出来的留给模组。</summary>
    private const int MaxLeadingGroups = 4;

    private static readonly char[] SegmentSeparators = ['/', ':', ' ', '\t'];

    /// <summary>阈值："error" = ERROR/FATAL，"warn" = WARN 及以上，其它（含 all）= 不过滤。</summary>
    public static int Threshold(string? level) => (level ?? "").Trim().ToLowerInvariant() switch
    {
        "error" => 4,
        "fatal" => 5,
        "warn" or "warning" => 3,
        "info" => 2,
        _ => 0
    };

    public static int RankOf(string? level) => level switch
    {
        "TRACE" => 0,
        "DEBUG" => 1,
        "INFO" => 2,
        "WARN" => 3,
        "ERROR" => 4,
        "FATAL" => 5,
        _ => -1
    };

    public static bool IsHeaderLine(string line) => Analyze(line).Header;

    public static int RankOfLine(string line) => Analyze(line).Rank;

    /// <summary>
    /// 认日志行的开头。各家的前缀长得不一样，所以不认时间格式，只认"行首那几组方括号里有没有级别"：
    /// 原版 <c>[12:00:01] [Render thread/INFO]:</c>，
    /// Forge/NeoForge <c>[157月2026 10:11:18.196] [main/INFO] [cpw.mods.Launcher/MODLAUNCHER]:</c>
    /// （日期段是本地化的，按时间正则匹配会把整份日志判成续行，那才是真正踩过的坑），
    /// Fabric <c>[main/INFO:modid]</c>。
    /// 第一组没有级别但长得像时间戳的行也算新条目：它不该被并进上一条错误的堆栈里。
    /// </summary>
    private static (bool Header, int Rank) Analyze(string line)
    {
        if (line.Length == 0 || line[0] != '[') return (false, -1);

        var at = 0;
        var firstGroup = "";
        for (var group = 0; group < MaxLeadingGroups && at < line.Length && line[at] == '['; group++)
        {
            var close = line.IndexOf(']', at);
            if (close < 0) break;
            var body = line.Substring(at + 1, close - at - 1);
            if (group == 0) firstGroup = body;
            foreach (var segment in body.Split(SegmentSeparators, StringSplitOptions.TrimEntries))
            {
                var rank = RankOf(segment);
                if (rank >= 0) return (true, rank);
            }

            at = close + 1;
            while (at < line.Length && line[at] == ' ') at++;
        }

        return (FirstGroupLooksLikeTime(firstGroup), -1);
    }

    private static bool FirstGroupLooksLikeTime(string group)
    {
        if (group.Length < 5 || !group.Contains(':')) return false;
        foreach (var c in group)
            if (char.IsDigit(c)) return true;
        return false;
    }

    public static LogFilterResult Select(IReadOnlyList<string> lines, string? level, int contextLines,
        int budgetChars)
    {
        var threshold = Threshold(level);
        if (threshold <= 0) // 不过滤时直接原样返回，交给调用方的截断逻辑
            return new LogFilterResult(string.Join("\n", lines), lines.Count, lines.Count, false);

        var keep = new bool[lines.Count];
        var matched = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            if (RankOfLine(lines[i]) < threshold) continue;
            matched++;
            keep[i] = true;
            for (var c = 1; c <= Math.Max(0, contextLines); c++)
            {
                var before = i - c;
                if (before < 0) break;
                keep[before] = true;
            }

            // 往后把所有"非新行首"的行都带上：整段堆栈才是一条信息
            for (var after = i + 1; after < lines.Count && !IsHeaderLine(lines[after]); after++)
                keep[after] = true;
        }

        return matched == 0
            ? new LogFilterResult("", 0, lines.Count, false)
            : Compose(lines, keep, budgetChars, matched);
    }

    /// <summary>
    /// 从后往前一行一行攒：预算不够时留下的是最近的错误。
    /// 按行而不是按块攒，否则相邻的几十条错误会被当成一块，预算再小也裁不动。
    /// </summary>
    private static LogFilterResult Compose(IReadOnlyList<string> lines, bool[] keep, int budgetChars, int matched)
    {
        var selected = new bool[lines.Count];
        var used = 0;
        var cut = false;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (!keep[i]) continue;
            if (used + lines[i].Length + 1 > budgetChars)
            {
                cut = true;
                break;
            }

            used += lines[i].Length + 1;
            selected[i] = true;
        }

        if (used == 0) return ClampOnly(lines, keep, budgetChars, matched);

        var builder = new System.Text.StringBuilder();
        var gap = false;
        var started = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (!selected[i])
            {
                if (started) gap = true;
                continue;
            }

            if (gap)
            {
                builder.Append(Separator).Append('\n');
                gap = false;
            }

            builder.Append(lines[i]).Append('\n');
            started = true;
        }

        var text = builder.ToString().TrimEnd('\n');
        if (cut && !text.StartsWith(Separator)) text = Separator + "\n" + text;
        return new LogFilterResult(text, matched, lines.Count, cut);
    }

    /// <summary>
    /// 最近的一条命中单行就超预算时走这里：日志里塞了整份注册表快照就是这样。
    /// 返回空正文会让"命中 N 行"配上一句空白，模型只能瞎猜，所以至少给它一个开头。
    /// </summary>
    private static LogFilterResult ClampOnly(IReadOnlyList<string> lines, bool[] keep, int budgetChars, int matched)
    {
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (!keep[i]) continue;
            var take = Math.Max(0, budgetChars - Separator.Length - 1);
            var text = lines[i].Length <= take ? lines[i] : lines[i][..take] + Separator;
            return new LogFilterResult(text, matched, lines.Count, true);
        }

        return new LogFilterResult("", matched, lines.Count, true);
    }
}
