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
    private CancellationTokenSource? _cts;

    public PageChatRight()
    {
        InitializeComponent();
        Loaded += (_, _) => RenderMessages();
        InputBar.SendRequested += text => _ = SendAsync(text);
        InputBar.StopRequested += () => _cts?.Cancel();
    }

    public void NewConversation()
    {
        AssistantSessionStore.New();
        RenderMessages();
    }

    public void LoadSession(AssistantSession session)
    {
        AssistantSessionStore.Use(session);
        RenderMessages();
    }

    public void SendFromExternal(string text) => _ = SendAsync(text);

    private AssistantSession CurrentSession =>
        AssistantSessionStore.Current ?? AssistantSessionStore.New();

    private void RenderMessages()
    {
        PanMessages.Children.Clear();
        var messages = AssistantSessionStore.Current?.Messages;
        TextEmpty.Visibility = messages is null || messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (messages is null) return;
        foreach (var message in messages)
            AppendBubble(message.Role == "user" ? "Assistant.Chat.RoleUser" : "Assistant.Chat.RoleAssistant",
                message.Content);
    }

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

        var session = CurrentSession;
        if (string.IsNullOrWhiteSpace(session.Title))
            session.Title = input.Length > 24 ? input[..24] + "…" : input;

        TextEmpty.Visibility = Visibility.Collapsed;
        AppendBubble("Assistant.Chat.RoleUser", input);
        session.Messages.Add(new AssistantMessage("user", input));
        var answer = AppendBubble("Assistant.Chat.RoleAssistant", "");

        _cts = new CancellationTokenSource();
        InputBar.SetBusy(true);
        try
        {
            var messages = new List<AssistantMessage> { new("system", BuildSystemPrompt()) };
            messages.AddRange(session.Messages);
            var text = await AssistantClient.ChatAsync(messages, Config.Assistant.ApiBaseUrl,
                Config.Assistant.ModelName, Config.Assistant.ApiKey,
                new Progress<string>(delta =>
                {
                    answer.Text += delta;
                    PanBack.ScrollToBottom();
                }), _cts.Token);
            answer.Text = text;
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
            if (answer.Text.Length > 0) session.Messages.Add(new AssistantMessage("assistant", answer.Text));
            AssistantSessionStore.Save();
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
