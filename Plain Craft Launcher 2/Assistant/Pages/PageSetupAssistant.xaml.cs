using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Assistant;

namespace PCL.Assistant;

public partial class PageSetupAssistant
{
    private bool _loading;
    private readonly Dictionary<string, (MyListItem Row, MyIconButton Toggle)> _siteRows =
        new(StringComparer.OrdinalIgnoreCase);

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
        TextCurseForgeKey.Password = Config.Assistant.CurseForgeKey;
        TextBaseUrl.Text = Config.Assistant.ApiBaseUrl;
        LoadProviders();
        LoadSites();
        RefreshAddressRow();
    }

    private void LoadSites()
    {
        _siteRows.Clear();
        PanSites.Children.Clear();
        foreach (var site in AssistantSites.All)
        {
            var toggle = new MyIconButton { LogoScale = 1d, Tag = site.Id, SvgIcon = "lucide/circle-check" };
            var row = new MyListItem
            {
                Title = site.Label,
                Height = 38,
                MinPaddingRight = 35,
                Type = MyListItem.CheckType.Clickable,
                LogoScale = 0.9,
                SvgIcon = SiteIcon(site.Id),
                ToolTip = site.Host + "\n" + site.Hint,
                Buttons = [toggle]
            };
            // 整行可点，跟模组页一样不必精准去戳那个小图标
            row.Click += (_, _) => ToggleSite(site.Id);
            toggle.Click += (_, _) => ToggleSite(site.Id);
            _siteRows[site.Id] = (row, toggle);
            PanSites.Children.Add(row);
        }

        RefreshSites();
    }

    private void ToggleSite(string id)
    {
        var allowed = Config.Assistant.AllowedSites ?? [];
        var enabled = AssistantSites.All.Where(s => AssistantSites.Allows(s.Id, allowed))
            .Select(s => s.Id)
            .ToList();
        if (!enabled.Remove(id)) enabled.Add(id);
        // 全关时写入哨兵：空列表在 AssistantSites 里表示"没配置过 = 默认全开"
        Config.Assistant.AllowedSites = enabled.Count == 0 ? [AssistantSites.NoneMarker] : enabled;
        RefreshSites();
    }

    private void RefreshSites()
    {
        var allowed = Config.Assistant.AllowedSites ?? [];
        foreach (var (site, entry) in _siteRows)
        {
            var on = AssistantSites.Allows(site, allowed);
            entry.Row.Opacity = on ? 1d : 0.55d;
            // 图标与提示都表示"点下去会发生什么"，与实例设置→模组的启用/禁用一致
            entry.Toggle.SvgIcon = on ? "lucide/circle-minus" : "lucide/circle-check";
            entry.Toggle.ToolTip = Lang.Text(on ? "Instance.Resource.Disable" : "Instance.Resource.Enable");
        }
    }

    private static string SiteIcon(string id) => id switch
    {
        "mcmod" => "lucide/book-marked",
        "github" => "lucide/git-pull-request-arrow",
        "modpackindex" => "lucide/boxes",
        "mojang" => "lucide/earth",
        "bmclapi" => "lucide/download",
        "fabric" => "lucide/package",
        "forge" => "lucide/hammer",
        "neoforge" => "lucide/anvil",
        "quilt" => "lucide/grid-2x2",
        "liteloader" => "lucide/box",
        "optifine" => "lucide/palette",
        "modrinth" => "lucide/rocket",
        "curseforge" => "lucide/tool-case",
        _ => "lucide/globe"
    };

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

    private void TextCurseForgeKey_LostFocus(object sender, RoutedEventArgs e)
    {
        var key = TextCurseForgeKey.Password.Trim();
        // 工具清单是每次发送时按"有没有 key"重建的，写完不用通知别的地方
        if (key != Config.Assistant.CurseForgeKey) Config.Assistant.CurseForgeKey = key;
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
