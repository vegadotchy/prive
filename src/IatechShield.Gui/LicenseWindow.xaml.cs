using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using IatechShield.Licensing;

namespace IatechShield.Gui;

public partial class LicenseWindow : Window
{
    private readonly LicenseManager _license;

    /// <summary>Vrai si une licence a été activée avec succès pendant cette fenêtre.</summary>
    public bool Activated { get; private set; }

    public LicenseWindow(LicenseManager license, LicenseStatus status)
    {
        InitializeComponent();
        _license = license;
        TrialInfoText.Text = status.Message;
    }

    private void OnActivate(object sender, RoutedEventArgs e)
    {
        string key = KeyInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            ShowResult("Veuillez saisir une clé.", false);
            return;
        }

        var check = _license.Activate(key, DateTimeOffset.UtcNow);
        if (check.Valid)
        {
            Activated = true;
            ShowResult($"Licence activée : {check.License!.TierLabel}. Merci !", true);
        }
        else
        {
            ShowResult(check.Message, false);
        }
    }

    private async void OnActivateOnline(object sender, RoutedEventArgs e)
    {
        string key = KeyInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            ShowResult("Saisissez d'abord votre clé d'achat.", false);
            return;
        }

        OnlineActivateButton.IsEnabled = false;
        ShowResult("Activation en ligne en cours…", true);
        try
        {
            string version = System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString() ?? "0.0.0";
            var client = new OnlineActivationClient();
            var result = await client.ActivateAsync(key, version);

            if (result.Success && result.LicenseKey is not null)
            {
                // La licence renvoyée par le serveur est vérifiée hors-ligne (signature).
                var check = _license.Activate(result.LicenseKey, DateTimeOffset.UtcNow);
                if (check.Valid)
                {
                    Activated = true;
                    ShowResult($"Activée en ligne : {check.License!.TierLabel}. Merci !", true);
                }
                else
                {
                    ShowResult($"Licence du serveur invalide : {check.Message}", false);
                }
            }
            else
            {
                ShowResult(result.Message, false);
            }
        }
        catch (Exception ex)
        {
            ShowResult($"Échec de l'activation en ligne : {ex.Message}", false);
        }
        finally
        {
            OnlineActivateButton.IsEnabled = true;
        }
    }

    private void ShowResult(string message, bool ok)
    {
        ResultText.Text = message;
        ResultText.Foreground = ok
            ? (Brush)FindResource("GreenBrush")
            : new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x5C));
    }

    /// <summary>Ouvre la boutique IATECHFUTUR sur la formule choisie (mensuel / annuel / à vie).</summary>
    private void OnBuyPlan(object sender, MouseButtonEventArgs e)
    {
        string plan = (sender as FrameworkElement)?.Tag as string ?? "";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                Branding.ShopUrlForPlan(plan)) { UseShellExecute = true });
            ShowResult("La boutique iatechfutur.be s'ouvre dans votre navigateur. " +
                       "Après l'achat, collez la clé reçue par e-mail ci-dessous puis cliquez sur « Activer la licence ».", true);
        }
        catch (Exception ex)
        {
            ShowResult($"Impossible d'ouvrir la boutique : {ex.Message}\nRendez-vous sur {Branding.ShopUrl}", false);
        }
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
