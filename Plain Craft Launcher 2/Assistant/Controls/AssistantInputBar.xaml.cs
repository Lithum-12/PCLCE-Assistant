using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PCL.Core.App;
using PCL.Core.App.Configuration;
using PCL.Core.App.Localization;
using PCL.Core.Assistant;

namespace PCL.Assistant;

public partial class AssistantInputBar
{
    private bool _loadingModels;
    private bool _busy;
    private Action<string>? _onChoice;

    public event Action<string>? SendRequested;
    public event Action? StopRequested;

    public AssistantInputBar()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RefreshInstance();
            RefreshModels();
            RefreshPermission();
        };
        States.Game.SelectedInstanceConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            _ => ModBase.RunInUi(RefreshInstance)));
    }

    public string DraftText
    {
        get => TextChatInput.Text ?? "";
        set => TextChatInput.Text = value ?? "";
    }

    /// <summary>本页自己的动画单元：上游的页面遍历无法穿过外层 ScrollViewer，聊天页需要点名带动画。</summary>
    public FrameworkElement[] AnimControls => [CardInput, CardFooter];

    public void ClearInput() => TextChatInput.Clear();

    public void SetBusy(bool busy)
    {
        _busy = busy;
        BtnSend.Text = busy ? Lang.Text("Assistant.Chat.Stop") : Lang.Text("Assistant.Chat.Send");
    }

    public bool ChoiceVisible => CardChoice.Visibility == Visibility.Visible;

    /// <summary>
    /// 用选择栏顶掉输入框。第一条按约定就是模型推荐的方案，标出来，不让玩家去猜它想让我做哪个。
    /// </summary>
    public void ShowChoice(AssistantChoiceSet choice, Action<string> onPick)
    {
        _onChoice = onPick;
        LabChoice.Text = choice.Question.Length == 0 ? Lang.Text("Assistant.Chat.Choice.Question") : choice.Question;
        PanChoice.Children.Clear();
        for (var i = 0; i < choice.Options.Count; i++)
        {
            var option = choice.Options[i];
            var button = new MyIconTextButton
            {
                Text = i == 0 ? Lang.Text("Assistant.Chat.Choice.Recommended", option) : option,
                ToolTip = option,
                Tag = option
            };
            if (i == 0) button.SvgIcon = "lucide/sparkles";
            button.Click += Choice_Click;
            PanChoice.Children.Add(button);
        }

        // 最后永远留一个退出口：玩家想自己说两句，不该被这排按钮堵在栏外面
        var other = new MyIconTextButton
        {
            Text = Lang.Text("Assistant.Chat.Choice.Other"),
            SvgIcon = "lucide/x",
            LogoScale = 0.8
        };
        other.Click += Choice_Click;
        PanChoice.Children.Add(other);

        // 入场动画只遍历可见控件之外的名单，这里显式给回 1，免得它被上一次动画留在透明状态
        CardChoice.Opacity = 1d;
        CardInput.Visibility = Visibility.Collapsed;
        CardChoice.Visibility = Visibility.Visible;
    }

    public void HideChoice()
    {
        _onChoice = null;
        PanChoice.Children.Clear();
        if (!ChoiceVisible) return;
        CardChoice.Visibility = Visibility.Collapsed;
        CardInput.Visibility = Visibility.Visible;
    }

    private void Choice_Click(object sender, ModBase.RouteEventArgs e)
    {
        if (sender is not MyIconTextButton button) return;
        var pick = button.Tag as string;
        var onPick = _onChoice;
        HideChoice();
        if (pick is null)
        {
            TextChatInput.Focus(); // 「我自己说」：只把输入框还回来，不替玩家发任何东西
            return;
        }

        onPick?.Invoke(pick);
    }

    private void BtnInstance_Click(object sender, ModBase.RouteEventArgs e)
    {
        ModMain.frmMain?.PageChange(FormMain.PageType.InstanceSelect);
    }

    private void RefreshPermission()
    {
        var mode = AssistantPermissions.Parse(Config.Assistant.PermissionMode);
        var (icon, key) = mode switch
        {
            AssistantPermissionMode.Read => ("lucide/search", "Read"),
            AssistantPermissionMode.Full => ("lucide/wrench", "Full"),
            _ => ("lucide/shield-check", "Ask")
        };
        BtnPermission.SvgIcon = icon;
        BtnPermission.Text = Lang.Text($"Assistant.Chat.Permission.{key}");
        BtnPermission.ToolTip = Lang.Text($"Assistant.Chat.Permission.{key}.Tip");
    }

    private void BtnPermission_Click(object sender, ModBase.RouteEventArgs e)
    {
        var next = AssistantPermissions.Next(AssistantPermissions.Parse(Config.Assistant.PermissionMode));
        Config.Assistant.PermissionMode = AssistantPermissions.ToConfig(next);
        RefreshPermission();
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
        var match = Array.IndexOf(models, Config.Assistant.ModelName);
        ComboModel.SelectedIndex = match >= 0 ? match : 0;
        _loadingModels = false;
    }

    private void ComboModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingModels || ComboModel.SelectedItem is not MyComboBoxItem item) return;
        var model = item.Tag?.ToString();
        if (string.IsNullOrEmpty(model) || model == Config.Assistant.ModelName) return;
        Config.Assistant.ModelName = model;
    }

    private void TextChatInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyboardDevice.Modifiers == ModifierKeys.Shift) return;
        e.Handled = true;
        RaiseSend();
    }

    private void BtnSend_Click(object sender, MouseButtonEventArgs e) => RaiseSend();

    private void RaiseSend()
    {
        if (_busy)
        {
            StopRequested?.Invoke();
            return;
        }

        var text = TextChatInput.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        ClearInput();
        SendRequested?.Invoke(text);
    }
}
