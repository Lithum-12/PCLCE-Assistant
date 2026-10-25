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
        private bool _warmed;
        private bool _busy;

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
            // 悬停就把详情页要用的 CompProject 拉进缓存，点下去才不用干等一个网络往返
            MouseEnter += (_, _) => Warm();
            MouseLeftButtonUp += async (_, _) => await OpenClickedAsync();
            _ = FillAsync();
        }

        private void Warm()
        {
            if (_warmed) return; // 一张卡最多预热一次，划过滚轮不该刷一堆请求
            _warmed = true;
            _ = AssistantProjectCards.WarmAsync(_link);
        }

        private async Task OpenClickedAsync()
        {
            if (_busy) return; // 连点不再叠页面
            _busy = true;
            Opacity = 0.55d; // 慢网络下也要让人看见"这一下有反应"
            try
            {
                await AssistantProjectCards.OpenAsync(_link);
            }
            finally
            {
                Opacity = 1d;
                _busy = false;
            }
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
            LoadIcon(info.IconUrl);
            _meta.Children.Clear();
            AddMeta("lucide/layout-list", info.LoaderText);
            AddMeta("lucide/download", info.Downloads > 0 ? Lang.CompactNumber(info.Downloads) : "");
            AddMeta("lucide/arrow-up-from-line",
                info.UpdatedAt > DateTime.MinValue ? Lang.TimeSpan(info.UpdatedAt - DateTime.Now, 1) : "");
            // 来源单独占一格，跟下载页那张列表条目一样，不然只有标题旁边一小截灰字看不出是哪站的
            AddMeta("lucide/globe", info.Source);
        }

        /// <summary>
        /// 先把图标下载到本地再交给 MyImage：它的 http 分支会在 UI 线程上发起下载，
        /// 一张卡一个图标，网络稍慢整个窗口就跟着卡死。
        /// </summary>
        private void LoadIcon(string iconUrl)
        {
            if (string.IsNullOrEmpty(iconUrl)) return;
            AssistantWeb.DownloadIconAsync(iconUrl).ContinueWith(t =>
            {
                var path = t.IsCompletedSuccessfully ? t.Result : null;
                if (!string.IsNullOrEmpty(path)) ModBase.RunInUi(() => _logo.Source = path);
            }, TaskScheduler.Default);
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

    private static bool _opening;

    /// <summary>
    /// 点卡片：能进启动器自己的资源详情页就进，退一步才开网页。
    /// 同一时刻只允许一次跳转，否则慢网络下连点会在稍后堆出好几个详情页。
    /// </summary>
    public static async Task OpenAsync(AssistantProjectLink link)
    {
        if (_opening) return;
        _opening = true;
        ModComp.CompProject? project;
        try
        {
            project = await ResolveAsync(link);
        }
        finally
        {
            _opening = false;
        }

        if (project is null || ModMain.frmMain is null)
        {
            ModBase.OpenWebsite(link.Url);
            return;
        }

        ModMain.frmMain.PageChange(new FormMain.PageStackData
        {
            page = FormMain.PageType.CompDetail,
            additional = (project, new List<string>(), string.Empty, ModComp.CompLoaderType.Any,
                ModComp.CompType.Any, string.Empty)
        });
    }

    /// <summary>悬停预取：结果不要紧，详情页读的是上游那张 compProjectCache。</summary>
    public static async Task WarmAsync(AssistantProjectLink link)
    {
        try
        {
            await ResolveAsync(link);
        }
        catch (Exception e)
        {
            ModBase.Log(e, "[Assistant] 预取资源信息失败");
        }
    }

    /// <summary>
    /// 详情页需要完整的 CompProject，而它只能从资源站再取一次——这一步就是原来那 0.5~6 秒。
    /// 上游每构造一个 CompProject 都会写进 ModComp.compProjectCache，所以先查缓存，
    /// 命中就直接跳转，看过的模组第二次点基本是秒开。
    /// </summary>
    private static async Task<ModComp.CompProject?> ResolveAsync(AssistantProjectLink link)
    {
        var info = AssistantProjectLinks.TryPeek(link, out var cached) ? cached : await FetchAsync(link);
        if (info is null) return null;
        if (ModComp.compProjectCache.TryGetValue(info.Id, out var known)) return known;

        try
        {
            var projects = await ModComp.CompRequest.GetCompProjectsByIdsAsync([info.Id]);
            return projects.Count > 0 ? projects.First() : null;
        }
        catch (Exception e)
        {
            ModBase.Log(e, "[Assistant] 获取资源详情失败，退回浏览器");
            return null;
        }
    }
}
