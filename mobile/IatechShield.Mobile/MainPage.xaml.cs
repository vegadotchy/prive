using IatechShield.Mobile.Services;

namespace IatechShield.Mobile;

public partial class MainPage : ContentPage
{
    // Boutique IATECHFUTUR (même produit que la version Windows).
    private const string ShopUrl = "https://iatechfutur.be/products/iatech-shield-pro-antivirus-nouvelle-generation";

    private readonly CloudAccount _cloud = new();
    private bool _restored;

    public MainPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_restored)
        {
            _restored = true;
            await _cloud.RestoreAsync();
        }
        await RefreshAccountAsync();
    }

    private async Task RefreshAccountAsync()
    {
        if (_cloud.IsSignedIn)
        {
            AccountLabel.Text = $"Connecté : {_cloud.User!.Email}";
            AccountButton.Text = "Se déconnecter";

            var lic = await _cloud.FetchLicenseAsync();
            LicenseLabel.Text = lic is null
                ? "Aucune licence liée à ce compte."
                : DescribeLicense(lic);
        }
        else
        {
            AccountLabel.Text = "Non connecté";
            LicenseLabel.Text = "";
            AccountButton.Text = "Se connecter (Google / e-mail)";
        }
    }

    private static string DescribeLicense(CloudLicense lic)
    {
        string tier = lic.Tier.ToLowerInvariant() switch
        {
            "lifetime" => "Licence à vie",
            "yearly" => "Licence annuelle",
            "monthly" => "Licence mensuelle",
            _ => "Licence"
        };
        if (lic.ExpiresAt is { } e && lic.Tier.ToLowerInvariant() != "lifetime")
            return $"✓ {tier} — valide jusqu'au {e.ToLocalTime():dd/MM/yyyy}";
        return $"✓ {tier}";
    }

    private async void OnAccountClicked(object sender, EventArgs e)
    {
        if (_cloud.IsSignedIn)
        {
            bool ok = await DisplayAlert("Mon compte",
                $"Connecté : {_cloud.User!.Email}\n\nSe déconnecter ?", "Oui", "Non");
            if (ok)
            {
                _cloud.SignOut();
                await RefreshAccountAsync();
            }
            return;
        }

        await Navigation.PushAsync(new LoginPage(_cloud));
    }

    private async void OnBuyClicked(object sender, EventArgs e)
    {
        try
        {
            await Launcher.Default.OpenAsync(new Uri(ShopUrl));
        }
        catch
        {
            await DisplayAlert("Boutique", "Rendez-vous sur iatechfutur.be", "OK");
        }
    }
}
