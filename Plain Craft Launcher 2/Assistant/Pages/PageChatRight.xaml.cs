using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Assistant;

namespace PCL.Assistant;

public partial class PageChatRight
{
    private readonly List<AssistantMessage> _history = new();
    private CancellationTokenSource? _cts;

    public PageChatRight()
    {
        InitializeComponent();
        InputBar.SendRequested += text => _ = SendAsync(text);
        InputBar.StopRequested += () => _cts?.Cancel();
    }

    public void NewConversation()
    {
        _history.Clear();
        PanMessages.Children.Clear();
        PanMessages.Children.Add(TextEmpty);
        TextEmpty.Visibility = Visibility.Visible;
    }

    public void SendFromExternal(string text) => _ = SendAsync(text);

    private async Task SendAsync(string input)
    {
        if (!Config.Assistant.Enabled)
        {
            AppendBubble("Assistant.Chat.RoleAssistant", Lang.Text("Assistant.Chat.Disabled"));
            return;
        }

        if (string.IsNullOrWhiteSpace(Config.Assistant.ApiKey) ||
            string.IsNullOrWhiteSpace(Config.Assistant.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(Config.Assistant.ModelName))
        {
            AppendBubble("Assistant.Chat.RoleUser", input);
            AppendBubble("Assistant.Chat.RoleAssistant", Lang.Text("Assistant.Chat.NotConfigured"));
            return;
        }

        TextEmpty.Visibility = Visibility.Collapsed;
        AppendBubble("Assistant.Chat.RoleUser", input);
        _history.Add(new AssistantMessage("user", input));
        var answer = AppendBubble("Assistant.Chat.RoleAssistant", "");

        _cts = new CancellationTokenSource();
        InputBar.SetBusy(true);
        try
        {
            var messages = new List<AssistantMessage> { new("system", BuildSystemPrompt()) };
            messages.AddRange(_history);
            var text = await AssistantClient.ChatAsync(messages, Config.Assistant.ApiBaseUrl,
                Config.Assistant.ModelName, Config.Assistant.ApiKey,
                new Progress<string>(delta =>
                {
                    answer.Text += delta;
                    PanBack.ScrollToBottom();
                }), _cts.Token);
            _history.Add(new AssistantMessage("assistant", text));
        }
        catch (OperationCanceledException)
        {
            answer.Text += Lang.Text("Assistant.Chat.Stopped");
        }
        catch (AssistantException e)
        {
            answer.Text = e.Detail is null ? e.Message : $"{e.Message}\n{e.Detail}";
        }
        catch (Exception e)
        {
            answer.Text = e.Message;
        }
        finally
        {
            InputBar.SetBusy(false);
            _cts.Dispose();
            _cts = null;
        }
    }

    private string BuildSystemPrompt()
    {
        var prompt = Lang.Text("Assistant.Chat.System");
        var instance = ModInstanceList.McMcInstanceSelected?.Name;
        return string.IsNullOrWhiteSpace(instance) ? prompt : prompt + "\n" + Lang.Text("Assistant.Chat.System.Instance", instance);
    }

    private TextBlock AppendBubble(string roleKey, string text)
    {
        var card = new MyCard
        {
            Title = Lang.Text(roleKey),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(25, 40, 25, 20)
        };
        card.Children.Add(block);
        PanMessages.Children.Add(card);
        PanBack.ScrollToBottom();
        return block;
    }
}
