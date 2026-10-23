using System.Windows;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.UI;

namespace PCL.Assistant;

public partial class PageSetupAssistant
{
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

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        Config.Assistant.ApiBaseUrl = TextBaseUrl.Text.Trim();
        Config.Assistant.ModelName = TextModel.Text.Trim();
        Config.Assistant.ApiKey = TextApiKey.Password.Trim();
        HintService.Hint(Lang.Text("Setup.Assistant.Saved"), HintType.Info);
    }
}
