using System.Windows;

namespace PCL.Assistant;

public partial class PageChatLeft
{
    public PageChatLeft()
    {
        InitializeComponent();
        AnimatedControl = PanItem;
        ItemChat.Logo = ModBase.pathImage + "Blocks/Fabric.png";
        Loaded += (_, _) => ItemChat.SetChecked(true, false, false);
    }
}
