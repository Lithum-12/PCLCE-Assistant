using PCL.Core.App;
using PCL.Core.Assistant;

namespace PCL.Assistant;

public partial class PageAssistantHome
{
    public PageAssistantHome()
    {
        InitializeComponent();
        InputBar.SendRequested += text =>
        {
            var left = AssistantPages.FrmChatLeft ??= new PageChatLeft();
            var chat = (PageChatRight)left.PageGet(FormMain.PageSubType.AssistantChat);
            chat.NewConversation();
            left.PageChange(FormMain.PageSubType.AssistantChat);
            chat.SendFromExternal(text);
        };
    }
}
