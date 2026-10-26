using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Assistant;
using PCL.Core.UI;

namespace PCL.Assistant;

public partial class PageChatLeft
{
    // Keep a single right-hand chat page. AssistantHome remains a legacy enum value only.
    public FormMain.PageSubType pageID = FormMain.PageSubType.AssistantChat;

    public PageChatLeft()
    {
        InitializeComponent();
        AnimatedControl = PanItem;
        Loaded += (_, _) => RefreshHistory();
        AssistantSessionStore.SessionsChanged += () => ModBase.RunInUi(RefreshHistory);
        AssistantSessionStore.DraftsChanged += () => ModBase.RunInUi(RefreshHistory);
        AssistantSessionStore.CurrentChanged += () => ModBase.RunInUi(RefreshHistory);
    }

    private void NewChatClick(object senderRaw, System.Windows.Input.MouseButtonEventArgs e)
    {
        FlushDraftText();
        ((PageChatRight)PageGet(FormMain.PageSubType.AssistantChat)).NewConversation();
    }

    private void DraftClick(object senderRaw, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (((MyListItem)senderRaw).Tag is not AssistantSession draft) return;
        FlushDraftText();
        AssistantSessionStore.Use(draft);
    }

    /// <summary>
    /// 切换前把输入框内容写回当前会话，store 才能判断这条草稿该留还是该丢。
    /// </summary>
    private void FlushDraftText()
    {
        if (AssistantSessionStore.Current is { } current && AssistantPages.FrmChatRight is { } chat)
            current.DraftText = chat.InputDraftText;
    }

    private void HistoryCheck(object senderRaw, ModBase.RouteEventArgs e)
    {
        if (!e.raiseByMouse) return;
        if (((MyListItem)senderRaw).Tag is not AssistantSession session) return;
        FlushDraftText();
        AssistantSessionStore.Use(session);
        // Selecting history only changes the current session. It must not trigger page routing.
    }

    private void HistoryDelete(object senderRaw, EventArgs e)
    {
        if (((MyIconButton)senderRaw).Tag is not AssistantSession session) return;
        DeleteWithConfirm(session);
    }

    /// <summary>删了就找不回来，先确认一眼删的是哪条。图标按钮与右键菜单都走这里。</summary>
    private void DeleteWithConfirm(AssistantSession session)
    {
        if (1 != ModMain.MyMsgBox(Lang.Text("Assistant.Chat.DeleteConfirm", Describe(session)),
                Lang.Text("Assistant.Chat.DeleteConfirm.Title"),
                Lang.Text("Common.Action.Delete"), Lang.Text("Common.Action.Cancel"), isWarn: true)) return;
        ((PageChatRight)PageGet(FormMain.PageSubType.AssistantChat)).DeleteSession(session);
    }

    /// <summary>右键菜单：打开、修改名称、导出、删除。用上游的 MyMenuItem，跟实例列表那些右键菜单一个样式。</summary>
    private void ItemRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not MyListItem { Tag: AssistantSession session } item) return;
        var menu = new ContextMenu { PlacementTarget = item, Placement = PlacementMode.Bottom };
        menu.Items.Add(BuildMenuItem("lucide/folder-open", "Assistant.Left.Open", (_, _) => OpenStore()));
        // 草稿没有标题可改（它的名字就是输入框里那半句话），空草稿也没什么东西可导出
        menu.Items.Add(BuildMenuItem("lucide/pencil", "Assistant.Left.Rename", (_, _) => Rename(session),
            AssistantSessionStore.IsPersisted(session)));
        menu.Items.Add(BuildMenuItem("lucide/download", "Assistant.Left.Export", (_, _) => Export(session),
            session.Messages.Count > 0));
        menu.Items.Add(BuildMenuItem("lucide/trash-2", "Assistant.Left.Delete", (_, _) => DeleteWithConfirm(session)));
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static MyMenuItem BuildMenuItem(string icon, string key, RoutedEventHandler handler, bool enabled = true)
    {
        var item = new MyMenuItem { Header = Lang.Text(key), SvgIcon = icon, IsEnabled = enabled };
        item.Click += handler;
        return item;
    }

    /// <summary>对话都存在同一个 JSON 里，所以"打开"就是打开资源管理器并选中那个文件。</summary>
    private static void OpenStore() => ModBase.OpenExplorer(AssistantSessionStore.StorePath);

    private static void Rename(AssistantSession session)
    {
        var input = ModMain.MyMsgBoxInput(Lang.Text("Assistant.Left.Rename"),
            Lang.Text("Assistant.Left.Rename.Tip"), session.Title);
        // 手改过的名字也算"定下来了"，别让 AI 下一次起名又把它盖掉
        if (!string.IsNullOrWhiteSpace(input)) AssistantSessionStore.RenameSession(session, input.Trim());
    }

    private static void Export(AssistantSession session)
    {
        var path = SystemDialogs.SelectSaveFile(Lang.Text("Assistant.Left.Export"),
            AssistantExport.SafeFileName(session.Title) + ".md", Lang.Text("Assistant.Left.Export.Filter"));
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            File.WriteAllText(path, AssistantExport.ToMarkdown(session), new UTF8Encoding(false));
            HintService.Hint(Lang.Text("Assistant.Left.Export.Done", Path.GetFileName(path)), HintType.Success);
        }
        catch (Exception e)
        {
            ModBase.Log(e, "[Assistant] 导出对话失败");
            HintService.Hint(Lang.Text("Assistant.Left.Export.Failed", e.Message), HintType.Error);
        }
    }

    /// <summary>确认框里说清楚删的是哪一条：历史条目看标题，草稿看输入框里那半句话。</summary>
    private static string Describe(AssistantSession session)
    {
        var text = (AssistantSessionStore.IsPersisted(session) ? session.Title : session.DraftText)
            .Trim().Replace('\r', ' ').Replace('\n', ' ');
        if (text.Length > 40) text = text[..40] + "…";
        return string.IsNullOrWhiteSpace(text) ? Lang.Text("Assistant.Left.Untitled") : text;
    }

    public object PageGet(FormMain.PageSubType? id = null)
    {
        var target = id ?? pageID;
        return target switch
        {
            FormMain.PageSubType.AssistantChat or FormMain.PageSubType.AssistantHome =>
                AssistantPages.FrmChatRight ??= new PageChatRight(),
            _ => throw new Exception("未知的助手子页面种类：" + (int)target)
        };
    }

    private void RefreshHistory()
    {
        PanDrafts.Children.Clear();
        LabDraftsHeader.Visibility = AssistantSessionStore.Drafts.Count > 0 ?
            System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        foreach (var draft in AssistantSessionStore.Drafts)
        {
            var delete = new MyIconButton
            {
                SvgIcon = "lucide/x",
                LogoScale = 0.85,
                Tag = draft,
                ToolTip = Lang.Text("Assistant.Left.Delete")
            };
            delete.Click += HistoryDelete;

            var preview = draft.DraftText.Trim().Replace('\r', ' ').Replace('\n', ' ');
            if (preview.Length > 24) preview = preview[..24] + "…";
            var item = new MyListItem
            {
                Title = string.IsNullOrWhiteSpace(preview) ? Lang.Text("Assistant.Left.Draft") : preview,
                Tag = draft,
                Type = MyListItem.CheckType.Clickable,
                Height = 36,
                MinPaddingRight = 35,
                IsScaleAnimationEnabled = false,
                LogoScale = 0.9,
                SvgIcon = "lucide/pencil",
                Buttons = [delete]
            };
            item.Click += DraftClick;
            item.MouseRightButtonUp += ItemRightClick;
            PanDrafts.Children.Add(item);
        }

        PanHistory.Children.Clear();
        foreach (var session in AssistantSessionStore.Sessions)
        {
            var delete = new MyIconButton
            {
                SvgIcon = "lucide/trash-2",
                LogoScale = 0.85,
                Tag = session,
                ToolTip = Lang.Text("Assistant.Left.Delete")
            };
            delete.Click += HistoryDelete;

            var item = new MyListItem
            {
                Title = string.IsNullOrWhiteSpace(session.Title) ? Lang.Text("Assistant.Left.Untitled") : session.Title,
                Tag = session,
                Type = MyListItem.CheckType.RadioBox,
                Height = 36,
                MinPaddingRight = 35,
                IsScaleAnimationEnabled = false,
                LogoScale = 0.9,
                SvgIcon = "lucide/scroll-text",
                Buttons = [delete]
            };
            item.Check += HistoryCheck;
            item.MouseRightButtonUp += ItemRightClick;
            if (ReferenceEquals(AssistantSessionStore.Current, session))
                item.SetChecked(true, false, false);
            PanHistory.Children.Add(item);
        }
    }
}
