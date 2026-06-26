namespace IatechShield.Mobile;

public partial class MainPage : ContentPage
{
    // Boutique IATECHFUTUR (même produit que la version Windows).
    private const string ShopUrl = "https://iatechfutur.be/products/iatech-shield-pro-antivirus-nouvelle-generation";

    public MainPage()
    {
        InitializeComponent();
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
