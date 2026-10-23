using System.Windows;

namespace PCL.Assistant;

public partial class PageChatLeft
{
    public PageChatLeft()
    {
        InitializeComponent();
        AnimatedControl = PanItem;
        Loaded += (_, _) => ItemChat.SetChecked(true, false, false);
    }
}
