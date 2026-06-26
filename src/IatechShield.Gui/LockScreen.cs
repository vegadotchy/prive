using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using IatechShield.Tools;

namespace IatechShield.Gui;

/// <summary>
/// Écran de verrouillage plein écran au logo IATECH-SHIELD, déverrouillé par PIN.
/// Confidentialité visuelle : ne désactive aucune touche système (pas de blocage
/// de Ctrl+Alt+Suppr / Gestionnaire des tâches), conformément à un usage sain.
/// </summary>
public sealed class LockScreen : Window
{
    private readonly string? _pinHash;
    private readonly string? _passwordHash;
    private readonly bool _helloEnabled;
    private readonly PasswordBox _pin = new();
    private readonly TextBlock _error = new();
    private bool _unlocked;

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
            Width = 320
        };

        // Logo : bouclier stylisé.
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
            Text = "Session verrouillée — saisissez votre code PIN ou mot de passe",
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
        _error.Margin = new Thickness(0, 10, 0, 0);
        _error.Visibility = Visibility.Collapsed;
        center.Children.Add(_error);

        var unlock = new Button
        {
            Content = "DÉVERROUILLER",
            Padding = new Thickness(20, 8, 20, 8),
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = Cursors.Hand
        };
        unlock.Click += (_, _) => TryUnlock();
        center.Children.Add(unlock);

        if (_helloEnabled)
        {
            var hello = new Button
            {
                Content = "🙂  Windows Hello (visage / empreinte)",
                Padding = new Thickness(16, 7, 16, 7),
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb(0x10, 0x23, 0x38)),
                Foreground = Brushes.White
            };
            hello.Click += async (_, _) =>
            {
                bool ok = await BiometricAuth.VerifyAsync("Déverrouiller IATECH-SHIELD PRO");
                if (ok) { _unlocked = true; Close(); }
                else { _error.Text = "Windows Hello a échoué ou n'est pas disponible."; _error.Visibility = Visibility.Visible; }
            };
            center.Children.Add(hello);
        }

        root.Children.Add(center);
        Content = root;
        Loaded += (_, _) => _pin.Focus();
    }

    private void TryUnlock()
    {
        bool ok = (!string.IsNullOrEmpty(_pinHash) && SecretHash.Verify(_pin.Password, _pinHash))
               || (!string.IsNullOrEmpty(_passwordHash) && SecretHash.Verify(_pin.Password, _passwordHash));
        if (ok)
        {
            _unlocked = true;
            Close();
            return;
        }
        _error.Text = "Code PIN ou mot de passe incorrect.";
        _error.Visibility = Visibility.Visible;
        _pin.Clear();
        _pin.Focus();
    }

    // Empêche la fermeture (Alt+F4) sans PIN — sans toucher aux touches système.
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_unlocked) e.Cancel = true;
        base.OnClosing(e);
    }
}
