using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace IatechShield.Gui;

/// <summary>
/// Écran d'accueil plein écran (bleu marine) affiché après un déverrouillage par carte
/// d'identité : « Bienvenue [prénom nom] », avant l'ouverture de la session. Se ferme
/// automatiquement après quelques secondes (ou au clic).
/// </summary>
public sealed class WelcomeWindow : Window
{
    public WelcomeWindow(string fullName)
    {
        WindowStyle = WindowStyle.None;
        WindowState = WindowState.Maximized;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x08, 0x1C, 0x3A)); // bleu marine

        var center = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Bouclier / logo doré.
        center.Children.Add(new TextBlock
        {
            Text = "",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 64,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 24),
            Effect = new DropShadowEffect { Color = Color.FromRgb(0xFB, 0xBF, 0x24), BlurRadius = 30, ShadowDepth = 0, Opacity = 0.7 }
        });

        center.Children.Add(new TextBlock
        {
            Text = "Bienvenue",
            FontSize = 34,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0xC5, 0xF0)),
            HorizontalAlignment = HorizontalAlignment.Center,
            FontWeight = FontWeights.Light
        });

        center.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(fullName) ? "Utilisateur" : fullName,
            FontSize = 52,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
            Effect = new DropShadowEffect { Color = Color.FromRgb(0x22, 0xD3, 0xE8), BlurRadius = 24, ShadowDepth = 0, Opacity = 0.55 }
        });

        center.Children.Add(new TextBlock
        {
            Text = "IATECH-SHIELD PRO — accès autorisé par carte d'identité",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5E, 0x7A, 0x9E)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 22, 0, 0)
        });

        Content = center;

        // Fermeture automatique après ~3 secondes, ou au clic.
        var timer = new DispatcherTimer { Interval = System.TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) => { timer.Stop(); try { Close(); } catch { } };
        Loaded += (_, _) => timer.Start();
        MouseLeftButtonUp += (_, _) => { try { Close(); } catch { } };
    }
}
