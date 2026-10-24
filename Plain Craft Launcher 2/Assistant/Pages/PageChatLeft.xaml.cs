using System;
using System.Windows.Controls;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Assistant;

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
        ((PageChatRight)PageGet(FormMain.PageSubType.AssistantChat)).DeleteSession(session);
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
                SvgIcon = "lucide/file-pen-line",
                Buttons = [delete]
            };
            item.Click += DraftClick;
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
                SvgIcon = "lucide/message-square",
                Buttons = [delete]
            };
            item.Check += HistoryCheck;
            if (ReferenceEquals(AssistantSessionStore.Current, session))
                item.SetChecked(true, false, false);
            PanHistory.Children.Add(item);
        }
    }
}
