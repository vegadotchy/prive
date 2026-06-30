using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using IatechShield.Tools;

namespace IatechShield.Gui;

/// <summary>
/// Écran de verrouillage plein écran au logo IATECH-SHIELD, déverrouillé par PIN,
/// mot de passe ou Windows Hello. En cas d'échecs répétés : blocage progressif
/// (1, 5, 15, 30 puis 60 min) et sirène d'alerte de 3 secondes.
/// Confidentialité visuelle : ne désactive aucune touche système (Ctrl+Alt+Suppr).
/// </summary>
public sealed class LockScreen : Window
{
    private readonly string? _pinHash;
    private readonly string? _passwordHash;
    private readonly bool _helloEnabled;
    private readonly PasswordBox _pin = new();
    private readonly TextBlock _error = new();
    private readonly Button _unlockButton;
    private readonly Button? _helloButton;
    private bool _unlocked;

    // Moyen de déverrouillage retenu, pour le registre des accès.
    public string UnlockMethod { get; private set; } = "PIN / mot de passe";
    public string UnlockIdentity { get; private set; } = Environment.UserName;
    public string UnlockDetail { get; private set; } = "";
    /// <summary>Carte d'identité lue lors du déverrouillage (null si autre moyen).</summary>
    public CardInfo? UnlockCard { get; private set; }

    private DispatcherTimer? _cardPoll;

    // Compteur d'échecs et blocage progressif.
    private int _attempts;
    private int _lockoutTier;
    private bool _sirenPlaying;
    private DispatcherTimer? _lockoutTimer;
    private int _lockoutRemaining;
    private static readonly int[] LockoutSeconds = { 60, 300, 900, 1800, 3600 }; // 1, 5, 15, 30, 60 min

    public LockScreen(string? pinHash, string? passwordHash = null, bool helloEnabled = false)
    {
        _pinHash = pinHash;
        _passwordHash = passwordHash;
        _helloEnabled = helloEnabled;

        WindowStyle = WindowStyle.None;
        WindowState = WindowState.Maximized;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x05, 0x0B, 0x14));

        var root = new Grid();
        var center = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 340
        };

        var shield = new Path
        {
            Data = Geometry.Parse("M 50,4 L 92,20 V 54 C 92,82 72,98 50,108 C 28,98 8,82 8,54 V 20 Z"),
            Fill = new SolidColorBrush(Color.FromArgb(0x22, 0x22, 0xD3, 0xE8)),
            Stroke = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xE8)),
            StrokeThickness = 3,
            Stretch = Stretch.Uniform,
            Width = 96,
            Height = 96,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        center.Children.Add(shield);

        center.Children.Add(new TextBlock
        {
            Text = "IATECH-SHIELD PRO",
            Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xE8)),
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 14, 0, 2)
        });
        center.Children.Add(new TextBlock
        {
            Text = "🔒 Session verrouillée",
            Foreground = Brushes.White,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 2)
        });
        center.Children.Add(new TextBlock
        {
            Text = "Saisissez votre code PIN ou votre mot de passe",
            Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 18)
        });

        _pin.Width = 220;
        _pin.FontSize = 20;
        _pin.Padding = new Thickness(10);
        _pin.HorizontalContentAlignment = HorizontalAlignment.Center;
        _pin.Background = new SolidColorBrush(Color.FromRgb(0x10, 0x23, 0x38));
        _pin.Foreground = Brushes.White;
        _pin.BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xE8));
        _pin.KeyDown += (_, e) => { if (e.Key == Key.Enter) TryUnlock(); };
        center.Children.Add(_pin);

        _error.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x5C));
        _error.FontSize = 12;
        _error.HorizontalAlignment = HorizontalAlignment.Center;
        _error.TextAlignment = TextAlignment.Center;
        _error.Margin = new Thickness(0, 10, 0, 0);
        _error.Visibility = Visibility.Collapsed;
        center.Children.Add(_error);

        _unlockButton = new Button
        {
            Content = "DÉVERROUILLER",
            Padding = new Thickness(20, 8, 20, 8),
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = Cursors.Hand
        };
        _unlockButton.Click += (_, _) => TryUnlock();
        center.Children.Add(_unlockButton);

        if (_helloEnabled)
        {
            _helloButton = new Button
            {
                Content = "🙂  Windows Hello (visage / empreinte)",
                Padding = new Thickness(16, 7, 16, 7),
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb(0x10, 0x23, 0x38)),
                Foreground = Brushes.White
            };
            _helloButton.Click += async (_, _) =>
            {
                if (_lockoutTimer is not null && _lockoutTimer.IsEnabled) return;
                bool ok = await BiometricAuth.VerifyAsync("Déverrouiller IATECH-SHIELD PRO");
                if (ok)
                {
                    UnlockMethod = "Windows Hello";
                    UnlockIdentity = Environment.UserName;
                    _cardPoll?.Stop();
                    _unlocked = true;
                    Close();
                }
                else Fail("Windows Hello a échoué ou n'est pas disponible.");
            };
            center.Children.Add(_helloButton);
        }

        // --- Déverrouillage avec itsme ---
        var itsmeBtn = new Button
        {
            Content = "📱  Déverrouiller avec itsme",
            Padding = new Thickness(16, 7, 16, 7),
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x4B, 0x55)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold
        };
        itsmeBtn.Click += (_, _) => TryItsme();
        center.Children.Add(itsmeBtn);

        // --- Déverrouillage avec carte d'identité (eID) ---
        var eidBtn = new Button
        {
            Content = "🪪  Carte d'identité (eID)",
            Padding = new Thickness(16, 7, 16, 7),
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(0x10, 0x23, 0x38)),
            Foreground = Brushes.White
        };
        eidBtn.Click += (_, _) => TryEid(manual: true);
        center.Children.Add(eidBtn);

        center.Children.Add(new TextBlock
        {
            Text = "Insérez votre carte d'identité : la session se déverrouille automatiquement.",
            Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0x8B, 0xA0)),
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
            MaxWidth = 300,
            TextWrapping = TextWrapping.Wrap
        });

        root.Children.Add(center);
        Content = root;
        Loaded += (_, _) =>
        {
            _pin.Focus();
            StartCardPolling();
        };
    }

    /// <summary>Surveille l'insertion d'une carte d'identité pour déverrouiller automatiquement.</summary>
    private void StartCardPolling()
    {
        _cardPoll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _cardPoll.Tick += (_, _) =>
        {
            if (_lockoutTimer is not null && _lockoutTimer.IsEnabled) return;
            TryEid(manual: false);
        };
        _cardPoll.Start();
    }

    private void TryEid(bool manual)
    {
        if (_lockoutTimer is not null && _lockoutTimer.IsEnabled) return;
        CardInfo info;
        try { info = EidReader.Read(); }
        catch { info = new CardInfo(); }

        if (!info.CardPresent)
        {
            if (manual) Fail("Aucune carte détectée. Insérez votre carte d'identité dans le lecteur.");
            return;   // en mode auto, on attend silencieusement
        }

        UnlockMethod = "Carte d'identité (eID)";
        UnlockIdentity = info.DisplayIdentity;
        UnlockDetail = info.IsBelgianEid
            ? $"N° national : {info.NationalNumber} · Né(e) le {info.BirthDate} à {info.BirthPlace} · {info.Nationality} · Lecteur : {info.ReaderName}"
            : $"Carte non-eID · ATR : {info.Atr} · Lecteur : {info.ReaderName}";
        UnlockCard = info;
        _cardPoll?.Stop();
        _unlocked = true;
        Close();
    }

    private async void TryItsme()
    {
        if (_lockoutTimer is not null && _lockoutTimer.IsEnabled) return;

        var itsme = ItsmeAuth.FromStore();
        if (itsme is null || !itsme.IsConfigured)
        {
            // Pas d'identifiants partenaire itsme : on explique comment l'activer.
            _error.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
            _error.Text = "itsme non configuré. Renseignez vos identifiants partenaire itsme\n" +
                          "dans IATECH-SHIELD → Réglages → « Connexion itsme » pour recevoir\n" +
                          "la vraie notification sur votre téléphone.";
            _error.Visibility = Visibility.Visible;
            return;
        }

        _error.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xE8));
        _error.Text = "📲 Notification envoyée sur votre téléphone — confirmez dans l'app itsme…";
        _error.Visibility = Visibility.Visible;

        ItsmeResult result;
        try { result = await itsme.AuthenticateAsync(); }
        catch (Exception ex) { result = new ItsmeResult { Error = ex.Message }; }

        if (result.Success)
        {
            UnlockMethod = "itsme";
            UnlockIdentity = string.IsNullOrWhiteSpace(result.FullName) ? "Compte itsme" : result.FullName;
            UnlockDetail = string.IsNullOrWhiteSpace(result.Phone) ? "Confirmé via itsme" : $"itsme · {result.Phone}";
            _cardPoll?.Stop();
            _unlocked = true;
            Close();
        }
        else
        {
            _error.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x5C));
            _error.Text = "itsme : " + result.Error;
            _error.Visibility = Visibility.Visible;
        }
    }

    private void TryUnlock()
    {
        if (_lockoutTimer is not null && _lockoutTimer.IsEnabled) return; // bloqué

        bool pinOk = !string.IsNullOrEmpty(_pinHash) && SecretHash.Verify(_pin.Password, _pinHash);
        bool pwdOk = !string.IsNullOrEmpty(_passwordHash) && SecretHash.Verify(_pin.Password, _passwordHash);
        if (pinOk || pwdOk)
        {
            UnlockMethod = pinOk ? "PIN" : "Mot de passe";
            UnlockIdentity = Environment.UserName;
            _cardPoll?.Stop();
            _unlocked = true;
            Close();
            return;
        }
        Fail(null);
    }

    /// <summary>Gère un échec : sirène, compteur, et blocage progressif après 3 essais.</summary>
    private void Fail(string? customMessage)
    {
        _pin.Clear();
        PlaySiren();
        _attempts++;

        if (_attempts >= 3)
        {
            int idx = Math.Min(_lockoutTier, LockoutSeconds.Length - 1);
            int seconds = LockoutSeconds[idx];
            _lockoutTier++;
            _attempts = 0;
            StartLockout(seconds);
            return;
        }

        _error.Text = customMessage ?? $"Code incorrect. Tentative {_attempts}/3 avant blocage.";
        _error.Visibility = Visibility.Visible;
        _pin.Focus();
    }

    private void StartLockout(int seconds)
    {
        _lockoutRemaining = seconds;
        SetControlsEnabled(false);
        UpdateLockoutText();

        _lockoutTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _lockoutTimer.Tick -= OnLockoutTick;
        _lockoutTimer.Tick += OnLockoutTick;
        _lockoutTimer.Start();
    }

    private void OnLockoutTick(object? sender, EventArgs e)
    {
        _lockoutRemaining--;
        if (_lockoutRemaining <= 0)
        {
            _lockoutTimer?.Stop();
            SetControlsEnabled(true);
            _error.Text = "Vous pouvez réessayer.";
            _pin.Focus();
            return;
        }
        UpdateLockoutText();
    }

    private void UpdateLockoutText()
    {
        int m = _lockoutRemaining / 60, s = _lockoutRemaining % 60;
        _error.Text = $"⛔ Trop de tentatives. Réessayez dans {m:00}:{s:00}.";
        _error.Visibility = Visibility.Visible;
    }

    private void SetControlsEnabled(bool enabled)
    {
        _pin.IsEnabled = enabled;
        _unlockButton.IsEnabled = enabled;
        if (_helloButton is not null) _helloButton.IsEnabled = enabled;
    }

    /// <summary>Sirène d'alerte (≈ 3 secondes) en cas d'échec, via le haut-parleur système.</summary>
    private void PlaySiren()
    {
        if (_sirenPlaying) return;
        _sirenPlaying = true;
        Task.Run(() =>
        {
            try
            {
                // Alterne deux fréquences pour un effet « sirène » pendant ~3 s.
                for (int i = 0; i < 10; i++)
                    Console.Beep(i % 2 == 0 ? 980 : 620, 300);
            }
            catch { /* haut-parleur indisponible */ }
            finally { _sirenPlaying = false; }
        });
    }

    // Empêche la fermeture (Alt+F4) sans déverrouillage — sans toucher aux touches système.
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_unlocked) e.Cancel = true;
        else _cardPoll?.Stop();
        base.OnClosing(e);
    }
}
