using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    // 工具往返的轮数上限：装一套模组（查→下→再查依赖）很容易就三四轮，4 轮太紧；
    // 用完还有话要说时另加一轮"不带工具"的收尾，见 SendAsync
    private const int MaxToolRounds = 6;

    // 一轮问答里最多允许带上多少字的本地内容，超过就不再执行新工具
    private const int MaxToolChars = 24000;

    // 一次只画最近这么多条：每条消息是一个 MyCard + 一次 Markdig 解析，
    // 长对话全量重建就是"切换会话卡一下"的来源，跟发给模型的上下文长度无关
    private const int RenderWindow = 24;

    // 入场动画只给最靠近输入框的几条做，几十条一起起故事板会直接掉帧
    private const int AnimatedControls = 8;

    // 玩家发一次言之后，允许助手自己接着说几轮（等它发起的下载结束）。
    // 没有上限它就会"下载→继续→又下载→又继续"自己聊下去，每一轮都是真金白银的请求。
    private const int MaxAutoContinuations = 2;
    private int _autoContinuations;

    // A response belongs to its session, not to whichever session happens to be selected later.
    private readonly Dictionary<string, CancellationTokenSource> _runs = new();
    private readonly HashSet<string> _discardDraftOnSwitch = new();
    // 折叠状态只活在渲染层：重建气泡（流式结束、切回会话）时按消息序号还原，否则卡片会全部弹回展开。
    private readonly Dictionary<string, HashSet<int>> _collapsed = new();
    private readonly DispatcherTimer _markdownTimer;
    private AssistantSession? _renderedSession;
    private string? _renderedSessionId;
    // -1 表示"取尾部窗口"；点了"显示更早的消息"之后就是显式起点
    private int _windowStart = -1;
    private int _scrollAnchorIndex = -1;
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
        AssistantTasks.BatchReady += OnAssistantTasksReady;
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
        if (switched)
        {
            _windowStart = -1; // 换会话回到尾部窗口，别把上一个会话展开的整段历史带过来
            InputBar.HideChoice(); // 选择栏属于上一个会话的那一轮回答，切走就该消失
        }

        PanMessages.Children.Clear();
        _streamViewer = null;
        _streamStatus = null;
        _renderedSession = session;
        _renderedSessionId = session?.Id;
        var messages = session?.Messages;
        TextEmpty.Visibility = messages is null || messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (messages is not null)
        {
            var start = _windowStart >= 0
                ? Math.Min(_windowStart, messages.Count)
                : Math.Max(0, messages.Count - RenderWindow);
            if (start > 0) PanMessages.Children.Add(BuildEarlierButton(start, messages.Count - start));
            var generating = _runs.ContainsKey(session!.Id);
            _collapsed.TryGetValue(session.Id, out var collapsed);
            collapsed ??= [];
            for (var i = start; i < messages.Count; i++)
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

        if (_scrollAnchorIndex >= 0)
        {
            // 点了"显示更早的消息"之后停在原来看的那条上，否则补出来的内容全在视野上面，像是没反应
            var anchor = _scrollAnchorIndex;
            _scrollAnchorIndex = -1;
            FrameworkElement? target = null;
            foreach (var child in PanMessages.Children)
                if (child is FrameworkElement { Tag: BubbleSource(_, var index) } element && index == anchor)
                {
                    target = element;
                    break;
                }

            if (target is not null)
                Dispatcher.BeginInvoke(new Action(target.BringIntoView), DispatcherPriority.Render);
        }
        else
        {
            PanBack.ScrollToBottom();
        }

        UpdateInputState();
        // 换会话只是换数据，不走页面路由，因此手动重播一次进入动画
        if (switched && IsLoaded && PageState == PageStates.ContentStay) ReplayEnterAnimation();
    }

    private MyIconTextButton BuildEarlierButton(int firstRenderedIndex, int hidden)
    {
        var button = new MyIconTextButton
        {
            SvgIcon = "lucide/chevrons-up",
            Text = Lang.Text("Assistant.Chat.LoadEarlier", hidden),
            Height = 28,
            Margin = new Thickness(0, 0, 0, 8)
        };
        button.Click += (_, _) => ShowEarlier(firstRenderedIndex);
        return button;
    }

    private void ShowEarlier(int anchorIndex)
    {
        _scrollAnchorIndex = anchorIndex;
        _windowStart = Math.Max(0, anchorIndex - RenderWindow);
        RenderCurrentSession();
    }

    private async Task SendAsync(string input, bool auto = false)
    {
        input = input.Trim();
        if (input.Length == 0) return;

        if (!auto)
        {
            // 玩家自己发言就是新一轮：自动继续的额度重置，上一批发起的任务也不再插话
            _autoContinuations = 0;
            AssistantTasks.Clear();
        }

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
        // 新一轮开始了：上一轮留给玩家拍板的选项已经过期（自动继续、或者玩家自己打字都会走到这儿）
        InputBar.HideChoice();
        // 只有第一次提问去要一个短标题；真正发出去要等这轮回答结束，见 finally
        var needsTitle = !session.TitleLocked && session.Messages.Count(m => m.Role == "user") == 1;
        if (ReferenceEquals(AssistantSessionStore.Current, session)) RenderCurrentSession();
        _streamSession = session;
        _streamIndex = answerIndex;
        _markdownTimer.Start();
        UpdateInputState();

        var response = new StringBuilder();
        // 实例在发送时就固定，回答中途切换实例不会把两份数据混在一起
        var toolset = new AssistantToolset(ModInstanceList.McMcInstanceSelected);
        var history = new List<AssistantMessage> { new("system", BuildSystemPrompt(toolset)) };
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
        var answered = false;
        var corrected = false;
        var roundsTruncated = false;
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

                if (reply.ToolCalls.Count == 0)
                {
                    if (!corrected && toolset.ActionsExecuted == 0 && !cts.IsCancellationRequested &&
                        AssistantClaims.DescribesAction(response.ToString()))
                    {
                        // 它说"已经在下载"却一个执行工具都没调（真实发生过）：把这句话退回给它自己纠正。
                        // 改正必须留在轮次循环里做：单独发一次只取正文的请求，会把它这回真的发出来的
                        // tool_call 整个丢掉，回答就停在"现在真的发起下载："后面再没内容
                        corrected = true;
                        history.Add(new AssistantMessage("assistant", response.ToString()));
                        history.Add(new AssistantMessage("user", Lang.Text("Assistant.Chat.ClaimCorrection")));
                        response.Clear();
                        AssistantSessionStore.UpdateMessage(session, answerIndex, "");
                        _streamDirty = true;
                        _streamStatus?.ShowThinking(Lang.Text("Assistant.Chat.Thinking.Next"));
                        continue;
                    }

                    answered = true;
                    break;
                }

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

            if (!answered)
            {
                // 轮次用完而模型还想继续调工具：直接停手会让回答停在半路，看着就像"聊着聊着断了"。
                // 再要一轮，这次不给工具，逼它把手上已有的信息总结完。
                roundsTruncated = true;
                var before = response.Length;
                var wrap = await AssistantClient.SendAsync(history, Config.Assistant.ApiBaseUrl,
                    Config.Assistant.ModelName, Config.Assistant.ApiKey, null, progress, cts.Token);
                if (response.Length != before + wrap.Content.Length)
                {
                    response.Length = before;
                    progress.Report(wrap.Content);
                }
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
            if (roundsTruncated && issue is null)
                // 让它自己收尾了，也要告诉玩家为什么只做到一半
                issue = new AssistantIssue("warn", "Assistant.Chat.RoundsExhausted", "");
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
            // 标题请求排在回答之后：同时打两个请求容易撞上服务商的限流，
            // 而且这一轮都失败了（多半是没配密钥）再问一次也只是白费
            if (needsTitle && issue is null) _ = GenerateTitleAsync(session, input);
            _streamSession = null;
            _streamIndex = -1;
            UpdateInputState();
            if (ReferenceEquals(AssistantSessionStore.Current, session)) RenderCurrentSession();
            // 模型这一轮把决定权交回给玩家：先让他选，选完才有下一轮。
            // 排队等续的下载批次因此往后挪一次，不会丢（Ready 队列还在，下一轮结束时照样会排空）
            var awaitingChoice = false;
            if (toolset.PendingChoice is { } choice && !_runs.ContainsKey(session.Id) && !cts.IsCancellationRequested)
            {
                awaitingChoice = true;
                InputBar.ShowChoice(choice, pick => _ = SendAsync(pick));
            }

            if (!awaitingChoice) OnAssistantTasksReady();
        }
    }

    /// <summary>
    /// 给这次对话要一个短标题，让左侧列表少点截断的原文。
    /// 不带本轮的 CancellationToken：它在回答结束时就被 Dispose 了，而标题请求是那之后才发的；
    /// 会话要是中途被删了，RenameSession 会自己把结果丢掉。
    /// </summary>
    private async Task GenerateTitleAsync(AssistantSession session, string request)
    {
        try
        {
            var title = await AssistantTitles.GenerateAsync(Lang.Text("Assistant.Title.Instruction"), request,
                Config.Assistant.ApiBaseUrl, Config.Assistant.ModelName, Config.Assistant.ApiKey, CancellationToken.None);
            if (title is null) return;
            ModBase.RunInUi(() => AssistantSessionStore.RenameSession(session, title));
        }
        catch (Exception)
        {
            // 标题只是锦上添花：起不出来就留着截断出来的原文，不打扰用户也不影响对话
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

    /// <summary>
    /// 助手自己发起的下载结束后续一轮。批次是排队的：下载常常比回答先完成，
    /// 那种情况下这里只是看一眼、什么都不做，等这轮回答结束时再取，通知不会丢。
    /// </summary>
    private void OnAssistantTasksReady()
    {
        var session = AssistantSessionStore.Current;
        if (session is null || _runs.ContainsKey(session.Id)) return;
        var batch = AssistantTasks.TakeReady();
        if (batch is null || batch.Count == 0) return;

        if (_autoContinuations >= MaxAutoContinuations)
        {
            AssistantSessionStore.AppendHintMessage(session, "info", Lang.Text("Assistant.Chat.AutoContinue.Limited"));
            if (ReferenceEquals(AssistantSessionStore.Current, session)) RenderCurrentSession();
            return;
        }

        _autoContinuations++;
        var summary = string.Join("；", batch.Select(r => r.Label + (r.Success ? " 已完成" : " 未成功")));
        _ = SendAsync(Lang.Text("Assistant.Chat.AutoContinue", summary, MaxAutoContinuations - _autoContinuations),
            auto: true);
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

    private string BuildSystemPrompt(AssistantToolset toolset)
    {
        var prompt = Lang.Text("Assistant.Chat.System") + "\n" +
                      Lang.Text("Assistant.Chat.System.Env", DescribeEnvironment(toolset));
        // CurseForge 的工具是按密钥挂载的，没挂载就得告诉模型别推荐那条路
        if (string.IsNullOrWhiteSpace(Secrets.CurseForgeAPIKey))
            prompt += "\n" + Lang.Text("Assistant.Chat.System.NoCurseForge");
        // 让模型知道自己现在处在哪一档：只读时该说"只读模式我无法操作文件"，而不是含糊的"我没这个能力"
        var tier = AssistantPermissions.Parse(Config.Assistant.PermissionMode);
        prompt += "\n" + Lang.Text(tier switch
        {
            AssistantPermissionMode.Read => "Assistant.Chat.System.Tier.Read",
            AssistantPermissionMode.Full => "Assistant.Chat.System.Tier.Full",
            _ => "Assistant.Chat.System.Tier.Ask"
        });
        if (tier != AssistantPermissionMode.Read)
            // "帮我安装光影包"这种口语说法，直接对上工具名，省得它以为自己装不了
            prompt += "\n" + Lang.Text("Assistant.Chat.System.Intent");
        return prompt;
    }

    /// <summary>
    /// 实例概况、档位、这一轮真的挂了哪些工具——模型之前会说自己装不了光影，
    /// 就是这些信息只能凭记忆猜。
    /// </summary>
    private static string DescribeEnvironment(AssistantToolset toolset)
    {
        var lines = new List<string>();
        var instance = ModInstanceList.McMcInstanceSelected;
        if (instance is null)
        {
            lines.Add("- " + Lang.Text("Assistant.Chat.System.Env.NoInstance"));
        }
        else
        {
            var info = instance.Info;
            lines.Add("- " + Lang.Text("Assistant.Chat.System.Env.Instance",
                AssistantEnvironment.DescribeInstance(instance.Name, info.VanillaName,
                    AssistantEnvironment.DescribeLoaders(info.HasFabric, info.HasLegacyFabric, info.HasQuilt,
                        info.HasNeoForge, info.HasForge, info.HasLiteLoader),
                    IsIsolated(instance), CountModFiles(instance))));
        }

        lines.Add("- " + Lang.Text("Assistant.Chat.System.Env.Tier", Lang.Text(
            AssistantPermissions.Parse(Config.Assistant.PermissionMode) switch
            {
                AssistantPermissionMode.Read => "Assistant.Chat.Permission.Read",
                AssistantPermissionMode.Full => "Assistant.Chat.Permission.Full",
                _ => "Assistant.Chat.Permission.Ask"
            })));
        lines.Add("- " + Lang.Text(toolset.Definitions.Count == 0
                ? "Assistant.Chat.System.Env.NoTools"
                : "Assistant.Chat.System.Env.Tools",
            string.Join("、", toolset.Definitions.Select(d => d.Name))));
        lines.Add("- " + Lang.Text(toolset.SiteLabels.Count == 0
                ? "Assistant.Chat.System.Env.NoSites"
                : "Assistant.Chat.System.Env.Sites", string.Join("、", toolset.SiteLabels)));
        return string.Join("\n", lines);
    }

    /// <summary>版本隔离开没开：PathIndie 与 PathInstance 不同就说明这个实例有自己的目录。</summary>
    private static bool IsIsolated(McInstance instance)
    {
        static string Trim(string p) => (p ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !string.Equals(Trim(Path.GetFullPath(instance.PathIndie ?? "")),
            Trim(Path.GetFullPath(instance.PathInstance ?? "")), StringComparison.OrdinalIgnoreCase);
    }

    private static int CountModFiles(McInstance instance)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in new[] { instance.PathInstance, instance.PathIndie }
                 .Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var name in AssistantLocalReader.ReadModFileNames(dir)) names.Add(name);
        return names.Count;
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
        var rendered = PanMessages.Children.OfType<FrameworkElement>().ToList();
        var controls = new List<FrameworkElement>();
        // 只给最靠近输入的几条做入场动画：整屏几十条一起铺故事板，切会话时就是它在掉帧
        controls.AddRange(rendered.Skip(Math.Max(0, rendered.Count - AnimatedControls)));
        if (TextEmpty.Visibility == Visibility.Visible) controls.Add(TextEmpty);
        controls.AddRange(InputBar.AnimControls);

        // 复位要覆盖全部：上一次动画做到一半的条目这次可能已经不在尾部窗口里，
        // 不复位就会永远停在半透明上
        foreach (var control in rendered)
        {
            control.IsHitTestVisible = true;
            control.Opacity = 1d;
            if (control.RenderTransform is TranslateTransform) control.RenderTransform = null;
        }

        foreach (var control in controls)
        {
            if (rendered.Contains(control)) continue; // 上面已经复位过了
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
