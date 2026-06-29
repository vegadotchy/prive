using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace IatechShield.Gui;

/// <summary>
/// Notification « maison » IATECH (style futuriste) : fond noir, bordure cyan
/// lumineuse, triangle d'alerte rouge. Apparaît en bas à droite, se ferme seule
/// après quelques secondes ou au clic (qui déclenche l'action fournie).
/// </summary>
public sealed class ToastWindow : Window
{
    private readonly Action? _onClick;
    private readonly DispatcherTimer _life;

    public ToastWindow(string title, string message, Action? onClick = null)
    {
        _onClick = onClick;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Width = 380;

        Content = BuildContent(title, message);

        Loaded += (_, _) => PositionAndAnimateIn();
        MouseLeftButtonUp += (_, _) => { try { _onClick?.Invoke(); } catch { } Dismiss(); };

        _life = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _life.Tick += (_, _) => Dismiss();
        _life.Start();
    }

    private UIElement BuildContent(string title, string message)
    {
        // Cadre noir + bordure cyan lumineuse.
        var border = new Border
        {
            Width = 380,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromRgb(0x05, 0x0B, 0x14)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xE8)),
            BorderThickness = new Thickness(1.4),
            Padding = new Thickness(14),
            Cursor = Cursors.Hand,
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0x22, 0xD3, 0xE8),
                BlurRadius = 28, ShadowDepth = 0, Opacity = 0.55
            }
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Pastille rouge avec triangle d'alerte (Segoe MDL2 — monochrome, colorable).
        var iconWrap = new Grid { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 12, 0) };
        iconWrap.Children.Add(new Ellipse
        {
            Width = 44, Height = 44,
            Fill = new SolidColorBrush(Color.FromArgb(0x33, 0xEF, 0x44, 0x44)),
            Stroke = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
            StrokeThickness = 1.2
        });
        iconWrap.Children.Add(new TextBlock
        {
            Text = "\uE7BA",          // triangle d'alerte
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 20,
            Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(iconWrap, 0);
        grid.Children.Add(iconWrap);

        // Texte.
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = "IATECH-SHIELD PRO",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xE8)),
            Opacity = 0.85,
            Margin = new Thickness(0, 0, 0, 2)
        });
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap
        });
        stack.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9F, 0xB4, 0xC7)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        });
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);

        // Liseré cyan animé en bas (touche futuriste).
        var outer = new StackPanel();
        outer.Children.Add(grid);
        var line = new Border
        {
            Height = 2,
            Margin = new Thickness(0, 12, 0, 0),
            CornerRadius = new CornerRadius(1),
            Background = new LinearGradientBrush(
                Color.FromRgb(0x22, 0xD3, 0xE8), Color.FromRgb(0xEF, 0x44, 0x44), 0)
        };
        outer.Children.Add(line);
        border.Child = outer;
        return border;
    }

    private void PositionAndAnimateIn()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - 16;
        Top = wa.Bottom - ActualHeight - 16;

        // Glisse depuis la droite + fondu.
        Opacity = 0;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220));
        BeginAnimation(OpacityProperty, fade);
    }

    private bool _closing;
    private void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        _life.Stop();
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) => { try { Close(); } catch { } };
        BeginAnimation(OpacityProperty, fade);
    }
}
