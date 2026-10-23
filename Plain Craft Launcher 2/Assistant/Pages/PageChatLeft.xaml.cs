using System.Windows;
using PCL.Core.App;

namespace PCL.Assistant;

public partial class PageChatLeft
{
    public FormMain.PageSubType pageID = FormMain.PageSubType.AssistantHome;

    public PageChatLeft()
    {
        InitializeComponent();
        AnimatedControl = PanItem;
        Loaded += (_, _) => SyncCheckedItem();
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
            if (target == FormMain.PageSubType.AssistantChat) AssistantPages.FrmChatRight?.NewConversation();
            return;
        }

        PageChange(target);
    }

    public void PageChange(FormMain.PageSubType id)
    {
        pageID = id;
        ModMain.frmMain?.PageChange(FormMain.PageType.Assistant, id);
    }

    public object PageGet(FormMain.PageSubType? id = null)
    {
        var target = id ?? pageID;
        return target == FormMain.PageSubType.AssistantChat
            ? (AssistantPages.FrmChatRight ??= new PageChatRight())
            : (AssistantPages.FrmAssistantHome ??= new PageAssistantHome());
    }
}
