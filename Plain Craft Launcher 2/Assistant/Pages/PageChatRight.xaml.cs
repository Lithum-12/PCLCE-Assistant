using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL.Assistant;

public partial class PageChatRight
{
    public PageChatRight()
    {
        InitializeComponent();
    }

    private void TextInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyboardDevice.Modifiers != ModifierKeys.Shift)
        {
            e.Handled = true;
            SendCurrentInput();
        }
    }

    private void BtnSend_Click(object sender, RoutedEventArgs e) => SendCurrentInput();

    private void SendCurrentInput()
    {
        var input = TextChatInput.Text?.Trim();
        if (string.IsNullOrEmpty(input)) return;
        TextChatInput.Clear();
        TextEmpty.Visibility = Visibility.Collapsed;
        AppendBubble("Assistant.Chat.RoleUser", input);

        if (!Config.Assistant.Enabled || string.IsNullOrWhiteSpace(Config.Assistant.ApiKey) ||
            string.IsNullOrWhiteSpace(Config.Assistant.ApiBaseUrl) || string.IsNullOrWhiteSpace(Config.Assistant.ModelName))
        {
            AppendBubble("Assistant.Chat.RoleAssistant", Lang.Text("Assistant.Chat.NotConfigured"));
            return;
        }

        AppendBubble("Assistant.Chat.RoleAssistant", Lang.Text("Assistant.Chat.StubReply"));
    }

    private void AppendBubble(string roleKey, string text)
    {
        var card = new MyCard
        {
            Title = Lang.Text(roleKey),
            Margin = new Thickness(0, 0, 0, 10)
        };
        card.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(25, 40, 25, 20)
        });
        PanMessages.Children.Add(card);
        PanBack.ScrollToBottom();
    }
}
