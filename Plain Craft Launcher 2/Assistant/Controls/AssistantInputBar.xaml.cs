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

    public event Action<string>? SendRequested;
    public event Action? StopRequested;

    public AssistantInputBar()
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

    public void ClearInput() => TextChatInput.Clear();

    public void SetBusy(bool busy)
    {
        _busy = busy;
        BtnSend.Text = busy ? Lang.Text("Assistant.Chat.Stop") : Lang.Text("Assistant.Chat.Send");
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

    private void TextChatInput_KeyDown(object sender, KeyEventArgs e)
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
