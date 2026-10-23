using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PCL.Core.App;
using PCL.Core.App.Configuration;
using PCL.Core.App.Localization;
using PCL.Core.Assistant;

namespace PCL.Assistant;

public partial class PageChatRight
{
    private readonly List<AssistantMessage> _history = new();
    private CancellationTokenSource? _cts;
    private bool _loadingModels;

    public PageChatRight()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RefreshInstance();
            RefreshModels();
        };
        States.Game.SelectedInstanceConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            _ => ModBase.RunInUi(RefreshInstance)));
    }

    private void BtnInstance_Click(object sender, ModBase.RouteEventArgs e)
    {
        ModMain.frmMain?.PageChange(FormMain.PageType.InstanceSelect);
    }

    private void RefreshInstance()
    {
        var name = ModInstanceList.McMcInstanceSelected?.Name;
        BtnInstance.Text = string.IsNullOrWhiteSpace(name) ? Lang.Text("Assistant.Chat.SelectInstance") : name;
    }

    private void RefreshModels()
    {
        _loadingModels = true;
        ComboModel.Items.Clear();
        var models = AssistantProviders.ModelsFor(Config.Assistant.ApiBaseUrl, Config.Assistant.ModelName);
        if (models.Length == 0)
        {
            ComboModel.Items.Add(new MyComboBoxItem { Content = Lang.Text("Assistant.Chat.Model.None"), IsEnabled = false });
            ComboModel.SelectedIndex = 0;
            _loadingModels = false;
            return;
        }

        foreach (var model in models) ComboModel.Items.Add(new MyComboBoxItem { Content = model, Tag = model });

        var current = Config.Assistant.ModelName;
        var match = models.IndexOf(current);
        ComboModel.SelectedIndex = match >= 0 ? match : models.Length > 0 ? 0 : -1;
        _loadingModels = false;
    }

    private void ComboModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingModels || ComboModel.SelectedItem is not MyComboBoxItem item) return;
        var model = item.Tag?.ToString();
        if (string.IsNullOrEmpty(model) || model == Config.Assistant.ModelName) return;
        Config.Assistant.ModelName = model;
    }

    private void TextChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyboardDevice.Modifiers == ModifierKeys.Shift) return;
        e.Handled = true;
        SendOrStop();
    }

    private void BtnSend_Click(object sender, MouseButtonEventArgs e) => SendOrStop();

    private void SendOrStop()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            return;
        }

        _ = SendAsync();
    }

    private async Task SendAsync()
    {
        var input = TextChatInput.Text?.Trim();
        if (string.IsNullOrEmpty(input)) return;

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

        TextChatInput.Clear();
        TextEmpty.Visibility = Visibility.Collapsed;
        AppendBubble("Assistant.Chat.RoleUser", input);
        _history.Add(new AssistantMessage("user", input));
        var answer = AppendBubble("Assistant.Chat.RoleAssistant", "");

        _cts = new CancellationTokenSource();
        SetBusy(true);
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
            SetBusy(false);
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

    private void SetBusy(bool busy)
    {
        BtnSend.Text = busy ? Lang.Text("Assistant.Chat.Stop") : Lang.Text("Assistant.Chat.Send");
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
