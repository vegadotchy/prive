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
                if (ok) { _unlocked = true; Close(); }
                else Fail("Windows Hello a échoué ou n'est pas disponible.");
            };
            center.Children.Add(_helloButton);
        }

        root.Children.Add(center);
        Content = root;
        Loaded += (_, _) => _pin.Focus();
    }

    private void TryUnlock()
    {
        if (_lockoutTimer is not null && _lockoutTimer.IsEnabled) return; // bloqué

        bool ok = (!string.IsNullOrEmpty(_pinHash) && SecretHash.Verify(_pin.Password, _pinHash))
               || (!string.IsNullOrEmpty(_passwordHash) && SecretHash.Verify(_pin.Password, _passwordHash));
        if (ok)
        {
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
        base.OnClosing(e);
    }
}
