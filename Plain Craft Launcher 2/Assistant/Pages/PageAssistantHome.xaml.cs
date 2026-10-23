using System.Windows.Input;
using PCL.Core.App;

namespace PCL.Assistant;

public partial class PageAssistantHome
{
    public PageAssistantHome()
    {
        InitializeComponent();
    }

    private void BtnStart_Click(object sender, MouseButtonEventArgs e)
    {
        ModMain.frmMain?.PageChange(FormMain.PageType.Assistant, FormMain.PageSubType.AssistantChat);
    }
}
