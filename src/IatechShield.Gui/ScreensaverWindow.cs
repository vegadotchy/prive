using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace IatechShield.Gui;

/// <summary>
/// Écran de veille IATECH : plein écran noir avec un cœur qui bat au centre
/// (comme le dashboard). À la première interaction (souris / clavier), il se
/// ferme — l'appelant enchaîne alors sur l'écran de verrouillage (PIN / mot de passe).
/// </summary>
public sealed class ScreensaverWindow : Window
{
    private Point _startPos;
    private bool _hasStart;

    public ScreensaverWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        Topmost = true;
        Background = Brushes.Black;
        ShowInTaskbar = false;
        Cursor = Cursors.None;

        Content = BuildContent();

        // La moindre interaction réveille l'écran.
        MouseMove += OnAnyMouseMove;
        MouseDown += (_, _) => Wake();
        KeyDown += (_, _) => Wake();
        Loaded += (_, _) => Focus();
    }

    private UIElement BuildContent()
    {
        var root = new Grid();

        // Halo lumineux derrière le cœur.
        var glow = new Ellipse
        {
            Width = 320,
            Height = 320,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = new RadialGradientBrush(Color.FromArgb(0x55, 0xFF, 0x2D, 0x55), Colors.Transparent)
        };
        root.Children.Add(glow);

        // Cœur (deux lobes + pointe) en dégradé rouge → doré.
        var heart = new Path
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Data = Geometry.Parse(
                "M50,88 C12,58 8,30 28,18 C40,11 50,22 50,30 C50,22 60,11 72,18 C92,30 88,58 50,88 Z"),
            Width = 220,
            Height = 220,
            Stretch = Stretch.Uniform,
            Fill = new LinearGradientBrush(
                Color.FromRgb(0xFF, 0x2D, 0x55), Color.FromRgb(0xF5, 0xC2, 0x42), 90),
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0xFF, 0x2D, 0x55),
                BlurRadius = 50,
                ShadowDepth = 0,
                Opacity = 0.9
            },
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        var scale = new ScaleTransform(1, 1);
        heart.RenderTransform = scale;
        root.Children.Add(heart);

        // Texte sous le cœur.
        var label = new TextBlock
        {
            Text = "IATECH-SHIELD PRO",
            Foreground = new SolidColorBrush(Color.FromRgb(0x9F, 0xB4, 0xC7)),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 320, 0, 0),
            Opacity = 0.6
        };
        root.Children.Add(label);

        var hint = new TextBlock
        {
            Text = "Bougez la souris pour déverrouiller",
            Foreground = new SolidColorBrush(Color.FromRgb(0x5C, 0x6E, 0x80)),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 48)
        };
        root.Children.Add(hint);

        // Battement de cœur : deux pulsations rapprochées puis pause (≈ vrai cœur).
        Loaded += (_, _) =>
        {
            var beat = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            beat.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.0))));
            beat.KeyFrames.Add(new EasingDoubleKeyFrame(1.18, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.14)),
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            beat.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.30))));
            beat.KeyFrames.Add(new EasingDoubleKeyFrame(1.12, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.44)),
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            beat.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.60))));
            beat.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.20))));

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, beat);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, (DoubleAnimationUsingKeyFrames)beat.Clone());

            // Halo qui respire.
            var pulse = new DoubleAnimation(0.35, 0.85, TimeSpan.FromSeconds(0.6))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            glow.BeginAnimation(OpacityProperty, pulse);
        };

        return root;
    }

    private void OnAnyMouseMove(object sender, MouseEventArgs e)
    {
        // On ignore les micro-mouvements parasites : il faut un vrai déplacement.
        Point p = e.GetPosition(this);
        if (!_hasStart) { _startPos = p; _hasStart = true; return; }
        if ((p - _startPos).Length > 12) Wake();
    }

    private void Wake()
    {
        try { DialogResult = true; } catch { }
        Close();
    }
}
