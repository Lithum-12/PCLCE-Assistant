using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PCL.Core.Assistant;

/// <summary>
/// 本地文件内容在离开机器之前的尽力脱敏。
/// </summary>
public static partial class AssistantRedactor
{
    // 崩溃报告与 latest.log 里的个人标识形式太多，这里只覆盖确定有害的几类，
    // 定位是"少发出去"，不是可证明的匿名化，所以调用方不该把它当成免死金牌。
    private const string UserMarker = "<用户>";
    private const string ValueMarker = "<数值>";
    private const string SecretMarker = "<已隐藏>";

    [GeneratedRegex(@"(?i)\b([a-z]:\\*users\\*)(?![<>])[^\s\\<>""|]+")]
    private static partial Regex WindowsUserDir();

    [GeneratedRegex(@"(?i)\b([a-z]:/+users/+)(?![<>])[^/\s<>""|]+")]
    private static partial Regex WindowsUserDirSlash();

    [GeneratedRegex(@"(?i)((?:/home|/users|/mnt|/media)/)(?![<>])[^/\s<>""|]+")]
    private static partial Regex UnixHomeDir();

    // 四段数字和 "1.20.1.40" 这类构建号长得一样，所以只抓有上下文的：带关键词的、带端口的、
    // 以及内网/回环段。公网 IP 裸写在日志里又恰好是四段的，宁可留下版本号被误伤的情况更少。
    [GeneratedRegex(@"(?i)\b(ip|address|addr|host|server|地址)\s*[:=]?\s*(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)")]
    private static partial Regex Ipv4Keyword();

    [GeneratedRegex(@"(?<![\w.])((?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?=:\d{1,5}\b)")]
    private static partial Regex Ipv4WithPort();

    [GeneratedRegex(@"(?<![\w.])(?:127\.|10\.|192\.168\.|172\.(?:1[6-9]|2\d|3[01])\.)(?:\d{1,3}\.){2}\d{1,3}")]
    private static partial Regex Ipv4Private();

    [GeneratedRegex(@"(?i)(?<![\w:])(?:::1|::ffff:[\w:.]+|fe80:(?::[\da-f]{0,4}){1,7})(?![\w:])")]
    private static partial Regex Ipv6();

    [GeneratedRegex(@"(?i)\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b")]
    private static partial Regex Uuid();

    [GeneratedRegex(@"(?i)\b([xyz]\s*[:=]\s*)(-?\d+(?:\.\d+)?)(?:[fd])?\b")]
    private static partial Regex SingleAxis();

    [GeneratedRegex(@"(?i)\b(?:pos|position|coords?|location|位置)\s*[:=]?\s*[\[\(]?\s*-?\d+(?:\.\d+)?(?:[,\s]+-?\d+(?:\.\d+)?){2}")]
    private static partial Regex CoordinateTriplet();

    [GeneratedRegex(@"(?i)\b(seed)\s*[:=]\s*""?[\w\-]+""?")]
    private static partial Regex WorldSeed();

    [GeneratedRegex(@"(?i)\b[\w.\-+]+@[\w\-]+\.[\w.\-]+")]
    private static partial Regex Email();

    [GeneratedRegex(@"(?i)\b(access[_-]?token|client[_-]?token|refresh[_-]?token|secret|password|passwd|api[_-]?key|authorization|bearer)\b\s*[:=]\s*\S+")]
    private static partial Regex Credential();

    private static readonly ConcurrentDictionary<string, Regex> TermCache = new(StringComparer.OrdinalIgnoreCase);

    public static string Redact(string? text, IEnumerable<string>? extraTerms = null)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var result = text!;

        result = WindowsUserDir().Replace(result, "$1" + UserMarker);
        result = WindowsUserDirSlash().Replace(result, "$1" + UserMarker);
        result = UnixHomeDir().Replace(result, "$1" + UserMarker);
        result = Credential().Replace(result, "$1=" + SecretMarker);
        result = Ipv6().Replace(result, "<IPv6>");
        result = Ipv4Keyword().Replace(result, "$1=<IP>");
        result = Ipv4WithPort().Replace(result, "<IP>");
        result = Ipv4Private().Replace(result, "<IP>");
        result = Uuid().Replace(result, "<UUID>");
        result = CoordinateTriplet().Replace(result, "<坐标>");
        result = SingleAxis().Replace(result, "$1" + ValueMarker);
        result = WorldSeed().Replace(result, "$1=" + SecretMarker);
        result = Email().Replace(result, "<邮箱>");

        if (extraTerms is not null)
            foreach (var term in extraTerms)
                if (!string.IsNullOrWhiteSpace(term) && term.Length >= 2)
                    result = TermCache.GetOrAdd(term, BuildTermRegex).Replace(result, UserMarker);

        return result;
    }

    private static Regex BuildTermRegex(string term) =>
        new(Regex.Escape(term), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}
