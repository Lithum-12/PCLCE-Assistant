using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Markdig.Wpf;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Assistant;

namespace PCL.Assistant;

public partial class PageChatRight
{
    // 工具往返的轮数上限：一次追问一般 1~2 轮就够，留一点余量又不至于跑飞
    private const int MaxToolRounds = 4;

    // 一轮问答里最多允许带上多少字的本地内容，超过就不再执行新工具
    private const int MaxToolChars = 24000;

    // A response belongs to its session, not to whichever session happens to be selected later.
    private readonly Dictionary<string, CancellationTokenSource> _runs = new();
    private readonly HashSet<string> _discardDraftOnSwitch = new();
    // 折叠状态只活在渲染层：重建气泡（流式结束、切回会话）时按消息序号还原，否则卡片会全部弹回展开。
    private readonly Dictionary<string, HashSet<int>> _collapsed = new();
    private readonly DispatcherTimer _markdownTimer;
    private AssistantSession? _renderedSession;
    private string? _renderedSessionId;
    private MarkdownViewer? _streamViewer;
    private AssistantStatusPanel? _streamStatus;
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
        _collapsed.Remove(session.Id);
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
        _streamStatus = null;
        _renderedSession = session;
        _renderedSessionId = session?.Id;
        var messages = session?.Messages;
        TextEmpty.Visibility = messages is null || messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (messages is not null)
        {
            var generating = _runs.ContainsKey(session!.Id);
            _collapsed.TryGetValue(session.Id, out var collapsed);
            for (var i = 0; i < messages.Count; i++)
            {
                var message = messages[i];
                // 诊断提示用上游的警告框画出来，不占对话气泡
                if (message.Role == "hint")
                {
                    PanMessages.Children.Add(new MyHint
                    {
                        Text = message.Content,
                        Theme = message.Level switch
                        {
                            "warn" => MyHint.Themes.Yellow,
                            "info" => MyHint.Themes.Blue,
                            _ => MyHint.Themes.Red
                        },
                        Margin = new Thickness(0, 0, 0, 10)
                    });
                    continue;
                }

                AppendBubble(session, i, message,
                    streamingTarget: message.Role == "assistant" && generating && i == messages.Count - 1,
                    collapsed);
            }
        }

        PanBack.ScrollToBottom();
        UpdateInputState();
        // 换会话只是换数据，不走页面路由，因此手动重播一次进入动画
        if (switched && IsLoaded && PageState == PageStates.ContentStay) ReplayEnterAnimation();
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
            AssistantSessionStore.AppendHintMessage(session, "warn", Lang.Text("Assistant.Chat.Disabled"));
            if (ReferenceEquals(AssistantSessionStore.Current, session)) RenderCurrentSession();
            return;
        }

        if (string.IsNullOrWhiteSpace(Config.Assistant.ApiKey) ||
            string.IsNullOrWhiteSpace(Config.Assistant.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(Config.Assistant.ModelName))
        {
            AssistantSessionStore.AppendHintMessage(session, "warn", Lang.Text("Assistant.Chat.NotConfigured"));
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
        // 实例在发送时就固定，回答中途切换实例不会把两份数据混在一起
        var toolset = new AssistantToolset(ModInstanceList.McMcInstanceSelected);
        var history = new List<AssistantMessage> { new("system", BuildSystemPrompt()) };
        // 警告框只是给人看的诊断，不能混进模型上下文
        history.AddRange(session.Messages.GetRange(0, answerIndex).Where(m => m.Role != "hint"));
        var progress = new InlineProgress<string>(delta =>
        {
            response.Append(delta);
            AssistantSessionStore.UpdateMessage(session, answerIndex, response.ToString());
            _streamDirty = true;
            _streamStatus?.HideThinking();
        });
        var toolsRejected = false;
        var toolChars = 0;
        AssistantIssue? issue = null;
        _streamStatus?.ShowThinking(Lang.Text("Assistant.Chat.Thinking"));
        try
        {
            for (var round = 0; round < MaxToolRounds; round++)
            {
                var before = response.Length;
                var reply = await AssistantClient.SendAsync(history, Config.Assistant.ApiBaseUrl,
                    Config.Assistant.ModelName, Config.Assistant.ApiKey,
                    toolset.Definitions.Count > 0 ? toolset.Definitions : null, progress, cts.Token);
                toolsRejected = reply.ToolsRejected;
                // 增量与最终文本对不上时以最终文本为准，避免同一轮内容重复
                if (response.Length != before + reply.Content.Length)
                {
                    response.Length = before;
                    progress.Report(reply.Content);
                }

                if (reply.ToolCalls.Count == 0) break;

                history.Add(AssistantMessage.AssistantWithTools(reply.ToolCalls, reply.Content));
                if (reply.Content.Length > 0) progress.Report("\n\n");
                var status = _streamStatus;
                foreach (var call in reply.ToolCalls)
                {
                    var (icon, label) = toolset.Describe(call.Name);
                    var row = status?.BeginTool(icon, label) ?? -1;
                    AssistantToolOutcome outcome;
                    if (toolChars >= MaxToolChars)
                    {
                        // 每个 tool_call 都必须有对应回复，否则部分兼容层会直接 400，
                        // 所以超预算的调用不是丢掉，而是明确告诉模型没执行
                        outcome = new AssistantToolOutcome(
                            $"本地内容预算（{MaxToolChars} 字）已用尽，本次未执行。请基于已获得的信息回答。", "超出预算", false);
                    }
                    else
                    {
                        outcome = await toolset.RunAsync(call, cts.Token);
                        toolChars += outcome.Content.Length;
                    }

                    status?.EndTool(row, !outcome.Failed, outcome.Note);
                    history.Add(AssistantMessage.Tool(call.Id, outcome.Content));
                }

                status?.ShowThinking(Lang.Text("Assistant.Chat.Thinking.Next"));
            }
        }
        catch (OperationCanceledException)
        {
            response.Append(Lang.Text("Assistant.Chat.Stopped"));
        }
        catch (Exception e)
        {
            // 归成"没配密钥 / 余额不足 / 网络不通 / 服务商挂了"这类人话，用上游的警告框画出来
            issue = AssistantErrors.Classify(e);
        }
        finally
        {
            _markdownTimer.Stop();
            FlushStreamMarkdown();
            if (issue is not null && response.Length == 0)
                AssistantSessionStore.RemoveMessage(session, answerIndex); // 一个字都没出来，别留个空气泡
            else
                AssistantSessionStore.CompleteAssistantMessage(session, answerIndex, response.ToString(),
                    TraceLines(toolset, toolsRejected));
            if (issue is not null)
                AssistantSessionStore.AppendHintMessage(session, issue.Level, IssueText(issue));
            if (_runs.TryGetValue(session.Id, out var active) && ReferenceEquals(active, cts))
                _runs.Remove(session.Id);
            cts.Dispose();
            _streamSession = null;
            _streamIndex = -1;
            UpdateInputState();
            if (ReferenceEquals(AssistantSessionStore.Current, session)) RenderCurrentSession();
        }
    }

    private static string IssueText(AssistantIssue issue)
    {
        var text = Lang.Text(issue.Key);
        return issue.Detail.Length == 0 ? text : $"{text}（{issue.Detail}）";
    }

    /// <summary>工具痕迹不进正文，只进卡片左下角那一行的提示里。</summary>
    private static List<string>? TraceLines(AssistantToolset toolset, bool toolsRejected)
    {
        List<string> lines = [.. toolset.Trace];
        if (toolsRejected) lines.Add(Lang.Text("Assistant.Chat.Tools.Rejected"));
        return lines.Count == 0 ? null : lines;
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
        // CurseForge 的工具是按密钥挂载的，没挂载就得告诉模型别推荐那条路
        if (string.IsNullOrWhiteSpace(Secrets.CurseForgeAPIKey))
            prompt += "\n" + Lang.Text("Assistant.Chat.System.NoCurseForge");
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

    private void AppendBubble(AssistantSession session, int index, AssistantMessage message,
        bool streamingTarget, HashSet<int> collapsed)
    {
        var isUser = message.Role == "user";
        // 折叠直接复用 MyCard 自带的 CanSwap：右上角箭头 + 40px 标题行是上游控件的既有度量。
        var card = new MyCard
        {
            Title = Lang.Text(isUser ? "Assistant.Chat.RoleUser" : "Assistant.Chat.RoleAssistant"),
            Margin = new Thickness(0, 0, 0, 8),
            CanSwap = true
        };
        var body = new StackPanel();
        if (!isUser)
            body.Children.Add(new AssistantStatusPanel { Margin = new Thickness(25, 36, 25, 0) });

        if (isUser)
        {
            var text = new TextBlock
            {
                Text = message.Content,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(25, 36, 25, 4)
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1");
            body.Children.Add(text);
        }
        else
        {
            // 流式期间只画正文，否则每个增量都要重新联网查一遍资源卡片
            List<AssistantSegment> segments = streamingTarget
                ? [new AssistantSegment(false, message.Content)]
                : AssistantProjectCards.Split(message.Content);
            FrameworkElement? firstContent = null;
            MarkdownViewer? streamViewer = null;
            foreach (var segment in segments)
            {
                FrameworkElement element;
                if (segment.IsCard)
                {
                    element = BuildProjectCard(segment.Text);
                }
                else
                {
                    var viewer = new MarkdownViewer { Markdown = segment.Text, FontWeight = FontWeights.Normal };
                    viewer.SetResourceReference(Control.ForegroundProperty, "ColorBrush1");
                    streamViewer ??= viewer;
                    element = viewer;
                }

                element.Margin = new Thickness(25, firstContent is null ? 36 : 6, 25, 8);
                firstContent = element;
                body.Children.Add(element);
            }

            // 状态条出现时正文往上贴，否则标题下面会空出一大截
            if (body.Children[0] is AssistantStatusPanel status)
            {
                status.OnLayoutChanged = () =>
                {
                    if (firstContent is null) return;
                    firstContent.Margin = status.Visibility == Visibility.Visible
                        ? new Thickness(25, 4, 25, 8)
                        : new Thickness(25, 36, 25, 8);
                };
            }

            if (streamingTarget)
            {
                _streamViewer = streamViewer;
                _streamStatus = body.Children[0] as AssistantStatusPanel;
            }
        }

        body.Children.Add(BuildFooter(session, index, message, card));
        card.SwapControl = body;
        card.Tag = new BubbleSource(session, index);
        card.Children.Add(body);
        if (collapsed?.Contains(index) == true) card.IsSwapped = true;
        card.Swap += (_, _) => RecordCollapse(session, index, card);
        PanMessages.Children.Add(card);
    }

    /// <summary>左下角小灰字（时间 · 用了几个工具），右下角复制。</summary>
    private FrameworkElement BuildFooter(AssistantSession session, int index, AssistantMessage message, MyCard card)
    {
        var note = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.WordEllipsis,
            Text = FooterNote(message)
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush4");
        if (message.Trace is { Count: > 0 } trace)
        {
            note.ToolTip = string.Join("\n", trace);
            note.TextTrimming = TextTrimming.None;
        }

        var copy = new MyIconButton
        {
            SvgIcon = "lucide/copy",
            // MyIconButton 的图标区固定留 5px 内边距，小于 25 就只剩一个点；这也是上游列表按钮的既有尺寸
            Height = 25,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            ToolTip = Lang.Text("Assistant.Chat.Copy")
        };
        copy.Click += (_, _) => ModBase.ClipboardSet(BubbleText(card));

        var row = new Grid { Margin = new Thickness(25, 0, 20, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(note);
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);
        return row;
    }

    private static string FooterNote(AssistantMessage message)
    {
        List<string> parts = [];
        if (message.At > 0)
            parts.Add(DateTimeOffset.FromUnixTimeMilliseconds(message.At).LocalDateTime
                .ToString("yyyy.M.d H:mm", CultureInfo.InvariantCulture));
        if (message.Trace is { Count: > 0 } trace)
            parts.Add(Lang.Text("Assistant.Chat.UsedTools", trace.Count));
        return string.Join(" · ", parts);
    }

    /// <summary>复制取当前正文而不是建卡时的快照：助手气泡在流式期间会一直变。</summary>
    private static string BubbleText(MyCard card) =>
        card.Tag is BubbleSource(var source, var index) && index < source.Messages.Count
            ? source.Messages[index].Content
            : "";

    /// <summary>回答里出现的资源链接换成卡片；解析失败时退回普通文字。</summary>
    private static FrameworkElement BuildProjectCard(string url) =>
        AssistantProjectCards.Parse(url) is { } link
            ? AssistantProjectCards.Create(link)
            : new TextBlock { Text = url, TextWrapping = TextWrapping.Wrap };

    private sealed record BubbleSource(AssistantSession Session, int Index);

    private void RecordCollapse(AssistantSession session, int index, MyCard card)
    {
        if (!_collapsed.TryGetValue(session.Id, out var set)) _collapsed[session.Id] = set = new HashSet<int>();
        if (card.IsSwapped) set.Add(index);
        else set.Remove(index);
    }

    /// <summary>
    ///     重播本页的逐个进入动画。上游的 GetAllAnimControls 走到 MyScrollViewer 就断了
    ///     （它既不是 Panel 也不是 ContentControl），而没有加载器时状态机只认识 Child，
    ///     所以消息区与输入栏永远对不齐；这里按上游同一组参数把本页可见卡片排进同一次动画。
    /// </summary>
    private void ReplayEnterAnimation()
    {
        var controls = new List<FrameworkElement>();
        foreach (var child in PanMessages.Children)
            if (child is FrameworkElement element) controls.Add(element);
        if (TextEmpty.Visibility == Visibility.Visible) controls.Add(TextEmpty);
        controls.AddRange(InputBar.AnimControls);

        foreach (var control in controls)
        {
            // TextEmpty 在 XAML 里声明了不吃点击（它盖在消息区上），重播动画时不该替它改回来
            if (!ReferenceEquals(control, TextEmpty)) control.IsHitTestVisible = true;
            control.Opacity = 1d;
            if (control.RenderTransform is TranslateTransform) control.RenderTransform = null;
        }

        var aniList = new List<ModAnimation.AniData>();
        var delay = 0;
        foreach (var control in controls)
        {
            control.Opacity = 0d;
            control.RenderTransform = new TranslateTransform(0d, -16d);
            aniList.Add(ModAnimation.AaOpacity(control, 1d, 100, delay,
                new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)));
            aniList.Add(ModAnimation.AaTranslateY(control, 5d, 250, delay, new ModAnimation.AniEaseOutFluent()));
            aniList.Add(ModAnimation.AaTranslateY(control, 11d, 350, delay, new ModAnimation.AniEaseOutBack()));
            // 长对话的逐个延迟封在 300ms，否则尾部要等一秒多才动起来
            delay = Math.Min(delay + 25, 300);
        }

        ModAnimation.AniStart(aniList, "PageRight PageChange " + pageUuid, true);
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
