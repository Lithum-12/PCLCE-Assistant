using System;
using System.Windows.Controls;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Assistant;

namespace PCL.Assistant;

public partial class PageChatLeft
{
    public FormMain.PageSubType pageID = FormMain.PageSubType.AssistantHome;
    private bool isPageSwitched;

    public PageChatLeft()
    {
        InitializeComponent();
        AnimatedControl = PanItem;
        Loaded += (_, _) =>
        {
            if (!isPageSwitched) SyncCheckedItem();
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
        if (!e.raiseByMouse) return;
        var sender = (MyListItem)senderRaw;
        if (sender.Tag is null) return;
        var target = (FormMain.PageSubType)ModBase.Val(sender.Tag);
        if (target == FormMain.PageSubType.AssistantChat)
        {
            ((PageChatRight)PageGet(target)).NewConversation();
            PageChange(target);
            return;
        }

        PageChange(target);
    }

    private void HistoryCheck(object senderRaw, ModBase.RouteEventArgs e)
    {
        if (!e.raiseByMouse) return;
        if (((MyListItem)senderRaw).Tag is not AssistantSession session) return;
        AssistantSessionStore.Use(session);
        PageChange(FormMain.PageSubType.AssistantChat);
    }

    private void HistoryDelete(object senderRaw, EventArgs e)
    {
        if (((MyIconButton)senderRaw).Tag is not AssistantSession session) return;
        AssistantSessionStore.Delete(session);
        if (pageID == FormMain.PageSubType.AssistantChat)
            ((PageChatRight)PageGet(pageID)).RenderCurrentSession();
    }

    /// <summary>
    ///     同一主页面内换右页，与 PageToolsLeft 同构，不走 FormMain.PageChange。
    /// </summary>
    public void PageChange(FormMain.PageSubType id)
    {
        if (pageID == id) return;
        ModAnimation.AniControlEnabled += 1;
        isPageSwitched = true;
        try
        {
            PageChangeRun((MyPageRight)PageGet(id));
            pageID = id;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"切换分页面失败（ID {(int)id}）", ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Tools.Error.OperationFailed"));
        }
        finally
        {
            ModAnimation.AniControlEnabled -= 1;
        }
    }

    private static void PageChangeRun(MyPageRight target)
    {
        ModAnimation.AniStop("FrmMain PageChangeRight");
        if (target.Parent is not null)
            target.SetValue(ContentPresenter.ContentProperty, null);
        ModMain.frmMain.pageRight = target;
        ((MyPageRight)ModMain.frmMain.PanMainRight.Child).PageOnExit();
        ModAnimation.AniStart(new[]
        {
            ModAnimation.AaCode(() =>
            {
                ((MyPageRight)ModMain.frmMain.PanMainRight.Child).PageOnForceExit();
                ModMain.frmMain.PanMainRight.Child = ModMain.frmMain.pageRight;
                ModMain.frmMain.pageRight.Opacity = 0d;
            }, 130),
            ModAnimation.AaCode(() =>
            {
                ModMain.frmMain.pageRight.Opacity = 1d;
                ModMain.frmMain.pageRight.PageOnEnter();
            }, 30, true)
        }, "PageLeft PageChange");
    }

    public object PageGet(FormMain.PageSubType? id = null)
    {
        var target = id ?? pageID;
        switch (target)
        {
            case FormMain.PageSubType.AssistantChat:
            {
                return AssistantPages.FrmChatRight ??= new PageChatRight();
            }
            case FormMain.PageSubType.AssistantHome:
            {
                return AssistantPages.FrmAssistantHome ??= new PageAssistantHome();
            }
            default:
            {
                throw new Exception("未知的助手子页面种类：" + (int)target);
            }
        }
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
                LogoScale = 0.9,
                SvgIcon = "lucide/scroll-text",
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
