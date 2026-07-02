using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace IatechShield.Gui;

/// <summary>
/// Écran de veille IATECH : plein écran noir, gros cœur cyan (style dashboard)
/// entouré de cercles concentriques et de nœuds en orbite, cœur battant avec
/// des éclairs / pulsations électriques qui parcourent tout l'écran. À la
/// première interaction, il se ferme et l'appelant enchaîne sur le verrouillage.
/// </summary>
public sealed class ScreensaverWindow : Window
{
    // Couleur du cœur du dashboard (cyan).
    private static readonly Color Accent = Color.FromRgb(0x22, 0xD3, 0xE8);
    private static readonly Color AccentDim = Color.FromRgb(0x1A, 0x8F, 0xA0);
    private static readonly SolidColorBrush AccentBrush = new(Accent);
    private static readonly SolidColorBrush AccentDimBrush = new(AccentDim);

    private Point _startPos;
    private bool _hasStart;
    private readonly Random _rng = new();
    private Canvas? _boltCanvas;

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

        MouseMove += OnAnyMouseMove;
        MouseDown += (_, _) => Wake();
        KeyDown += (_, _) => Wake();
        Loaded += (_, _) => { Focus(); BuildLightning(); };
    }

    private UIElement BuildContent()
    {
        var root = new Grid();

        // Calque des éclairs (derrière tout le reste), rempli au chargement.
        _boltCanvas = new Canvas();
        root.Children.Add(_boltCanvas);

        // Texte du haut.
        var top = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 64, 0, 0),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 900,
            FontSize = 22
        };
        top.Inlines.Add(new System.Windows.Documents.Run("Votre ordinateur et votre vie privée sont protégés par ")
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xDC, 0xEA))
        });
        top.Inlines.Add(new System.Windows.Documents.Run("IATECH-SHIELD PRO")
        {
            Foreground = AccentBrush,
            FontWeight = FontWeights.Bold
        });
        root.Children.Add(top);

        // Bloc central : cercles concentriques + cœur (style dashboard, agrandi).
        root.Children.Add(BuildHeartCluster());

        // Indice en bas.
        var hint = new TextBlock
        {
            Text = "Bougez la souris pour déverrouiller",
            Foreground = new SolidColorBrush(Color.FromRgb(0x5C, 0x6E, 0x80)),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 54)
        };
        root.Children.Add(hint);

        return root;
    }

    private UIElement BuildHeartCluster()
    {
        var cluster = new Grid
        {
            Width = 460,
            Height = 460,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Cercle externe fixe.
        cluster.Children.Add(new Ellipse { Width = 460, Height = 460, Stroke = new SolidColorBrush(Color.FromRgb(0x14, 0x32, 0x47)), StrokeThickness = 1 });

        // Anneau pointillé rotatif.
        var ringRotate = new RotateTransform(0, 207, 207);
        var dashed = new Ellipse
        {
            Width = 414, Height = 414,
            Stroke = AccentDimBrush, StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 2, 6 },
            Opacity = 0.7,
            RenderTransform = ringRotate
        };
        cluster.Children.Add(dashed);

        // Anneau principal cyan + halo.
        cluster.Children.Add(new Ellipse
        {
            Width = 340, Height = 340,
            Stroke = AccentBrush, StrokeThickness = 2, Opacity = 0.85,
            Effect = new DropShadowEffect { Color = Accent, BlurRadius = 26, ShadowDepth = 0, Opacity = 0.6 }
        });

        // Halo qui pulse au rythme du cœur.
        var glow = new Ellipse
        {
            Width = 320, Height = 320,
            Fill = new SolidColorBrush(Color.FromArgb(0x22, 0x22, 0xD3, 0xE8))
        };
        cluster.Children.Add(glow);

        // Nœuds en orbite.
        var orbitRotate = new RotateTransform(0, 230, 230);
        var orbit = new Canvas { Width = 460, Height = 460, RenderTransform = orbitRotate };
        AddNode(orbit, 428, 220, Accent, 0.85);
        AddNode(orbit, 322, 400, Color.FromRgb(0x2B, 0xE0, 0xA6), 0.8);
        AddNode(orbit, 118, 400, Accent, 0.75);
        AddNode(orbit, 12, 220, AccentDim, 0.85);
        AddNode(orbit, 118, 40, Color.FromRgb(0x2B, 0xE0, 0xA6), 0.8);
        AddNode(orbit, 322, 40, Accent, 0.75);
        cluster.Children.Add(orbit);

        // Gros cœur cyan (même tracé que le dashboard).
        var heartScale = new ScaleTransform(1, 1);
        var heart = new Path
        {
            Data = Geometry.Parse("M75,128 C40,100 18,78 18,52 C18,33 33,20 50,20 C61,20 70,26 75,36 C80,26 89,20 100,20 C117,20 132,33 132,52 C132,78 110,100 75,128 Z"),
            Stretch = Stretch.Uniform,
            Width = 210, Height = 210,
            Fill = new SolidColorBrush(Color.FromRgb(0x11, 0x31, 0x4A)),
            Stroke = AccentBrush, StrokeThickness = 3,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.55),
            RenderTransform = heartScale,
            Effect = new DropShadowEffect { Color = Accent, BlurRadius = 40, ShadowDepth = 0, Opacity = 0.95 }
        };
        cluster.Children.Add(heart);

        // Point lumineux central.
        var dot = new Ellipse
        {
            Width = 44, Height = 44, Fill = AccentBrush,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { Color = Accent, BlurRadius = 44, ShadowDepth = 0, Opacity = 1 }
        };
        cluster.Children.Add(dot);

        Loaded += (_, _) =>
        {
            // Rotation des anneaux.
            ringRotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(18))) { RepeatBehavior = RepeatBehavior.Forever });
            orbitRotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(26))) { RepeatBehavior = RepeatBehavior.Forever });

            // Battement (deux pulsations rapprochées puis pause).
            var beat = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            beat.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.0))));
            beat.KeyFrames.Add(new EasingDoubleKeyFrame(1.16, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.14)), new CubicEase { EasingMode = EasingMode.EaseOut }));
            beat.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.30))));
            beat.KeyFrames.Add(new EasingDoubleKeyFrame(1.10, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.44)), new CubicEase { EasingMode = EasingMode.EaseOut }));
            beat.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.60))));
            beat.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.20))));
            heartScale.BeginAnimation(ScaleTransform.ScaleXProperty, beat);
            heartScale.BeginAnimation(ScaleTransform.ScaleYProperty, (DoubleAnimationUsingKeyFrames)beat.Clone());

            glow.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0.35, 0.9, new Duration(TimeSpan.FromSeconds(0.6))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        };

        return cluster;
    }

    private static void AddNode(Canvas canvas, double left, double top, Color color, double opacity)
    {
        var node = new Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(color), Opacity = opacity };
        Canvas.SetLeft(node, left);
        Canvas.SetTop(node, top);
        canvas.Children.Add(node);
    }

    /// <summary>Crée des éclairs cyan qui jaillissent du centre vers tout l'écran et clignotent au rythme du cœur.</summary>
    private void BuildLightning()
    {
        if (_boltCanvas is null) return;
        _boltCanvas.Children.Clear();

        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double cx = w / 2, cy = h / 2;
        double reach = Math.Sqrt(cx * cx + cy * cy);

        const int count = 14;
        for (int i = 0; i < count; i++)
        {
            double angle = (Math.PI * 2 * i / count) + (_rng.NextDouble() - 0.5) * 0.25;
            var bolt = MakeBolt(cx, cy, angle, reach);
            _boltCanvas.Children.Add(bolt);

            // Clignotement synchronisé (période ≈ battement) avec léger décalage.
            var flash = new DoubleAnimation(0.0, 0.9, new Duration(TimeSpan.FromSeconds(0.16)))
            {
                AutoReverse = true,
                BeginTime = TimeSpan.FromSeconds((i % 4) * 0.05),
                RepeatBehavior = RepeatBehavior.Forever
            };
            // Recrée un cycle de 1,2 s : flash bref puis pause.
            var keyed = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds((i % 5) * 0.04) };
            keyed.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.0))));
            keyed.KeyFrames.Add(new LinearDoubleKeyFrame(0.85, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.14))));
            keyed.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.34))));
            keyed.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.20))));
            bolt.BeginAnimation(OpacityProperty, keyed);
        }
    }

    private Polyline MakeBolt(double cx, double cy, double angle, double reach)
    {
        double dx = Math.Cos(angle), dy = Math.Sin(angle);
        double px = -dy, py = dx; // perpendiculaire
        const int steps = 9;
        var points = new PointCollection();
        // On démarre au bord du cœur (≈ 150 px) et on va jusqu'au bord de l'écran.
        for (int s = 0; s <= steps; s++)
        {
            double t = s / (double)steps;
            double dist = 150 + t * (reach - 150);
            double jitter = (s == 0 || s == steps) ? 0 : (_rng.NextDouble() - 0.5) * 60;
            double x = cx + dx * dist + px * jitter;
            double y = cy + dy * dist + py * jitter;
            points.Add(new Point(x, y));
        }

        return new Polyline
        {
            Points = points,
            Stroke = AccentBrush,
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Opacity = 0,
            Effect = new DropShadowEffect { Color = Accent, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.9 }
        };
    }

    private void OnAnyMouseMove(object sender, MouseEventArgs e)
    {
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
