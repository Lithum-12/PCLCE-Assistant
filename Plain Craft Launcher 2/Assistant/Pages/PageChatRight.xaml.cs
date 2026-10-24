using System;
using System.Collections.Generic;
using System.Text;
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
    // A response belongs to its session, not to whichever session happens to be selected later.
    private readonly Dictionary<string, CancellationTokenSource> _runs = new();
    private readonly HashSet<string> _discardDraftOnSwitch = new();
    private TextBlock? _lastAssistantBubble;
    private AssistantSession? _renderedSession;
    private string? _renderedSessionId;

    public PageChatRight()
    {
        InitializeComponent();
        PageEnter += RenderCurrentSession;
        InputBar.SendRequested += text => _ = SendAsync(text);
        InputBar.StopRequested += StopCurrentSession;
        AssistantSessionStore.CurrentChanged += () => ModBase.RunInUi(HandleCurrentChanged);
    }

    /// <summary>Starts a blank draft on the existing chat surface. Drafts are not history items.</summary>
    public void NewConversation()
    {
        var current = AssistantSessionStore.Current;
        if (current is null || AssistantSessionStore.IsPersisted(current) || current.Messages.Count > 0 ||
            !string.IsNullOrEmpty(InputBar.DraftText))
        {
            // If this is an unsent draft, HandleCurrentChanged parks it in the separate Drafts list.
            AssistantSessionStore.StartNewDraft();
            return;
        }
        RenderCurrentSession();
    }

    public void DeleteSession(AssistantSession session)
    {
        if (AssistantSessionStore.IsPersisted(session))
        {
            if (_runs.TryGetValue(session.Id, out var cts)) cts.Cancel();
            if (ReferenceEquals(AssistantSessionStore.Current, session))
                _discardDraftOnSwitch.Add(session.Id);
            AssistantSessionStore.Delete(session);
            return;
        }
        AssistantSessionStore.DeleteDraft(session);
    }

    public void RenderCurrentSession()
    {
        PanMessages.Children.Clear();
        _lastAssistantBubble = null;
        var session = AssistantSessionStore.Current;
        _renderedSession = session;
        _renderedSessionId = session?.Id;
        var messages = session?.Messages;
        TextEmpty.Visibility = messages is null || messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (messages is null)
        {
            UpdateInputState();
            return;
        }

        foreach (var message in messages)
        {
            var bubble = AppendBubble(
                message.Role == "user" ? "Assistant.Chat.RoleUser" : "Assistant.Chat.RoleAssistant",
                message.Content);
            if (message.Role == "assistant") _lastAssistantBubble = bubble;
        }
        PanBack.ScrollToBottom();
        UpdateInputState();
    }

    private async Task SendAsync(string input)
    {
        input = input.Trim();
        if (input.Length == 0) return;

        var session = AssistantSessionStore.GetCurrentOrCreateDraft();
        // One active generation per conversation; other conversations can continue independently.
        if (_runs.ContainsKey(session.Id)) return;

        AssistantSessionStore.AppendUserMessage(session, input);
        session.DraftText = "";
        if (!Config.Assistant.Enabled)
        {
            AssistantSessionStore.AppendAssistantMessage(session, Lang.Text("Assistant.Chat.Disabled"));
            if (ReferenceEquals(AssistantSessionStore.Current, session)) RenderCurrentSession();
            return;
        }

        if (string.IsNullOrWhiteSpace(Config.Assistant.ApiKey) ||
            string.IsNullOrWhiteSpace(Config.Assistant.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(Config.Assistant.ModelName))
        {
            AssistantSessionStore.AppendAssistantMessage(session, Lang.Text("Assistant.Chat.NotConfigured"));
            if (ReferenceEquals(AssistantSessionStore.Current, session)) RenderCurrentSession();
            return;
        }

        var answerIndex = AssistantSessionStore.AppendAssistantMessage(session, "");
        var cts = new CancellationTokenSource();
        _runs[session.Id] = cts;
        if (ReferenceEquals(AssistantSessionStore.Current, session)) RenderCurrentSession();
        UpdateInputState();

        var response = new StringBuilder();
        try
        {
            var messages = new List<AssistantMessage> { new("system", BuildSystemPrompt()) };
            messages.AddRange(session.Messages.GetRange(0, answerIndex));
            var text = await AssistantClient.ChatAsync(messages, Config.Assistant.ApiBaseUrl,
                Config.Assistant.ModelName, Config.Assistant.ApiKey,
                new InlineProgress<string>(delta =>
                {
                    response.Append(delta);
                    AssistantSessionStore.UpdateMessage(session, answerIndex, response.ToString());
                    // Only paint into the view when it is still displaying this session.
                    if (ReferenceEquals(AssistantSessionStore.Current, session) &&
                        _renderedSessionId == session.Id && _lastAssistantBubble is not null &&
                        _lastAssistantBubble.Parent is not null)
                    {
                        _lastAssistantBubble.Text = response.ToString();
                        PanBack.ScrollToBottom();
                    }
                }), cts.Token);
            response.Clear();
            response.Append(text);
        }
        catch (OperationCanceledException)
        {
            response.Append(Lang.Text("Assistant.Chat.Stopped"));
        }
        catch (AssistantException e)
        {
            response.Clear();
            response.Append(e.Detail is null ? e.Message : $"{e.Message}\n{e.Detail}");
        }
        catch (Exception e)
        {
            response.Clear();
            response.Append(e.Message);
        }
        finally
        {
            AssistantSessionStore.CompleteAssistantMessage(session, answerIndex, response.ToString());
            if (_runs.TryGetValue(session.Id, out var active) && ReferenceEquals(active, cts))
                _runs.Remove(session.Id);
            cts.Dispose();
            UpdateInputState();
            if (ReferenceEquals(AssistantSessionStore.Current, session)) RenderCurrentSession();
        }
    }


    private void HandleCurrentChanged()
    {
        var previous = _renderedSession;
        if (previous is not null)
        {
            if (_discardDraftOnSwitch.Remove(previous.Id))
            {
                previous.DraftText = "";
            }
            else
            {
                previous.DraftText = InputBar.DraftText;
                // Keep unsent content in a separate draft list; never disguise it as chat history.
                AssistantSessionStore.ParkDraft(previous);
            }
        }

        var current = AssistantSessionStore.Current;
        InputBar.DraftText = current?.DraftText ?? "";
        RenderCurrentSession();
        UpdateInputState();
    }

    private void StopCurrentSession()
    {
        var session = AssistantSessionStore.Current;
        if (session is not null && _runs.TryGetValue(session.Id, out var cts)) cts.Cancel();
    }

    private void UpdateInputState()
    {
        var session = AssistantSessionStore.Current;
        InputBar.SetBusy(session is not null && _runs.ContainsKey(session.Id));
    }

    private string BuildSystemPrompt()
    {
        var prompt = Lang.Text("Assistant.Chat.System");
        var instance = ModInstanceList.McMcInstanceSelected?.Name;
        return string.IsNullOrWhiteSpace(instance) ? prompt : prompt + "\n" + Lang.Text("Assistant.Chat.System.Instance", instance);
    }

    // AssistantClient awaits on the UI context. Report inline so deltas cannot be queued after
    // the final response has already replaced the accumulated text.
    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
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
        return block;
    }
}
