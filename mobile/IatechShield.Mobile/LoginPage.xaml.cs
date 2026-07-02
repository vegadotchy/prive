using IatechShield.Mobile.Services;

namespace IatechShield.Mobile;

public partial class LoginPage : ContentPage
{
    private readonly CloudAccount _cloud;

    public LoginPage(CloudAccount cloud)
    {
        InitializeComponent();
        _cloud = cloud;
    }

    private async void OnSignIn(object sender, EventArgs e)
    {
        if (!Validate(out string email, out string pwd)) return;
        await RunAsync("Connexion…", () => _cloud.SignInEmailAsync(email, pwd));
    }

    private async void OnSignUp(object sender, EventArgs e)
    {
        if (!Validate(out string email, out string pwd)) return;
        await RunAsync("Création du compte…", () => _cloud.SignUpEmailAsync(email, pwd));
    }

    private async void OnGoogle(object sender, EventArgs e)
    {
        await RunAsync("Ouverture de Google…", () => _cloud.SignInWithGoogleAsync());
    }

    private bool Validate(out string email, out string pwd)
    {
        email = EmailEntry.Text?.Trim() ?? "";
        pwd = PasswordEntry.Text ?? "";
        if (!email.Contains('@')) { Show("Saisissez une adresse e-mail valide.", false); return false; }
        if (pwd.Length < 6) { Show("Mot de passe : 6 caractères minimum.", false); return false; }
        return true;
    }

    private async Task RunAsync(string message, Func<Task<string?>> action)
    {
        Busy.IsRunning = true;
        Show(message, true);
        string? error = await action();
        Busy.IsRunning = false;

        if (_cloud.IsSignedIn)
        {
            Show("✓ Connecté.", true);
            await Navigation.PopAsync();
        }
        else
        {
            Show(error ?? "Échec de la connexion.", false);
        }
    }

    private void Show(string message, bool ok)
    {
        StatusLabel.Text = message;
        StatusLabel.TextColor = ok
            ? Color.FromArgb("#22D3E8")
            : Color.FromArgb("#FF6B6B");
    }
}
