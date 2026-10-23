using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Assistant;
using PCL.Core.UI;

namespace PCL.Assistant;

public partial class PageSetupAssistant
{
    private bool _loadingProvider;

    public PageSetupAssistant()
    {
        InitializeComponent();
        Loaded += (_, _) => Load();
    }

    private void Load()
    {
        CheckEnabled.SetChecked(Config.Assistant.Enabled, false);
        CheckBypass.SetChecked(Config.Assistant.AllowRestrictedFeatures, false);
        TextBaseUrl.Text = Config.Assistant.ApiBaseUrl;
        TextModel.Text = Config.Assistant.ModelName;
        TextApiKey.Password = Config.Assistant.ApiKey;
        LoadProviders();
    }

    private void LoadProviders()
    {
        _loadingProvider = true;
        ComboProvider.Items.Clear();
        foreach (var provider in AssistantProviders.All)
            ComboProvider.Items.Add(new MyComboBoxItem { Content = provider.Name, Tag = provider.BaseUrl });
        var match = System.Array.FindIndex(AssistantProviders.All,
            p => p.BaseUrl.Length > 0 && p.BaseUrl == Config.Assistant.ApiBaseUrl);
        ComboProvider.SelectedIndex = match >= 0 ? match : AssistantProviders.All.Length - 1;
        _loadingProvider = false;
    }

    private void ComboProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingProvider || ComboProvider.SelectedItem is not MyComboBoxItem item) return;
        var baseUrl = item.Tag?.ToString() ?? "";
        Config.Assistant.ApiBaseUrl = baseUrl;
        TextBaseUrl.Text = baseUrl;

        var models = AssistantProviders.ModelsFor(baseUrl, "");
        if (models.Length > 0 && System.Array.IndexOf(models, Config.Assistant.ModelName) < 0)
        {
            Config.Assistant.ModelName = models[0];
            TextModel.Text = models[0];
        }
    }

    private void CheckEnabled_OnChange(object senderRaw, bool user)
    {
        if (!user || ModAnimation.AniControlEnabled != 0) return;
        Config.Assistant.Enabled = ((MyCheckBox)senderRaw).Checked == true;
    }

    private void CheckBypass_OnChange(object senderRaw, bool user)
    {
        if (!user || ModAnimation.AniControlEnabled != 0) return;
        Config.Assistant.AllowRestrictedFeatures = ((MyCheckBox)senderRaw).Checked == true;
        HintService.Hint(Lang.Text("Setup.Assistant.Saved"), HintType.Info);
    }

    private void BtnSave_Click(object sender, MouseButtonEventArgs e)
    {
        Config.Assistant.ApiBaseUrl = TextBaseUrl.Text.Trim();
        Config.Assistant.ModelName = TextModel.Text.Trim();
        Config.Assistant.ApiKey = TextApiKey.Password.Trim();
        HintService.Hint(Lang.Text("Setup.Assistant.Saved"), HintType.Info);
    }
}
