using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PCL.Core.App;
using PCL.Core.Assistant;

namespace PCL.Assistant;

public partial class PageSetupAssistant
{
    private bool _loading;

    public PageSetupAssistant()
    {
        InitializeComponent();
        Loaded += (_, _) => Load();
    }

    private void Load()
    {
        CheckEnabled.SetChecked(Config.Assistant.Enabled, false);
        CheckBypass.SetChecked(Config.Assistant.AllowRestrictedFeatures, false);
        CheckTools.SetChecked(Config.Assistant.ToolsEnabled, false);
        CheckRedact.SetChecked(Config.Assistant.RedactLocalContent, false);
        TextModel.Text = Config.Assistant.ModelName;
        TextApiKey.Password = Config.Assistant.ApiKey;
        TextBaseUrl.Text = Config.Assistant.ApiBaseUrl;
        LoadProviders();
        LoadSites();
        RefreshAddressRow();
    }

    private void LoadSites()
    {
        PanSites.Children.Clear();
        foreach (var site in AssistantSites.All)
        {
            var box = new MyCheckBox
            {
                Text = site.Label,
                Tag = site.Id,
                Height = 22,
                Margin = new Thickness(0, 0, 0, 6),
                ToolTip = site.Host + "\n" + site.Hint
            };
            box.SetChecked(AssistantSites.Allows(site.Id, Config.Assistant.AllowedSites), false);
            box.Change += Site_OnChange;
            PanSites.Children.Add(box);
        }
    }

    private void Site_OnChange(object senderRaw, bool user)
    {
        if (!user || ModAnimation.AniControlEnabled != 0) return;
        var enabled = PanSites.Children.OfType<MyCheckBox>()
            .Where(c => c.Checked == true && c.Tag is string)
            .Select(c => (string)c.Tag!)
            .ToList();
        // 全关时写入哨兵，空列表在 AssistantSites 里表示"没配置过 = 默认全开"
        Config.Assistant.AllowedSites = enabled.Count == 0 ? [AssistantSites.NoneMarker] : enabled;
    }

    private void LoadProviders()
    {
        _loading = true;
        ComboProvider.Items.Clear();
        foreach (var provider in AssistantProviders.All)
            ComboProvider.Items.Add(new MyComboBoxItem { Content = provider.Name, Tag = provider.BaseUrl });
        var index = Array.FindIndex(AssistantProviders.All, p =>
            p.BaseUrl.Length > 0 &&
            p.BaseUrl.Equals(AssistantClient.NormalizeBaseUrl(Config.Assistant.ApiBaseUrl),
                StringComparison.OrdinalIgnoreCase));
        ComboProvider.SelectedIndex = index >= 0 ? index : AssistantProviders.All.Length - 1;
        _loading = false;
    }

    private bool IsCustomProvider =>
        ComboProvider.SelectedItem is MyComboBoxItem item && item.Tag?.ToString() == "";

    private void RefreshAddressRow()
    {
        var showCustom = IsCustomProvider;
        TextBaseUrlLabel.Visibility = showCustom ? Visibility.Visible : Visibility.Collapsed;
        TextBaseUrl.Visibility = showCustom ? Visibility.Visible : Visibility.Collapsed;
        RowAddress.Height = new GridLength(showCustom ? 28 : 0);
        RowAddressSpacer.Height = new GridLength(showCustom ? 5 : 0);
    }

    private void ComboProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ComboProvider.SelectedItem is not MyComboBoxItem item) return;
        var baseUrl = item.Tag?.ToString() ?? "";

        if (baseUrl.Length > 0)
        {
            Config.Assistant.ApiBaseUrl = baseUrl;
            TextBaseUrl.Text = baseUrl;
            var models = AssistantProviders.ModelsFor(baseUrl, "");
            if (models.Length > 0)
            {
                Config.Assistant.ModelName = models[0];
                TextModel.Text = models[0];
            }
        }

        RefreshAddressRow();
    }

    private void TextModel_ValidateChanged(object sender, EventArgs e)
    {
        Config.Assistant.ModelName = TextModel.Text.Trim();
    }

    private void TextBaseUrl_ValidateChanged(object sender, EventArgs e)
    {
        Config.Assistant.ApiBaseUrl = AssistantClient.NormalizeBaseUrl(TextBaseUrl.Text);
        TextBaseUrl.Text = Config.Assistant.ApiBaseUrl;
    }

    private void TextApiKey_LostFocus(object sender, RoutedEventArgs e)
    {
        var key = TextApiKey.Password.Trim();
        if (key != Config.Assistant.ApiKey) Config.Assistant.ApiKey = key;
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
    }

    private void CheckTools_OnChange(object senderRaw, bool user)
    {
        if (!user || ModAnimation.AniControlEnabled != 0) return;
        Config.Assistant.ToolsEnabled = ((MyCheckBox)senderRaw).Checked == true;
    }

    private void CheckRedact_OnChange(object senderRaw, bool user)
    {
        if (!user || ModAnimation.AniControlEnabled != 0) return;
        Config.Assistant.RedactLocalContent = ((MyCheckBox)senderRaw).Checked == true;
    }
}
