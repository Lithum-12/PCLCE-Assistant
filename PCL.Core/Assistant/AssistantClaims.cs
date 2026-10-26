using System;
using System.Text.RegularExpressions;

namespace PCL.Core.Assistant;

/// <summary>
/// 识别"它说自己在下载/已经改好了"这类话。
/// 真实事故：模型一个工具都没调，却输出了"四件事都在跑、都还在下载中"，
/// 因为上一轮它确实发起过下载，这一轮就顺着自己的历史编了下去。
/// </summary>
public static class AssistantClaims
{
    private static readonly Regex Action = new(
        "(已经开始|已开始|正在下载|下载中|都在跑|都在下载|已经提交|已提交|已经加入|已加入下载|" +
        "已经下载|已下载|已经安装|已安装|装好了|已经写入|已写入|已经删除|已删除|已经改好|已改好|" +
        "现在开始下载|开始下载|开始安装|" +
        @"(has|have|I'?m)\s+(started|downloading|installing|submitting)|downloading now|is in progress|already (downloaded|installed|written|deleted))",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool DescribesAction(string? text) =>
        !string.IsNullOrEmpty(text) && Action.IsMatch(text);
}
