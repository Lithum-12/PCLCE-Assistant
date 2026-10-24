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
        PageEnter += RenderCurrentSession;
        InputBar.SendRequested += text => _ = SendAsync(text);
        InputBar.StopRequested += () => _cts?.Cancel();
    }

    /// <summary>
    ///     空会话不重复新建，避免连点“新对话”堆积空白记录。
    /// </summary>
    public void NewConversation()
    {
        var current = AssistantSessionStore.Current;
        if (current is null || current.Messages.Count > 0) AssistantSessionStore.New();
        RenderCurrentSession();
    }

    public void SendFromExternal(string text)
    {
        if (IsLoaded)
        {
            _ = SendAsync(text);
            return;
        }

        // Let the page's normal Loaded handler restore the selected session before sending.
        RoutedEventHandler? onLoaded = null;
        onLoaded = (_, _) =>
        {
            Loaded -= onLoaded;
            _ = SendAsync(text);
        };
        Loaded += onLoaded;
    }

    private AssistantSession CurrentSession =>
        AssistantSessionStore.Current ?? AssistantSessionStore.New();

    public void RenderCurrentSession()
    {
        PanMessages.Children.Clear();
        PanMessages.Children.Add(TextEmpty);
        var messages = AssistantSessionStore.Current?.Messages;
        TextEmpty.Visibility = messages is null || messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (messages is null) return;
        foreach (var message in messages)
            AppendBubble(message.Role == "user" ? "Assistant.Chat.RoleUser" : "Assistant.Chat.RoleAssistant",
                message.Content);
    }

    private async Task SendAsync(string input)
    {
        var session = CurrentSession;
        if (string.IsNullOrWhiteSpace(session.Title))
            session.Title = input.Length > 24 ? input[..24] : input;

        TextEmpty.Visibility = Visibility.Collapsed;
        AppendBubble("Assistant.Chat.RoleUser", input);
        session.Messages.Add(new AssistantMessage("user", input));
        // Persist the user turn and refresh the history title before the network request starts.
        AssistantSessionStore.Save();

        if (!Config.Assistant.Enabled)
        {
            AppendStoredResponse(session, Lang.Text("Assistant.Chat.Disabled"));
            return;
        }

        if (string.IsNullOrWhiteSpace(Config.Assistant.ApiKey) ||
            string.IsNullOrWhiteSpace(Config.Assistant.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(Config.Assistant.ModelName))
        {
            AppendStoredResponse(session, Lang.Text("Assistant.Chat.NotConfigured"));
            return;
        }

        var answer = AppendBubble("Assistant.Chat.RoleAssistant", "");
        var cts = new CancellationTokenSource();
        _cts = cts;
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
                }), cts.Token);
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
            if (answer.Text.Length > 0) session.Messages.Add(new AssistantMessage("assistant", answer.Text));
            if (ReferenceEquals(_cts, cts))
            {
                InputBar.SetBusy(false);
                _cts = null;
            }
            cts.Dispose();
            AssistantSessionStore.Save();
            if (ReferenceEquals(AssistantSessionStore.Current, session)) RenderCurrentSession();
        }
    }

    private void AppendStoredResponse(AssistantSession session, string text)
    {
        AppendBubble("Assistant.Chat.RoleAssistant", text);
        session.Messages.Add(new AssistantMessage("assistant", text));
        AssistantSessionStore.Save();
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
