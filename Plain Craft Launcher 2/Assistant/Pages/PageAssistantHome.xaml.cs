using PCL.Core.App;

namespace PCL.Assistant;

public partial class PageAssistantHome
{
    public PageAssistantHome()
    {
        InitializeComponent();
        InputBar.SendRequested += text =>
        {
            AssistantPages.FrmChatLeft ??= new PageChatLeft();
            var chat = (PageChatRight)AssistantPages.FrmChatLeft.PageGet(FormMain.PageSubType.AssistantChat);
            ModMain.frmMain?.PageChange(FormMain.PageType.Assistant, FormMain.PageSubType.AssistantChat);
            chat.SendFromExternal(text);
        };
    }
}
