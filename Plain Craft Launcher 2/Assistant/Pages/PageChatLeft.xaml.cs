using System.Windows;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Assistant;

namespace PCL.Assistant;

public partial class PageChatLeft
{
    public FormMain.PageSubType pageID = FormMain.PageSubType.AssistantHome;

    public PageChatLeft()
    {
        InitializeComponent();
        AnimatedControl = PanItem;
        Loaded += (_, _) =>
        {
            SyncCheckedItem();
            RefreshHistory();
        };
        AssistantSessionStore.Changed += () => ModBase.RunInUi(RefreshHistory);
    }

    private void SyncCheckedItem()
    {
        if (pageID == FormMain.PageSubType.AssistantChat) ItemNewChat.SetChecked(true, false, false);
        else ItemHome.SetChecked(true, false, false);
    }

    private void PageCheck(object senderRaw, ModBase.RouteEventArgs e)
    {
        var sender = (MyListItem)senderRaw;
        if (sender.Tag is null) return;
        var target = (FormMain.PageSubType)ModBase.Val(sender.Tag);
        if (target == pageID)
        {
            if (target == FormMain.PageSubType.AssistantChat) StartNewSession();
            return;
        }

        PageChange(target);
    }

    private void HistoryCheck(object senderRaw, ModBase.RouteEventArgs e)
    {
        if (((MyListItem)senderRaw).Tag is not AssistantSession session) return;
        AssistantSessionStore.Use(session);
        if (pageID != FormMain.PageSubType.AssistantChat)
        {
            PageChange(FormMain.PageSubType.AssistantChat);
            return;
        }

        AssistantPages.FrmChatRight?.LoadSession(session);
    }

    private void HistoryDelete(object senderRaw, EventArgs e)
    {
        if (((MyIconButton)senderRaw).Tag is not AssistantSession session) return;
        AssistantSessionStore.Delete(session);
        if (pageID == FormMain.PageSubType.AssistantChat && AssistantSessionStore.Current is null)
            AssistantPages.FrmChatRight?.NewConversation();
    }

    public void PageChange(FormMain.PageSubType id)
    {
        pageID = id;
        if (id == FormMain.PageSubType.AssistantChat && AssistantSessionStore.Current is null)
            AssistantSessionStore.New();
        ModMain.frmMain?.PageChange(FormMain.PageType.Assistant, id);
    }

    public object PageGet(FormMain.PageSubType? id = null)
    {
        var target = id ?? pageID;
        return target == FormMain.PageSubType.AssistantChat
            ? (AssistantPages.FrmChatRight ??= new PageChatRight())
            : (AssistantPages.FrmAssistantHome ??= new PageAssistantHome());
    }

    private void StartNewSession()
    {
        AssistantSessionStore.New();
        AssistantPages.FrmChatRight?.NewConversation();
    }

    private void RefreshHistory()
    {
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
                Buttons = [delete]
            };
            item.Check += HistoryCheck;
            if (ReferenceEquals(AssistantSessionStore.Current, session) &&
                pageID == FormMain.PageSubType.AssistantChat)
                item.SetChecked(true, false, false);
            PanHistory.Children.Add(item);
        }
    }
}
