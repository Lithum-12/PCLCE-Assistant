using System;
using System.Text;

namespace PCL.Core.Assistant;

/// <summary>
/// 把一段对话导出成 Markdown。单独放在 Core 里是因为这段拼装是纯逻辑，
/// 能被单测钉住——导出出去的格式一旦定了就别再悄悄变。
/// </summary>
public static class AssistantExport
{
    /// <summary>文件名里不能出现的字符一律换成下划线，标题再长也截一下。</summary>
    public static string SafeFileName(string? title)
    {
        var text = (title ?? "").Trim();
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            text = text.Replace(c, '_');
        text = text.Replace(" ", "_").Trim('_');
        if (text.Length == 0) text = "conversation";
        return text.Length > 60 ? text[..60] : text;
    }

    public static string ToMarkdown(AssistantSession session)
    {
        var builder = new StringBuilder();
        var title = string.IsNullOrWhiteSpace(session.Title) ? "未命名对话" : session.Title;
        builder.Append("# ").Append(title).Append("\n\n");
        builder.Append("- 导出时间：").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append('\n');
        builder.Append("- 消息条数：").Append(session.Messages.Count).Append("\n\n");
        foreach (var message in session.Messages)
        {
            var clock = Clock(message);
            if (message.Role == "hint")
            {
                builder.Append("> ⚠ ").Append(OneLine(message.Content));
                if (clock.Length > 0) builder.Append(" · ").Append(clock);
                builder.Append("\n\n");
                continue;
            }

            builder.Append("## ").Append(message.Role == "user" ? "玩家" : "助手");
            if (clock.Length > 0) builder.Append(" · ").Append(clock);
            builder.Append('\n').Append(message.Content.Trim()).Append('\n');
            if (message.Trace is { Count: > 0 })
            {
                builder.Append("\n> 本次工具调用：").Append(string.Join("；", message.Trace)).Append('\n');
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>没有时间点（旧数据里 At 为 0）时就干脆不写时间，别印出 1970 这种假时间。</summary>
    private static string Clock(AssistantMessage message) =>
        message.At > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(message.At).LocalDateTime.ToString("HH:mm") : "";

    private static string OneLine(string text) => text.Replace("\r", " ").Replace("\n", " ").Trim();
}
