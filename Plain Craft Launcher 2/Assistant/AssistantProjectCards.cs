using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PCL.Core.App.Localization;
using PCL.Core.Assistant;
using PCL.Core.UI.Controls.SvgIcon;

namespace PCL.Assistant;

/// <summary>
/// 把回答里的 Modrinth / CurseForge 链接换成可点开的资源卡片。
/// 这里刻意不复用下载页的 MyCompItem：它内部的点击逻辑会去读
/// pageCurrent.additional.Value 和 frmDownloadMod 的加载器输入，
/// 从聊天页点一下就抛异常——卡片只需要长得像，不该带上那些页面假设。
/// </summary>
internal static class AssistantProjectCards
{
    public static List<AssistantSegment> Split(string text) => AssistantProjectLinks.Split(text);

    public static AssistantProjectLink? Parse(string url) => AssistantProjectLinks.Parse(url);

    public static Task<AssistantProjectInfo?> FetchAsync(AssistantProjectLink link) =>
        AssistantProjectLinks.FetchAsync(link);

    public static FrameworkElement Create(AssistantProjectLink link) => new ProjectCard(link);

    private sealed class ProjectCard : MyCard
    {
        private readonly MyImage _logo = new()
        {
            Width = 50,
            Height = 50,
            Stretch = Stretch.UniformToFill
        };
        private readonly TextBlock _title = new()
        {
            FontSize = 14,
            TextTrimming = TextTrimming.WordEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        private readonly TextBlock _slug = new()
        {
            FontSize = 12,
            Opacity = 0.4,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.WordEllipsis
        };
        private readonly TextBlock _description = new()
        {
            FontSize = 12,
            TextTrimming = TextTrimming.WordEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        private readonly StackPanel _meta = new() { Orientation = System.Windows.Controls.Orientation.Horizontal };
        private readonly AssistantProjectLink _link;

        public ProjectCard(AssistantProjectLink link)
        {
            _link = link;
            HasMouseAnimation = true;
            Cursor = System.Windows.Input.Cursors.Hand;
            Margin = new Thickness(0, 2, 0, 6);
            ToolTip = link.Url;
            Title = "";

            _title.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1");
            _description.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush4");

            var logoClip = new RectangleGeometry(new Rect(0, 0, 50, 50), 6, 6);
            var logoHost = new Border
            {
                Width = 50,
                Height = 50,
                Clip = logoClip,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Child = _logo
            };

            var head = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            head.Children.Add(_title);
            head.Children.Add(_slug);

            var grid = new Grid { Margin = new Thickness(14, 9, 14, 9) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(19) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(17) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(15) });
            Grid.SetRowSpan(logoHost, 3);
            grid.Children.Add(logoHost);
            grid.Children.Add(head);
            Grid.SetColumn(head, 2);
            grid.Children.Add(_description);
            Grid.SetColumn(_description, 2);
            Grid.SetRow(_description, 1);
            grid.Children.Add(_meta);
            Grid.SetColumn(_meta, 2);
            Grid.SetRow(_meta, 2);
            Children.Add(grid);

            _title.Text = link.Slug;
            _description.Text = link.Url;
            MouseLeftButtonUp += async (_, _) => await OpenAsync(_link);
            _ = FillAsync();
        }

        private async Task FillAsync()
        {
            var info = await AssistantProjectLinks.FetchAsync(_link);
            if (info is null) return;
            ModBase.RunInUi(() => Apply(info));
        }

        private void Apply(AssistantProjectInfo info)
        {
            _title.Text = info.Title;
            _slug.Text = _link.Slug;
            _description.Text = info.Description;
            if (!string.IsNullOrEmpty(info.IconUrl)) _logo.Source = info.IconUrl;
            _meta.Children.Clear();
            AddMeta("lucide/layout-list", info.LoaderText);
            AddMeta("lucide/download", info.Downloads > 0 ? Lang.CompactNumber(info.Downloads) : "");
            AddMeta("lucide/arrow-up-from-line",
                info.UpdatedAt > DateTime.MinValue ? Lang.TimeSpan(info.UpdatedAt - DateTime.Now, 1) : "");
            // 来源单独占一格，跟下载页那张列表条目一样，不然只有标题旁边一小截灰字看不出是哪站的
            AddMeta("lucide/globe", info.Source);
        }

        private void AddMeta(string icon, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var group = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 0, 14, 0)
            };
            var svg = new SvgIcon
            {
                Width = 12,
                Height = 12,
                Icon = icon,
                VerticalAlignment = VerticalAlignment.Center
            };
            svg.SetResourceReference(SvgIcon.IconBrushProperty, "ColorBrush4");
            var label = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush4");
            group.Children.Add(svg);
            group.Children.Add(label);
            _meta.Children.Add(group);
        }
    }

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
}
