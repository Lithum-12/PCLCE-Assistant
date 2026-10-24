using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Markdig.Wpf;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Assistant;

namespace PCL.Assistant;

public partial class PageChatRight
{
    // A response belongs to its session, not to whichever session happens to be selected later.
    private readonly Dictionary<string, CancellationTokenSource> _runs = new();
    private readonly HashSet<string> _discardDraftOnSwitch = new();
    private readonly DispatcherTimer _markdownTimer;
    private AssistantSession? _renderedSession;
    private string? _renderedSessionId;
    private MarkdownViewer? _streamViewer;
    private AssistantSession? _streamSession;
    private int _streamIndex = -1;
    private bool _streamDirty;

    public PageChatRight()
    {
        InitializeComponent();
        CommandBindings.Add(new CommandBinding(Commands.Hyperlink, OpenMarkdownLink));
        PageEnter += RenderCurrentSession;
        InputBar.SendRequested += text => _ = SendAsync(text);
        InputBar.StopRequested += StopCurrentSession;
        AssistantSessionStore.CurrentChanged += () => ModBase.RunInUi(HandleCurrentChanged);
        // 每个 token 都重解析 Markdown 会明显掉帧，攒到 120ms 一次
        _markdownTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _markdownTimer.Tick += (_, _) => FlushStreamMarkdown();
    }

    public string InputDraftText => InputBar.DraftText;

    /// <summary>Starts a blank draft on the existing chat surface. Drafts are not history items.</summary>
    public void NewConversation()
    {
        var current = AssistantSessionStore.Current;
        if (current is null || AssistantSessionStore.IsPersisted(current) || current.Messages.Count > 0 ||
            !string.IsNullOrEmpty(InputBar.DraftText))
        {
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
        var session = AssistantSessionStore.Current;
        var switched = !string.Equals(_renderedSessionId, session?.Id, StringComparison.Ordinal);
        PanMessages.Children.Clear();
        _streamViewer = null;
        _renderedSession = session;
        _renderedSessionId = session?.Id;
        var messages = session?.Messages;
        TextEmpty.Visibility = messages is null || messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (messages is not null)
        {
            var generating = _runs.ContainsKey(session!.Id);
            for (var i = 0; i < messages.Count; i++)
            {
                var message = messages[i];
                AppendBubble(
                    message.Role == "user" ? "Assistant.Chat.RoleUser" : "Assistant.Chat.RoleAssistant",
                    message.Content,
                    streamingTarget: message.Role == "assistant" && generating && i == messages.Count - 1);
            }
        }

        PanBack.ScrollToBottom();
        UpdateInputState();
        // 换会话只是换数据，不走页面路由，因此手动重播一次进入动画
        if (switched && IsLoaded && PageState == PageStates.ContentStay) PageOnContentExit();
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
        _streamSession = session;
        _streamIndex = answerIndex;
        _markdownTimer.Start();
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
                    _streamDirty = true;
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
            _markdownTimer.Stop();
            FlushStreamMarkdown();
            AssistantSessionStore.CompleteAssistantMessage(session, answerIndex, response.ToString());
            if (_runs.TryGetValue(session.Id, out var active) && ReferenceEquals(active, cts))
                _runs.Remove(session.Id);
            cts.Dispose();
            _streamSession = null;
            _streamIndex = -1;
            UpdateInputState();
            if (ReferenceEquals(AssistantSessionStore.Current, session)) RenderCurrentSession();
        }
    }

    private void FlushStreamMarkdown()
    {
        if (!_streamDirty) return;
        _streamDirty = false;
        if (_streamViewer is null || _streamSession is null) return;
        if (_renderedSession != _streamSession || _streamIndex >= _streamSession.Messages.Count) return;
        _streamViewer.Markdown = _streamSession.Messages[_streamIndex].Content;
        PanBack.ScrollToBottom();
    }

    private void HandleCurrentChanged()
    {
        var deleted = _renderedSession is not null &&
                      _discardDraftOnSwitch.Remove(_renderedSession.Id);
        if (deleted && _renderedSession is not null) _renderedSession.DraftText = "";

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
        return string.IsNullOrWhiteSpace(instance)
            ? prompt
            : prompt + "\n" + Lang.Text("Assistant.Chat.System.Instance", instance);
    }

    // AssistantClient awaits on the UI context. Report inline so deltas cannot be queued after
    // the final response has already replaced the accumulated text.
    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    private void AppendBubble(string roleKey, string text, bool streamingTarget = false)
    {
        var card = new MyCard
        {
            Title = Lang.Text(roleKey),
            Margin = new Thickness(0, 0, 0, 10)
        };
        if (roleKey == "Assistant.Chat.RoleUser")
        {
            card.Children.Add(new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(25, 40, 25, 20)
            });
        }
        else
        {
            var viewer = new MarkdownViewer
            {
                Markdown = text,
                Margin = new Thickness(25, 40, 25, 20),
                FontWeight = FontWeights.Normal
            };
            viewer.SetResourceReference(Control.ForegroundProperty, "ColorBrush1");
            card.Children.Add(viewer);
            if (streamingTarget) _streamViewer = viewer;
        }

        PanMessages.Children.Add(card);
    }

    private static void OpenMarkdownLink(object sender, ExecutedRoutedEventArgs e)
    {
        var url = e.Parameter?.ToString();
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return;
        ModBase.OpenWebsite(url);
    }
}
