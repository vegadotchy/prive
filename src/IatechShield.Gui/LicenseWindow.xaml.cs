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

    private void ShowResult(string message, bool ok)
    {
        ResultText.Text = message;
        ResultText.Foreground = ok
            ? (Brush)FindResource("GreenBrush")
            : new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x5C));
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
