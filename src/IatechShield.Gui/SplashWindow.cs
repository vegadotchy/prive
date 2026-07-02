using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace IatechShield.Gui;

/// <summary>
/// Écran de démarrage : fond sombre, bouclier doré au reflet métallique,
/// logo « IATECH-SHIELD PRO » et slogan « POWERED BY IATECHFUTUR », avec une
/// apparition progressive. Affiché brièvement avant le tableau de bord.
/// </summary>
public sealed class SplashWindow : Window
{
    public SplashWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = 520;
        Height = 320;
        ShowInTaskbar = false;
        Topmost = true;

        var root = new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = new RadialGradientBrush(
                Color.FromRgb(0x0C, 0x1B, 0x2E), Color.FromRgb(0x04, 0x0A, 0x12))
            { GradientOrigin = new Point(0.5, 0.35), Center = new Point(0.5, 0.35), RadiusX = 0.9, RadiusY = 0.9 },
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xD4, 0xAF, 0x37)),
            BorderThickness = new Thickness(1)
        };

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Dégradé doré (reflet métallique) pour le bouclier et le titre.
        var gold = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0xFD, 0xE6, 0x8A), 0),
                new GradientStop(Color.FromRgb(0xD4, 0xAF, 0x37), 0.5),
                new GradientStop(Color.FromRgb(0x9A, 0x76, 0x16), 1),
            }
        };

        var shield = new Path
        {
            Data = Geometry.Parse("M 50,4 L 92,20 V 54 C 92,82 72,98 50,108 C 28,98 8,82 8,54 V 20 Z"),
            Fill = new SolidColorBrush(Color.FromArgb(0x22, 0xD4, 0xAF, 0x37)),
            Stroke = gold,
            StrokeThickness = 3,
            Stretch = Stretch.Uniform,
            Width = 110,
            Height = 110,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            { Color = Color.FromRgb(0xD4, 0xAF, 0x37), BlurRadius = 30, ShadowDepth = 0, Opacity = 0.6 }
        };
        stack.Children.Add(shield);

        stack.Children.Add(new TextBlock
        {
            Text = "IATECH-SHIELD PRO",
            Foreground = gold,
            FontSize = 30,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0)
        });
        stack.Children.Add(new TextBlock
        {
            Text = "POWERED BY IATECHFUTUR",
            Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA6, 0xB8)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        });

        root.Child = stack;
        Content = root;

        // Apparition progressive.
        Opacity = 0;
        Loaded += (_, _) => BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)));
    }
}
