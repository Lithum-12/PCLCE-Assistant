using System;
using System.Net.Http;
using System.Net.Sockets;

namespace PCL.Core.Assistant;

/// <param name="Level">info / warn / error，决定聊天里那条提示用哪种警告框。</param>
/// <param name="Key">界面侧取文案用的本地化键。</param>
/// <param name="Detail">填进文案的补充信息（状态码或原始错误），不含密钥。</param>
public sealed record AssistantIssue(string Level, string Key, string Detail);

/// <summary>
/// 把服务商返回的错误归成本地能看懂的几类。国产兼容层的错误体千奇百怪，
/// 所以状态码和正文关键字都要看，判不出来时宁可留原文。
/// </summary>
public static class AssistantErrors
{
    private static readonly string[] BalanceWords =
    [
        "余额", "欠费", "额度不足", "超出限额", "insufficient balance", "insufficient_quota", "quota exceeded",
        "payment required", "not enough balance", "arrears", "exceeded your current quota", "billing"
    ];

    private static readonly string[] NetworkWords =
    [
        "no such host", "resolve dns", "failed to resolve", "network", "connection", "connect ",
        "timed out", "timeout", "ssl", "tls", "proxy", "unreachable", "getaddrinfo", "unable to connect",
        "connection was refused", "中断"
    ];

    public static AssistantIssue Classify(Exception error)
    {
        var assistant = error as AssistantException;
        var status = assistant?.StatusCode ?? 0;
        var haystack = ((error.Message ?? "") + " " + (assistant?.Detail ?? "")).ToLowerInvariant();

        if (status == 402 || ContainsAny(haystack, BalanceWords))
            return new AssistantIssue("error", "Assistant.Error.Balance", Status(status));

        if (status is 401 or 403)
            return new AssistantIssue("error", "Assistant.Error.Auth", Status(status));

        if (status == 429)
            return new AssistantIssue("error", "Assistant.Error.RateLimited", Status(status));

        if (status >= 500)
            return new AssistantIssue("error", "Assistant.Error.Server", Status(status));

        if (error is HttpRequestException or SocketException || ContainsAny(haystack, NetworkWords))
            return new AssistantIssue("error", "Assistant.Error.Network", Shorten(error.Message));

        if (status is >= 400 and < 500)
            return new AssistantIssue("error", "Assistant.Error.Rejected", Status(status));

        return new AssistantIssue("error", "Assistant.Error.Unknown", Shorten(error.Message));
    }

    private static string Status(int status) => status == 0 ? "" : status.ToString();

    private static bool ContainsAny(string haystack, string[] words)
    {
        foreach (var word in words)
            if (haystack.Contains(word, StringComparison.Ordinal))
                return true;

        return false;
    }

    private static string Shorten(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "" : text.Length > 160 ? text[..160] + "…" : text;
}
