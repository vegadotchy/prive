using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using IatechShield.Engine;
using IatechShield.Reputation;
using IatechShield.Update;
using IatechShield.Licensing;
using IatechShield.Ai;
using IatechShield.Tools;

namespace IatechShield.Gui;

public partial class MainWindow : Window
{
    private SignatureDatabase? _db;
    private Scanner? _scanner;
    private Quarantine? _quarantine;
    private RealtimeMonitor? _monitor;
    private RansomwareGuard? _ransomGuard;
    private readonly ProcessCuller _culler = new();
    private readonly RegistryGuard _registry = new();
    private readonly LicenseManager _license = new();
    private readonly CloudAccount _cloud = new();

    private bool _activated = true;
    /// <summary>Expiration de la licence active (null = à vie ou pas de licence) — pour le compte à rebours.</summary>
    private DateTimeOffset? _licenseExpiry;
    private bool _ready;
    private int _threatCount;
    private bool _scanning;
    /// <summary>Permet d'arrêter le scan en cours (bouton « Arrêter »).</summary>
    private CancellationTokenSource? _scanCts;
    /// <summary>Barrière de pause : ouverte = scan actif, fermée = en pause.</summary>
    private readonly System.Threading.ManualResetEventSlim _scanPauseGate = new(true);
    private bool _scanPaused;
    private readonly StringBuilder _log = new();
    private readonly ObservableCollection<ThreatItem> _threats = new();
    private readonly ObservableCollection<NetworkDeviceItem> _networkDevices = new();
    private readonly DeviceNameStore _deviceNames = new();
    private DispatcherTimer? _scheduleTimer;
    private List<QuarantineEntry> _quarantineEntries = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ScanResultsList.ItemsSource = _threats;
            NetworkList.ItemsSource = _networkDevices;
            InitEngine();
            SetupTray();
            StartLockWatcher();
            StartHeartbeat();
            StartCredentialBridge();
            StartMetricsTimer();
            LoadSystemInfo();
            var v = CurrentAppVersion;
            HeaderVersion.Text = $"{v.Major}.{v.Minor}.{v.Build}";
            _ready = true;
            ShowPage("Dashboard");
            RefreshDashboardKpis();
            SoundFx.ReactorStartup();   // démarrage « réacteur » à l'ouverture du dashboard
            _ = InitCloudAsync();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            _knownDrives = new HashSet<string>(GetReadyRemovableDrives(), StringComparer.OrdinalIgnoreCase);
            source.AddHook(WndProc);
            try { AddClipboardFormatListener(source.Handle); } catch { /* presse-papiers indisponible */ }
        }
    }

    // ------------------------------------------------------------- navigation -

    private void OnNav(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not RadioButton rb)
            return;
        ShowPage(rb.Content?.ToString() ?? "Dashboard");
    }

    private void ShowPage(string name)
    {
        if (PageDashboard is null)
            return;

        PageDashboard.Visibility = Visibility.Collapsed;
        PageProtection.Visibility = Visibility.Collapsed;
        PageScan.Visibility = Visibility.Collapsed;
        PageFirewall.Visibility = Visibility.Collapsed;
        PageTools.Visibility = Visibility.Collapsed;
        PageNetwork.Visibility = Visibility.Collapsed;
        PageRadar.Visibility = Visibility.Collapsed;
        PageVpn.Visibility = Visibility.Collapsed;
        PageVault.Visibility = Visibility.Collapsed;
        PageCentre.Visibility = Visibility.Collapsed;
        PageIntegrity.Visibility = Visibility.Collapsed;
        PageTwin.Visibility = Visibility.Collapsed;
        PageInvestigation.Visibility = Visibility.Collapsed;
        PageCopilot.Visibility = Visibility.Collapsed;
        PageDevice.Visibility = Visibility.Collapsed;
        PageSystem.Visibility = Visibility.Collapsed;
        PageProcesses.Visibility = Visibility.Collapsed;
        PageWorld.Visibility = Visibility.Collapsed;
        PageDna.Visibility = Visibility.Collapsed;
        PageThreatRadar.Visibility = Visibility.Collapsed;
        PageSettings.Visibility = Visibility.Collapsed;
        PageLogs.Visibility = Visibility.Collapsed;

        Grid page = name switch
        {
            "Protection" => PageProtection,
            "Scan" => PageScan,
            "Firewall" => PageFirewall,
            "Outils" => PageTools,
            "Réseau" => PageNetwork,
            "Radar" => PageRadar,
            "VPN" => PageVpn,
            "Coffre-fort" => PageVault,
            "Centre" => PageCentre,
            "Intégrité" => PageIntegrity,
            "Jumeau" => PageTwin,
            "Investigation" => PageInvestigation,
            "Copilote" => PageCopilot,
            "Appareil" => PageDevice,
            "Système" => PageSystem,
            "Processus" => PageProcesses,
            "Mondiale" => PageWorld,
            "ADN" => PageDna,
            "Menaces" => PageThreatRadar,
            "Settings" => PageSettings,
            "Logs" => PageLogs,
            _ => PageDashboard
        };
        page.Visibility = Visibility.Visible;

        // Dès qu'on quitte le coffre-fort, on le re-verrouille : le mot de passe
        // sera redemandé à chaque retour.
        if (page != PageVault)
            _vaultUnlocked = false;

        ApplyTabAccent(name);
        AnimatePageIn(page);

        if (page == PageSettings)
        {
            ApiKeyStatusText.Text = AiAssistant.IsConfigured
                ? "✓ Clé Anthropic configurée — assistant IA actif."
                : "Aucune clé. Collez votre clé Anthropic ci-dessous pour activer l'assistant IA.";
            if (AppVersionText is not null)
                AppVersionText.Text = $"Version installée : {CurrentAppVersion}";
            if (TamperStatus is not null)
                TamperStatus.Text = TamperEnabled ? "Protection par mot de passe activée." : "Aucun mot de passe défini.";
            LoadLockSettings();
            OnRefreshQuarantine(this, new RoutedEventArgs());
        }
        else if (page == PageSystem)
        {
            AccountUserText.Text = $"Connecté en tant que : {Environment.UserName}";
            OnRefreshPerf(this, new RoutedEventArgs());
        }
        else if (page == PageVpn)
        {
            LoadVpnSettings();
        }
        else if (page == PageVault)
        {
            if (!EnsureVaultUnlocked())
            {
                // Mauvais mot de passe / annulé : on renvoie au dashboard, coffre masqué.
                if (NavDashboard is not null) NavDashboard.IsChecked = true;
                return;
            }
            LoadVault();
        }
        else if (page == PageCopilot)
        {
            EnsureChatLoaded();
        }
        else if (page == PageInvestigation)
        {
            OnRefreshInvestigation(this, new RoutedEventArgs());
        }
        else if (page == PageDashboard)
        {
            RefreshDashboardKpis();
        }
        else if (page == PageProcesses)
        {
            BuildProcessGraph();
        }
        else if (page == PageWorld)
        {
            _ = BuildWorldMapAsync();
        }
        else if (page == PageDna)
        {
            _ = BuildDnaAsync();
        }
        else if (page == PageThreatRadar)
        {
            BuildThreatRadar();
        }
    }

    // ----------------------------------------------- Radar de menaces ----------

    /// <summary>
    /// Radar circulaire HUD : cercles concentriques, ligne de balayage qui tourne en
    /// continu, et un blip par menace détectée / incident. Vert = secteur sûr.
    /// </summary>
    private void BuildThreatRadar()
    {
        if (ThreatRadarCanvas is null) return;
        if (ThreatRadarCanvas.ActualWidth <= 1)
        {
            Dispatcher.BeginInvoke(new Action(BuildThreatRadar),
                System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }
        ThreatRadarCanvas.Children.Clear();

        double w = ThreatRadarCanvas.ActualWidth, h = ThreatRadarCanvas.ActualHeight;
        double cx = w / 2, cy = h / 2;
        double r = Math.Min(w, h) / 2 - 20;
        var accent = Color.FromRgb(0x22, 0xD3, 0xE8);
        var red = Color.FromRgb(0xEF, 0x44, 0x44);

        // Cercles concentriques + croix.
        for (int k = 1; k <= 4; k++)
        {
            double rr = r * k / 4;
            var ring = new System.Windows.Shapes.Ellipse
            {
                Width = rr * 2, Height = rr * 2,
                Stroke = new SolidColorBrush(Color.FromArgb(0x44, accent.R, accent.G, accent.B)),
                StrokeThickness = 1
            };
            Canvas.SetLeft(ring, cx - rr); Canvas.SetTop(ring, cy - rr);
            ThreatRadarCanvas.Children.Add(ring);
        }
        ThreatRadarCanvas.Children.Add(new System.Windows.Shapes.Line
        { X1 = cx - r, Y1 = cy, X2 = cx + r, Y2 = cy, Stroke = new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B)), StrokeThickness = 0.7 });
        ThreatRadarCanvas.Children.Add(new System.Windows.Shapes.Line
        { X1 = cx, Y1 = cy - r, X2 = cx, Y2 = cy + r, Stroke = new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B)), StrokeThickness = 0.7 });

        // Secteur de balayage (wedge) qui tourne en continu.
        var sweep = new System.Windows.Shapes.Path
        {
            Fill = new RadialGradientBrush(Color.FromArgb(0x55, accent.R, accent.G, accent.B), Colors.Transparent)
            { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 },
            Data = SweepWedge(cx, cy, r),
            RenderTransformOrigin = new Point(0, 0)
        };
        var rot = new RotateTransform(0, cx, cy);
        sweep.RenderTransform = rot;
        ThreatRadarCanvas.Children.Add(sweep);
        rot.BeginAnimation(RotateTransform.AngleProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(3.5))
            { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });

        // Centre.
        var core = new System.Windows.Shapes.Ellipse
        {
            Width = 12, Height = 12, Fill = new SolidColorBrush(accent),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = accent, BlurRadius = 16, ShadowDepth = 0 }
        };
        Canvas.SetLeft(core, cx - 6); Canvas.SetTop(core, cy - 6);
        ThreatRadarCanvas.Children.Add(core);

        // Blips : une menace détectée = un point rouge.
        int count = _threats.Count;
        for (int i = 0; i < count; i++)
        {
            double ang = 2 * Math.PI * i / Math.Max(1, count) + (i * 0.7);
            double dist = r * (0.35 + 0.55 * ((i % 3) / 2.0));
            double bx = cx + dist * Math.Cos(ang), by = cy + dist * Math.Sin(ang);
            var blip = new System.Windows.Shapes.Ellipse
            {
                Width = 12, Height = 12, Fill = new SolidColorBrush(red),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = red, BlurRadius = 14, ShadowDepth = 0 },
                ToolTip = i < _threats.Count ? $"{_threats[i].Name}\n{_threats[i].Path}" : "Menace"
            };
            Canvas.SetLeft(blip, bx - 6); Canvas.SetTop(blip, by - 6);
            // Pulsation du blip.
            var pulse = new System.Windows.Media.Animation.DoubleAnimation(1, 0.3, TimeSpan.FromSeconds(0.8))
            { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
            blip.BeginAnimation(OpacityProperty, pulse);
            ThreatRadarCanvas.Children.Add(blip);
        }

        ThreatRadarStatus.Text = count == 0
            ? "✓ Secteur sûr — aucune menace active."
            : $"⚠ {count} menace(s) détectée(s) — voir l'onglet Scan pour agir.";
    }

    private static Geometry SweepWedge(double cx, double cy, double r)
    {
        // Un secteur de ~50° partant du centre vers la droite.
        double a0 = -0.45, a1 = 0.0;
        var p0 = new Point(cx, cy);
        var p1 = new Point(cx + r * Math.Cos(a0), cy + r * Math.Sin(a0));
        var p2 = new Point(cx + r * Math.Cos(a1), cy + r * Math.Sin(a1));
        var fig = new PathFigure { StartPoint = p0, IsClosed = true };
        fig.Segments.Add(new LineSegment(p1, true));
        fig.Segments.Add(new ArcSegment(p2, new Size(r, r), 0, false, SweepDirection.Clockwise, true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        return geo;
    }

    // ----------------------------------------------- ADN / indice de confiance -

    private void OnRefreshDna(object sender, RoutedEventArgs e) => _ = BuildDnaAsync();

    /// <summary>Construit le profil de confiance de chaque programme au démarrage.</summary>
    private async Task BuildDnaAsync()
    {
        if (DnaList is null) return;
        DnaStatus.Text = "Analyse des programmes…";
        DnaRefreshButton.IsEnabled = false;
        try
        {
            var items = await Task.Run(() =>
            {
                var list = new List<DnaItem>();
                foreach (var entry in _registry.ListAutoRuns())
                {
                    string exe = ExtractExePath(entry.Command);
                    var prof = TrustIndex.Evaluate(entry.Name, exe, autostart: true);
                    var color = ScoreColor(prof.Score);
                    list.Add(new DnaItem
                    {
                        Name = string.IsNullOrWhiteSpace(prof.Name) ? System.IO.Path.GetFileName(exe) : prof.Name,
                        Publisher = $"{prof.Signature} · {prof.Publisher}",
                        Line = $"{prof.Location} · âge : {prof.Age} · {prof.Behavior}\n{exe}",
                        Score = prof.Score,
                        ScoreText = prof.Score + "%",
                        ScoreColor = new SolidColorBrush(color)
                    });
                }
                return list.OrderBy(i => i.Score).ToList();
            });

            DnaList.ItemsSource = items;
            int risky = items.Count(i => i.Score < 50);
            DnaStatus.Text = $"{items.Count} programme(s) au démarrage · {risky} à surveiller.";
        }
        catch (Exception ex)
        {
            DnaStatus.Text = $"Erreur : {ex.Message}";
        }
        finally
        {
            DnaRefreshButton.IsEnabled = true;
        }
    }

    // ----------------------------------------------- Carte mondiale ------------

    private void OnRefreshWorld(object sender, RoutedEventArgs e) => _ = BuildWorldMapAsync();

    /// <summary>
    /// Dessine une carte mondiale HUD : graticule lat/lon, position locale, et arcs
    /// lumineux vers chaque serveur distant réellement contacté (connexions TCP géolocalisées).
    /// </summary>
    private async Task BuildWorldMapAsync()
    {
        if (WorldCanvas is null) return;
        if (WorldCanvas.ActualWidth <= 1)
        {
            Dispatcher.BeginInvoke(new Action(() => _ = BuildWorldMapAsync()),
                System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }

        WorldStatus.Text = "Géolocalisation des connexions…";
        WorldRefreshButton.IsEnabled = false;
        WorldMapData data;
        try { data = await WorldConnections.GetAsync(); }
        catch { data = new WorldMapData(null, Array.Empty<GeoConnection>()); }
        finally { WorldRefreshButton.IsEnabled = true; }

        double w = WorldCanvas.ActualWidth, h = WorldCanvas.ActualHeight;
        WorldCanvas.Children.Clear();
        DrawGraticule(w, h);

        var accent = Color.FromRgb(0x22, 0xD3, 0xE8);
        var red = Color.FromRgb(0xEF, 0x44, 0x44);

        (double X, double Y) Project(double lat, double lon)
            => ((lon + 180.0) / 360.0 * w, (90.0 - lat) / 180.0 * h);

        // Position locale.
        double hx = w / 2, hy = h / 2;
        if (data.Home is { } home)
        {
            var hp = Project(home.Lat, home.Lon);
            hx = hp.X; hy = hp.Y;
        }

        // Arcs + points distants.
        foreach (var c in data.Connections)
        {
            var p = Project(c.Lat, c.Lon);
            bool many = c.Count >= 4;
            var col = many ? red : accent;

            var arc = new System.Windows.Shapes.Path
            {
                Stroke = new SolidColorBrush(Color.FromArgb(0xAA, col.R, col.G, col.B)),
                StrokeThickness = 1.2,
                Data = ArcGeometry(hx, hy, p.X, p.Y),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                { Color = col, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.7 }
            };
            WorldCanvas.Children.Add(arc);

            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 9, Height = 9, Fill = new SolidColorBrush(col),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                { Color = col, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.9 },
                ToolTip = $"{c.City} ({c.Country}) — {c.Ip} · {c.Count} connexion(s)"
            };
            Canvas.SetLeft(dot, p.X - 4.5);
            Canvas.SetTop(dot, p.Y - 4.5);
            WorldCanvas.Children.Add(dot);
        }

        // Nœud local (au-dessus).
        var homeDot = new System.Windows.Shapes.Ellipse
        {
            Width = 16, Height = 16, Fill = new SolidColorBrush(Color.FromRgb(0x2B, 0xE0, 0xA6)),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            { Color = Color.FromRgb(0x2B, 0xE0, 0xA6), BlurRadius = 20, ShadowDepth = 0, Opacity = 1 },
            ToolTip = data.Home is { } hh ? $"Vous : {hh.City} ({hh.Country})" : "Position locale"
        };
        Canvas.SetLeft(homeDot, hx - 8);
        Canvas.SetTop(homeDot, hy - 8);
        WorldCanvas.Children.Add(homeDot);

        int risky = data.Connections.Count(c => c.Count >= 4);
        WorldStatus.Text = data.Connections.Count == 0
            ? "Aucune connexion externe géolocalisée (ou hors-ligne)."
            : $"{data.Connections.Count} serveur(s) distant(s) · {data.Connections.Select(c => c.Country).Distinct().Count()} pays.";
    }

    private void DrawGraticule(double w, double h)
    {
        var grid = new SolidColorBrush(Color.FromArgb(0x33, 0x22, 0xD3, 0xE8));
        for (int lon = -180; lon <= 180; lon += 30)
        {
            double x = (lon + 180.0) / 360.0 * w;
            WorldCanvas.Children.Add(new System.Windows.Shapes.Line
            { X1 = x, Y1 = 0, X2 = x, Y2 = h, Stroke = grid, StrokeThickness = 0.5 });
        }
        for (int lat = -90; lat <= 90; lat += 30)
        {
            double y = (90.0 - lat) / 180.0 * h;
            WorldCanvas.Children.Add(new System.Windows.Shapes.Line
            { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = grid, StrokeThickness = 0.5 });
        }
    }

    /// <summary>Arc courbé (quadratique) entre deux points pour un effet « réseau ».</summary>
    private static Geometry ArcGeometry(double x1, double y1, double x2, double y2)
    {
        double mx = (x1 + x2) / 2, my = (y1 + y2) / 2;
        double dx = x2 - x1, dy = y2 - y1;
        double len = Math.Sqrt(dx * dx + dy * dy);
        // Point de contrôle décalé perpendiculairement (courbure proportionnelle).
        double off = Math.Min(120, len * 0.25);
        double cx = mx - dy / (len == 0 ? 1 : len) * off;
        double cy = my + dx / (len == 0 ? 1 : len) * off;
        var fig = new PathFigure { StartPoint = new Point(x1, y1) };
        fig.Segments.Add(new QuadraticBezierSegment(new Point(cx, cy), new Point(x2, y2), true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        return geo;
    }

    // ----------------------------------------------- Vue système / processus ---

    private void OnRefreshProcesses(object sender, RoutedEventArgs e) => BuildProcessGraph();

    /// <summary>
    /// Dessine les processus en cours comme des nœuds reliés à un hub central « PC ».
    /// Taille selon la mémoire, couleur selon la confiance (signé/connu = cyan/vert,
    /// inconnu/non signé = rouge). Clic = détails. 100 % données réelles.
    /// </summary>
    private void BuildProcessGraph()
    {
        if (ProcCanvas is null) return;
        // Au premier affichage, le Canvas n'est pas encore mesuré : on redessine après layout.
        if (ProcCanvas.ActualWidth <= 1)
        {
            Dispatcher.BeginInvoke(new Action(BuildProcessGraph),
                System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }
        ProcCanvas.Children.Clear();

        // Top processus par mémoire (les plus significatifs).
        var procs = System.Diagnostics.Process.GetProcesses()
            .Select(p =>
            {
                try { return (Name: p.ProcessName, Mem: p.WorkingSet64, Id: p.Id); }
                catch { return (Name: "", Mem: 0L, Id: 0); }
            })
            .Where(t => t.Mem > 0 && !string.IsNullOrEmpty(t.Name))
            .GroupBy(t => t.Name)
            .Select(g => (Name: g.Key, Mem: g.Sum(x => x.Mem), Count: g.Count(), Id: g.First().Id))
            .OrderByDescending(t => t.Mem)
            .Take(16)
            .ToList();

        double w = ProcCanvas.ActualWidth > 0 ? ProcCanvas.ActualWidth : 760;
        double h = ProcCanvas.ActualHeight > 0 ? ProcCanvas.ActualHeight : 520;
        double cx = w / 2, cy = h / 2;
        double radius = Math.Min(w, h) / 2 - 80;

        var accent = Color.FromRgb(0x22, 0xD3, 0xE8);
        var green = Color.FromRgb(0x2B, 0xE0, 0xA6);
        var red = Color.FromRgb(0xEF, 0x44, 0x44);

        // Liens hub → nœuds (dessinés d'abord, derrière).
        for (int i = 0; i < procs.Count; i++)
        {
            double angle = 2 * Math.PI * i / Math.Max(1, procs.Count);
            double nx = cx + radius * Math.Cos(angle);
            double ny = cy + radius * Math.Sin(angle);
            var line = new System.Windows.Shapes.Line
            {
                X1 = cx, Y1 = cy, X2 = nx, Y2 = ny,
                Stroke = new SolidColorBrush(Color.FromArgb(0x55, 0x22, 0xD3, 0xE8)),
                StrokeThickness = 1
            };
            ProcCanvas.Children.Add(line);
        }

        // Hub central « PC ».
        AddProcNode(cx, cy, 64, accent, "PC", isHub: true, onClick: null);

        for (int i = 0; i < procs.Count; i++)
        {
            var p = procs[i];
            double angle = 2 * Math.PI * i / Math.Max(1, procs.Count);
            double nx = cx + radius * Math.Cos(angle);
            double ny = cy + radius * Math.Sin(angle);

            bool trusted = IsLikelyTrusted(p.Name);
            Color c = trusted ? (i % 3 == 0 ? green : accent) : red;
            double size = 26 + Math.Min(28, p.Mem / (120L * 1024 * 1024)); // mémoire → taille
            double memMb = p.Mem / (1024.0 * 1024.0);

            string detail =
                $"🧩 {p.Name}\n" +
                $"PID : {p.Id}\n" +
                $"Instances : {p.Count}\n" +
                $"Mémoire : {memMb:0} Mo\n" +
                $"Confiance : {(trusted ? "✓ Connu / signé" : "⚠ Non reconnu")}";

            AddProcNode(nx, ny, size, c, p.Name, isHub: false, onClick: () =>
            {
                ProcDetail.Text = detail;
                ProcDetail.Foreground = new SolidColorBrush(trusted
                    ? Color.FromRgb(0xE8, 0xF6, 0xFB) : red);
            });
        }

        int suspicious = procs.Count(p => !IsLikelyTrusted(p.Name));
        ProcStatus.Text = $"{procs.Count} processus majeurs · {suspicious} non reconnu(s).";
    }

    private void AddProcNode(double x, double y, double size, Color color, string label, bool isHub, Action? onClick)
    {
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = size, Height = size,
            Fill = new SolidColorBrush(Color.FromArgb(isHub ? (byte)0x44 : (byte)0x33, color.R, color.G, color.B)),
            Stroke = new SolidColorBrush(color),
            StrokeThickness = isHub ? 2.5 : 1.6,
            Cursor = onClick is null ? Cursors.Arrow : Cursors.Hand,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            { Color = color, BlurRadius = isHub ? 30 : 14, ShadowDepth = 0, Opacity = 0.8 }
        };
        Canvas.SetLeft(dot, x - size / 2);
        Canvas.SetTop(dot, y - size / 2);
        if (onClick is not null) dot.MouseLeftButtonUp += (_, _) => onClick();
        ProcCanvas.Children.Add(dot);

        var text = new TextBlock
        {
            Text = isHub ? "PC" : (label.Length > 12 ? label[..12] : label),
            Foreground = new SolidColorBrush(isHub ? color : Color.FromRgb(0xC8, 0xDC, 0xEA)),
            FontSize = isHub ? 14 : 10,
            FontWeight = isHub ? FontWeights.Bold : FontWeights.Normal,
            TextAlignment = TextAlignment.Center,
            Width = 90
        };
        Canvas.SetLeft(text, x - 45);
        Canvas.SetTop(text, y + size / 2 + 2);
        ProcCanvas.Children.Add(text);
    }

    /// <summary>Heuristique simple : processus Windows/éditeurs connus = de confiance.</summary>
    private static bool IsLikelyTrusted(string name)
    {
        string n = name.ToLowerInvariant();
        string[] known =
        {
            "system", "idle", "svchost", "explorer", "csrss", "wininit", "winlogon", "services",
            "lsass", "smss", "dwm", "fontdrvhost", "taskhostw", "runtimebroker", "sihost",
            "ctfmon", "searchindexer", "spoolsv", "conhost", "registry", "memcompression",
            "iatech-shield-gui", "iatech-shield", "msmpeng", "securityhealthservice", "audiodg",
            "chrome", "msedge", "firefox", "code", "devenv", "explorer", "powershell", "pwsh",
            "teams", "outlook", "winword", "excel", "onedrive", "discord", "steam", "nvcontainer"
        };
        return known.Any(k => n == k || n.StartsWith(k));
    }

    // Couleur d'accent propre à chaque onglet.
    private static readonly Dictionary<string, Color> TabAccents = new()
    {
        ["Dashboard"]  = Color.FromRgb(0x22, 0xD3, 0xE8), // cyan
        ["Protection"] = Color.FromRgb(0x34, 0xD3, 0x99), // vert
        ["Scan"]       = Color.FromRgb(0x3B, 0x82, 0xF6), // bleu
        ["Firewall"]   = Color.FromRgb(0xF5, 0x9E, 0x0B), // ambre
        ["Outils"]     = Color.FromRgb(0xA7, 0x8B, 0xFA), // violet
        ["Réseau"]     = Color.FromRgb(0x2D, 0xD4, 0xBF), // turquoise
        ["VPN"]        = Color.FromRgb(0x10, 0xB9, 0x81), // vert émeraude
        ["Radar"]      = Color.FromRgb(0x06, 0xB6, 0xD4), // cyan radar
        ["Coffre-fort"] = Color.FromRgb(0xFB, 0xBF, 0x24), // or
        ["Centre"]     = Color.FromRgb(0xEF, 0x44, 0x44), // rouge sécurité
        ["Intégrité"]  = Color.FromRgb(0x34, 0xD3, 0x99), // vert santé
        ["Jumeau"]     = Color.FromRgb(0x38, 0xBD, 0xF8), // bleu clair jumeau
        ["Investigation"] = Color.FromRgb(0xF4, 0x72, 0xB6), // rose investigation
        ["Copilote"]   = Color.FromRgb(0x8B, 0x5C, 0xF6), // violet IA
        ["Appareil"]   = Color.FromRgb(0xEC, 0x48, 0x99), // rose
        ["Système"]    = Color.FromRgb(0x60, 0xA5, 0xFA), // bleu clair
        ["Settings"]   = Color.FromRgb(0x94, 0xA3, 0xB8), // gris-bleu
        ["Logs"]       = Color.FromRgb(0x64, 0x74, 0x8B), // ardoise
    };

    private void ApplyTabAccent(string name)
    {
        if (AccentStrip is null) return;
        Color target = TabAccents.TryGetValue(name, out var c) ? c : TabAccents["Dashboard"];

        var brush = AccentStrip.Background as SolidColorBrush;
        if (brush is null || brush.IsFrozen)
        {
            brush = new SolidColorBrush((AccentStrip.Background as SolidColorBrush)?.Color ?? target);
            AccentStrip.Background = brush;
        }
        brush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(target, TimeSpan.FromMilliseconds(280)) { EasingFunction = new QuadraticEase() });
    }

    // ----------------------------------------- CPU / RAM temps réel + infos ---

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct FileTimeRaw { public uint Low; public uint High; }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTimeRaw idle, out FileTimeRaw kernel, out FileTimeRaw user);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private sealed class MemoryStatusEx
    {
        public uint dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MemoryStatusEx));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile;
        public ulong ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] MemoryStatusEx mem);

    private DispatcherTimer? _metricsTimer;
    private ulong _prevIdle, _prevKernel, _prevUser;
    private bool _sysInfoLoaded;

    private void StartMetricsTimer()
    {
        _metricsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _metricsTimer.Tick += (_, _) => { UpdateMetrics(); UpdateLicenseCountdown(); };
        _metricsTimer.Start();
        UpdateMetrics();
    }

    private static ulong Ft(FileTimeRaw f) => ((ulong)f.High << 32) | f.Low;

    private void UpdateMetrics()
    {
        try
        {
            if (GetSystemTimes(out var idle, out var kern, out var usr))
            {
                ulong i = Ft(idle), k = Ft(kern), u = Ft(usr);
                if (_prevKernel > 0 || _prevUser > 0)
                {
                    ulong total = (k - _prevKernel) + (u - _prevUser);
                    ulong idleDelta = i - _prevIdle;
                    double cpu = total > 0 ? Math.Clamp((1.0 - (double)idleDelta / total) * 100.0, 0, 100) : 0;
                    CpuPercent.Text = $"{cpu:0}%";
                    CpuBar.Value = cpu;
                    CpuPercent.Foreground = new SolidColorBrush(cpu < 70 ? Color.FromRgb(0x34, 0xD3, 0x99)
                        : cpu < 90 ? Color.FromRgb(0xF5, 0x9E, 0x0B) : Color.FromRgb(0xEF, 0x44, 0x44));
                }
                _prevIdle = i; _prevKernel = k; _prevUser = u;
            }

            var mem = new MemoryStatusEx();
            if (GlobalMemoryStatusEx(mem) && mem.ullTotalPhys > 0)
            {
                double load = mem.dwMemoryLoad;
                RamPercent.Text = $"{load:0}%";
                RamBar.Value = load;
                double totalGb = mem.ullTotalPhys / 1073741824.0;
                double usedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / 1073741824.0;
                RamDetail.Text = $"{usedGb:0.0} / {totalGb:0.0} Go";
                RamPercent.Foreground = new SolidColorBrush(load < 75 ? Color.FromRgb(0x22, 0xD3, 0xE8)
                    : load < 90 ? Color.FromRgb(0xF5, 0x9E, 0x0B) : Color.FromRgb(0xEF, 0x44, 0x44));
            }
        }
        catch { /* compteurs indisponibles */ }
    }

    private async void LoadSystemInfo()
    {
        if (_sysInfoLoaded) return;
        _sysInfoLoaded = true;
        try
        {
            var info = await SystemInfo.GatherAsync();
            SysOs.Text = $"Windows : {info.Os} — {info.OsVersion}";
            SysCpu.Text = $"Processeur : {info.Cpu} ({info.CpuCores})";
            SysRam.Text = $"Mémoire : {info.RamTotal}";
            SysGpu.Text = $"Carte graphique : {info.Gpu}";
            SysMachine.Text = $"Machine : {info.Machine}";
            SysInstall.Text = $"Windows installé le : {info.InstallDate}";
            SysDisks.ItemsSource = info.Disks;
        }
        catch (Exception ex) { SysOs.Text = $"Infos système indisponibles : {ex.Message}"; }
    }

    /// <summary>Met à jour les indicateurs animés du tableau de bord (comptage progressif).</summary>
    private void RefreshDashboardKpis()
    {
        if (KpiThreats is null) return;
        AnimateCount(KpiThreats, _threats.Count);
        AnimateCount(KpiSignatures, _db?.Signatures.Count ?? 0);
        try { AnimateCount(KpiDevices, LoadRadarKnown().Count); } catch { }

        bool rt = _monitor is not null;
        KpiRealtime.Text = rt ? "ACTIF" : "INACTIF";
        KpiRealtime.Foreground = new SolidColorBrush(rt ? Color.FromRgb(0x34, 0xD3, 0x99) : Color.FromRgb(0x94, 0xA3, 0xB8));

        _ = RefreshScoreKpiAsync();
    }

    private async Task RefreshScoreKpiAsync()
    {
        try
        {
            var sec = await SecurityScore.EvaluateAsync(_monitor is not null, TamperEnabled);
            AnimateCount(KpiScore, sec.Score);
            KpiScore.Foreground = new SolidColorBrush(ScoreColor(sec.Score));
        }
        catch { /* score indisponible */ }
    }

    /// <summary>Anime un compteur de 0 à la valeur cible (effet « count-up »).</summary>
    private static void AnimateCount(TextBlock target, int value)
    {
        const int frames = 28;
        int i = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(22) };
        timer.Tick += (_, _) =>
        {
            i++;
            double t = i / (double)frames;
            target.Text = ((int)Math.Round(value * (1 - Math.Pow(1 - t, 3)))).ToString(); // easeOutCubic
            if (i >= frames) { target.Text = value.ToString(); timer.Stop(); }
        };
        timer.Start();
    }

    /// <summary>Fait battre le cœur central du tableau de bord (rythme « lub-dub » réaliste).</summary>
    private void StartHeartbeat()
    {
        // Un cycle d'environ 1,15 s : forte contraction, légère reprise, puis repos.
        var beat = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        beat.KeyFrames.Add(new EasingDoubleKeyFrame(1.00, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0)), ease));
        beat.KeyFrames.Add(new EasingDoubleKeyFrame(1.14, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140)), ease)); // lub
        beat.KeyFrames.Add(new EasingDoubleKeyFrame(1.00, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300)), ease));
        beat.KeyFrames.Add(new EasingDoubleKeyFrame(1.09, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(430)), ease)); // dub
        beat.KeyFrames.Add(new EasingDoubleKeyFrame(1.00, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(580)), ease));
        beat.KeyFrames.Add(new EasingDoubleKeyFrame(1.00, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1150)), ease)); // repos

        HeartScale.BeginAnimation(ScaleTransform.ScaleXProperty, beat);
        HeartScale.BeginAnimation(ScaleTransform.ScaleYProperty, beat);
        CoreDotScale.BeginAnimation(ScaleTransform.ScaleXProperty, beat);
        CoreDotScale.BeginAnimation(ScaleTransform.ScaleYProperty, beat);

        // Le halo enfle un peu plus fort et son intensité pulse.
        var halo = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        halo.KeyFrames.Add(new EasingDoubleKeyFrame(1.00, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0)), ease));
        halo.KeyFrames.Add(new EasingDoubleKeyFrame(1.22, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160)), ease));
        halo.KeyFrames.Add(new EasingDoubleKeyFrame(1.00, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)), ease));
        halo.KeyFrames.Add(new EasingDoubleKeyFrame(1.00, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1150)), ease));
        GlowScale.BeginAnimation(ScaleTransform.ScaleXProperty, halo);
        GlowScale.BeginAnimation(ScaleTransform.ScaleYProperty, halo);

        var glowOpacity = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        glowOpacity.KeyFrames.Add(new EasingDoubleKeyFrame(0.45, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0)), ease));
        glowOpacity.KeyFrames.Add(new EasingDoubleKeyFrame(1.00, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150)), ease));
        glowOpacity.KeyFrames.Add(new EasingDoubleKeyFrame(0.45, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(450)), ease));
        glowOpacity.KeyFrames.Add(new EasingDoubleKeyFrame(0.45, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1150)), ease));
        HeartGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, glowOpacity);
    }

    private static void AnimatePageIn(UIElement page)
    {
        var translate = new TranslateTransform();
        page.RenderTransform = translate;

        page.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new QuadraticEase() });
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    // ----------------------------------------------- mise à jour de l'app ---

    private ReleaseInfo? _pendingUpdate;

    private static Version CurrentAppVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0);

    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        if (CheckUpdateButton is null) return;
        CheckUpdateButton.IsEnabled = false;
        InstallUpdateButton.Visibility = Visibility.Collapsed;
        AppUpdateStatus.Text = "Recherche d'une mise à jour…";

        try
        {
            var check = await AppUpdater.CheckAsync(CurrentAppVersion);
            if (check.Error is not null && check.Latest is null)
            {
                AppUpdateStatus.Text = $"Impossible de vérifier : {check.Error}";
            }
            else if (check.UpdateAvailable && check.Latest is not null)
            {
                _pendingUpdate = check.Latest;
                AppUpdateStatus.Text = $"Nouvelle version disponible : {check.Latest.Name} (vous avez {CurrentAppVersion}).";
                InstallUpdateButton.Visibility = check.Latest.InstallerUrl is not null
                    ? Visibility.Visible : Visibility.Collapsed;
                Notify("Mise à jour disponible", $"IATECH-SHIELD {check.Latest.Tag} est disponible.", "Settings");
            }
            else
            {
                AppUpdateStatus.Text = $"Vous avez déjà la dernière version ({CurrentAppVersion}).";
            }
        }
        catch (Exception ex)
        {
            AppUpdateStatus.Text = $"Erreur : {ex.Message}";
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private async void OnInstallUpdate(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate?.InstallerUrl is null) return;

        InstallUpdateButton.IsEnabled = false;
        CheckUpdateButton.IsEnabled = false;
        AppUpdateProgress.Visibility = Visibility.Visible;
        AppUpdateProgress.Value = 0;
        AppUpdateStatus.Text = "Téléchargement de l'installateur…";

        try
        {
            string fileName = $"IatechShield-Setup-{_pendingUpdate.Tag}.exe";
            var progress = new Progress<int>(p => AppUpdateProgress.Value = p);
            string path = await AppUpdater.DownloadAsync(_pendingUpdate.InstallerUrl, fileName, progress);

            AppUpdateStatus.Text = "Téléchargement terminé. Lancement de l'installateur…";
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

            // L'installateur prend le relais : on quitte proprement l'application.
            _reallyExit = true;
            Close();
        }
        catch (Exception ex)
        {
            AppUpdateStatus.Text = $"Échec du téléchargement : {ex.Message}";
            InstallUpdateButton.IsEnabled = true;
            CheckUpdateButton.IsEnabled = true;
        }
        finally
        {
            AppUpdateProgress.Visibility = Visibility.Collapsed;
        }
    }

    // ------------------------------------------------------------------ VPN ---

    private readonly VpnManager _vpn = new();
    private bool _vpnLoaded;
    private bool _vpnProfilesLoading;
    private bool _vpnConnected;

    private const string VpnProfilesKey = "vpn_profiles";

    private void LoadVpnSettings()
    {
        if (_vpnLoaded) return;
        _vpnLoaded = true;
        RefreshVpnProfileBox(null);
        ApplyProfileMap(SecretVault.Load("vpn")); // dernier profil utilisé
        UpdatePskVisibility();
    }

    private void ApplyProfileMap(IReadOnlyDictionary<string, string> s)
    {
        if (s.TryGetValue("server", out var srv)) VpnServer.Text = srv;
        if (s.TryGetValue("user", out var usr)) VpnUser.Text = usr;
        if (s.TryGetValue("password", out var pwd)) VpnPassword.Password = pwd;
        if (s.TryGetValue("psk", out var psk)) VpnPsk.Password = psk;
        if (s.TryGetValue("type", out var t) && int.TryParse(t, out int ti) && ti >= 0 && ti < VpnType.Items.Count)
            VpnType.SelectedIndex = ti;
    }

    private Dictionary<string, string> CurrentProfileMap() => new()
    {
        ["server"] = VpnServer.Text.Trim(),
        ["user"] = VpnUser.Text.Trim(),
        ["password"] = VpnPassword.Password,
        ["psk"] = VpnPsk.Password,
        ["type"] = VpnType.SelectedIndex.ToString()
    };

    // --- Profils nommés (plusieurs serveurs VPN) ---

    private static Dictionary<string, Dictionary<string, string>> LoadVpnProfiles()
    {
        var raw = SecretVault.Load(VpnProfilesKey);
        var result = new Dictionary<string, Dictionary<string, string>>();
        foreach (var (name, json) in raw)
        {
            try
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (map is not null) result[name] = map;
            }
            catch { /* entrée corrompue ignorée */ }
        }
        return result;
    }

    private static void SaveVpnProfiles(Dictionary<string, Dictionary<string, string>> profiles)
    {
        var raw = new Dictionary<string, string>();
        foreach (var (name, map) in profiles)
            raw[name] = JsonSerializer.Serialize(map);
        SecretVault.Save(VpnProfilesKey, raw);
    }

    private void RefreshVpnProfileBox(string? select)
    {
        _vpnProfilesLoading = true;
        string current = select ?? (VpnProfileBox.Text ?? "");
        VpnProfileBox.Items.Clear();
        foreach (var name in LoadVpnProfiles().Keys.OrderBy(n => n))
            VpnProfileBox.Items.Add(name);
        VpnProfileBox.Text = current;
        _vpnProfilesLoading = false;
    }

    private void OnVpnProfileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_vpnProfilesLoading || VpnProfileBox.SelectedItem is not string name)
            return;
        var profiles = LoadVpnProfiles();
        if (profiles.TryGetValue(name, out var map))
        {
            ApplyProfileMap(map);
            UpdatePskVisibility();
            VpnLog.Text = $"Profil « {name} » chargé.";
        }
    }

    private void OnVpnSaveProfile(object sender, RoutedEventArgs e)
    {
        string name = (VpnProfileBox.Text ?? "").Trim();
        if (name.Length == 0)
        {
            VpnLog.Text = "Donnez un nom au profil avant d'enregistrer.";
            return;
        }
        var profiles = LoadVpnProfiles();
        profiles[name] = CurrentProfileMap();
        SaveVpnProfiles(profiles);
        RefreshVpnProfileBox(name);
        VpnLog.Text = $"Profil « {name} » enregistré.";
    }

    private void OnVpnDeleteProfile(object sender, RoutedEventArgs e)
    {
        string name = (VpnProfileBox.Text ?? "").Trim();
        var profiles = LoadVpnProfiles();
        if (name.Length == 0 || !profiles.Remove(name))
        {
            VpnLog.Text = "Aucun profil de ce nom à supprimer.";
            return;
        }
        SaveVpnProfiles(profiles);
        VpnProfileBox.Text = "";
        RefreshVpnProfileBox(null);
        VpnLog.Text = $"Profil « {name} » supprimé.";
    }

    private void OnVpnTypeChanged(object sender, SelectionChangedEventArgs e) => UpdatePskVisibility();

    private void UpdatePskVisibility()
    {
        // La clé pré-partagée ne concerne que L2TP/IPSec (et le mode Automatique).
        if (VpnPskPanel is null) return;
        int idx = VpnType.SelectedIndex;
        VpnPskPanel.Visibility = (idx == 0 || idx == 3) ? Visibility.Visible : Visibility.Collapsed;
    }

    private VpnProfile CurrentVpnProfile() => new()
    {
        Server = VpnServer.Text.Trim(),
        Username = VpnUser.Text.Trim(),
        Password = VpnPassword.Password,
        PreSharedKey = VpnPsk.Password,
        Type = VpnType.SelectedIndex switch
        {
            1 => IatechShield.Tools.VpnType.Sstp,
            2 => IatechShield.Tools.VpnType.Pptp,
            3 => IatechShield.Tools.VpnType.Automatic,
            _ => IatechShield.Tools.VpnType.L2tp
        }
    };

    // --- Statut VPN en direct (débit, IP publique, pays) ---

    private DispatcherTimer? _vpnStatsTimer;
    private long _vpnPrevIn, _vpnPrevOut;
    private DateTime _vpnConnectedAt;
    private bool _vpnGeoFetched;

    private void StartVpnLiveMonitor()
    {
        _vpnConnectedAt = DateTime.Now;
        _vpnPrevIn = _vpnPrevOut = 0;
        _vpnGeoFetched = false;
        VpnLiveState.Text = "Connecté";
        _vpnStatsTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _vpnStatsTimer.Tick -= OnVpnStatsTick;
        _vpnStatsTimer.Tick += OnVpnStatsTick;
        _vpnStatsTimer.Start();
        OnVpnStatsTick(this, EventArgs.Empty);
        _ = FetchVpnGeoAsync();
    }

    private void StopVpnLiveMonitor()
    {
        _vpnStatsTimer?.Stop();
        VpnLiveState.Text = "Déconnecté";
        VpnUptime.Text = VpnLocalIpText.Text = VpnPublicIpText.Text = VpnCountryText.Text = VpnIspText.Text = "—";
        VpnDownRate.Text = VpnUpRate.Text = "—";
        VpnDownTotal.Text = "Reçu : —";
        VpnUpTotal.Text = "Envoyé : —";
    }

    private void OnVpnStatsTick(object? sender, EventArgs e)
    {
        var s = VpnStats.Read(CurrentVpnProfile().Name);
        if (s is null)
        {
            VpnLiveState.Text = "Connexion : adaptateur VPN introuvable…";
            return;
        }

        double seconds = _vpnStatsTimer?.Interval.TotalSeconds ?? 2;
        if (_vpnPrevIn > 0 || _vpnPrevOut > 0)
        {
            VpnDownRate.Text = VpnStats.FormatRate(Math.Max(0, s.BytesIn - _vpnPrevIn) / seconds);
            VpnUpRate.Text = VpnStats.FormatRate(Math.Max(0, s.BytesOut - _vpnPrevOut) / seconds);
        }
        _vpnPrevIn = s.BytesIn;
        _vpnPrevOut = s.BytesOut;

        VpnLiveState.Text = $"Connecté ({s.AdapterName})";
        VpnLocalIpText.Text = s.LocalIp;
        VpnDownTotal.Text = "Reçu : " + VpnStats.FormatBytes(s.BytesIn);
        VpnUpTotal.Text = "Envoyé : " + VpnStats.FormatBytes(s.BytesOut);
        var up = DateTime.Now - _vpnConnectedAt;
        VpnUptime.Text = up.TotalHours >= 1 ? $"{(int)up.TotalHours}h {up.Minutes:00}m {up.Seconds:00}s" : $"{up.Minutes:00}m {up.Seconds:00}s";
    }

    private async Task FetchVpnGeoAsync()
    {
        if (_vpnGeoFetched) return;
        _vpnGeoFetched = true;
        VpnCountryText.Text = "localisation…";
        var geo = await GeoInfo.LookupAsync();
        if (geo is null)
        {
            VpnCountryText.Text = "indisponible (hors-ligne ?)";
            return;
        }
        VpnPublicIpText.Text = geo.Ip;
        VpnCountryText.Text = string.IsNullOrEmpty(geo.City) ? geo.Country : $"{geo.Country} — {geo.City}";
        VpnIspText.Text = geo.Isp;
    }

    private void SetVpnUi(bool connected)
    {
        _vpnConnected = connected;
        if (connected) StartVpnLiveMonitor(); else StopVpnLiveMonitor();
        VpnDot.Fill = new SolidColorBrush(connected
            ? Color.FromRgb(0x10, 0xB9, 0x81) : Color.FromRgb(0x64, 0x74, 0x8B));
        VpnStatus.Text = connected ? "Connecté" : "Déconnecté";
        VpnConnectButton.IsEnabled = !connected;
        VpnDisconnectButton.IsEnabled = connected;
    }

    private async void OnVpnConnect(object sender, RoutedEventArgs e)
    {
        var profile = CurrentVpnProfile();
        if (string.IsNullOrWhiteSpace(profile.Server))
        {
            VpnLog.Text = "Renseignez l'adresse du serveur VPN.";
            return;
        }

        VpnConnectButton.IsEnabled = false;
        VpnLog.Text = "Connexion en cours…";
        try
        {
            var state = await _vpn.ConnectAsync(profile);
            SetVpnUi(state.Connected);
            VpnLog.Text = state.Message;
            VpnIpText.Text = state.AssignedIp is not null ? $"IP : {state.AssignedIp}" : "";
            if (state.Connected)
            {
                Log($"VPN connecté à {profile.Server}.");
                Notify("VPN", $"Connexion sécurisée établie ({profile.Server}).", "VPN");
                if (VpnSaveCreds.IsChecked == true) SaveVpnSettings(profile);
            }
            else
            {
                VpnConnectButton.IsEnabled = true;
                Log($"Échec VPN : {state.Message}");
            }
        }
        catch (Exception ex)
        {
            VpnConnectButton.IsEnabled = true;
            VpnLog.Text = $"Erreur : {ex.Message}";
        }
    }

    private async void OnVpnDisconnect(object sender, RoutedEventArgs e)
    {
        VpnDisconnectButton.IsEnabled = false;
        try
        {
            var state = await _vpn.DisconnectAsync();
            SetVpnUi(false);
            VpnIpText.Text = "";
            VpnLog.Text = state.Message;
            Log("VPN déconnecté.");
        }
        catch (Exception ex)
        {
            VpnLog.Text = $"Erreur : {ex.Message}";
        }
    }

    private void SaveVpnSettings(VpnProfile p)
    {
        // Mémorise le dernier profil utilisé (rechargé au prochain démarrage).
        SecretVault.Save("vpn", CurrentProfileMap());
    }

    // ------------------------------------------------------------ coffre-fort ---

    private readonly ObservableCollection<VaultItem> _vault = new();
    private bool _vaultLoaded;
    private bool _vaultUnlocked;

    private sealed record VaultDto(string Id, string Title, string Username, string Password, string Url, string Notes);

    /// <summary>
    /// Exige le mot de passe du coffre-fort avant d'y accéder (défini à la première
    /// ouverture). Une fois validé, l'accès reste ouvert pour la session.
    /// </summary>
    private bool EnsureVaultUnlocked()
    {
        if (_vaultUnlocked) return true;

        string? hash = SecretVault.Load("vaultlock").GetValueOrDefault("hash");

        if (string.IsNullOrWhiteSpace(hash))
        {
            // Première ouverture : on définit le mot de passe maître du coffre-fort.
            var create = new PromptWindow("Coffre-fort",
                "Définissez un mot de passe pour protéger l'accès au coffre-fort :", "Définir") { Owner = this };
            if (create.ShowDialog() != true || create.Value.Length < 4)
            {
                MessageBox.Show("Mot de passe trop court (4 caractères minimum). Accès au coffre-fort annulé.",
                    "Coffre-fort", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            SecretVault.Save("vaultlock", new Dictionary<string, string> { ["hash"] = SecretHash.Hash(create.Value) });
            _vaultUnlocked = true;
            Log("Mot de passe du coffre-fort défini.");
            return true;
        }

        var prompt = new PromptWindow("Coffre-fort verrouillé",
            "Saisissez le mot de passe du coffre-fort :", "Déverrouiller") { Owner = this };
        if (prompt.ShowDialog() != true)
            return false;
        if (!SecretHash.Verify(prompt.Value, hash))
        {
            MessageBox.Show("Mot de passe incorrect.", "Coffre-fort", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        _vaultUnlocked = true;
        return true;
    }

    private void LoadVault()
    {
        if (_vaultLoaded) return;
        _vaultLoaded = true;
        VaultList.ItemsSource = _vault;

        var raw = SecretVault.Load("vault").GetValueOrDefault("data");
        if (string.IsNullOrEmpty(raw)) return;
        try
        {
            var dtos = JsonSerializer.Deserialize<List<VaultDto>>(raw) ?? new();
            foreach (var d in dtos)
                _vault.Add(new VaultItem { Id = d.Id, Title = d.Title, Username = d.Username, Password = d.Password, Url = d.Url, Notes = d.Notes });
        }
        catch { /* coffre illisible : on repart à vide sans écraser */ }
    }

    private void SaveVault()
    {
        var dtos = _vault.Select(v => new VaultDto(v.Id, v.Title, v.Username, v.Password, v.Url, v.Notes)).ToList();
        SecretVault.Save("vault", new Dictionary<string, string> { ["data"] = JsonSerializer.Serialize(dtos) });
    }

    private void OnVaultAdd(object sender, RoutedEventArgs e)
    {
        string title = VaultTitle.Text.Trim();
        if (title.Length == 0)
        {
            VaultStatus.Text = "Donnez au moins un titre.";
            return;
        }
        _vault.Insert(0, new VaultItem
        {
            Title = title,
            Username = VaultUser.Text.Trim(),
            Password = VaultPassword.Text,
            Url = VaultUrl.Text.Trim(),
            Notes = VaultNotes.Text.Trim()
        });
        SaveVault();
        VaultTitle.Clear(); VaultUser.Clear(); VaultPassword.Clear(); VaultUrl.Clear(); VaultNotes.Clear();
        VaultStatus.Text = "Entrée ajoutée et chiffrée.";
    }

    private void OnVaultImportCsv(object sender, RoutedEventArgs e)
    {
        if (!_vaultLoaded) LoadVault();
        var dlg = new OpenFileDialog
        {
            Title = "Importer un CSV de mots de passe (export Google / Edge / Chrome)",
            Filter = "Fichiers CSV (*.csv)|*.csv|Tous les fichiers (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var rows = ParseCsv(File.ReadAllText(dlg.FileName));
            if (rows.Count == 0) { VaultStatus.Text = "CSV vide ou illisible."; return; }

            // En-tête type Chrome/Edge : name,url,username,password[,note]
            var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
            int iName = header.IndexOf("name"), iUrl = header.IndexOf("url");
            int iUser = header.IndexOf("username"), iPwd = header.IndexOf("password");
            int iNote = header.IndexOf("note");
            int start = (iUser >= 0 || iPwd >= 0) ? 1 : 0; // saute l'en-tête s'il existe
            if (iUser < 0) iUser = 2;
            if (iPwd < 0) iPwd = 3;
            if (iName < 0) iName = 0;
            if (iUrl < 0) iUrl = 1;

            int added = 0;
            for (int r = start; r < rows.Count; r++)
            {
                var c = rows[r];
                string Get(int i) => i >= 0 && i < c.Count ? c[i] : "";
                string pwd = Get(iPwd);
                string user = Get(iUser);
                if (string.IsNullOrEmpty(pwd) && string.IsNullOrEmpty(user)) continue;
                string title = Get(iName);
                if (string.IsNullOrWhiteSpace(title)) title = Get(iUrl);
                _vault.Insert(0, new VaultItem
                {
                    Title = string.IsNullOrWhiteSpace(title) ? "(importé)" : title,
                    Username = user,
                    Password = pwd,
                    Url = Get(iUrl),
                    Notes = iNote >= 0 ? Get(iNote) : ""
                });
                added++;
            }
            SaveVault();
            VaultStatus.Text = $"{added} mot(s) de passe importé(s) et chiffré(s).";
            Log($"Coffre-fort : import CSV de {added} entrées.");
        }
        catch (Exception ex) { VaultStatus.Text = $"Échec de l'import : {ex.Message}"; }
    }

    private void OnVaultImportBrowser(object sender, RoutedEventArgs e)
    {
        // L'export sécurisé du navigateur (DPAPI/AES) est protégé : on ouvre la page
        // d'export du navigateur, puis l'utilisateur réimporte le CSV obtenu.
        System.Windows.MessageBox.Show(this,
            "Pour importer vos mots de passe du navigateur :\n\n" +
            "1) Dans Chrome/Edge : Paramètres → Mots de passe → « Exporter les mots de passe » → enregistrez le CSV.\n" +
            "2) Revenez ici et cliquez « Importer CSV ».\n\n" +
            "J'ouvre la page des mots de passe de votre navigateur.",
            "Importer depuis le navigateur", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        foreach (var url in new[] { "edge://settings/passwords", "chrome://settings/passwords" })
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); return; }
            catch { /* navigateur suivant */ }
        }
    }

    private void OnVaultExport(object sender, RoutedEventArgs e)
    {
        if (!_vaultLoaded) LoadVault();
        if (_vault.Count == 0) { VaultStatus.Text = "Le coffre est vide."; return; }

        // Téléchargement protégé : exige le mot de passe de protection.
        if (!TamperEnabled)
        {
            VaultStatus.Text = "Définissez d'abord un mot de passe de protection (Réglages) pour pouvoir exporter.";
            return;
        }
        if (!RequireTamperAuth("Exporter (télécharger) les mots de passe du coffre-fort"))
            return;

        var dlg = new SaveFileDialog
        {
            Title = "Exporter le coffre-fort (CSV)",
            FileName = $"coffre-iatech-shield-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            Filter = "Fichier CSV (*.csv)|*.csv"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("name,url,username,password,note");
            foreach (var v in _vault)
                sb.AppendLine(string.Join(",", new[] { v.Title, v.Url, v.Username, v.Password, v.Notes }.Select(CsvField)));
            File.WriteAllText(dlg.FileName, sb.ToString());
            VaultStatus.Text = $"{_vault.Count} entrée(s) exportée(s). ⚠ Fichier en clair : conservez-le en lieu sûr.";
            Log("Coffre-fort exporté (CSV protégé par mot de passe).");
        }
        catch (Exception ex) { VaultStatus.Text = $"Échec de l'export : {ex.Message}"; }
    }

    private static string CsvField(string s)
    {
        s ??= "";
        return s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
    }

    /// <summary>Analyse un CSV (gère les guillemets, virgules et sauts de ligne échappés).</summary>
    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(ch);
            }
            else
            {
                switch (ch)
                {
                    case '"': inQuotes = true; break;
                    case ',': row.Add(field.ToString()); field.Clear(); break;
                    case '\r': break;
                    case '\n':
                        row.Add(field.ToString()); field.Clear();
                        if (row.Count > 1 || row[0].Length > 0) rows.Add(row);
                        row = new List<string>();
                        break;
                    default: field.Append(ch); break;
                }
            }
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            if (row.Count > 1 || row[0].Length > 0) rows.Add(row);
        }
        return rows;
    }

    private void OnVaultGenerate(object sender, RoutedEventArgs e)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$%&*?";
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(18);
        var sb = new StringBuilder(bytes.Length);
        foreach (byte b in bytes) sb.Append(chars[b % chars.Length]);
        VaultPassword.Text = sb.ToString();
        VaultStatus.Text = "Mot de passe fort généré.";
    }

    private void OnVaultReveal(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VaultItem item })
            item.Revealed = !item.Revealed;
    }

    private void OnVaultCopyUser(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VaultItem item })
            CopyToClipboard(item.Username, "Identifiant copié.");
    }

    private void OnVaultCopyPassword(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VaultItem item })
            CopyToClipboard(item.Password, "Mot de passe copié (efface le presse-papiers après usage).");
    }

    private void OnVaultDelete(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VaultItem item })
        {
            _vault.Remove(item);
            SaveVault();
            VaultStatus.Text = "Entrée supprimée.";
        }
    }

    private void CopyToClipboard(string text, string okMessage)
    {
        try
        {
            if (string.IsNullOrEmpty(text)) { VaultStatus.Text = "Rien à copier."; return; }
            System.Windows.Clipboard.SetText(text);
            VaultStatus.Text = okMessage;
        }
        catch (Exception ex)
        {
            VaultStatus.Text = $"Copie impossible : {ex.Message}";
        }
    }

    // ----------------------------------------------- centre de sécurité ---

    private async void OnComputeScore(object sender, RoutedEventArgs e)
    {
        ScoreRefreshButton.IsEnabled = false;
        ScoreValue.Text = "…";
        ScoreGrade.Text = "Analyse en cours…";
        try
        {
            bool realtimeOn = _monitor is not null;
            var result = await SecurityScore.EvaluateAsync(realtimeOn, TamperEnabled);
            ScoreValue.Text = result.Score.ToString();
            ScoreGrade.Text = result.Grade;
            ScoreValue.Foreground = new SolidColorBrush(ScoreColor(result.Score));

            ScoreList.ItemsSource = result.Checks.Select(c => new ScoreRow
            {
                Name = c.Name,
                Icon = c.Passed ? "✓" : "✗",
                Color = new SolidColorBrush(c.Passed ? Color.FromRgb(0x34, 0xD3, 0x99) : Color.FromRgb(0xEF, 0x44, 0x44)),
                Advice = c.Passed ? "" : c.Recommendation,
                AdviceVisibility = c.Passed ? Visibility.Collapsed : Visibility.Visible
            }).ToList();

            Log($"Score de sécurité : {result.Score}/100 ({result.Grade}).");
        }
        catch (Exception ex)
        {
            ScoreGrade.Text = $"Erreur : {ex.Message}";
        }
        finally
        {
            ScoreRefreshButton.IsEnabled = true;
        }
    }

    /// <summary>Remplit le panneau « Niveau de sécurité » animé à partir de mesures réelles.</summary>
    private void PopulateSecurityLevel(IntegrityReport report)
    {
        if (SecGlobalMeter is null) return;

        SecGlobalPct.Text = report.Overall + "%";
        SecGlobalPct.Foreground = new SolidColorBrush(ScoreColor(report.Overall));
        AnimateMeter(SecGlobalMeter, report.Overall);

        SecBars.Children.Clear();
        foreach (var p in report.Pillars)
        {
            int score = p.Available ? p.Score : 0;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 11) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });

            var label = new TextBlock
            {
                Text = p.Name,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            var bar = new ProgressBar
            {
                Style = (Style)FindResource("Meter"),
                Height = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(bar, 1);
            row.Children.Add(bar);

            var val = new TextBlock
            {
                Text = p.Available ? score + "%" : "N/A",
                Foreground = new SolidColorBrush(p.Available ? ScoreColor(score) : Color.FromRgb(0x64, 0x74, 0x8B)),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(val, 2);
            row.Children.Add(val);

            SecBars.Children.Add(row);
            if (p.Available) AnimateMeter(bar, score);
        }

        int threats = _threats.Count;
        SecThreatLine.Text = threats == 0 ? "✓ Aucune menace active" : $"⚠ {threats} menace(s) active(s)";
        SecThreatLine.Foreground = new SolidColorBrush(threats == 0
            ? Color.FromRgb(0x2B, 0xE0, 0xA6)
            : Color.FromRgb(0xEF, 0x44, 0x44));
    }

    /// <summary>Anime le remplissage d'une jauge de 0 à la valeur cible (effet organique).</summary>
    private void AnimateMeter(ProgressBar bar, int target)
    {
        bar.Foreground = new SolidColorBrush(ScoreColor(target));
        var anim = new System.Windows.Media.Animation.DoubleAnimation(0, target, TimeSpan.FromMilliseconds(900))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        };
        bar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, anim);
    }

    private async void OnRefreshIntegrity(object sender, RoutedEventArgs e)
    {
        IntegrityRefreshButton.IsEnabled = false;
        IntegrityStatus.Text = "Mesure en cours…";
        try
        {
            var sec = await SecurityScore.EvaluateAsync(_monitor is not null, TamperEnabled);
            var report = await IntegrityDashboard.EvaluateAsync(sec.Score);

            IntegrityOverall.Text = report.Overall.ToString();
            IntegrityOverall.Foreground = new SolidColorBrush(ScoreColor(report.Overall));
            IntegrityList.ItemsSource = report.Pillars.Select(p => new PillarItem
            {
                Name = p.Name,
                Display = p.Available ? p.Score.ToString() : "N/A",
                Detail = p.Detail,
                Color = new SolidColorBrush(p.Available ? ScoreColor(p.Score) : Color.FromRgb(0x64, 0x74, 0x8B))
            }).ToList();
            IntegrityStatus.Text = $"Santé globale : {report.Overall}/100.";
            PopulateSecurityLevel(report);
            Log($"Tableau d'intégrité : {report.Overall}/100.");
        }
        catch (Exception ex)
        {
            IntegrityStatus.Text = $"Erreur : {ex.Message}";
        }
        finally
        {
            IntegrityRefreshButton.IsEnabled = true;
        }
    }

    // -------------------------------------------- jumeau numérique / ADN ---

    private readonly ObservableCollection<DiffItem> _twin = new();
    private bool _twinBound;

    private async void OnTwinCapture(object sender, RoutedEventArgs e)
    {
        TwinCaptureButton.IsEnabled = false;
        TwinStatus.Text = "Capture de l'empreinte de référence…";
        try
        {
            var items = await SystemFingerprint.CaptureAsync();
            SystemFingerprint.SaveBaseline(items);
            TwinStatus.Text = $"Empreinte de référence enregistrée ({items.Count} éléments).";
            Log($"Jumeau numérique : empreinte de référence capturée ({items.Count} éléments).");
        }
        catch (Exception ex) { TwinStatus.Text = $"Erreur : {ex.Message}"; }
        finally { TwinCaptureButton.IsEnabled = true; }
    }

    private async void OnTwinCompare(object sender, RoutedEventArgs e)
    {
        if (!_twinBound) { TwinList.ItemsSource = _twin; _twinBound = true; }
        if (!SystemFingerprint.HasBaseline)
        {
            TwinStatus.Text = "Capturez d'abord une empreinte de référence.";
            return;
        }

        TwinCompareButton.IsEnabled = false;
        _twin.Clear();
        TwinStatus.Text = "Comparaison en cours…";
        try
        {
            var baseline = SystemFingerprint.LoadBaseline();
            var current = await SystemFingerprint.CaptureAsync();
            var diffs = SystemFingerprint.Compare(baseline, current);

            bool sensitive = false;
            foreach (var d in diffs)
            {
                bool risky = d.Category is "FILE" or "DRIVER" or "SERVICE" or "STARTUP";
                if (risky) sensitive = true;
                _twin.Add(new DiffItem
                {
                    Kind = d.Kind,
                    Category = CategoryLabel(d.Category),
                    Key = d.Key,
                    Change = d.Kind == "Modifié" ? $"{d.Before}  →  {d.After}" : (d.After.Length > 0 ? d.After : d.Before),
                    Color = new SolidColorBrush(
                        d.Kind == "Supprimé" ? Color.FromRgb(0xEF, 0x44, 0x44)
                        : d.Kind == "Ajouté" ? Color.FromRgb(0xF5, 0x9E, 0x0B)
                        : Color.FromRgb(0x38, 0xBD, 0xF8))
                });
            }

            TwinStatus.Text = diffs.Count == 0
                ? "Aucune modification — le système est identique à l'empreinte de référence."
                : $"{diffs.Count} modification(s) détectée(s).";
            Log($"Jumeau numérique : {diffs.Count} modification(s).");

            if (sensitive)
            {
                Notify("⚠ Jumeau numérique", "Des éléments sensibles (services/pilotes/démarrage/hosts) ont changé.", "Jumeau");
                CopilotAlert("Le jumeau numérique a détecté des modifications sur des éléments sensibles (services, pilotes, démarrage ou fichier hosts). Cela peut indiquer une infection ou une altération — vérifiez la liste dans l'onglet Jumeau.");
            }
        }
        catch (Exception ex) { TwinStatus.Text = $"Erreur : {ex.Message}"; }
        finally { TwinCompareButton.IsEnabled = true; }
    }

    private static string CategoryLabel(string cat) => cat switch
    {
        "APP" => "Logiciel", "SERVICE" => "Service", "DRIVER" => "Pilote",
        "STARTUP" => "Démarrage", "TASK" => "Tâche", "FILE" => "Fichier", _ => cat
    };

    // ------------------------------------------- centre d'investigation ---

    private readonly ObservableCollection<TimelineItem> _timeline = new();
    private bool _timelineBound;

    /// <summary>Enregistre un évènement dans la chronologie d'investigation.</summary>
    private void RecordIncident(string category, IncidentSeverity severity, string title, string detail)
    {
        try { IncidentLog.Record(category, severity, title, detail, DateTimeOffset.Now); }
        catch { /* best-effort */ }
    }

    private void OnRefreshInvestigation(object sender, RoutedEventArgs e)
    {
        if (!_timelineBound) { InvList.ItemsSource = _timeline; _timelineBound = true; }
        _timeline.Clear();
        var events = IncidentLog.Load();
        foreach (var ev in events)
        {
            _timeline.Add(new TimelineItem
            {
                Title = ev.Title,
                Detail = ev.Detail,
                Category = ev.Category,
                Time = ev.Time.LocalDateTime.ToString("dd/MM HH:mm:ss"),
                Color = new SolidColorBrush(ev.Severity switch
                {
                    IncidentSeverity.Critical => Color.FromRgb(0xEF, 0x44, 0x44),
                    IncidentSeverity.Warning => Color.FromRgb(0xF5, 0x9E, 0x0B),
                    _ => Color.FromRgb(0x34, 0xD3, 0x99)
                })
            });
        }
        InvStatus.Text = events.Count == 0 ? "Aucun évènement enregistré pour l'instant." : $"{events.Count} évènement(s).";
    }

    private void OnExportInvestigation(object sender, RoutedEventArgs e)
    {
        var events = IncidentLog.Load();
        if (events.Count == 0) { InvStatus.Text = "Rien à exporter."; return; }

        var dlg = new SaveFileDialog
        {
            Title = "Exporter le rapport d'incident",
            FileName = $"rapport-iatech-shield-{DateTime.Now:yyyyMMdd-HHmm}.html",
            Filter = "Rapport HTML (*.html)|*.html"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            File.WriteAllText(dlg.FileName, BuildIncidentReportHtml(events));
            InvStatus.Text = "Rapport exporté. Ouvrez-le et imprimez-le en PDF si besoin.";
            Log($"Rapport d'incident exporté : {dlg.FileName}");
            try { Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true }); } catch { }
        }
        catch (Exception ex) { InvStatus.Text = $"Échec de l'export : {ex.Message}"; }
    }

    private void OnClearInvestigation(object sender, RoutedEventArgs e)
    {
        IncidentLog.Clear();
        _timeline.Clear();
        InvStatus.Text = "Chronologie vidée.";
    }

    private static string BuildIncidentReportHtml(IReadOnlyList<IncidentEvent> events)
    {
        string Esc(string s) => System.Net.WebUtility.HtmlEncode(s);
        var rows = new StringBuilder();
        foreach (var ev in events)
        {
            string color = ev.Severity switch
            {
                IncidentSeverity.Critical => "#ef4444",
                IncidentSeverity.Warning => "#f59e0b",
                _ => "#34d399"
            };
            rows.Append(
                $"<tr><td>{ev.Time.LocalDateTime:dd/MM/yyyy HH:mm:ss}</td>" +
                $"<td><b style='color:{color}'>{ev.Severity}</b></td>" +
                $"<td>{Esc(ev.Category)}</td><td>{Esc(ev.Title)}</td><td>{Esc(ev.Detail)}</td></tr>");
        }
        return
            "<!doctype html><html lang='fr'><head><meta charset='utf-8'><title>Rapport IATECH-SHIELD PRO</title>" +
            "<style>body{font-family:Segoe UI,Arial,sans-serif;background:#0a1726;color:#e6eefb;padding:28px}" +
            "h1{color:#22d3e8}table{border-collapse:collapse;width:100%;font-size:13px}" +
            "th,td{border:1px solid #24364f;padding:7px;text-align:left;vertical-align:top}th{background:#10233a;color:#22d3e8}" +
            "tr:nth-child(even){background:#0e1d31}</style></head><body>" +
            $"<h1>🛡️ IATECH-SHIELD PRO — Rapport d'incident</h1>" +
            $"<p>Généré le {DateTime.Now:dd/MM/yyyy à HH:mm} · {events.Count} évènement(s) · poste {Esc(Environment.MachineName)}</p>" +
            "<table><tr><th>Date</th><th>Gravité</th><th>Catégorie</th><th>Évènement</th><th>Détail</th></tr>" +
            rows + "</table></body></html>";
    }

    private static Color ScoreColor(int score) => score switch
    {
        >= 75 => Color.FromRgb(0x34, 0xD3, 0x99),
        >= 50 => Color.FromRgb(0xF5, 0x9E, 0x0B),
        _ => Color.FromRgb(0xEF, 0x44, 0x44)
    };

    private async void OnPanicEngage(object sender, RoutedEventArgs e)
    {
        var confirm = new PromptWindow("Mode Panic",
            "Cela va couper Internet, bloquer l'USB, arrêter les processus suspects et verrouiller la session. Tapez OUI pour confirmer.",
            "Activer") { Owner = this };
        if (confirm.ShowDialog() != true || !string.Equals(confirm.Value.Trim(), "OUI", StringComparison.OrdinalIgnoreCase))
        {
            PanicStatus.Text = "Mode Panic annulé.";
            return;
        }

        PanicButton.IsEnabled = false;
        PanicStatus.Text = "Activation du mode Panic…";
        Log("Mode Panic activé.");
        Notify("🚨 Mode Panic", "Réseau coupé, USB bloqué, session verrouillée.", "Centre");
        RecordIncident("Mode Panic", IncidentSeverity.Warning, "Mode Panic activé", "Réseau coupé, USB bloqué, processus suspects arrêtés, session verrouillée.");
        try
        {
            string report = await PanicMode.EngageAsync();
            PanicStatus.Text = "Mode Panic ACTIF.\n" + report;
            Log("Mode Panic : " + report.Replace("\n", " "));
        }
        catch (Exception ex)
        {
            PanicStatus.Text = $"Erreur : {ex.Message}";
        }
        finally
        {
            PanicButton.IsEnabled = true;
        }
    }

    private async void OnPanicDisengage(object sender, RoutedEventArgs e)
    {
        PanicOffButton.IsEnabled = false;
        PanicStatus.Text = "Rétablissement…";
        try
        {
            string report = await PanicMode.DisengageAsync();
            PanicStatus.Text = "Mode Panic désactivé.\n" + report;
            Log("Mode Panic désactivé.");
        }
        catch (Exception ex)
        {
            PanicStatus.Text = $"Erreur : {ex.Message}";
        }
        finally
        {
            PanicOffButton.IsEnabled = true;
        }
    }

    // --- Surveillance du presse-papiers (anti-hijack crypto) ---

    private bool _clipboardGuardOn = true;
    private string? _lastCryptoAddress;
    private CryptoKind _lastCryptoKind;

    private void OnToggleClipboardGuard(object sender, RoutedEventArgs e)
    {
        _clipboardGuardOn = ClipboardGuardSwitch.IsChecked == true;
        ClipboardStatus.Text = _clipboardGuardOn
            ? "Surveillance active — en attente d'une copie d'adresse crypto…"
            : "Surveillance désactivée.";
    }

    private void OnClipboardChanged()
    {
        if (!_clipboardGuardOn) return;
        string text;
        try { text = System.Windows.Clipboard.GetText(); }
        catch { return; }

        var kind = ClipboardGuard.Classify(text);
        if (kind == CryptoKind.None) return;
        string addr = text.Trim();

        // Remplacement rapide d'une adresse par une AUTRE = signe d'un hijacker.
        if (_lastCryptoAddress is not null && _lastCryptoKind == kind && _lastCryptoAddress != addr)
        {
            string msg = $"⚠ Adresse {ClipboardGuard.Label(kind)} dans le presse-papiers REMPLACÉE par une autre — possible malware voleur de crypto !";
            ClipboardStatus.Text = msg + $"\nAvant : {_lastCryptoAddress}\nAprès : {addr}";
            ClipboardStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            SoundFx.Threat();
            Notify("⚠ Presse-papiers compromis", "Une adresse crypto copiée a été modifiée. Vérifiez avant d'envoyer des fonds !", "Centre");
            Log("ALERTE presse-papiers : adresse crypto remplacée.");
        }
        else
        {
            ClipboardStatus.Text = $"Adresse {ClipboardGuard.Label(kind)} copiée — surveillée.";
            ClipboardStatus.Foreground = (Brush)FindResource("AccentBrush");
        }

        _lastCryptoAddress = addr;
        _lastCryptoKind = kind;
    }

    // ------------------------------------------------ radar réseau local ---

    private readonly ObservableCollection<RadarItem> _radar = new();
    private bool _radarBound;

    private static string RadarKnownPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IatechShield", "radar-known.json");

    /// <summary>
    /// Score de sécurité d'un appareil réseau (imprimante, caméra IP, NAS, objet connecté…) :
    /// part de 100 et retire des points selon les ports dangereux et les risques détectés.
    /// </summary>
    private static int DeviceSecurityScore(RadarDevice d)
    {
        int score = 100;
        bool Has(int port) => d.OpenPorts.Any(p => p.Number == port);

        if (Has(23)) score -= 30;                 // Telnet : très exposé
        if (Has(21)) score -= 18;                 // FTP en clair
        if (Has(3389)) score -= 18;               // RDP exposé
        if (Has(445)) score -= 12;                // SMB exposé
        if (Has(80) && !Has(443)) score -= 8;     // admin web non chiffrée

        // Caméras et imprimantes accessibles sans HTTPS = risque de creds par défaut.
        if (d.Type is LanDeviceType.CameraIp or LanDeviceType.Imprimante && Has(80) && !Has(443))
            score -= 10;

        score -= Math.Min(20, d.Risks.Count * 6);  // chaque risque détecté pèse
        return Math.Clamp(score, 5, 100);
    }

    private async void OnRadarScan(object sender, RoutedEventArgs e)
    {
        if (!_radarBound) { RadarList.ItemsSource = _radar; _radarBound = true; }

        RadarButton.IsEnabled = false;
        _radar.Clear();
        RadarStatus.Text = "Radar en cours…";
        try
        {
            var known = LoadRadarKnown();
            var radar = new LanRadar();
            var devices = await radar.ScanAsync(s => Dispatcher.Invoke(() => RadarStatus.Text = s));

            int newCount = 0, riskCount = 0;
            foreach (var d in devices)
            {
                string key = d.Mac is not ("—" or "") ? d.Mac : d.Ip;
                bool isNew = known.Count > 0 && !known.Contains(key);
                if (isNew) newCount++;
                if (d.Risks.Count > 0) riskCount++;

                bool https = d.OpenPorts.Any(p => p.Number is 443 or 8443);
                bool http = d.OpenPorts.Any(p => p.Number is 80 or 8080);
                string adminUrl = https ? $"https://{d.Ip}" : http ? $"http://{d.Ip}" : $"http://{d.Ip}";

                int score = DeviceSecurityScore(d);
                var scoreColor = new SolidColorBrush(ScoreColor(score));

                _radar.Add(new RadarItem
                {
                    Ip = d.Ip,
                    Mac = d.Mac,
                    TypeLabel = LanRadar.TypeLabel(d.Type),
                    NameLine = d.Name,
                    PortsLine = d.OpenPorts.Count == 0
                        ? "Aucun port courant ouvert"
                        : "Ports : " + string.Join(", ", d.OpenPorts.Select(p => $"{p.Number} ({p.Service})")),
                    Risks = d.Risks.ToList(),
                    RisksText = string.Join(" ", d.Risks),
                    AdminUrl = adminUrl,
                    ScoreText = $"Sécurité {score}%",
                    ScoreColor = scoreColor,
                    ActionsVisibility = d.Risks.Count > 0 ? Visibility.Visible : Visibility.Collapsed,
                    IsNew = isNew,
                    NewVisibility = isNew ? Visibility.Visible : Visibility.Collapsed,
                    BorderBrush = new SolidColorBrush(
                        d.Risks.Count > 0 ? Color.FromRgb(0xEF, 0x44, 0x44)
                        : isNew ? Color.FromRgb(0xF5, 0x9E, 0x0B)
                        : Color.FromArgb(0x33, 0x44, 0xE0, 0xFF))
                });
            }

            SaveRadarKnown(devices.Select(d => d.Mac is not ("—" or "") ? d.Mac : d.Ip));

            RadarStatus.Text = $"{devices.Count} appareil(s) — {newCount} nouveau(x), {riskCount} à risque.";
            Log($"Radar réseau : {devices.Count} appareils, {newCount} nouveaux, {riskCount} à risque.");
            if (newCount > 0 || riskCount > 0)
            {
                if (newCount > 0) SoundFx.NewDevice();
                Notify("Radar réseau", $"{newCount} nouvel(s) appareil(s), {riskCount} à risque détecté(s).", "Radar");
                if (riskCount > 0)
                    CopilotAlert($"Le radar réseau a trouvé {riskCount} appareil(s) présentant des risques (ports exposés). Consultez l'onglet Radar.");
            }
        }
        catch (Exception ex)
        {
            RadarStatus.Text = $"Échec : {ex.Message}";
        }
        finally
        {
            RadarButton.IsEnabled = true;
        }
    }

    private void OnRadarOpenAdmin(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: RadarItem item }) return;
        try
        {
            Process.Start(new ProcessStartInfo(item.AdminUrl) { UseShellExecute = true });
            RadarStatus.Text = $"Interface ouverte : {item.AdminUrl} — changez le mot de passe par défaut.";
            Log($"Radar : ouverture de l'interface {item.AdminUrl}");
        }
        catch (Exception ex) { RadarStatus.Text = $"Ouverture impossible : {ex.Message}"; }
    }

    private async void OnRadarBlock(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: RadarItem item }) return;
        var confirm = new PromptWindow("Bloquer l'appareil",
            $"Bloquer {item.Ip} via le pare-feu Windows (ce PC ne communiquera plus avec lui) ? Tapez OUI.",
            "Bloquer") { Owner = this };
        if (confirm.ShowDialog() != true || !string.Equals(confirm.Value.Trim(), "OUI", StringComparison.OrdinalIgnoreCase))
            return;

        RadarStatus.Text = $"Blocage de {item.Ip}…";
        try
        {
            bool ok = await NetGuard.BlockIpAsync(item.Ip);
            RadarStatus.Text = ok ? $"Appareil {item.Ip} bloqué (pare-feu). Réversible via « Débloquer »."
                                  : $"Échec du blocage de {item.Ip} (droits administrateur requis ?).";
            if (ok)
            {
                RecordIncident("Radar", IncidentSeverity.Warning, $"Appareil bloqué : {item.Ip}", item.RisksText);
                Log($"Radar : appareil {item.Ip} bloqué au pare-feu.");
            }
        }
        catch (Exception ex) { RadarStatus.Text = $"Erreur : {ex.Message}"; }
    }

    private async void OnRadarUnblock(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: RadarItem item }) return;
        RadarStatus.Text = $"Déblocage de {item.Ip}…";
        try
        {
            bool ok = await NetGuard.UnblockIpAsync(item.Ip);
            RadarStatus.Text = ok ? $"Appareil {item.Ip} débloqué." : $"Aucune règle de blocage pour {item.Ip}.";
            if (ok) Log($"Radar : appareil {item.Ip} débloqué.");
        }
        catch (Exception ex) { RadarStatus.Text = $"Erreur : {ex.Message}"; }
    }

    private async void OnRadarAdvice(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: RadarItem item }) return;
        if (!AiAssistant.IsConfigured)
        {
            System.Windows.MessageBox.Show(this,
                $"Appareil : {item.NameLine} ({item.Ip}) — {item.TypeLabel}\n\nRisques :\n{item.RisksText}\n\n" +
                "Conseils : changez le mot de passe par défaut, désactivez Telnet/FTP, mettez à jour le firmware, " +
                "et coupez l'accès Internet de l'appareil s'il n'en a pas besoin.",
                "Conseils de correction", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        ShowPage("Copilote");
        EnsureChatLoaded();
        AddChat("Vous", $"Comment corriger les risques de l'appareil {item.TypeLabel} ({item.Ip}) ?", true);
        try
        {
            string answer = await new AiAssistant().AskAsync(
                $"Un appareil du réseau local présente des risques. Type : {item.TypeLabel}. Adresse : {item.Ip}. " +
                $"Nom : {item.NameLine}. Risques détectés : {item.RisksText}. " +
                "Donne des étapes concrètes et simples pour corriger ces risques.");
            AddChat("Copilote", answer, false);
        }
        catch (Exception ex) { AddChat("Copilote", $"Erreur : {ex.Message}", false); }
    }

    private static HashSet<string> LoadRadarKnown()
    {
        try
        {
            if (File.Exists(RadarKnownPath))
                return JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(RadarKnownPath))
                       ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch { }
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static void SaveRadarKnown(IEnumerable<string> keys)
    {
        try
        {
            var known = LoadRadarKnown();
            foreach (var k in keys) known.Add(k);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(RadarKnownPath)!);
            File.WriteAllText(RadarKnownPath, JsonSerializer.Serialize(known));
        }
        catch { /* persistance best-effort */ }
    }

    // --------------------------------------- webcam/micro + mode gamer ---

    private readonly ObservableCollection<MediaItem> _media = new();
    private bool _mediaBound;
    private bool _gamerMode;

    private async void OnScanMedia(object sender, RoutedEventArgs e)
    {
        if (!_mediaBound) { MediaList.ItemsSource = _media; _mediaBound = true; }
        MediaScanButton.IsEnabled = false;
        _media.Clear();
        MediaStatus.Text = "Analyse des accès caméra/micro…";
        try
        {
            var accesses = await MediaAccessMonitor.ScanAsync();
            int inUse = 0;
            foreach (var a in accesses)
            {
                if (a.InUse) inUse++;
                _media.Add(new MediaItem
                {
                    Device = a.Device,
                    App = a.App,
                    Status = a.InUse ? "EN COURS" : "déjà utilisé",
                    Color = new SolidColorBrush(a.InUse ? Color.FromRgb(0xEF, 0x44, 0x44) : Color.FromRgb(0x64, 0x74, 0x8B))
                });
            }
            MediaStatus.Text = accesses.Count == 0
                ? "Aucun accès caméra/micro enregistré."
                : $"{accesses.Count} application(s) — {inUse} en cours d'utilisation.";
            if (inUse > 0)
            {
                Notify("📷 Webcam/micro", $"{inUse} application(s) utilisent actuellement votre caméra/micro.", "Protection");
                CopilotAlert($"{inUse} application(s) utilisent actuellement votre webcam ou votre micro. Vérifiez l'onglet Centre si ce n'est pas attendu.");
            }
        }
        catch (Exception ex) { MediaStatus.Text = $"Erreur : {ex.Message}"; }
        finally { MediaScanButton.IsEnabled = true; }
    }

    private void OnToggleGamerMode(object sender, RoutedEventArgs e)
    {
        _gamerMode = GamerModeSwitch.IsChecked == true;
        try
        {
            using var proc = Process.GetCurrentProcess();
            proc.PriorityClass = _gamerMode ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal;
        }
        catch { /* priorité best-effort */ }

        GamerStatus.Text = _gamerMode
            ? "Activé : notifications suspendues, analyse allégée. Protection toujours active."
            : "Désactivé.";
        Log($"Mode Gamer {(_gamerMode ? "activé" : "désactivé")}.");
    }

    // ------------------------------------------------------- copilote IA ---

    private readonly ObservableCollection<ChatMessage> _chat = new();
    private bool _chatLoaded;

    private void EnsureChatLoaded()
    {
        if (_chatLoaded) return;
        _chatLoaded = true;
        ChatList.ItemsSource = _chat;
        AddChat("Copilote", AiAssistant.IsConfigured
            ? "Bonjour 👋 Je surveille votre système. Posez-moi une question, ou je vous alerterai en cas d'activité suspecte."
            : "Pour discuter avec moi, définissez la variable d'environnement ANTHROPIC_API_KEY puis relancez l'application.",
            isUser: false);
    }

    private void AddChat(string sender, string text, bool isUser)
    {
        _chat.Add(new ChatMessage
        {
            Sender = sender,
            Text = text,
            Bubble = new SolidColorBrush(isUser ? Color.FromRgb(0x14, 0x2A, 0x44) : Color.FromRgb(0x1E, 0x16, 0x38)),
            Align = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left
        });
        ChatScroll?.ScrollToBottom();
    }

    private void OnChatKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnChatSend(sender, new RoutedEventArgs());
    }

    private async void OnChatSend(object sender, RoutedEventArgs e)
    {
        string q = ChatInput.Text.Trim();
        if (q.Length == 0) return;
        if (!AiAssistant.IsConfigured)
        {
            AddChat("Copilote", "Assistant IA non configuré (ANTHROPIC_API_KEY).", false);
            return;
        }

        AddChat("Vous", q, true);
        ChatInput.Clear();
        ChatSendButton.IsEnabled = false;
        try
        {
            string context = $"Contexte système : {_threatCount} menace(s) détectée(s) lors de la session.\n\nQuestion de l'utilisateur : {q}";
            string answer = await new AiAssistant().AskAsync(context);
            AddChat("Copilote", answer, false);
        }
        catch (Exception ex)
        {
            AddChat("Copilote", $"Erreur : {ex.Message}", false);
        }
        finally { ChatSendButton.IsEnabled = true; }
    }

    private async void OnExplainThreat(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ThreatItem item }) return;
        ShowPage("Copilote");
        EnsureChatLoaded();
        if (!AiAssistant.IsConfigured)
        {
            AddChat("Copilote", "Pour l'explication IA, définissez ANTHROPIC_API_KEY.", false);
            return;
        }
        AddChat("Vous", $"Explique la menace : {item.Name}", true);
        try
        {
            string details = $"Type de détection : {item.Name}. SHA-256 : {item.Sha}.";
            string answer = await new AiAssistant().ExplainThreatAsync(item.Name, item.Path, details);
            AddChat("Copilote", answer, false);
        }
        catch (Exception ex) { AddChat("Copilote", $"Erreur : {ex.Message}", false); }
    }

    private async void OnAnalyzeScam(object sender, RoutedEventArgs e)
    {
        string content = ScamInput.Text.Trim();
        if (content.Length == 0) { ScamResult.Text = "Collez un e-mail, SMS ou URL."; return; }
        if (!AiAssistant.IsConfigured) { ScamResult.Text = "Configurez ANTHROPIC_API_KEY pour l'analyse IA."; return; }

        ScamButton.IsEnabled = false;
        ScamResult.Text = "Analyse en cours…";
        try { ScamResult.Text = await new AiAssistant().AnalyzeScamAsync(content); }
        catch (Exception ex) { ScamResult.Text = $"Erreur : {ex.Message}"; }
        finally { ScamButton.IsEnabled = true; }
    }

    private async void OnPredictFile(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Fichier à analyser (prédictif)" };
        if (dlg.ShowDialog(this) != true) return;
        if (!AiAssistant.IsConfigured) { PredictResult.Text = "Configurez ANTHROPIC_API_KEY pour l'analyse IA."; return; }

        PredictButton.IsEnabled = false;
        PredictResult.Text = "Calcul des caractéristiques et estimation…";
        try
        {
            string path = dlg.FileName;
            string metadata = await Task.Run(() =>
            {
                var info = new FileInfo(path);
                var h = HeuristicAnalyzer.Analyze(path);
                return $"Nom : {info.Name}\nExtension : {info.Extension}\nTaille : {info.Length} octets\n" +
                       $"Entropie : {h.Entropy:0.0}/8\nIndice heuristique : {(h.Suspicious ? h.Reason : "rien de notable")}";
            });
            PredictResult.Text = await new AiAssistant().AssessFileAsync(metadata);
        }
        catch (Exception ex) { PredictResult.Text = $"Erreur : {ex.Message}"; }
        finally { PredictButton.IsEnabled = true; }
    }

    /// <summary>Alerte proactive du copilote (appelée par les détections temps réel/ransomware).</summary>
    private void CopilotAlert(string text)
    {
        EnsureChatLoaded();
        AddChat("Copilote", "⚠ " + text, false);
    }

    // ------------------ enregistrement auto des identifiants (extension) ---

    private CredentialBridge? _credBridge;

    private void StartCredentialBridge()
    {
        try
        {
            _credBridge = new CredentialBridge(Dispatcher);
            _credBridge.CredentialReceived += OnBrowserCredential;
            _credBridge.Start();
        }
        catch { /* pont indisponible : sans incidence */ }
    }

    private void OnBrowserCredential(string url, string user, string pass)
    {
        if (!_vaultLoaded) LoadVault();

        // Évite les doublons exacts déjà enregistrés.
        if (_vault.Any(v => string.Equals(v.Url, url, StringComparison.OrdinalIgnoreCase)
                            && v.Username == user && v.Password == pass))
            return;

        string host = url;
        try { host = new Uri(url).Host; } catch { }

        var result = System.Windows.MessageBox.Show(this,
            $"Un identifiant vient d'être saisi sur :\n{url}\n\nIdentifiant : {user}\n\nL'enregistrer dans le coffre-fort IATECH-SHIELD ?",
            "Nouvel identifiant détecté", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (result != System.Windows.MessageBoxResult.Yes) return;

        // Met à jour si le même site+utilisateur existe déjà, sinon ajoute.
        var existing = _vault.FirstOrDefault(v =>
            string.Equals(v.Url, url, StringComparison.OrdinalIgnoreCase) && v.Username == user);
        if (existing is not null) { existing.Password = pass; }
        else _vault.Insert(0, new VaultItem { Title = host, Username = user, Password = pass, Url = url });

        SaveVault();
        Notify("Coffre-fort", $"Identifiant pour {host} enregistré.", "Coffre-fort");
        Log($"Coffre-fort : identifiant enregistré via le navigateur ({host}).");
    }

    private void OnOpenExtensionFolder(object sender, RoutedEventArgs e)
    {
        string folder = System.IO.Path.Combine(AppContext.BaseDirectory, "browser-extension");
        try
        {
            if (System.IO.Directory.Exists(folder))
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            else
                System.Windows.MessageBox.Show(this,
                    "Le dossier de l'extension est fourni avec l'application (browser-extension). " +
                    "Chargez-le dans Chrome/Edge via « Gérer les extensions » → « Mode développeur » → « Charger l'extension non empaquetée ».",
                    "Extension navigateur", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex) { Log($"Ouverture du dossier extension impossible : {ex.Message}"); }
    }

    // -------------------------------------------------- clé API assistant ---

    private void OnSaveApiKey(object sender, RoutedEventArgs e)
    {
        string key = ApiKeyInput.Password.Trim();
        if (string.IsNullOrEmpty(key))
        {
            AiAssistant.ClearApiKey();
            ApiKeyStatusText.Text = "Clé effacée. L'assistant IA est désactivé.";
            return;
        }
        if (!key.StartsWith("sk-", StringComparison.OrdinalIgnoreCase))
        {
            ApiKeyStatusText.Text = "⚠ La clé Anthropic commence normalement par « sk-ant-… ». Vérifiez le copier-coller.";
            return;
        }
        AiAssistant.SaveApiKey(key);
        ApiKeyInput.Clear();
        ApiKeyStatusText.Text = "✓ Clé enregistrée (chiffrée). Assistant IA actif — aucun redémarrage nécessaire.";
        Log("Clé API Anthropic enregistrée.");
    }

    private void OnGetApiKey(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://console.anthropic.com/settings/keys") { UseShellExecute = true }); }
        catch (Exception ex) { ApiKeyStatusText.Text = $"Ouverture impossible : {ex.Message}"; }
    }

    // ----------------------------------------------- réputation VirusTotal ---

    private async void OnVirusTotalCheck(object sender, RoutedEventArgs e)
    {
        string key = VtApiKeyInput.Password;
        if (string.IsNullOrWhiteSpace(key))
            key = VirusTotalClient.KeyFromEnvironment ?? "";
        if (string.IsNullOrWhiteSpace(key))
        {
            VtStatus.Text = "Renseignez une clé API VirusTotal (ou la variable VIRUSTOTAL_API_KEY).";
            return;
        }

        var dlg = new OpenFileDialog { Title = "Fichier à vérifier sur VirusTotal" };
        if (dlg.ShowDialog(this) != true)
            return;

        VtCheckFileButton.IsEnabled = false;
        VtStatus.Text = "Calcul du hash et interrogation de VirusTotal…";
        try
        {
            string path = dlg.FileName;
            string sha = await Task.Run(() => Scanner.ComputeSha256(path));
            var client = new VirusTotalClient(key.Trim());
            var report = await client.LookupAsync(sha);

            if (!report.Found)
            {
                VtStatus.Text = $"{Path.GetFileName(path)} : inconnu de VirusTotal (jamais analysé).";
            }
            else if (report.IsMalicious)
            {
                string label = report.PopularName is { Length: > 0 } ? $" — {report.PopularName}" : "";
                VtStatus.Text = $"⚠ {Path.GetFileName(path)} : {report.Summary}{label}.";
                Notify("VirusTotal — menace", $"{Path.GetFileName(path)} : {report.Summary}", "Scan");
                Log($"VirusTotal : {Path.GetFileName(path)} — {report.Summary}{label}");
            }
            else
            {
                VtStatus.Text = $"✓ {Path.GetFileName(path)} : aucun moteur ne le signale ({report.TotalEngines} moteurs).";
            }
        }
        catch (Exception ex)
        {
            VtStatus.Text = $"Échec : {ex.Message}";
        }
        finally
        {
            VtCheckFileButton.IsEnabled = true;
        }
    }

    // --------------------------------------------------------------- moteur ---

    private void InitEngine()
    {
        try
        {
            string dbPath = Path.Combine(AppContext.BaseDirectory, "signatures.json");
            _db = SignatureDatabase.LoadFromFile(dbPath);
            _scanner = new Scanner(_db) { Yara = YaraEngine.BuiltIn() };
            _quarantine = new Quarantine(DefaultQuarantineDir());

            SignatureCountText.Text = $"{_db.Signatures.Count} signatures chargées";
            UpdatesDateText.Text = $"Dernière vérification : {DateTime.Now:dd/MM/yyyy}";
            Log($"Moteur prêt — {_db.Signatures.Count} signatures.");
        }
        catch (Exception ex)
        {
            ScanStatusText.Text = $"Erreur moteur : {ex.Message}";
            ScanButton.IsEnabled = false;
            Log($"Erreur moteur : {ex.Message}");
        }

        RefreshLicense();
    }

    private static string DefaultQuarantineDir()
    {
        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "IatechShield", "quarantine");
    }

    private static string DefaultScanFolder()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string downloads = Path.Combine(profile, "Downloads");
        return Directory.Exists(downloads) ? downloads : profile;
    }

    // -------------------------------------------------------------- licence ---

    private void RefreshLicense()
    {
        var status = _license.GetStatus(DateTimeOffset.UtcNow);
        _activated = status.IsActivated;

        var green = (Brush)FindResource("GreenBrush");
        var accent = (Brush)FindResource("AccentBrush");
        var alert = new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x5C));

        _licenseExpiry = null;

        switch (status.State)
        {
            case LicenseState.Licensed:
                LicenseStatusText.Foreground = green;
                LicenseBadge.BorderBrush = green;
                LicenseBadge.Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x2E, 0x1F));
                if (status.License!.IsLifetime)
                {
                    // Achat « à vie » : pas de compte à rebours.
                    LicenseStatusText.Text = "✓ Licence à vie";
                }
                else
                {
                    // Mensuel / annuel : on mémorise l'échéance pour le compte à rebours (j/h/min).
                    _licenseExpiry = status.License.ExpiresUtc;
                    UpdateLicenseCountdown();
                }
                // Le bouton « Activer » devient vert avec la mention « Activé ✓ ».
                ActivateButton.Visibility = Visibility.Visible;
                ActivateButton.Content = "✓ Activé";
                ActivateButton.Background = green;
                ActivateButton.Foreground = Brushes.White;
                break;
            case LicenseState.TrialActive:
                LicenseStatusText.Text = $"Essai — {status.TrialDaysRemaining} j";
                LicenseStatusText.Foreground = accent;
                LicenseBadge.BorderBrush = accent;
                LicenseBadge.Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x2A, 0x3F));
                ActivateButton.Visibility = Visibility.Visible;
                ActivateButton.Content = "Activer";
                ActivateButton.Background = new SolidColorBrush(Color.FromRgb(0x15, 0x77, 0xC9));
                ActivateButton.Foreground = Brushes.White;
                break;
            default:
                LicenseStatusText.Text = "Essai expiré";
                LicenseStatusText.Foreground = alert;
                LicenseBadge.BorderBrush = alert;
                LicenseBadge.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x0E, 0x12));
                ActivateButton.Visibility = Visibility.Visible;
                ActivateButton.Content = "Activer";
                ActivateButton.Background = new SolidColorBrush(Color.FromRgb(0x15, 0x77, 0xC9));
                ActivateButton.Foreground = Brushes.White;
                break;
        }

        ScanButton.IsEnabled = _activated && _scanner is not null;
        if (FullScanButton is not null)
            FullScanButton.IsEnabled = _activated && _scanner is not null;

        ApplyTrialLock(status);
    }

    /// <summary>
    /// Verrou d'essai : dès que les 15 jours sont terminés (ni licence, ni essai actif),
    /// le logiciel reste figé sur le dashboard avec le message « Licence requise ».
    /// </summary>
    private void ApplyTrialLock(LicenseStatus status)
    {
        if (LicenseLockOverlay is null)
            return;

        bool locked = status.State is not (LicenseState.Licensed or LicenseState.TrialActive);
        LicenseLockOverlay.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;

        // On ramène l'utilisateur sur le dashboard et on le fige derrière le voile.
        if (locked && NavDashboard is not null)
            NavDashboard.IsChecked = true;
    }

    /// <summary>
    /// Met à jour le compte à rebours de la licence (mensuel / annuel) en jours, heures,
    /// minutes. Appelé chaque seconde. Pour une licence à vie, ne fait rien (pas d'échéance).
    /// </summary>
    private void UpdateLicenseCountdown()
    {
        if (_licenseExpiry is not { } expiry || LicenseStatusText is null)
            return;

        TimeSpan left = expiry - DateTimeOffset.UtcNow;
        if (left <= TimeSpan.Zero)
        {
            // Échéance atteinte : on réévalue tout l'état (bascule en « essai expiré » → verrou).
            LicenseStatusText.Text = "Licence expirée";
            _licenseExpiry = null;
            RefreshLicense();
            return;
        }

        // Format : « 12j 04h 09m » (les secondes évitent un affichage figé sans surcharger l'œil).
        LicenseStatusText.Text = left.TotalDays >= 1
            ? $"⏳ {left.Days}j {left.Hours:00}h {left.Minutes:00}m"
            : $"⏳ {left.Hours:00}h {left.Minutes:00}m {left.Seconds:00}s";
    }

    private void OnOpenLicense(object sender, RoutedEventArgs e)
    {
        var status = _license.GetStatus(DateTimeOffset.UtcNow);
        var dialog = new LicenseWindow(_license, status) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Activated)
        {
            RefreshLicense();
            // Si le client est connecté, on mémorise sa clé dans son compte cloud
            // (réinstallation = reconnexion, sans ressaisir la clé).
            _ = PushLicenseToCloudAsync();
            if (!_cloud.IsSignedIn)
                ProposeAccountAfterActivation();
        }
    }

    // ------------------------------------------------------------ compte cloud

    /// <summary>Au démarrage : restaure la session enregistrée et récupère la licence du compte.</summary>
    private async Task InitCloudAsync()
    {
        try
        {
            if (await _cloud.RestoreSessionAsync())
            {
                UpdateAccountButton();
                await PullLicenseFromCloudAsync();
            }
        }
        catch { /* hors-ligne : on reste en mode local */ }
    }

    private void OnOpenAccount(object sender, RoutedEventArgs e)
    {
        if (_cloud.IsSignedIn)
        {
            var choice = MessageBox.Show(
                $"Connecté en tant que : {_cloud.Session!.Email}\n\nVoulez-vous vous déconnecter de ce compte ?",
                "Mon compte", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Yes)
            {
                _cloud.SignOut();
                UpdateAccountButton();
                Log("Compte déconnecté.");
            }
            return;
        }

        var dialog = new LoginWindow(_cloud) { Owner = this };
        dialog.ShowDialog();
        if (_cloud.IsSignedIn)
        {
            UpdateAccountButton();
            Log($"Connecté : {_cloud.Session!.Email}");
            // Après connexion : on récupère la licence du compte, sinon on y pousse la licence locale.
            _ = AfterSignInSyncAsync();
        }
    }

    private async Task AfterSignInSyncAsync()
    {
        bool restored = await PullLicenseFromCloudAsync();
        if (!restored)
            await PushLicenseToCloudAsync(); // le compte n'avait pas de licence → on y met la nôtre si activée
    }

    /// <summary>Récupère la licence du compte cloud et l'active localement si présente. Vrai si activée.</summary>
    private async Task<bool> PullLicenseFromCloudAsync()
    {
        var cloud = await _cloud.FetchLicenseAsync();
        if (cloud is null || string.IsNullOrWhiteSpace(cloud.LicenseKey))
            return false;

        var check = _license.Activate(cloud.LicenseKey, DateTimeOffset.UtcNow);
        if (check.Valid)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshLicense();
                Log("Licence restaurée depuis votre compte.");
            });
            return true;
        }
        return false;
    }

    /// <summary>Enregistre la licence actuellement active dans le compte cloud.</summary>
    private async Task PushLicenseToCloudAsync()
    {
        if (!_cloud.IsSignedIn) return;
        var status = _license.GetStatus(DateTimeOffset.UtcNow);
        if (status.State != LicenseState.Licensed || status.License is null) return;

        string tier = status.License.Tier.ToString().ToLowerInvariant();
        await _cloud.SaveLicenseAsync(
            File.Exists(LicenseKeyFilePath()) ? File.ReadAllText(LicenseKeyFilePath()).Trim() : "",
            tier, status.License.ExpiresUtc);
    }

    private static string LicenseKeyFilePath()
        => Path.Combine(LicenseManager.DefaultStorageDir(), "license.key");

    private void UpdateAccountButton()
    {
        if (AccountButton is null) return;
        if (_cloud.IsSignedIn)
        {
            AccountButton.Content = "✓ Compte";
            AccountButton.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x4D, 0x3A));
            AccountButton.ToolTip = $"Connecté : {_cloud.Session!.Email} — cliquez pour vous déconnecter";
        }
        else
        {
            AccountButton.Content = "Compte";
            AccountButton.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x35, 0x50));
            AccountButton.ToolTip = "Se connecter (Google ou e-mail) pour retrouver sa licence après réinstallation";
        }
    }

    private void ProposeAccountAfterActivation()
    {
        var choice = MessageBox.Show(
            "Licence activée ✓\n\nVoulez-vous créer un compte (Google ou e-mail) pour lier cette licence ?\n" +
            "Ainsi, après une réinstallation, il suffira de vous reconnecter — sans ressaisir la clé.",
            "Lier ma licence à un compte", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (choice == MessageBoxResult.Yes)
            OnOpenAccount(this, new RoutedEventArgs());
    }

    private void OnDeactivateLicense(object sender, RoutedEventArgs e)
    {
        _license.Deactivate();
        RefreshLicense();
        Log("Licence désactivée (retour en mode essai).");
    }

    /// <summary>Voile « Licence requise » → ouvre la boutique IATECHFUTUR.</summary>
    private void OnBuyLicenseFromLock(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                Branding.ShopUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossible d'ouvrir la boutique : {ex.Message}\n\nRendez-vous sur {Branding.ShopUrl}",
                "IATECH-SHIELD PRO", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>Voile « Licence requise » → fenêtre d'activation (saisie de la clé achetée).</summary>
    private void OnActivateFromLock(object sender, RoutedEventArgs e)
    {
        OnOpenLicense(this, new RoutedEventArgs());
    }

    private bool EnsureActivated()
    {
        if (_activated)
            return true;
        OnOpenLicense(this, new RoutedEventArgs());
        return _activated;
    }

    // ----------------------------------------------------------- analyse rapide

    private async void OnQuickScan(object sender, RoutedEventArgs e)
    {
        if (_scanning || _scanner is null || !EnsureActivated())
            return;

        // « Analyse rapide » lance directement l'analyse de TOUT l'ordinateur
        // (tous les disques fixes/amovibles prêts), sans boîte de dialogue.
        var targets = AllComputerTargets();
        if (targets.Count == 0)
        {
            ScanStatusText.Text = "Aucun disque accessible à analyser.";
            return;
        }

        ScanStatusText.Text = "Analyse de l'ordinateur en cours…";
        await RunScanAsync(targets, status => ScanStatusText.Text = status);
        if (_threats.Count > 0)
            ScanStatusText.Text = $"{_threats.Count} menace(s) — voir l'onglet Scan pour agir.";
    }

    /// <summary>Tous les disques fixes et amovibles prêts (analyse complète de l'ordinateur).</summary>
    private static List<string> AllComputerTargets()
    {
        var targets = new List<string>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                if (d.DriveType is DriveType.Fixed or DriveType.Removable)
                    targets.Add(d.RootDirectory.FullName);
            }
            catch { /* disque inaccessible : ignoré */ }
        }
        return targets;
    }

    // ------------------------------------------------------------ scan complet -

    private async void OnFullScan(object sender, RoutedEventArgs e)
    {
        if (_scanning || _scanner is null || !EnsureActivated())
            return;

        var targets = BuildScanTargets();
        if (targets.Count == 0)
        {
            FullScanStatus.Text = "Sélectionnez au moins une option à analyser.";
            return;
        }

        await RunScanAsync(targets, status => FullScanStatus.Text = status);

        // Action après scan (extinction / redémarrage).
        if (PostShutdown.IsChecked == true)
            SchedulePower("/s", "extinction");
        else if (PostRestart.IsChecked == true)
            SchedulePower("/r", "redémarrage");
    }

    private List<string> BuildScanTargets()
    {
        var targets = new List<string>();
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? p)
        {
            if (!string.IsNullOrEmpty(p) && set.Add(p))
                targets.Add(p);
        }

        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                bool fixedDrive = d.DriveType == DriveType.Fixed;
                bool removable = d.DriveType == DriveType.Removable;
                if ((ChkAllFiles.IsChecked == true) ||
                    (ChkHardDrives.IsChecked == true && fixedDrive) ||
                    (ChkRemovable.IsChecked == true && removable))
                    Add(d.RootDirectory.FullName);
            }
            catch { /* lecteur indisponible */ }
        }

        if (ChkFolder.IsChecked == true)
        {
            var dlg = new OpenFolderDialog { Title = "Dossier à analyser", InitialDirectory = DefaultScanFolder() };
            if (dlg.ShowDialog(this) == true) Add(dlg.FolderName);
        }
        if (ChkFile.IsChecked == true)
        {
            var dlg = new OpenFileDialog { Title = "Fichier à analyser" };
            if (dlg.ShowDialog(this) == true) Add(dlg.FileName);
        }
        if (ChkMail.IsChecked == true)
            foreach (var folder in MailFolders())
                Add(folder);
        if (ChkStartup.IsChecked == true)
            foreach (var file in StartupFiles())
                Add(file);

        return targets;
    }

    private static IEnumerable<string> MailFolders()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string[] candidates =
        {
            Path.Combine(local, "Microsoft", "Outlook"),
            Path.Combine(local, "Microsoft", "Windows Live Mail"),
            Path.Combine(roaming, "Thunderbird", "Profiles")
        };
        foreach (var c in candidates)
            if (Directory.Exists(c)) yield return c;
    }

    private IEnumerable<string> StartupFiles()
    {
        foreach (var entry in _registry.ListAutoRuns())
        {
            string exe = ExtractExePath(entry.Command);
            if (File.Exists(exe)) yield return exe;
        }
    }

    private static string ExtractExePath(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 0 ? command.Substring(1, end - 1) : command.Trim('"');
        }
        int space = command.IndexOf(' ');
        return space > 0 ? command[..space] : command;
    }

    private async Task RunScanAsync(IReadOnlyList<string> targets, Action<string> report)
    {
        _scanning = true;
        _scanCts = new CancellationTokenSource();
        var cancel = _scanCts.Token;
        _scanPaused = false;
        _scanPauseGate.Set();              // démarre en position « actif »
        ScanButton.IsEnabled = false;
        if (FullScanButton is not null) FullScanButton.IsEnabled = false;
        ShowStopButtons(true);
        ResetThreats();
        SetProgress(0, indeterminate: true, visible: true);

        // Pas de quarantaine automatique : on liste les menaces et l'utilisateur choisit l'action.
        var service = new ScanService(_scanner!, quarantine: null);
        int threats = 0;
        Log($"Analyse démarrée ({targets.Count} cible(s)).");
        SoundFx.ScanStart();

        try
        {
            // 1) Inventaire des fichiers (permet une progression déterminée).
            //    On énumère fichier par fichier en vérifiant l'annulation à chaque pas :
            //    le bouton « Arrêter » réagit même pendant l'inventaire d'un disque entier.
            report("Préparation : inventaire des fichiers…");
            var allFiles = await Task.Run(() =>
            {
                var list = new List<string>();
                int seen = 0;
                foreach (string target in targets)
                {
                    cancel.ThrowIfCancellationRequested();
                    IEnumerable<string> files;
                    try { files = ScanService.EnumerateFiles(target); }
                    catch { continue; /* cible introuvable : ignorée */ }

                    foreach (string file in files)
                    {
                        _scanPauseGate.Wait(cancel);   // respecte la pause
                        cancel.ThrowIfCancellationRequested();
                        list.Add(file);
                        if (++seen % 500 == 0)
                        {
                            int snap = list.Count;
                            Dispatcher.Invoke(() => report($"Préparation : {snap} fichiers repérés…"));
                        }
                    }
                }
                return list;
            }, cancel);

            int total = allFiles.Count;
            Log($"{total} fichier(s) à analyser sur {Environment.ProcessorCount} cœur(s).");
            SetProgress(0, indeterminate: total == 0, visible: true);

            // 2) Analyse parallèle (multi-cœurs) avec progression.
            int lastPercent = -1;
            await Task.Run(() =>
            {
                service.ScanFilesParallel(allFiles, (result, scanned) =>
                {
                    _scanPauseGate.Wait(cancel);   // chaque worker se met en pause si demandé

                    if (result.IsThreat)
                    {
                        Interlocked.Increment(ref threats);
                        string sha = SafeSha(result.Path);
                        string name = result.Match!.Name;
                        string path = result.Path;
                        Dispatcher.Invoke(() =>
                        {
                            _threats.Add(new ThreatItem { Name = name, Path = path, Sha = sha });
                            RegisterThreat();
                            UpdateThreatUi();
                            Log($"MENACE : {name} — {path}");
                            SoundFx.Threat();
                            Notify("Menace détectée — cliquez pour agir", $"{name}\n{path}", "Scan");
                            RecordIncident("Analyse", IncidentSeverity.Critical, $"Menace détectée : {name}", path);
                        });
                    }

                    int percent = total > 0 ? (int)(scanned * 100L / total) : 0;
                    if (percent != lastPercent || scanned % 50 == 0)
                    {
                        lastPercent = percent;
                        int snapThreats = Volatile.Read(ref threats);
                        Dispatcher.Invoke(() =>
                        {
                            SetProgress(percent, indeterminate: total == 0, visible: true);
                            report($"Analyse… {scanned}/{total} fichiers ({percent}%), {snapThreats} menace(s)");
                        });
                    }
                }, cancel: cancel);
            }, cancel);

            SetProgress(100, indeterminate: false, visible: false);
            report($"Terminé : {total} fichiers analysés, {threats} menace(s).");
            _threatCount = _threats.Count;
            UpdateThreatUi();
            Log($"Analyse terminée : {total} fichiers, {threats} menace(s).");
            SoundFx.ScanDone();
        }
        catch (OperationCanceledException)
        {
            // Arrêt demandé par l'utilisateur (bouton « Arrêter »).
            SetProgress(0, indeterminate: false, visible: false);
            report($"⏹ Analyse arrêtée — {threats} menace(s) trouvée(s) avant l'arrêt.");
            _threatCount = _threats.Count;
            UpdateThreatUi();
            Log("Analyse arrêtée par l'utilisateur.");
        }
        catch (Exception ex)
        {
            SetProgress(0, indeterminate: false, visible: false);
            report($"Échec : {ex.Message}");
            Log($"Échec de l'analyse : {ex.Message}");
        }
        finally
        {
            _scanning = false;
            ShowStopButtons(false);
            _scanCts?.Dispose();
            _scanCts = null;
            RefreshLicense();
        }
    }

    /// <summary>Arrête le scan en cours (annulation coopérative).</summary>
    private void OnStopScan(object sender, RoutedEventArgs e)
    {
        if (_scanCts is { IsCancellationRequested: false })
        {
            _scanCts.Cancel();
            Log("Arrêt du scan demandé…");
            // Retour visuel immédiat (l'arrêt effectif suit dans la foulée).
            if (StopScanButton is not null) StopScanButton.IsEnabled = false;
            if (StopFullScanButton is not null) StopFullScanButton.IsEnabled = false;
            if (ScanStatusText is not null) ScanStatusText.Text = "⏹ Arrêt en cours…";
            if (FullScanStatus is not null) FullScanStatus.Text = "⏹ Arrêt en cours…";
        }
    }

    /// <summary>Met en pause ou reprend le scan en cours.</summary>
    private void OnPauseScan(object sender, RoutedEventArgs e)
    {
        if (!_scanning) return;
        _scanPaused = !_scanPaused;
        if (_scanPaused)
        {
            _scanPauseGate.Reset();   // ferme la barrière → les workers s'arrêtent
            Log("Analyse en pause.");
            if (ScanStatusText is not null) ScanStatusText.Text = "⏸ Analyse en pause.";
            if (FullScanStatus is not null) FullScanStatus.Text = "⏸ Analyse en pause.";
        }
        else
        {
            _scanPauseGate.Set();      // rouvre la barrière → reprise
            Log("Analyse reprise.");
        }
        UpdatePauseButtons();
    }

    private void UpdatePauseButtons()
    {
        string label = _scanPaused ? "▶ REPRENDRE" : "⏸ PAUSE";
        if (PauseScanButton is not null) PauseScanButton.Content = label;
        if (PauseFullScanButton is not null) PauseFullScanButton.Content = label;
    }

    /// <summary>Affiche / masque les boutons « Pause » et « Arrêter » des deux pages de scan.</summary>
    private void ShowStopButtons(bool show)
    {
        var vis = show ? Visibility.Visible : Visibility.Collapsed;
        if (StopScanButton is not null) { StopScanButton.Visibility = vis; StopScanButton.IsEnabled = show; }
        if (StopFullScanButton is not null) { StopFullScanButton.Visibility = vis; StopFullScanButton.IsEnabled = show; }
        if (PauseScanButton is not null) { PauseScanButton.Visibility = vis; PauseScanButton.IsEnabled = show; }
        if (PauseFullScanButton is not null) { PauseFullScanButton.Visibility = vis; PauseFullScanButton.IsEnabled = show; }
        if (show) { _scanPaused = false; UpdatePauseButtons(); }
    }

    /// <summary>Met à jour les deux barres de progression du scan (rapide + complet).</summary>
    private void SetProgress(int percent, bool indeterminate, bool visible)
    {
        foreach (var bar in new[] { ScanProgress, FullScanProgress })
        {
            if (bar is null) continue;
            bar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            bar.IsIndeterminate = indeterminate;
            if (!indeterminate) bar.Value = percent;
        }
    }

    private static string SafeSha(string path)
    {
        try { return Scanner.ComputeSha256(path); } catch { return ""; }
    }

    // Applique l'action choisie dans la liste déroulante d'une menace.
    private void OnApplyThreatAction(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ThreatItem item)
            return;

        try
        {
            switch (item.SelectedAction)
            {
                case "Supprimer":
                    if (File.Exists(item.Path)) File.Delete(item.Path);
                    Log($"Menace supprimée : {item.Path}");
                    break;

                case "Mettre en quarantaine":
                    _quarantine?.Add(item.Path, item.Name, item.Sha);
                    Log($"Menace mise en quarantaine : {item.Path}");
                    break;

                case "Analyser":
                    string info = $"Menace : {item.Name}\nFichier : {item.Path}\nSHA-256 : {item.Sha}";
                    MessageBox.Show(this, info, "Analyse de la menace", MessageBoxButton.OK, MessageBoxImage.Information);
                    return; // on garde la menace dans la liste

                case "Ignorer":
                    Log($"Menace ignorée : {item.Path}");
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Action impossible : {ex.Message}", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _threats.Remove(item);
        _threatCount = _threats.Count;
        UpdateThreatUi();
    }

    private void SchedulePower(string flag, string label)
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown", $"{flag} /t 60 /c \"IATECH-SHIELD : {label} après analyse\"")
            { CreateNoWindow = true, UseShellExecute = false });
            Log($"{label} planifié dans 60 s (annuler : shutdown /a).");
            MessageBox.Show(this,
                $"L'ordinateur va procéder à un {label} dans 60 secondes.\n\n" +
                "Pour annuler : ouvrez une invite de commande et tapez  shutdown /a",
                "IATECH-SHIELD PRO", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Log($"Action après scan impossible : {ex.Message}");
        }
    }

    // ----------------------------------------------------------- temps réel ---

    private void OnRealtimeToggled(object sender, RoutedEventArgs e)
    {
        if (!_ready || _scanner is null)
            return;

        if (RealtimeSwitch.IsChecked == true)
        {
            string folder = DefaultScanFolder();
            try
            {
                _monitor = new RealtimeMonitor(_scanner, folder);
                _monitor.ThreatDetected += OnRealtimeThreat;
                _monitor.Start();
                ScanStatusText.Text = $"Surveillance temps réel active : {folder}";
                Log($"Temps réel activé : {folder}");
            }
            catch (Exception ex)
            {
                ScanStatusText.Text = $"Impossible d'activer le temps réel : {ex.Message}";
                RealtimeSwitch.IsChecked = false;
            }
        }
        else
        {
            // Protection anti-altération : exige le mot de passe pour désactiver.
            if (!RequireTamperAuth("Désactiver la surveillance en temps réel"))
            {
                RealtimeSwitch.IsChecked = true; // on annule la désactivation
                return;
            }
            _monitor?.Dispose();
            _monitor = null;
            ScanStatusText.Text = "Surveillance temps réel désactivée.";
            Log("Temps réel désactivé.");
        }
    }

    // -------------------------------------------- protection anti-altération ---

    private string? _tamperHash;
    private bool _tamperLoaded;

    private bool TamperEnabled
    {
        get
        {
            if (!_tamperLoaded)
            {
                _tamperLoaded = true;
                _tamperHash = SecretVault.Load("tamper").GetValueOrDefault("pwd");
            }
            return !string.IsNullOrEmpty(_tamperHash);
        }
    }

    /// <summary>Demande le mot de passe de protection ; true si absent ou correct.</summary>
    private bool RequireTamperAuth(string action)
    {
        if (!TamperEnabled) return true;

        string message = $"{action} — saisissez le mot de passe de protection.";
        for (int attempt = 0; attempt < 5; attempt++)
        {
            var prompt = new PromptWindow("Protection par mot de passe", message, "Déverrouiller") { Owner = this };
            if (prompt.ShowDialog() != true)
                return false;
            if (SecretHash.Verify(prompt.Value, _tamperHash!))
                return true;
            message = "Mot de passe incorrect. Réessayez.";
        }
        return false;
    }

    private void OnSetTamperPassword(object sender, RoutedEventArgs e)
    {
        // Changer un mot de passe existant exige d'abord l'ancien.
        if (TamperEnabled && !RequireTamperAuth("Modifier le mot de passe de protection"))
            return;

        string pwd = TamperPwdInput.Password;
        if (pwd.Length < 4)
        {
            TamperStatus.Text = "Le mot de passe doit faire au moins 4 caractères.";
            return;
        }

        _tamperHash = SecretHash.Hash(pwd);
        SecretVault.Save("tamper", new Dictionary<string, string> { ["pwd"] = _tamperHash });
        TamperPwdInput.Clear();
        TamperStatus.Text = "Protection par mot de passe activée.";
        Log("Protection anti-altération activée.");
    }

    private void OnClearTamperPassword(object sender, RoutedEventArgs e)
    {
        if (!TamperEnabled)
        {
            TamperStatus.Text = "Aucun mot de passe défini.";
            return;
        }
        if (!RequireTamperAuth("Retirer le mot de passe de protection"))
            return;

        _tamperHash = null;
        SecretVault.Delete("tamper");
        TamperStatus.Text = "Protection par mot de passe retirée.";
        Log("Protection anti-altération retirée.");
    }

    // ---------------------------------------------- verrou de session (PIN) ---

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct LastInputInfo { public uint cbSize; public uint dwTime; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);

    private DispatcherTimer? _lockTimer;
    private string? _lockPinHash;
    private string? _lockPasswordHash;
    private bool _lockHello;
    private int _lockDelayMs = 5 * 60 * 1000;
    private bool _lockShowing;
    private bool _lockSettingsLoaded;

    private bool LockConfigured => !string.IsNullOrEmpty(_lockPinHash) || !string.IsNullOrEmpty(_lockPasswordHash) || _lockHello;

    private void LoadLockConfig()
    {
        var cfg = SecretVault.Load("lock");
        _lockPinHash = cfg.GetValueOrDefault("pin");
        _lockPasswordHash = cfg.GetValueOrDefault("password");
        _lockHello = cfg.GetValueOrDefault("hello") == "1";
        if (cfg.TryGetValue("delay", out var d) && int.TryParse(d, out int min) && min > 0)
            _lockDelayMs = min * 60 * 1000;
    }

    private void StartLockWatcher()
    {
        LoadLockConfig();
        if (string.IsNullOrEmpty(_lockPinHash) && string.IsNullOrEmpty(_lockPasswordHash)) return;

        _lockTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _lockTimer.Tick -= OnLockTick;
        _lockTimer.Tick += OnLockTick;
        _lockTimer.Start();
    }

    private void OnLockTick(object? sender, EventArgs e)
    {
        if (_lockShowing) return;
        if (string.IsNullOrEmpty(_lockPinHash) && string.IsNullOrEmpty(_lockPasswordHash)) return;
        if (IdleMilliseconds() >= _lockDelayMs)
            ShowLockScreen();
    }

    private static uint IdleMilliseconds()
    {
        var info = new LastInputInfo { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return 0;
        return (uint)Environment.TickCount - info.dwTime;
    }

    private void ShowLockScreen()
    {
        if (_lockShowing) return;
        _lockShowing = true;
        try
        {
            ShowFromTray();
            var lockScreen = new LockScreen(_lockPinHash, _lockPasswordHash, _lockHello) { Owner = this };
            lockScreen.ShowDialog();
        }
        catch { /* en cas d'échec d'affichage, on ne bloque pas l'utilisateur */ }
        finally { _lockShowing = false; }
    }

    /// <summary>
    /// Écran de veille : plein écran noir + cœur battant. À la sortie, on enchaîne
    /// sur l'écran de verrouillage IATECH (PIN / mot de passe) si configuré.
    /// </summary>
    private void OnScreensaver(object sender, RoutedEventArgs e)
    {
        try
        {
            var saver = new ScreensaverWindow { Owner = this };
            saver.ShowDialog();
        }
        catch { /* affichage impossible : on enchaîne quand même sur le verrou */ }

        SoundFx.Welcome();   // petit accueil sonore au réveil

        // Au réveil : verrouillage IATECH (sinon, rien — l'utilisateur revient au dashboard).
        LoadLockConfig();
        if (LockConfigured)
            ShowLockScreen();
    }

    // --- Carte « Verrouillage du PC » (mot de passe / PIN / Windows Hello) ---

    private void OnLockPcNow(object sender, RoutedEventArgs e)
    {
        LoadLockConfig();
        if (LockConfigured)
        {
            ShowLockScreen();
            return;
        }
        // Aucun identifiant IATECH défini : repli sur le verrouillage Windows.
        try { LockWorkStation(); Log("Verrouillage de la session Windows."); }
        catch (Exception ex) { PcLockStatus.Text = $"Verrouillage impossible : {ex.Message}"; }
    }

    private void OnSetPcLock(object sender, RoutedEventArgs e)
    {
        string pwd = PcLockPassword.Password;
        string pin = PcLockPin.Password;
        bool hello = PcHelloCheck.IsChecked == true;

        if (string.IsNullOrEmpty(pwd) && string.IsNullOrEmpty(pin) && !hello)
        {
            PcLockStatus.Text = "Définissez au moins un mot de passe, un PIN, ou activez Windows Hello.";
            return;
        }

        var cfg = SecretVault.Load("lock");
        if (!string.IsNullOrEmpty(pwd)) cfg["password"] = SecretHash.Hash(pwd);
        if (!string.IsNullOrEmpty(pin)) cfg["pin"] = SecretHash.Hash(pin);
        cfg["hello"] = hello ? "1" : "0";
        if (!cfg.ContainsKey("delay")) cfg["delay"] = "5";
        SecretVault.Save("lock", cfg);

        PcLockPassword.Clear();
        PcLockPin.Clear();
        LoadLockConfig();
        StartLockWatcher();

        var methods = new List<string>();
        if (!string.IsNullOrEmpty(_lockPasswordHash)) methods.Add("mot de passe");
        if (!string.IsNullOrEmpty(_lockPinHash)) methods.Add("PIN");
        if (_lockHello) methods.Add("Windows Hello");
        PcLockStatus.Text = $"Verrouillage configuré : {string.Join(" + ", methods)}.";
        Log($"Verrouillage du PC configuré ({string.Join("+", methods)}).");
    }

    private async void OnToggleHello(object sender, RoutedEventArgs e)
    {
        if (PcHelloCheck.IsChecked != true) { PcHelloStatus.Text = ""; return; }
        bool available = await BiometricAuth.IsAvailableAsync();
        PcHelloStatus.Text = available
            ? "Windows Hello est disponible sur ce PC."
            : "⚠ Windows Hello n'est pas configuré sur ce PC (Paramètres Windows → Comptes → Options de connexion).";
        if (!available) PcHelloCheck.IsChecked = false;
    }

    private void LoadLockSettings()
    {
        if (_lockSettingsLoaded) return;
        _lockSettingsLoaded = true;
        var cfg = SecretVault.Load("lock");
        if (cfg.TryGetValue("delay", out var d)) LockDelayInput.Text = d;
        LockStatus.Text = string.IsNullOrEmpty(cfg.GetValueOrDefault("pin"))
            ? "Verrou désactivé." : "Verrou actif.";
    }

    private void OnEnableLock(object sender, RoutedEventArgs e)
    {
        string pin = LockPinInput.Password;
        if (pin.Length < 4)
        {
            LockStatus.Text = "Le PIN doit faire au moins 4 chiffres/caractères.";
            return;
        }
        int min = int.TryParse(LockDelayInput.Text.Trim(), out int m) && m > 0 ? m : 5;
        _lockPinHash = SecretHash.Hash(pin);
        _lockDelayMs = min * 60 * 1000;
        SecretVault.Save("lock", new Dictionary<string, string>
        {
            ["pin"] = _lockPinHash,
            ["delay"] = min.ToString()
        });
        LockPinInput.Clear();
        LockStatus.Text = $"Verrou actif — après {min} min d'inactivité.";
        Log($"Verrou de session activé ({min} min).");
        StartLockWatcher();
    }

    private void OnDisableLock(object sender, RoutedEventArgs e)
    {
        // Désactiver le verrou exige le PIN (ou le mot de passe anti-altération).
        if (!string.IsNullOrEmpty(_lockPinHash))
        {
            var prompt = new PromptWindow("Verrou de session",
                "Saisissez le PIN pour désactiver le verrou.", "Désactiver") { Owner = this };
            if (prompt.ShowDialog() != true || !SecretHash.Verify(prompt.Value, _lockPinHash))
            {
                LockStatus.Text = "PIN incorrect — verrou conservé.";
                return;
            }
        }
        _lockPinHash = null;
        _lockTimer?.Stop();
        SecretVault.Delete("lock");
        LockStatus.Text = "Verrou désactivé.";
        Log("Verrou de session désactivé.");
    }

    private void OnSessionLockNow(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lockPinHash))
        {
            LockStatus.Text = "Définissez d'abord un PIN, puis « Activer ».";
            return;
        }
        ShowLockScreen();
    }

    private void OnRealtimeThreat(FileScanResult result)
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                string sha = Scanner.ComputeSha256(result.Path);
                _quarantine?.Add(result.Path, result.Match!.Name, sha);
            }
            catch { }
            RegisterThreat();
            UpdateThreatUi();
            ScanStatusText.Text = $"Menace bloquée : {Path.GetFileName(result.Path)}";
            Log($"Temps réel — menace bloquée : {result.Path}");
            SoundFx.Threat();
            Notify("Menace bloquée (temps réel)", Path.GetFileName(result.Path), "Scan");
            RecordIncident("Temps réel", IncidentSeverity.Critical, "Menace bloquée en temps réel", result.Path);
        });
    }

    // -------------------------------------------------------- anti-ransomware -

    private void OnRansomwareToggled(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (RansomwareSwitch.IsChecked == true)
        {
            string[] folders =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            try
            {
                _ransomGuard = new RansomwareGuard(folders);
                _ransomGuard.Alert += OnRansomwareAlert;
                _ransomGuard.Start();
                ScanStatusText.Text = "Bouclier anti-ransomware actif.";
                Log("Anti-ransomware activé.");
            }
            catch (Exception ex)
            {
                ScanStatusText.Text = $"Impossible d'activer le bouclier : {ex.Message}";
                RansomwareSwitch.IsChecked = false;
            }
        }
        else
        {
            _ransomGuard?.RemoveCanaries();
            _ransomGuard?.Dispose();
            _ransomGuard = null;
            ScanStatusText.Text = "Bouclier anti-ransomware désactivé.";
            Log("Anti-ransomware désactivé.");
        }
    }

    private void OnRansomwareAlert(RansomwareAlert alert)
    {
        Dispatcher.Invoke(() =>
        {
            var culprit = _culler.FindTopWriter();
            string action = "aucun processus dominant identifié";
            if (culprit is not null && _culler.Kill(culprit.Pid))
                action = $"processus {culprit.Name} (PID {culprit.Pid}) arrêté";
            RegisterThreat();
            UpdateThreatUi();
            ScanStatusText.Text = $"⚠ RANSOMWARE : {alert.Reason} — {action}.";
            Log($"RANSOMWARE : {alert.Reason} — {action}.");
            SoundFx.Danger();
            Notify("⚠ Ransomware bloqué", $"{alert.Reason} — {action}", "Centre");
            RecordIncident("Ransomware", IncidentSeverity.Critical, "Comportement de rançongiciel bloqué", $"{alert.Reason} — {action}");
            CopilotAlert($"Comportement de type rançongiciel détecté : {alert.Reason}. Action : {action}. Je recommande de lancer une analyse complète et de vérifier vos sauvegardes.");
            _ransomGuard?.Rearm();
        });
    }

    // ------------------------------------------------------------- démarrage --

    private void OnAutostartToggled(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        string exe = Environment.ProcessPath ?? "";
        bool ok = AutostartSwitch.IsChecked == true
            ? _registry.EnableSelfAutostart(exe)
            : _registry.DisableSelfAutostart();
        Log(AutostartSwitch.IsChecked == true
            ? (ok ? "Inscrit au démarrage de Windows." : "Échec de l'inscription au démarrage.")
            : "Retiré du démarrage de Windows.");
    }

    // -------------------------------------------------------------- pare-feu --

    private void OnOpenFirewall(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("firewall.cpl") { UseShellExecute = true }); }
        catch (Exception ex) { Log($"Ouverture du pare-feu impossible : {ex.Message}"); }
    }

    private async void OnFwRefresh(object sender, RoutedEventArgs e)
    {
        FwDomainStatus.Text = FwPrivateStatus.Text = FwPublicStatus.Text = "Vérification…";
        FwDomainStatus.Text = Fmt(await RunPs("(Get-NetFirewallProfile -Name Domain).Enabled"));
        FwPrivateStatus.Text = Fmt(await RunPs("(Get-NetFirewallProfile -Name Private).Enabled"));
        FwPublicStatus.Text = Fmt(await RunPs("(Get-NetFirewallProfile -Name Public).Enabled"));
        Log("État des profils du pare-feu rafraîchi.");

        static string Fmt(string v) => v.Trim() == "True" ? "Le pare-feu est activé ✓" : "Le pare-feu est désactivé";
    }

    private async void OnFwAction(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string tag) return;
        var parts = tag.Split(' ');
        if (parts.Length != 2) return;
        string profile = parts[0], state = parts[1];
        try
        {
            await RunPs($"Set-NetFirewallProfile -Name {profile} -Enabled {state}");
            Log($"Pare-feu {profile} → {(state == "True" ? "activé" : "désactivé")}.");
            OnFwRefresh(sender, e);
        }
        catch (Exception ex)
        {
            Log($"Modification du pare-feu impossible : {ex.Message}");
        }
    }

    private void OnListConnections(object sender, RoutedEventArgs e)
    {
        ConnectionsList.Items.Clear();
        try
        {
            var conns = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections();
            foreach (var c in conns)
                ConnectionsList.Items.Add($"{c.LocalEndPoint}  →  {c.RemoteEndPoint}   [{c.State}]");
            FirewallStateText.Text = $"{conns.Length} connexion(s) TCP active(s).";
            Log($"Connexions TCP listées : {conns.Length}.");
        }
        catch (Exception ex)
        {
            ConnectionsList.Items.Add($"Erreur : {ex.Message}");
        }
    }

    // ---------------------------------------------------------- quarantaine ---

    private void OnOpenQuarantine(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = Path.Combine(DefaultQuarantineDir(), "store");
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Log($"Ouverture de la quarantaine impossible : {ex.Message}"); }
    }

    // -------------------------------------------------------------- journaux --

    private void Log(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _log.AppendLine(line);
        if (LogBox is not null)
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        }
    }

    private void OnSendLogMail(object sender, RoutedEventArgs e)
    {
        string to = EmailInput?.Text.Trim() ?? "";
        string body = _log.ToString();
        if (body.Length > 1500) body = body[^1500..]; // mailto limité : on garde la fin
        string subject = Uri.EscapeDataString("Journal IATECH-SHIELD PRO");
        string mailto = $"mailto:{to}?subject={subject}&body={Uri.EscapeDataString(body)}";
        try
        {
            Process.Start(new ProcessStartInfo(mailto) { UseShellExecute = true });
            Log("Ouverture du client mail pour l'envoi du journal.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossible d'ouvrir le client mail : {ex.Message}\n\n" +
                "Utilisez « Enregistrer le journal » puis joignez le fichier manuellement.",
                "Envoi par e-mail", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnSaveLog(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            FileName = $"iatech-shield-log-{DateTime.Now:yyyyMMdd-HHmm}.txt",
            Filter = "Fichier texte (*.txt)|*.txt"
        };
        if (dlg.ShowDialog(this) == true)
        {
            try { File.WriteAllText(dlg.FileName, _log.ToString()); Log($"Journal enregistré : {dlg.FileName}"); }
            catch (Exception ex) { Log($"Enregistrement impossible : {ex.Message}"); }
        }
    }

    private void OnClearLog(object sender, RoutedEventArgs e)
    {
        _log.Clear();
        LogBox.Clear();
    }

    // -------------------------------------------------------------- USB --------

    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private HashSet<string> _knownDrives = new(StringComparer.OrdinalIgnoreCase);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DEVICECHANGE && wParam.ToInt32() == DBT_DEVICEARRIVAL)
            foreach (string drive in GetReadyRemovableDrives())
                if (_knownDrives.Add(drive))
                    AutoScanDrive(drive);
        else if (msg == WM_CLIPBOARDUPDATE)
            OnClipboardChanged();
        return IntPtr.Zero;
    }

    private static IEnumerable<string> GetReadyRemovableDrives()
    {
        foreach (var d in DriveInfo.GetDrives())
        {
            bool ok = false;
            try { ok = d.IsReady && d.DriveType == DriveType.Removable; } catch { }
            if (ok) yield return d.RootDirectory.FullName;
        }
    }

    private async void AutoScanDrive(string drive)
    {
        if (_scanner is null) return;
        SoundFx.NewDevice();   // matériel détecté
        ScanStatusText.Text = $"Clé USB détectée ({drive}) — analyse automatique…";
        Log($"Clé USB détectée : {drive} — analyse automatique.");
        var service = new ScanService(_scanner, _quarantine);
        try
        {
            var report = await Task.Run(() => service.Scan(drive));
            if (report.Threats.Count > 0)
            {
                _threatCount += report.Threats.Count;
                UpdateThreatUi();
            }
            ScanStatusText.Text = $"USB {drive} : {report.FilesScanned} fichiers, {report.Threats.Count} menace(s).";
            Log($"USB {drive} : {report.FilesScanned} fichiers, {report.Threats.Count} menace(s).");
        }
        catch (Exception ex) { ScanStatusText.Text = $"Analyse USB échouée : {ex.Message}"; }
    }

    // ----------------------------------------------------------- assistant IA -

    private void OnAiAssistant(object sender, RoutedEventArgs e)
    {
        if (!AiAssistant.IsConfigured)
        {
            // Pas de clé : on emmène l'utilisateur sur le réglage (sans redémarrage).
            ShowPage("Settings");
            try { ApiKeyInput.Focus(); } catch { }
            MessageBox.Show(this,
                "Pour activer l'assistant IA, collez votre clé Anthropic ici, dans « Réglages → Assistant IA », puis cliquez « Enregistrer la clé ».\n\n" +
                "• La clé s'obtient sur console.anthropic.com (bouton « Obtenir une clé »).\n" +
                "• Elle est stockée chiffrée sur ce PC — aucun redémarrage nécessaire.",
                "Assistant IA — clé requise", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Clé présente : on ouvre le Copilote (conversation complète).
        ShowPage("Copilote");
        EnsureChatLoaded();
    }

    // ------------------------------------------------------------- affichage --

    private void ResetThreats() { _threats.Clear(); _threatCount = 0; UpdateThreatUi(); }
    private void RegisterThreat() => _threatCount++;

    private void UpdateThreatUi()
    {
        ThreatCountText.Text = _threatCount.ToString();
        bool safe = _threatCount == 0;
        var green = (Brush)FindResource("GreenBrush");
        var alert = new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x5C));

        ThreatCountText.Foreground = safe ? green : alert;
        ThreatStatusText.Foreground = safe ? green : alert;
        ThreatStatusText.Text = safe ? "SÉCURISÉ" : "MENACES DÉTECTÉES";
        ShieldStatusText.Foreground = safe ? green : alert;
        ShieldStatusText.Text = safe ? "PROTÉGÉ" : "À RISQUE";
        ReactorPulseText.Text = safe
            ? "Pulsations du cœur-réacteur : STABLES"
            : "Pulsations du cœur-réacteur : INSTABLES";

        // Le cœur-réacteur passe au rouge en cas de danger, cyan sinon.
        var accentColor = Color.FromRgb(0x22, 0xD3, 0xE8);
        var dangerColor = Color.FromRgb(0xFF, 0x4D, 0x4D);
        Color c = safe ? accentColor : dangerColor;
        var brush = new SolidColorBrush(c);

        HeartPath.Stroke = brush;
        CoreDot.Fill = brush;
        MainRing.Stroke = brush;
        HeartGlow.Color = c;
        CoreDotGlow.Color = c;
        MainRingGlow.Color = c;
    }

    // ------------------------------------------------- chiffrement de dossier --

    private async void OnEncryptFolder(object sender, RoutedEventArgs e)
    {
        string password = CryptoPassword.Password;
        if (string.IsNullOrEmpty(password))
        {
            CryptoStatus.Text = "Saisissez d'abord un mot de passe.";
            return;
        }

        var dlg = new OpenFolderDialog { Title = "Dossier à chiffrer" };
        if (dlg.ShowDialog(this) != true)
            return;

        bool deleteOriginal = MessageBox.Show(this,
            "Supprimer le dossier d'origine après chiffrement ?\n(Le contenu ne sera plus accessible sans le mot de passe.)",
            "Chiffrement", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        CryptoStatus.Text = "Chiffrement en cours…";
        string folder = dlg.FolderName;
        try
        {
            string output = await Task.Run(() => FolderCrypto.EncryptFolder(folder, password, deleteOriginal));
            CryptoStatus.Text = $"Dossier chiffré : {output}";
            Log($"Dossier chiffré : {output}");
        }
        catch (Exception ex)
        {
            CryptoStatus.Text = $"Échec : {ex.Message}";
        }
    }

    private async void OnDecryptFolder(object sender, RoutedEventArgs e)
    {
        string password = CryptoPassword.Password;
        if (string.IsNullOrEmpty(password))
        {
            CryptoStatus.Text = "Saisissez le mot de passe du fichier .iasx.";
            return;
        }

        var dlg = new OpenFileDialog { Title = "Fichier .iasx à déchiffrer", Filter = "Conteneur chiffré (*.iasx)|*.iasx" };
        if (dlg.ShowDialog(this) != true)
            return;

        CryptoStatus.Text = "Déchiffrement en cours…";
        string file = dlg.FileName;
        try
        {
            string dest = await Task.Run(() => FolderCrypto.DecryptFolder(file, password));
            CryptoStatus.Text = $"Dossier restauré : {dest}";
            Log($"Dossier déchiffré : {dest}");
        }
        catch (Exception ex)
        {
            CryptoStatus.Text = $"Échec : {ex.Message}";
        }
    }

    private async void OnProtectUsb(object sender, RoutedEventArgs e)
    {
        string password = CryptoPassword.Password;
        if (string.IsNullOrEmpty(password))
        {
            CryptoStatus.Text = "Saisissez d'abord un mot de passe (section chiffrement).";
            return;
        }

        string usb = GetReadyRemovableDrives().FirstOrDefault() ?? "";
        var dlg = new OpenFolderDialog { Title = "Dossier de la clé USB à protéger", InitialDirectory = usb };
        if (dlg.ShowDialog(this) != true)
            return;

        bool del = MessageBox.Show(this, "Supprimer le dossier d'origine sur la clé après chiffrement ?",
            "Clé USB protégée", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        CryptoStatus.Text = "Protection de la clé en cours…";
        string folder = dlg.FolderName;
        try
        {
            string output = await Task.Run(() => FolderCrypto.EncryptFolder(folder, password, del));
            CryptoStatus.Text = $"Clé USB protégée : {output}";
            Log($"Dossier USB chiffré : {output}");
        }
        catch (Exception ex)
        {
            CryptoStatus.Text = $"Échec : {ex.Message}";
        }
    }

    // ----------------------------------------------------------- verrouillage --

    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    private void OnLockNow(object sender, RoutedEventArgs e)
    {
        Log("Verrouillage de la session.");
        LockWorkStation();
    }

    private void OnWakePasswordToggled(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        string value = WakePasswordSwitch.IsChecked == true ? "1" : "0";
        try
        {
            RunPowercfg($"/SETACVALUEINDEX SCHEME_CURRENT SUB_NONE CONSOLELOCK {value}");
            RunPowercfg($"/SETDCVALUEINDEX SCHEME_CURRENT SUB_NONE CONSOLELOCK {value}");
            RunPowercfg("/SETACTIVE SCHEME_CURRENT");
            Log(WakePasswordSwitch.IsChecked == true
                ? "Mot de passe exigé à la sortie de veille : activé."
                : "Mot de passe à la sortie de veille : désactivé.");
        }
        catch (Exception ex)
        {
            Log($"Réglage de la veille impossible : {ex.Message}");
        }
    }

    private static void RunPowercfg(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("powercfg", args)
        { CreateNoWindow = true, UseShellExecute = false });
        p?.WaitForExit(4000);
    }

    // -------------------------------------------------------------- réseau -----

    private async void OnNetworkScan(object sender, RoutedEventArgs e)
    {
        NetScanButton.IsEnabled = false;
        _networkDevices.Clear();
        NetStatus.Text = "Analyse du réseau en cours…";
        Log("Analyse du réseau démarrée.");
        try
        {
            var scanner = new NetworkScanner();
            var devices = await scanner.ScanAsync(s => Dispatcher.Invoke(() => NetStatus.Text = s));
            foreach (var d in devices)
            {
                string key = d.Mac is not ("—" or "") ? d.Mac : d.Ip;
                string custom = _deviceNames.Get(key) ?? d.Name;
                _networkDevices.Add(new NetworkDeviceItem
                {
                    Ip = d.Ip, Mac = d.Mac, DiscoveredName = d.Name, CustomName = custom
                });
            }
            NetStatus.Text = $"{devices.Count} appareil(s) détecté(s).";
            Log($"Réseau : {devices.Count} appareil(s) détecté(s).");
        }
        catch (Exception ex)
        {
            NetStatus.Text = $"Échec : {ex.Message}";
        }
        finally
        {
            NetScanButton.IsEnabled = true;
        }
    }

    // Enregistre le nom personnalisé d'un appareil réseau (persistant entre les scans).
    private void OnRenameDevice(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not NetworkDeviceItem item)
            return;
        string name = (item.CustomName ?? "").Trim();
        if (name.Length == 0)
            return;
        _deviceNames.Set(item.Key, name);
        NetStatus.Text = $"Appareil renommé : {item.Ip} → {name}";
        Log($"Appareil réseau renommé : {item.Key} → {name}");
    }

    // ------------------------------------------------------------ analyse URL --

    private void OnCheckUrl(object sender, RoutedEventArgs e)
    {
        string url = UrlInput.Text.Trim();
        if (string.IsNullOrEmpty(url))
        {
            UrlVerdictText.Text = "Saisissez une URL.";
            UrlVerdictText.Foreground = (Brush)FindResource("TextMutedBrush");
            UrlReasonsText.Text = "";
            return;
        }

        var verdict = UrlReputation.Analyze(url);
        var green = (Brush)FindResource("GreenBrush");
        var accent = (Brush)FindResource("AccentBrush");
        var alert = new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x5C));

        UrlVerdictText.Foreground = verdict.Band switch
        {
            UrlBand.Safe => green,
            UrlBand.Suspicious => accent,
            _ => alert
        };
        UrlVerdictText.Text = $"{verdict.BandLabel}  —  {verdict.Score}/100";
        UrlReasonsText.Text = string.Join("\n", verdict.Reasons.Select(r => "• " + r));
        Log($"URL analysée : {url} → {verdict.BandLabel} ({verdict.Score}/100)");
    }

    // ---------------------------------------------------------- page Système --

    private void OnOpenSetting(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string target)
            OpenShell(target);
    }

    private async void OnRefreshPerf(object sender, RoutedEventArgs e)
    {
        try
        {
            var c = new DriveInfo("C");
            if (c.IsReady)
            {
                double freeGb = c.TotalFreeSpace / 1024d / 1024 / 1024;
                double totalGb = c.TotalSize / 1024d / 1024 / 1024;
                PerfDiskText.Text = $"Disque C: {freeGb:0} Go libres sur {totalGb:0} Go.";
            }
        }
        catch { PerfDiskText.Text = "Disque : information indisponible."; }

        try
        {
            string free = await RunPs("[math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1MB,1)");
            string total = await RunPs("[math]::Round((Get-CimInstance Win32_OperatingSystem).TotalVisibleMemorySize/1MB,1)");
            if (free.Length > 0 && total.Length > 0)
                PerfRamText.Text = $"Mémoire : {free} Go libres sur {total} Go.";
        }
        catch { PerfRamText.Text = "Mémoire : information indisponible."; }
    }

    // ----------------------------------------------------- sécurité appareil --

    private async void OnCheckDevice(object sender, RoutedEventArgs e)
    {
        DeviceCheckButton.IsEnabled = false;
        CoreIsolationStatus.Text = TpmStatus.Text = SecureBootStatus.Text = BitLockerStatus.Text = "Vérification…";
        try
        {
            string hvci = await RunPs(
                @"(Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity' -ErrorAction SilentlyContinue).Enabled");
            CoreIsolationStatus.Text = hvci.Trim() == "1" ? "Activée ✓" : "Désactivée";

            string tpm = await RunPs("(Get-Tpm).TpmReady");
            TpmStatus.Text = tpm.Trim() == "True" ? "Présent et prêt ✓" : "Non disponible / non prêt";

            string sb = await RunPs("try { Confirm-SecureBootUEFI } catch { 'N/A' }");
            SecureBootStatus.Text = sb.Trim() switch
            {
                "True" => "Activé ✓",
                "False" => "Désactivé",
                _ => "Non applicable (BIOS hérité)"
            };

            string bl = await RunPs("try { (Get-BitLockerVolume -MountPoint C:).ProtectionStatus } catch { 'N/A' }");
            BitLockerStatus.Text = bl.Contains("On") ? "Activé sur C: ✓"
                                 : bl.Contains("Off") ? "Désactivé sur C:" : "Indisponible";

            Log("État de sécurité de l'appareil vérifié.");
        }
        catch (Exception ex)
        {
            Log($"Vérification appareil impossible : {ex.Message}");
        }
        finally
        {
            DeviceCheckButton.IsEnabled = true;
        }
    }

    private void OnOpenTpm(object sender, RoutedEventArgs e) => OpenShell("tpm.msc");

    private void OnOpenBitlocker(object sender, RoutedEventArgs e) =>
        OpenShell("control", "/name Microsoft.BitLockerDriveEncryption");

    private void OpenShell(string file, string args = "")
    {
        try { Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true }); }
        catch (Exception ex) { Log($"Ouverture impossible ({file}) : {ex.Message}"); }
    }

    private static async Task<string> RunPs(string command)
    {
        var psi = new ProcessStartInfo("powershell",
            $"-NoProfile -NonInteractive -Command \"{command}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        if (p is null) return "";
        string output = await p.StandardOutput.ReadToEndAsync();
        p.WaitForExit(8000);
        return output.Trim();
    }

    // ----------------------------------------------------- options avancées ---

    private void OnSilentToggled(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        SoundFx.Muted = SilentSwitch.IsChecked == true;
        Log(SoundFx.Muted ? "Mode silencieux activé." : "Mode silencieux désactivé.");
    }

    private void OnScheduleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        string choice = (ScheduleCombo.SelectedValue as string) ?? "Désactivée";
        _scheduleTimer?.Stop();
        _scheduleTimer = null;

        TimeSpan? interval = choice switch
        {
            "Toutes les heures" => TimeSpan.FromHours(1),
            "Toutes les 6 heures" => TimeSpan.FromHours(6),
            "Une fois par jour" => TimeSpan.FromDays(1),
            _ => null
        };

        if (interval is null)
        {
            Log("Analyse planifiée désactivée.");
            return;
        }

        _scheduleTimer = new DispatcherTimer { Interval = interval.Value };
        _scheduleTimer.Tick += async (_, _) =>
        {
            if (_scanning || _scanner is null || !_activated) return;
            Log("Analyse planifiée déclenchée.");
            await RunScanAsync(new[] { DefaultScanFolder() }, s => ScanStatusText.Text = s);
        };
        _scheduleTimer.Start();
        Log($"Analyse planifiée : {choice}.");
    }

    private async void OnUpdateSignatures(object sender, RoutedEventArgs e)
    {
        string url = UpdateUrlInput.Text.Trim();
        if (string.IsNullOrEmpty(url))
        {
            UpdateStatus.Text = "Saisissez l'URL d'une base signatures.json.";
            return;
        }

        UpdateStatus.Text = "Téléchargement…";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            string json = await http.GetStringAsync(url);
            string dbPath = Path.Combine(AppContext.BaseDirectory, "signatures.json");

            // On valide le JSON avant de remplacer la base existante.
            string tmp = Path.GetTempFileName();
            await File.WriteAllTextAsync(tmp, json);
            var test = SignatureDatabase.LoadFromFile(tmp);

            File.Copy(tmp, dbPath, overwrite: true);
            File.Delete(tmp);

            _db = SignatureDatabase.LoadFromFile(dbPath);
            _scanner = new Scanner(_db);
            SignatureCountText.Text = $"{_db.Signatures.Count} signatures chargées";
            UpdateStatus.Text = $"Base mise à jour : {test.Signatures.Count} signatures.";
            UpdatesDateText.Text = $"Dernière mise à jour : {DateTime.Now:dd/MM/yyyy HH:mm}";
            Log($"Signatures mises à jour depuis {url} ({test.Signatures.Count}).");
        }
        catch (Exception ex)
        {
            UpdateStatus.Text = $"Échec : {ex.Message}";
        }
    }

    // ------------------------------------------------- gestion de la quarantaine

    private void OnRefreshQuarantine(object sender, RoutedEventArgs e)
    {
        if (_quarantine is null) return;
        _quarantineEntries = _quarantine.List().ToList();
        QuarantineList.Items.Clear();
        foreach (var entry in _quarantineEntries)
            QuarantineList.Items.Add($"{entry.ThreatName}  —  {entry.OriginalPath}");
        if (_quarantineEntries.Count == 0)
            QuarantineList.Items.Add("(quarantaine vide)");
    }

    private void OnRestoreQuarantine(object sender, RoutedEventArgs e)
    {
        int i = QuarantineList.SelectedIndex;
        if (_quarantine is null || i < 0 || i >= _quarantineEntries.Count) return;
        var entry = _quarantineEntries[i];
        if (_quarantine.Restore(entry.Id))
            Log($"Fichier restauré : {entry.OriginalPath}");
        OnRefreshQuarantine(sender, e);
    }

    private void OnDeleteQuarantine(object sender, RoutedEventArgs e)
    {
        int i = QuarantineList.SelectedIndex;
        if (_quarantine is null || i < 0 || i >= _quarantineEntries.Count) return;
        var entry = _quarantineEntries[i];
        if (_quarantine.Delete(entry.Id))
            Log($"Fichier supprimé définitivement : {entry.OriginalPath}");
        OnRefreshQuarantine(sender, e);
    }

    // -------------------------------------------------------- fenêtre / chrome -

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private bool _isFullscreen;
    private Rect _restoreBounds;

    private void OnToggleMaximize(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11) ToggleFullscreen();
        else if (e.Key == Key.Escape && _isFullscreen) ToggleFullscreen();
    }

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            _restoreBounds = new Rect(Left, Top, Width, Height);
            WindowState = WindowState.Normal;
            Left = 0; Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
            RootBorder.CornerRadius = new CornerRadius(0);
            MaximizeButton.Content = "\uE923"; // restaurer
            MaximizeButton.ToolTip = "Quitter le plein écran (F11)";
            _isFullscreen = true;
        }
        else
        {
            Left = _restoreBounds.Left; Top = _restoreBounds.Top;
            Width = _restoreBounds.Width; Height = _restoreBounds.Height;
            RootBorder.CornerRadius = new CornerRadius(16);
            MaximizeButton.Content = "\uE922"; // plein écran
            MaximizeButton.ToolTip = "Plein écran (F11)";
            _isFullscreen = false;
        }
    }

    // Le bouton X ne ferme pas l'app : il la réduit dans la barre des tâches.
    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // ---------------------------------------------------- barre des tâches ----

    private System.Windows.Forms.NotifyIcon? _tray;
    private bool _reallyExit;
    /// <summary>Onglet à ouvrir si l'utilisateur clique la dernière notification.</summary>
    private string? _notifyTarget;

    private void SetupTray()
    {
        try
        {
            _tray = new System.Windows.Forms.NotifyIcon
            {
                Visible = true,
                Text = "IATECH-SHIELD PRO — protection active"
            };
            try { _tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? ""); }
            catch { _tray.Icon = System.Drawing.SystemIcons.Shield; }

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Ouvrir IATECH-SHIELD", null, (_, _) => ShowFromTray());
            menu.Items.Add("Analyse rapide", null, (_, _) => { ShowFromTray(); OnQuickScan(this, new RoutedEventArgs()); });
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Quitter", null, (_, _) =>
            {
                ShowFromTray();
                if (!RequireTamperAuth("Quitter IATECH-SHIELD")) return;
                _reallyExit = true;
                Close();
            });
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (_, _) => ShowFromTray();
            // Clic sur la bulle de notification → ouvre l'app sur l'onglet concerné.
            _tray.BalloonTipClicked += (_, _) => OnNotificationClicked();
        }
        catch { /* la barre des tâches n'est pas critique */ }
    }

    /// <summary>Ouvre l'application et navigue vers l'onglet visé par la dernière notification.</summary>
    private void OnNotificationClicked()
    {
        ShowFromTray();
        if (_ready && _notifyTarget is { Length: > 0 } target)
            ShowPage(target);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true; Topmost = false;
    }

    /// <summary>
    /// Notification système (bulle dans la barre des tâches). <paramref name="target"/>
    /// est l'onglet ouvert si l'utilisateur clique la bulle (ex. « Scan » pour une menace).
    /// </summary>
    private void Notify(string title, string message, string? target = null)
    {
        _notifyTarget = target;
        if (_gamerMode) return; // notifications suspendues en mode Gamer
        try
        {
            // Notification « maison » futuriste (fond noir, triangle rouge, bordure cyan).
            Dispatcher.Invoke(() =>
            {
                var toast = new ToastWindow(title, message, () =>
                {
                    ShowFromTray();
                    if (_ready && target is { Length: > 0 }) ShowPage(target);
                });
                toast.Show();
            });
        }
        catch { /* repli silencieux */ }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_reallyExit)
        {
            // Fermeture = passage en arrière-plan (la protection continue).
            e.Cancel = true;
            Hide();
            Notify("IATECH-SHIELD PRO", "La protection continue en arrière-plan. Clic droit sur l'icône pour quitter.");
            return;
        }

        // Coupe le VPN à la fermeture réelle de l'application.
        if (_vpnConnected)
        {
            try { _vpn.DisconnectAsync().GetAwaiter().GetResult(); }
            catch { /* déconnexion best-effort */ }
        }

        _monitor?.Dispose();
        _ransomGuard?.RemoveCanaries();
        _ransomGuard?.Dispose();
        _tray?.Dispose();
        base.OnClosing(e);
    }
}

/// <summary>Une menace affichée dans la liste, avec l'action choisie par l'utilisateur.</summary>
public sealed class ThreatItem
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string Sha { get; init; } = "";
    public string SelectedAction { get; set; } = "Mettre en quarantaine";
}

/// <summary>Une entrée du coffre-fort (mot de passe). Masqué par défaut.</summary>
public sealed class VaultItem : System.ComponentModel.INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Url { get; set; } = "";
    public string Notes { get; set; } = "";

    private bool _revealed;
    public bool Revealed
    {
        get => _revealed;
        set { _revealed = value; OnChanged(nameof(Revealed)); OnChanged(nameof(DisplayPassword)); }
    }

    public string DisplayPassword => _revealed
        ? Password
        : (string.IsNullOrEmpty(Password) ? "" : new string('•', Math.Min(12, Math.Max(6, Password.Length))));

    public System.Windows.Visibility UrlVisibility =>
        string.IsNullOrWhiteSpace(Url) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

/// <summary>Un appareil affiché par le radar réseau.</summary>
/// <summary>Profil ADN d'un programme affiché dans la liste.</summary>
public sealed class DnaItem
{
    public string Name { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string Line { get; init; } = "";
    public int Score { get; init; }
    public string ScoreText { get; init; } = "";
    public Brush ScoreColor { get; init; } = Brushes.Gray;
}

public sealed class RadarItem
{
    public string Ip { get; init; } = "";
    public string Mac { get; init; } = "";
    public string TypeLabel { get; init; } = "";
    public string NameLine { get; init; } = "";
    public string PortsLine { get; init; } = "";
    public List<string> Risks { get; init; } = new();
    public bool IsNew { get; init; }
    public Visibility NewVisibility { get; init; } = Visibility.Collapsed;
    public Brush BorderBrush { get; init; } = Brushes.Gray;

    /// <summary>URL de l'interface d'administration (https si dispo, sinon http).</summary>
    public string AdminUrl { get; init; } = "";
    /// <summary>Risques regroupés en une ligne (pour les conseils).</summary>
    public string RisksText { get; init; } = "";
    /// <summary>Affiche la barre d'actions seulement si l'appareil présente un risque.</summary>
    public Visibility ActionsVisibility { get; init; } = Visibility.Collapsed;

    /// <summary>Score de sécurité de l'appareil (ex. « Sécurité 72% »).</summary>
    public string ScoreText { get; init; } = "";
    public Brush ScoreColor { get; init; } = Brushes.Gray;
}

/// <summary>Un message dans la conversation du copilote IA.</summary>
public sealed class ChatMessage
{
    public string Sender { get; init; } = "";
    public string Text { get; init; } = "";
    public Brush Bubble { get; init; } = Brushes.Transparent;
    public HorizontalAlignment Align { get; init; } = HorizontalAlignment.Left;
}

/// <summary>Un évènement de la chronologie d'investigation.</summary>
public sealed class TimelineItem
{
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Category { get; init; } = "";
    public string Time { get; init; } = "";
    public Brush Color { get; init; } = Brushes.Gray;
}

/// <summary>Un accès webcam/micro affiché.</summary>
public sealed class MediaItem
{
    public string Device { get; init; } = "";
    public string App { get; init; } = "";
    public string Status { get; init; } = "";
    public Brush Color { get; init; } = Brushes.Gray;
}

/// <summary>Une différence du jumeau numérique.</summary>
public sealed class DiffItem
{
    public string Kind { get; init; } = "";
    public string Category { get; init; } = "";
    public string Key { get; init; } = "";
    public string Change { get; init; } = "";
    public Brush Color { get; init; } = Brushes.Gray;
}

/// <summary>Une carte de pilier du tableau d'intégrité.</summary>
public sealed class PillarItem
{
    public string Name { get; init; } = "";
    public string Display { get; init; } = "";
    public string Detail { get; init; } = "";
    public Brush Color { get; init; } = Brushes.Gray;
}

/// <summary>Ligne d'un critère du score de sécurité.</summary>
public sealed class ScoreRow
{
    public string Name { get; init; } = "";
    public string Icon { get; init; } = "";
    public Brush Color { get; init; } = Brushes.Gray;
    public string Advice { get; init; } = "";
    public Visibility AdviceVisibility { get; init; } = Visibility.Collapsed;
}

/// <summary>Un appareil réseau, avec un nom personnalisable.</summary>
public sealed class NetworkDeviceItem
{
    public string Ip { get; init; } = "";
    public string Mac { get; init; } = "";
    public string DiscoveredName { get; init; } = "";
    public string CustomName { get; set; } = "";
    public string Key => Mac is not ("—" or "") ? Mac : Ip;
}

/// <summary>Mémorise les noms personnalisés des appareils réseau (fichier JSON local).</summary>
public sealed class DeviceNameStore
{
    private readonly string _path;
    private Dictionary<string, string> _map;

    public DeviceNameStore()
    {
        string dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IatechShield");
        System.IO.Directory.CreateDirectory(dir);
        _path = System.IO.Path.Combine(dir, "device-names.json");
        _map = Load();
    }

    public string? Get(string key) => _map.TryGetValue(key, out var v) ? v : null;

    public void Set(string key, string name)
    {
        _map[key] = name;
        try { System.IO.File.WriteAllText(_path, JsonSerializer.Serialize(_map)); } catch { }
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            if (System.IO.File.Exists(_path))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(_path))
                       ?? new();
        }
        catch { }
        return new();
    }
}

/// <summary>Effets sonores via les sons système Windows (aucun fichier audio requis).</summary>
internal static class SoundFx
{
    public static bool Muted { get; set; }

    // Chaque alerte a sa propre signature sonore (séquences de bips, non bloquantes).
    public static void ScanStart() => Play((400, 60), (560, 60), (720, 60), (900, 90)); // balayage montant
    public static void ScanDone() => Play((760, 90), (1020, 140));                       // fin agréable
    public static void Threat() => Play((880, 120), (500, 120), (880, 120), (500, 160)); // menace urgente
    public static void Danger() => Play((300, 200), (300, 200), (300, 260));             // danger grave
    public static void NewDevice() => Play((680, 70), (1040, 110));                       // nouveau matériel
    public static void Alert() => Play((720, 90), (720, 120));                            // alerte générique

    /// <summary>Démarrage du dashboard : montée « réacteur » qui s'allume.</summary>
    public static void ReactorStartup() =>
        Play((180, 90), (240, 90), (320, 90), (430, 100), (570, 110), (760, 130), (1015, 220));

    /// <summary>Réveil après la mise en veille : petit accueil « bienvenue ».</summary>
    public static void Welcome() => Play((640, 110), (810, 110), (1080, 200));

    private static void Play(params (int Freq, int Dur)[] notes)
    {
        if (Muted) return;
        // Console.Beep est bloquant : on joue la séquence sur un thread d'arrière-plan.
        System.Threading.Tasks.Task.Run(() =>
        {
            try { foreach (var (f, d) in notes) Console.Beep(f, d); }
            catch { try { SystemSounds.Asterisk.Play(); } catch { } }
        });
    }
}
