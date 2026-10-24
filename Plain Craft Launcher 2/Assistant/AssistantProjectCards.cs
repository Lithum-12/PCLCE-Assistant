using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using PCL.Core.App.Localization;
using PCL.Core.Assistant;

namespace PCL.Assistant;

/// <summary>
/// 资源卡片的界面侧：把链接换成上游的 MyCompItem，点击时尽量跳进启动器自己的资源详情页。
/// 解析与联网查询在 PCL.Core 的 AssistantProjectLinks 里，那部分有单测。
/// </summary>
internal static class AssistantProjectCards
{
    public static List<AssistantSegment> Split(string text) => AssistantProjectLinks.Split(text);

    public static AssistantProjectLink? Parse(string url) => AssistantProjectLinks.Parse(url);

    public static Task<AssistantProjectInfo?> FetchAsync(AssistantProjectLink link) =>
        AssistantProjectLinks.FetchAsync(link);

    /// <summary>点卡片：能进启动器自己的资源详情页就进，退一步才开网页。</summary>
    public static async Task OpenAsync(AssistantProjectLink link)
    {
        try
        {
            var info = await AssistantProjectLinks.FetchAsync(link);
            if (info is null)
            {
                ModBase.OpenWebsite(link.Url);
                return;
            }

            var projects = await ModComp.CompRequest.GetCompProjectsByIdsAsync([info.Id]);
            if (projects.Count == 0)
            {
                ModBase.OpenWebsite(link.Url);
                return;
            }

            ModMain.frmMain.PageChange(new FormMain.PageStackData
            {
                page = FormMain.PageType.CompDetail,
                additional = (projects.First(), new List<string>(), string.Empty, ModComp.CompLoaderType.Any,
                    ModComp.CompType.Any, null)
            });
        }
        catch (Exception e)
        {
            ModBase.Log(e, "[Assistant] 打开资源详情页失败，退回浏览器");
            ModBase.OpenWebsite(link.Url);
        }
    }

    /// <summary>把查到的信息填进上游的资源条目控件；查不到就退化成一行可点文字。</summary>
    public static void Apply(MyCompItem item, AssistantProjectLink link, AssistantProjectInfo? info)
    {
        if (info is null)
        {
            item.Title = link.Slug;
            item.Description = link.Url;
            return;
        }

        item.Title = info.Title;
        item.SubTitle = link.Slug;
        item.Description = info.Description;
        item.Logo = info.IconUrl;
        item.Tags = [];
        item.LabSource.Text = info.Source;
        item.LabVersion.Text = info.LoaderText;
        item.LabVersion.Visibility = string.IsNullOrEmpty(info.LoaderText) ? Visibility.Collapsed : Visibility.Visible;
        item.LabDownload.Text = info.Downloads > 0 ? Lang.CompactNumber(info.Downloads) : "";
        item.LabTime.Text = info.UpdatedAt > DateTime.MinValue
            ? Lang.TimeSpan(info.UpdatedAt - DateTime.Now, 1)
            : "";
    }
}
