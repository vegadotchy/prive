using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace IatechShield.Gui;

public partial class LoginWindow : Window
{
    private readonly CloudAccount _cloud;

    /// <summary>Vrai si l'utilisateur s'est connecté avec succès pendant cette fenêtre.</summary>
    public bool SignedIn { get; private set; }

    public LoginWindow(CloudAccount cloud)
    {
        InitializeComponent();
        _cloud = cloud;
    }

    private async void OnSignIn(object sender, RoutedEventArgs e)
    {
        if (!ValidateInputs(out string email, out string pwd)) return;
        Busy(true, "Connexion en cours…");
        string? err = await _cloud.SignInEmailAsync(email, pwd);
        Done(err);
    }

    private async void OnSignUp(object sender, RoutedEventArgs e)
    {
        if (!ValidateInputs(out string email, out string pwd)) return;
        Busy(true, "Création du compte…");
        string? err = await _cloud.SignUpEmailAsync(email, pwd);
        Done(err);
    }

    private async void OnGoogle(object sender, RoutedEventArgs e)
    {
        Busy(true, "Ouverture de Google dans votre navigateur…");
        string? err = await _cloud.SignInWithGoogleAsync();
        Done(err);
    }

    private bool ValidateInputs(out string email, out string pwd)
    {
        email = EmailInput.Text.Trim();
        pwd = PasswordInput.Password;
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            Show("Saisissez une adresse e-mail valide.", false);
            return false;
        }
        if (pwd.Length < 6)
        {
            Show("Le mot de passe doit faire au moins 6 caractères.", false);
            return false;
        }
        return true;
    }

    private void Done(string? error)
    {
        Busy(false, null);
        if (_cloud.IsSignedIn)
        {
            SignedIn = true;
            Show("✓ Connecté. Récupération de votre licence…", true);
            DialogResult = true;
            Close();
        }
        else
        {
            Show(error ?? "Échec de la connexion.", false);
        }
    }

    private void Busy(bool busy, string? message)
    {
        GoogleButton.IsEnabled = !busy;
        if (message is not null) Show(message, true);
    }

    private void Show(string message, bool ok)
    {
        StatusText.Text = message;
        StatusText.Foreground = ok
            ? (Brush)FindResource("AccentBrush")
            : new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x5C));
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
