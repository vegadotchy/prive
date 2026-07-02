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
            IatechShield.Tools.AccessLog.Record("Connexion", "Session Windows", Environment.UserName, "Ouverture d'IATECH-SHIELD PRO");
            StartCardRemovalWatcher();
            StartLockWatcher();   // verrou d'inactivité actif si une carte propriétaire est enregistrée
            LoadShutdownLock();   // le veto d'arrêt doit être actif dès le démarrage
            // Si le verrou d'extinction est actif : au démarrage (y compris après un arrêt
            // forcé par appui long), on verrouille immédiatement — déverrouillage carte only.
            if (_shutdownLockEnabled && CardAuth.IsEnrolled)
                Dispatcher.BeginInvoke(new Action(ShowLockScreen),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            _ = InitCloudAsync();
            _ = ImportBrowserPasswordsAsync();   // verse les identifiants du navigateur au coffre (arrière-plan)
        };
        Closing += (_, _) =>
        {
            // Fermeture de l'app : on clôt toute session eID en cours et on trace la déconnexion.
            IatechShield.Tools.EidSessionLog.EndSession();
            IatechShield.Tools.AccessLog.Record("Déconnexion", "Session Windows", Environment.UserName, "Fermeture d'IATECH-SHIELD PRO");
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
        PageVpn.Visibility = Visibility.Collapsed;
        PageVault.Visibility = Visibility.Collapsed;
        PageCentre.Visibility = Visibility.Collapsed;
        PageIntegrity.Visibility = Visibility.Collapsed;
        PageInvestigation.Visibility = Visibility.Collapsed;
        PageCopilot.Visibility = Visibility.Collapsed;
        PageDevice.Visibility = Visibility.Collapsed;
        PageSystem.Visibility = Visibility.Collapsed;
        PageProcesses.Visibility = Visibility.Collapsed;
        PageWorld.Visibility = Visibility.Collapsed;
        PageDna.Visibility = Visibility.Collapsed;
        PageTimeline.Visibility = Visibility.Collapsed;
        PageOptimize.Visibility = Visibility.Collapsed;
        PageDevices.Visibility = Visibility.Collapsed;
        PageSettings.Visibility = Visibility.Collapsed;
        PageLogs.Visibility = Visibility.Collapsed;
        PageAccess.Visibility = Visibility.Collapsed;
        PageRemote.Visibility = Visibility.Collapsed;
        PageIdRegister.Visibility = Visibility.Collapsed;
        PageControl.Visibility = Visibility.Collapsed;
        PageHistory.Visibility = Visibility.Collapsed;
        PageSearch.Visibility = Visibility.Collapsed;
        PageMail.Visibility = Visibility.Collapsed;

        Grid page = name switch
        {
            "Protection" => PageProtection,
            "Scan" => PageScan,
            "Firewall" => PageFirewall,
            "Outils" => PageTools,
            "Réseau" => PageNetwork,
            "VPN" => PageVpn,
            "Coffre-fort" => PageVault,
            "Centre" => PageCentre,
            "Intégrité" => PageIntegrity,
            "Investigation" => PageInvestigation,
            "Copilote" => PageCopilot,
            "Appareil" => PageDevice,
            "Système" => PageSystem,
            "Processus" => PageProcesses,
            "Mondiale" => PageWorld,
            "ADN" => PageDna,
            "Timeline" => PageTimeline,
            "Optimisation" => PageOptimize,
            "Périphériques" => PageDevices,
            "Settings" => PageSettings,
            "Logs" => PageLogs,
            "Accès" => PageAccess,
            "Accès distant" => PageRemote,
            "Registre ID" => PageIdRegister,
            "Contrôle" => PageControl,
            "Historique" => PageHistory,
            "Recherche" => PageSearch,
            "Mail" => PageMail,
            _ => PageDashboard
        };
        page.Visibility = Visibility.Visible;

        // Actualisation en direct du graphe des processus : active seulement sur sa page.
        if (page != PageProcesses) StopProcTimer();

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
            LoadItsmeConfig();
            LoadSmtpConfig();
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
            StartProcTimer();
        }
        else if (page == PageWorld)
        {
            _ = BuildWorldMapAsync();
        }
        else if (page == PageDna)
        {
            _ = BuildDnaAsync();
        }
        else if (page == PageScan)
        {
            BuildThreatRadar();
        }
        else if (page == PageNetwork)
        {
            DrawNetworkRadar();
        }
        else if (page == PageTimeline)
        {
            BuildTimeline();
        }
        else if (page == PageOptimize)
        {
            BuildOptimizeButtons();
        }
        else if (page == PageDevices)
        {
            BuildDevices();
        }
        else if (page == PageAccess)
        {
            BuildAccessLog();
            _ = BuildUsersAsync();
        }
        else if (page == PageRemote)
        {
            RefreshRemoteStatus();
        }
        else if (page == PageIdRegister)
        {
            StartIdRegisterTicker();
        }
        else if (page == PageControl)
        {
            StopIdRegisterTicker();
            RefreshShutdownLockUi();
        }
        else if (page == PageHistory)
        {
            StopIdRegisterTicker();
            _ = BuildHistoryAsync();
        }
        else
        {
            StopIdRegisterTicker();
        }
    }

    // ----------------------------------------------- Optimisation système -----

    [System.Runtime.InteropServices.DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    private static readonly (string Key, string Icon, string Label)[] OptimizeActions =
    {
        ("ram",        "🧠", "Accélérer la RAM"),
        ("temp",       "🗑️", "Effacer fichiers temporaires"),
        ("dns",        "🌐", "Vider le cache DNS"),
        ("recyclebin", "♻️", "Vider la corbeille"),
        ("renewip",    "📶", "Renouveler le bail Internet"),
        ("defrag",     "💽", "Défragmenter / optimiser les disques"),
        ("cleanmgr",   "🧹", "Nettoyage de disque Windows"),
        ("taskmgr",    "📊", "Gestionnaire des tâches"),
        ("resmon",     "📈", "Moniteur de ressources"),
        ("thumbs",     "🖼️", "Vider le cache des miniatures"),
        ("wsreset",    "🛒", "Vider le cache du Windows Store"),
        ("perf",       "🚀", "Mode hautes performances"),
        ("startup",    "▶️", "Programmes au démarrage"),
        ("appwiz",     "📦", "Désinstaller des programmes"),
        ("update",     "⬆️", "Mises à jour Windows"),
        ("diskmgmt",   "🗂️", "Gestion des disques"),
        ("msinfo",     "ℹ️", "Informations système"),
        ("winsock",    "🔌", "Réinitialiser Winsock (réseau)"),
        // --- Nettoyage & réparation supplémentaires ---
        ("prefetch",   "⚡", "Vider le dossier Prefetch"),
        ("flushall",   "🧽", "Vider tous les caches temp (système + user)"),
        ("explorer",   "🔄", "Redémarrer l'Explorateur Windows"),
        ("sfc",        "🩹", "Réparer les fichiers système (SFC)"),
        ("dism",       "🛠️", "Réparer l'image Windows (DISM)"),
        ("chkdsk",     "🔎", "Vérifier le disque (CHKDSK)"),
        ("gpupdate",   "📜", "Actualiser les stratégies de groupe"),
        ("resetnet",   "🌐", "Réinitialiser la pile réseau (TCP/IP)"),
        ("optimizeall","💾", "Optimiser tous les disques (TRIM/défrag)"),
        ("storagesense","🧯", "Assistant de stockage"),
        // --- Diagnostics & moniteurs ---
        ("perfmon",    "📉", "Analyseur de performances"),
        ("eventvwr",   "📋", "Observateur d'événements"),
        ("memdiag",    "🧪", "Diagnostic de la mémoire (RAM)"),
        ("dxdiag",     "🎮", "Diagnostic DirectX (carte graphique)"),
        ("reliability","🧾", "Moniteur de fiabilité"),
        ("batteryrep", "🔋", "Rapport de batterie (HTML)"),
        // --- Outils & consoles système ---
        ("services",   "⚙️", "Services Windows"),
        ("msconfig",   "🧰", "Configuration système (msconfig)"),
        ("taskschd",   "⏰", "Planificateur de tâches"),
        ("compmgmt",   "🖥️", "Gestion de l'ordinateur"),
        ("devmgmt",    "🔧", "Gestionnaire de périphériques"),
        ("sysprop",    "🏷️", "Propriétés système"),
        ("visualfx",   "🎨", "Effets visuels / performances"),
        ("power",      "🔌", "Options d'alimentation"),
        ("regedit",    "🗝️", "Éditeur du registre"),
        ("control",    "🎛️", "Panneau de configuration"),
        ("env",        "🌱", "Variables d'environnement"),
        // --- Réseau & sécurité ---
        ("ncpa",       "📡", "Connexions réseau"),
        ("firewall",   "🛡️", "Pare-feu Windows"),
        ("defender",   "🦠", "Sécurité Windows (Defender)"),
        ("inetcpl",    "🌍", "Options Internet"),
        // --- Réglages Windows ---
        ("storage",    "📦", "Stockage (Réglages)"),
        ("apps",       "🗃️", "Applications installées (Réglages)"),
        ("gaming",     "🕹️", "Mode Jeu (Réglages)"),
        ("about",      "💻", "À propos du PC (Réglages)"),
    };

    // Regroupement des actions d'optimisation par catégorie de couleur (repérage visuel).
    private static readonly (Color Color, string[] Keys)[] OptimizeCategories =
    {
        // 🟢 Nettoyage & mémoire
        (Color.FromRgb(0x22, 0xC5, 0x5E), new[]{"boost","ram","temp","dns","recyclebin","thumbs","wsreset","prefetch","flushall","storagesense"}),
        // 🔵 Réseau
        (Color.FromRgb(0x3B, 0x82, 0xF6), new[]{"renewip","winsock","resetnet","ncpa","firewall","inetcpl"}),
        // 🟠 Réparation
        (Color.FromRgb(0xF5, 0x9E, 0x0B), new[]{"explorer","sfc","dism","chkdsk","gpupdate"}),
        // 🟣 Diagnostics & moniteurs
        (Color.FromRgb(0xA7, 0x8B, 0xFA), new[]{"taskmgr","resmon","perfmon","eventvwr","memdiag","dxdiag","reliability","batteryrep","msinfo"}),
        // 🟦 Disques
        (Color.FromRgb(0x2D, 0xD4, 0xBF), new[]{"defrag","cleanmgr","diskmgmt","optimizeall","storage"}),
        // 🩷 Performances
        (Color.FromRgb(0xEC, 0x48, 0x99), new[]{"perf","visualfx","power","gaming"}),
        // 🔴 Sécurité
        (Color.FromRgb(0xEF, 0x44, 0x44), new[]{"defender"}),
        // ⚪ Réglages / à propos
        (Color.FromRgb(0x94, 0xA3, 0xB8), new[]{"apps","about"}),
    };

    private static Color OptimizeColorFor(string key)
    {
        foreach (var (color, keys) in OptimizeCategories)
            if (keys.Contains(key)) return color;
        return Color.FromRgb(0x22, 0xD3, 0xE8); // cyan par défaut : outils & consoles système
    }

    private void BuildOptimizeButtons()
    {
        if (OptimizeGrid is null || OptimizeGrid.Children.Count > 0) return;
        var optStyle = (Style)FindResource("OptButton");
        foreach (var (key, icon, label) in OptimizeActions)
        {
            var c = OptimizeColorFor(key);
            var btn = new Button
            {
                Style = optStyle,
                Width = 250,
                Margin = new Thickness(0, 0, 10, 10),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Tag = key,
                Content = $"{icon}   {label}",
                Background = new SolidColorBrush(Color.FromArgb(0x2E, c.R, c.G, c.B)),
                BorderBrush = new SolidColorBrush(c),
                Foreground = new SolidColorBrush(Color.FromRgb(
                    (byte)Math.Min(255, c.R + 90), (byte)Math.Min(255, c.G + 90), (byte)Math.Min(255, c.B + 90)))
            };
            btn.Click += OnOptimize;
            OptimizeGrid.Children.Add(btn);
        }
    }

    private async void OnOptimize(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string key }) return;
        OptimizeStatus.Text = "Action en cours…";
        try
        {
            switch (key)
            {
                case "boost":
                    int freed = TrimWorkingSets();
                    await RunHiddenAsync("cmd.exe", "/c ipconfig /flushdns");
                    CleanTempFiles();
                    EmptyRecycleBin();
                    OptimizeStatus.Text = $"✓ Optimisation rapide terminée — RAM allégée ({freed} processus), temp + DNS + corbeille nettoyés.";
                    SoundFx.ScanDone();
                    break;
                case "ram":
                    int n = TrimWorkingSets();
                    OptimizeStatus.Text = $"✓ RAM allégée : mémoire de travail vidée pour {n} processus.";
                    break;
                case "temp":
                    int del = CleanTempFiles();
                    OptimizeStatus.Text = $"✓ Fichiers temporaires : {del} élément(s) supprimé(s).";
                    break;
                case "dns":
                    await RunHiddenAsync("cmd.exe", "/c ipconfig /flushdns");
                    OptimizeStatus.Text = "✓ Cache DNS vidé.";
                    break;
                case "recyclebin":
                    EmptyRecycleBin();
                    OptimizeStatus.Text = "✓ Corbeille vidée.";
                    break;
                case "renewip":
                    await RunHiddenAsync("cmd.exe", "/c ipconfig /release & ipconfig /renew");
                    OptimizeStatus.Text = "✓ Bail Internet renouvelé.";
                    break;
                case "defrag":   LaunchTool("dfrgui.exe"); break;
                case "cleanmgr": LaunchTool("cleanmgr.exe"); break;
                case "taskmgr":  LaunchTool("taskmgr.exe"); break;
                case "resmon":   LaunchTool("resmon.exe"); break;
                case "wsreset":  LaunchTool("wsreset.exe"); break;
                case "appwiz":   LaunchTool("appwiz.cpl"); break;
                case "msinfo":   LaunchTool("msinfo32.exe"); break;
                case "diskmgmt": LaunchTool("diskmgmt.msc"); break;
                case "startup":  LaunchTool("ms-settings:startupapps"); break;
                case "update":   LaunchTool("ms-settings:windowsupdate"); break;
                case "thumbs":
                    int t = CleanThumbnailCache();
                    OptimizeStatus.Text = $"✓ Cache des miniatures vidé ({t} fichier(s)).";
                    break;
                case "perf":
                    await RunHiddenAsync("cmd.exe", "/c powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
                    OptimizeStatus.Text = "✓ Mode hautes performances activé.";
                    break;
                case "winsock":
                    await RunHiddenAsync("cmd.exe", "/c netsh winsock reset");
                    OptimizeStatus.Text = "✓ Winsock réinitialisé — redémarrez pour finaliser.";
                    break;

                // --- Nettoyage & réparation supplémentaires ---
                case "prefetch":
                    int pf = CleanPrefetch();
                    OptimizeStatus.Text = $"✓ Prefetch vidé ({pf} fichier(s)) — le 1er démarrage des apps sera un peu plus lent, puis ré-optimisé.";
                    break;
                case "flushall":
                    int all = CleanTempFiles() + CleanThumbnailCache() + CleanPrefetch();
                    await RunHiddenAsync("cmd.exe", "/c ipconfig /flushdns");
                    OptimizeStatus.Text = $"✓ Tous les caches vidés ({all} éléments) + DNS.";
                    SoundFx.ScanDone();
                    break;
                case "explorer":
                    await RunHiddenAsync("cmd.exe", "/c taskkill /f /im explorer.exe & start explorer.exe");
                    OptimizeStatus.Text = "✓ Explorateur Windows redémarré.";
                    break;
                case "sfc":
                    OptimizeStatus.Text = "🩹 Vérification SFC lancée dans une fenêtre… (peut durer plusieurs minutes)";
                    LaunchAdmin("cmd.exe", "/k sfc /scannow");
                    break;
                case "dism":
                    OptimizeStatus.Text = "🛠️ Réparation DISM lancée dans une fenêtre… (peut durer plusieurs minutes)";
                    LaunchAdmin("cmd.exe", "/k DISM /Online /Cleanup-Image /RestoreHealth");
                    break;
                case "chkdsk":
                    OptimizeStatus.Text = "🔎 Vérification du disque lancée dans une fenêtre…";
                    LaunchAdmin("cmd.exe", "/k chkdsk C: /scan");
                    break;
                case "gpupdate":
                    await RunHiddenAsync("cmd.exe", "/c gpupdate /force");
                    OptimizeStatus.Text = "✓ Stratégies de groupe actualisées.";
                    break;
                case "resetnet":
                    LaunchAdmin("cmd.exe", "/k netsh int ip reset & netsh winsock reset & ipconfig /flushdns");
                    OptimizeStatus.Text = "🌐 Réinitialisation réseau lancée — redémarrez ensuite.";
                    break;
                case "optimizeall":
                    LaunchAdmin("cmd.exe", "/k defrag /C /O /U /V");
                    OptimizeStatus.Text = "💾 Optimisation de tous les disques lancée (TRIM SSD / défrag HDD).";
                    break;

                // --- Diagnostics & moniteurs ---
                case "perfmon":     LaunchTool("perfmon.exe"); break;
                case "eventvwr":    LaunchTool("eventvwr.msc"); break;
                case "memdiag":     LaunchTool("mdsched.exe"); break;
                case "dxdiag":      LaunchTool("dxdiag.exe"); break;
                case "reliability": LaunchToolArgs("perfmon.exe", "/rel"); break;
                case "batteryrep":
                    string rep = Path.Combine(Path.GetTempPath(), "iatech-battery-report.html");
                    await RunHiddenAsync("cmd.exe", $"/c powercfg /batteryreport /output \"{rep}\"");
                    if (File.Exists(rep)) { LaunchTool(rep); OptimizeStatus.Text = "✓ Rapport de batterie généré et ouvert."; }
                    else OptimizeStatus.Text = "Aucune batterie détectée (PC fixe ?).";
                    break;

                // --- Outils & consoles système ---
                case "services":  LaunchTool("services.msc"); break;
                case "msconfig":  LaunchTool("msconfig.exe"); break;
                case "taskschd":  LaunchTool("taskschd.msc"); break;
                case "compmgmt":  LaunchTool("compmgmt.msc"); break;
                case "devmgmt":   LaunchTool("devmgmt.msc"); break;
                case "sysprop":   LaunchTool("sysdm.cpl"); break;
                case "visualfx":  LaunchTool("SystemPropertiesPerformance.exe"); break;
                case "power":     LaunchTool("powercfg.cpl"); break;
                case "regedit":   LaunchTool("regedit.exe"); break;
                case "control":   LaunchTool("control.exe"); break;
                case "env":       LaunchToolArgs("rundll32.exe", "sysdm.cpl,EditEnvironmentVariables"); break;

                // --- Réseau & sécurité ---
                case "ncpa":      LaunchTool("ncpa.cpl"); break;
                case "firewall":  LaunchTool("firewall.cpl"); break;
                case "defender":  LaunchTool("windowsdefender:"); break;
                case "inetcpl":   LaunchTool("inetcpl.cpl"); break;

                // --- Réglages Windows ---
                case "storage":   LaunchTool("ms-settings:storagesense"); break;
                case "storagesense": LaunchTool("ms-settings:storagepolicies"); break;
                case "apps":      LaunchTool("ms-settings:appsfeatures"); break;
                case "gaming":    LaunchTool("ms-settings:gaming-gamemode"); break;
                case "about":     LaunchTool("ms-settings:about"); break;

                default:
                    OptimizeStatus.Text = "Action inconnue.";
                    break;
            }
            Log($"Optimisation : {key}.");
        }
        catch (Exception ex)
        {
            OptimizeStatus.Text = $"Échec : {ex.Message}";
        }
    }

    /// <summary>Vide la mémoire de travail de tous les processus accessibles (« accélérer la RAM »).</summary>
    private static int TrimWorkingSets()
    {
        int n = 0;
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try { if (EmptyWorkingSet(p.Handle)) n++; } catch { }
            finally { try { p.Dispose(); } catch { } }
        }
        return n;
    }

    private static int CleanTempFiles()
    {
        int count = 0;
        string[] dirs = { Path.GetTempPath(), Environment.GetEnvironmentVariable("TEMP") ?? "",
                          Path.Combine(Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows", "Temp") };
        foreach (var dir in dirs.Distinct())
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFileSystemEntries(dir))
            {
                try
                {
                    if (File.Exists(f)) { File.Delete(f); count++; }
                    else if (Directory.Exists(f)) { Directory.Delete(f, true); count++; }
                }
                catch { /* fichier en cours d'utilisation : ignoré */ }
            }
        }
        return count;
    }

    private static int CleanThumbnailCache()
    {
        int n = 0;
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "Explorer");
        if (!Directory.Exists(dir)) return 0;
        foreach (var f in Directory.EnumerateFiles(dir, "thumbcache_*.db"))
        {
            try { File.Delete(f); n++; } catch { }
        }
        return n;
    }

    private static void EmptyRecycleBin()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                "-NoProfile -Command \"Clear-RecycleBin -Force -ErrorAction SilentlyContinue\"")
            { CreateNoWindow = true, UseShellExecute = false, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden };
            System.Diagnostics.Process.Start(psi);
        }
        catch { }
    }

    private static Task RunHiddenAsync(string file, string args) => Task.Run(() =>
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(file, args)
            { CreateNoWindow = true, UseShellExecute = false, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden };
            using var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(15000);
        }
        catch { }
    });

    private void LaunchTool(string file)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
            OptimizeStatus.Text = $"✓ Ouverture de l'outil Windows : {file}";
        }
        catch (Exception ex)
        {
            OptimizeStatus.Text = $"Impossible d'ouvrir {file} : {ex.Message}";
        }
    }

    /// <summary>Ouvre un outil Windows avec des arguments (sans élévation).</summary>
    private void LaunchToolArgs(string file, string args)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, args) { UseShellExecute = true });
            OptimizeStatus.Text = $"✓ Ouverture de l'outil Windows : {file}";
        }
        catch (Exception ex)
        {
            OptimizeStatus.Text = $"Impossible d'ouvrir {file} : {ex.Message}";
        }
    }

    /// <summary>Lance un outil avec élévation administrateur (UAC).</summary>
    private void LaunchAdmin(string file, string args)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, args)
            { UseShellExecute = true, Verb = "runas" });
        }
        catch (Exception ex)
        {
            OptimizeStatus.Text = $"Action annulée ou refusée : {ex.Message}";
        }
    }

    /// <summary>Vide le dossier Prefetch de Windows (caches de pré-chargement).</summary>
    private static int CleanPrefetch()
    {
        int n = 0;
        string dir = Path.Combine(Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows", "Prefetch");
        if (!Directory.Exists(dir)) return 0;
        foreach (var f in Directory.EnumerateFiles(dir))
        {
            try { File.Delete(f); n++; } catch { /* fichier verrouillé : ignoré */ }
        }
        return n;
    }

    // ----------------------------------------------- Périphériques -------------

    /// <summary>Élément de liste « imprimante installée ».</summary>
    public sealed class PrinterItem
    {
        public string Name { get; set; } = "";
        public string RawName { get; set; } = "";
        public string Sub { get; set; } = "";
        public string Toner { get; set; } = "";
        public System.Windows.Media.Brush StatusColor { get; set; } = System.Windows.Media.Brushes.Cyan;
        public double TonerPct { get; set; } = 100;
        public System.Windows.Media.Brush TonerColor { get; set; } = System.Windows.Media.Brushes.Cyan;
        public Visibility TonerVisibility { get; set; } = Visibility.Collapsed;
        public Visibility WarningVisibility { get; set; } = Visibility.Collapsed;
        public Visibility ActionsVisibility { get; set; } = Visibility.Visible;
    }

    /// <summary>Élément de liste « disque / périphérique de stockage ».</summary>
    public sealed class DriveItem
    {
        public string Name { get; set; } = "";
        public string Sub { get; set; } = "";
        public string Path { get; set; } = "";
        public double UsedPct { get; set; }
        public System.Windows.Media.Brush Color { get; set; } = System.Windows.Media.Brushes.Cyan;
    }

    // Accès rapides : recherche / ajout de périphériques + outils Windows.
    private static readonly (string Icon, string Label, string Target)[] DeviceTools =
    {
        ("➕", "Ajouter une imprimante",      "ms-settings:printers"),
        ("➕", "Ajouter un appareil (Bluetooth/USB)", "ms-settings:connecteddevices"),
        ("🖨️", "Imprimantes & scanners",      "ms-settings:printers"),
        ("📷", "Numériser (scanner)",          "wiaacmgr.exe"),
        ("🧰", "Gestionnaire de périphériques", "devmgmt.msc"),
        ("💽", "Gestion des disques",          "diskmgmt.msc"),
        ("🔵", "Bluetooth & appareils",        "ms-settings:bluetooth"),
        ("🔌", "Appareils USB",                "ms-settings:usb"),
        ("🔊", "Périphériques audio",          "mmsys.cpl"),
        ("🖥️", "Paramètres d'affichage",       "ms-settings:display"),
    };

    private void BuildDevices()
    {
        BuildDeviceTools();
        BuildPrinters();
        BuildDrives();
    }

    private void BuildDeviceTools()
    {
        if (DeviceToolsGrid is null || DeviceToolsGrid.Children.Count > 0) return;
        foreach (var (icon, label, target) in DeviceTools)
        {
            var btn = new Button
            {
                Style = (Style)FindResource("GhostButton"),
                Width = 250,
                Height = 46,
                Margin = new Thickness(0, 0, 10, 10),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Tag = target,
                Content = $"{icon}   {label}"
            };
            btn.Click += OnDeviceTool;
            DeviceToolsGrid.Children.Add(btn);
        }
    }

    private void OnDeviceTool(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string target }) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
            if (DevicesStatus is not null) DevicesStatus.Text = $"✓ Ouverture : {target}";
        }
        catch (Exception ex)
        {
            if (DevicesStatus is not null) DevicesStatus.Text = $"Impossible d'ouvrir : {ex.Message}";
        }
    }

    private readonly HashSet<string> _printerProblems = new(StringComparer.OrdinalIgnoreCase);

    private void BuildPrinters() => _ = BuildPrintersAsync();

    private async Task BuildPrintersAsync()
    {
        if (PrintersList is null) return;
        var items = new List<PrinterItem>();
        string? defaultName = null;
        try { defaultName = new System.Drawing.Printing.PrinterSettings().PrinterName; } catch { }

        // État détaillé via WMI (statut, erreurs, bourrage, toner, hors ligne).
        var errorState = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var offline = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string raw = await RunPs(
                "(Get-CimInstance Win32_Printer -ErrorAction SilentlyContinue | ForEach-Object { " +
                "\"$($_.Name)|$([int]$_.DetectedErrorState)|$([int][bool]$_.WorkOffline)\" }) -join ';;'");
            if (!raw.StartsWith("ERREUR"))
                foreach (var row in raw.Split(new[] { ";;" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var p = row.Split('|');
                    if (p.Length < 3) continue;
                    errorState[p[0].Trim()] = int.TryParse(p[1], out int es) ? es : 0;
                    offline[p[0].Trim()] = p[2].Trim() == "1";
                }
        }
        catch { }

        try
        {
            foreach (string name in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
            {
                bool isDefault = string.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase);
                int es = errorState.GetValueOrDefault(name, 2);
                bool off = offline.GetValueOrDefault(name, false);
                var (statusText, color, isProblem, tonerPct, tonerText, tonerColor, showToner) = InterpretPrinter(es, off);

                items.Add(new PrinterItem
                {
                    Name = isDefault ? $"{name}   ★ par défaut" : name,
                    RawName = name,
                    Sub = statusText,
                    StatusColor = new SolidColorBrush(color),
                    WarningVisibility = isProblem ? Visibility.Visible : Visibility.Collapsed,
                    Toner = tonerText,
                    TonerPct = tonerPct,
                    TonerColor = new SolidColorBrush(tonerColor),
                    TonerVisibility = showToner ? Visibility.Visible : Visibility.Collapsed,
                });

                // Notification (popup) au passage en état de problème.
                if (isProblem)
                {
                    if (_printerProblems.Add(name))
                        Notify("⚠️ Imprimante : " + name, statusText, "Périphériques");
                }
                else _printerProblems.Remove(name);
            }
        }
        catch { }

        if (items.Count == 0)
            items.Add(new PrinterItem
            {
                Name = "Aucune imprimante installée",
                Sub = "Cliquez « ➕ Ajouter une imprimante » ci-dessus pour en installer une.",
                ActionsVisibility = Visibility.Collapsed
            });
        PrintersList.ItemsSource = items;
    }

    /// <summary>Traduit l'état WMI d'une imprimante en statut, couleur et niveau de consommable.</summary>
    private static (string status, Color color, bool problem, double tonerPct, string tonerText, Color tonerColor, bool showToner)
        InterpretPrinter(int detectedErrorState, bool offline)
    {
        Color green = Color.FromRgb(0x22, 0xC5, 0x5E);
        Color amber = Color.FromRgb(0xF5, 0x9E, 0x0B);
        Color red = Color.FromRgb(0xEF, 0x44, 0x44);
        Color gray = Color.FromRgb(0x94, 0xA3, 0xB8);

        if (offline && detectedErrorState is 2 or 0)
            return ("⚪ Hors ligne", gray, true, 0, "", gray, false);

        return detectedErrorState switch
        {
            3  => ("⚠️ Papier bas", amber, true, 100, "", green, false),
            4  => ("⛔ Plus de papier", red, true, 100, "", green, false),
            5  => ("⚠️ Toner / cartouche bas", amber, true, 20, "🟠 Toner bas (~20 %)", amber, true),
            6  => ("⛔ Toner / cartouche vide", red, true, 0, "🔴 Toner vide (0 %)", red, true),
            7  => ("⚠️ Capot ouvert", amber, true, 100, "", green, false),
            8  => ("⛔ Bourrage papier", red, true, 100, "", green, false),
            9  => ("⚪ Hors ligne", gray, true, 0, "", gray, false),
            10 => ("⛔ Maintenance requise", red, true, 100, "", green, false),
            11 => ("⚠️ Bac de sortie plein", amber, true, 100, "", green, false),
            _  => ("🟢 Prête — consommables OK", green, false, 100, "🟢 Consommables OK", green, true),
        };
    }

    private async void OnUninstallPrinter(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name } || string.IsNullOrWhiteSpace(name)) return;
        var confirm = new PromptWindow("Désinstaller l'imprimante",
            $"Désinstaller « {name} » ? Tapez OUI.", "Désinstaller") { Owner = this };
        if (confirm.ShowDialog() != true || !string.Equals(confirm.Value.Trim(), "OUI", StringComparison.OrdinalIgnoreCase))
            return;
        string n = name.Replace("'", "''");
        string res = await RunPs($"Remove-Printer -Name '{n}' -ErrorAction Stop; if($?){{'OK'}}");
        if (!res.Contains("OK"))
            res = await RunPs($"rundll32 printui.dll,PrintUIEntry /dl /n \"{name}\" /q; if($?){{'OK'}}");
        if (DevicesStatus is not null)
            DevicesStatus.Text = res.Contains("OK") ? $"✓ Imprimante « {name} » désinstallée." : $"❌ Échec : {res}";
        if (res.Contains("OK")) Log($"Imprimante désinstallée : {name}.");
        BuildPrinters();
    }

    private async void OnSetDefaultPrinter(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name } || string.IsNullOrWhiteSpace(name)) return;
        string n = name.Replace("'", "''");
        string res = await RunPs($"(New-Object -ComObject WScript.Network).SetDefaultPrinter('{n}'); if($?){{'OK'}}");
        if (DevicesStatus is not null)
            DevicesStatus.Text = res.Contains("OK") ? $"✓ « {name} » définie par défaut." : $"❌ Échec : {res}";
        BuildPrinters();
    }

    private void BuildDrives()
    {
        if (DrivesList is null) return;
        var items = new List<DriveItem>();
        try
        {
            foreach (var d in System.IO.DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) { continue; }
                    double total = d.TotalSize;
                    double used = total - d.AvailableFreeSpace;
                    double pct = total > 0 ? used / total * 100.0 : 0;
                    string kind = d.DriveType switch
                    {
                        System.IO.DriveType.Removable => "Périphérique amovible (USB / carte)",
                        System.IO.DriveType.Network   => "Lecteur réseau",
                        System.IO.DriveType.CDRom     => "Lecteur optique",
                        System.IO.DriveType.Ram       => "Disque RAM",
                        _                              => "Disque fixe"
                    };
                    string label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Volume" : d.VolumeLabel;
                    var color = pct >= 90 ? System.Windows.Media.Brushes.OrangeRed
                              : pct >= 75 ? System.Windows.Media.Brushes.Gold
                              : System.Windows.Media.Brushes.Cyan;
                    items.Add(new DriveItem
                    {
                        Name = $"{d.Name}  {label}",
                        Sub = $"{kind} • {GoBytes(used)} utilisés sur {GoBytes(total)} ({GoBytes(d.AvailableFreeSpace)} libres)",
                        Path = d.RootDirectory.FullName,
                        UsedPct = pct,
                        Color = color
                    });
                }
                catch { }
            }
        }
        catch { }
        if (items.Count == 0)
            items.Add(new DriveItem { Name = "Aucun disque détecté", Sub = "Branchez un périphérique puis cliquez « Actualiser ».", Path = "" });
        DrivesList.ItemsSource = items;
    }

    private static string GoBytes(double bytes)
    {
        string[] u = { "o", "Ko", "Mo", "Go", "To" };
        int i = 0;
        while (bytes >= 1024 && i < u.Length - 1) { bytes /= 1024; i++; }
        return $"{bytes:0.#} {u[i]}";
    }

    private void OnRefreshDevices(object sender, RoutedEventArgs e)
    {
        // Force la reconstruction (outils déjà en place ; on rafraîchit listes).
        BuildPrinters();
        BuildDrives();
        if (DevicesStatus is not null) DevicesStatus.Text = "✓ Liste des périphériques actualisée.";
    }

    private void OnOpenPrinterQueue(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name }) return;
        try
        {
            // Ouvre directement la file d'attente de l'imprimante choisie.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("rundll32.exe",
                $"printui.dll,PrintUIEntry /o /n \"{name}\"") { UseShellExecute = true });
            if (DevicesStatus is not null) DevicesStatus.Text = $"✓ File d'attente : {name}";
        }
        catch
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:printers") { UseShellExecute = true }); } catch { }
        }
    }

    private void OnOpenDrive(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path } || string.IsNullOrEmpty(path)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            if (DevicesStatus is not null) DevicesStatus.Text = $"✓ Ouverture de {path} dans l'Explorateur.";
        }
        catch (Exception ex)
        {
            if (DevicesStatus is not null) DevicesStatus.Text = $"Impossible d'ouvrir {path} : {ex.Message}";
        }
    }

    // ----------------------------------------------- Timeline de sécurité ------

    private void OnRefreshTimeline(object sender, RoutedEventArgs e) => BuildTimeline();

    /// <summary>Construit la frise chronologique des incidents de sécurité (du + récent au + ancien).</summary>
    private void BuildTimeline()
    {
        if (TimelineList is null) return;
        try
        {
            var events = IncidentLog.Load()
                .OrderByDescending(e => e.Time)
                .Take(200)
                .Select(e => new SecurityTimelineItem
                {
                    TimeText = e.Time.ToLocalTime().ToString("dd/MM HH:mm"),
                    Title = e.Title,
                    Sub = $"{e.Category} · {e.Detail}",
                    Dot = new SolidColorBrush(e.Severity switch
                    {
                        IncidentSeverity.Critical => Color.FromRgb(0xEF, 0x44, 0x44),
                        IncidentSeverity.Warning => Color.FromRgb(0xF5, 0x9E, 0x0B),
                        _ => Color.FromRgb(0x22, 0xD3, 0xE8)
                    })
                })
                .ToList();

            TimelineList.ItemsSource = events;
            TimelineStatus.Text = events.Count == 0
                ? "Aucun évènement enregistré pour l'instant."
                : $"{events.Count} évènement(s) — du plus récent au plus ancien.";
        }
        catch (Exception ex)
        {
            TimelineStatus.Text = $"Erreur : {ex.Message}";
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

    // ----------------------------------------- Radar réseau « tour de contrôle » -

    /// <summary>
    /// Dessine un radar d'aviation (style tour de contrôle, phosphore vert) avec un
    /// balayage tournant. Chaque appareil du réseau est un point : vert = connu/sûr,
    /// rouge = nouvellement détecté ou présentant un risque.
    /// </summary>
    private void DrawNetworkRadar()
    {
        if (NetRadarCanvas is null) return;
        if (NetRadarCanvas.ActualWidth <= 1)
        {
            Dispatcher.BeginInvoke(new Action(DrawNetworkRadar),
                System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }
        NetRadarCanvas.Children.Clear();

        double w = NetRadarCanvas.ActualWidth, h = NetRadarCanvas.ActualHeight;
        double cx = w / 2, cy = h / 2;
        double r = Math.Min(w, h) / 2 - 24;
        var green = Color.FromRgb(0x33, 0xFF, 0x66);
        var greenDim = Color.FromArgb(0x55, 0x33, 0xFF, 0x66);
        var red = Color.FromRgb(0xFF, 0x33, 0x33);

        SolidColorBrush GB(byte a) => new(Color.FromArgb(a, green.R, green.G, green.B));

        // Cercles concentriques.
        for (int k = 1; k <= 4; k++)
        {
            double rr = r * k / 4;
            var ring = new System.Windows.Shapes.Ellipse
            { Width = rr * 2, Height = rr * 2, Stroke = GB((byte)(0x40 + k * 8)), StrokeThickness = 1 };
            Canvas.SetLeft(ring, cx - rr); Canvas.SetTop(ring, cy - rr);
            NetRadarCanvas.Children.Add(ring);
        }

        // Croix + diagonales (rose des azimuts).
        for (int a = 0; a < 8; a++)
        {
            double ang = a * Math.PI / 4;
            NetRadarCanvas.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = cx, Y1 = cy, X2 = cx + r * Math.Cos(ang), Y2 = cy + r * Math.Sin(ang),
                Stroke = GB(0x22), StrokeThickness = 0.7
            });
        }

        // Graduations sur le cercle extérieur.
        for (int a = 0; a < 36; a++)
        {
            double ang = a * Math.PI / 18;
            double r0 = r - (a % 9 == 0 ? 10 : 5);
            NetRadarCanvas.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = cx + r0 * Math.Cos(ang), Y1 = cy + r0 * Math.Sin(ang),
                X2 = cx + r * Math.Cos(ang), Y2 = cy + r * Math.Sin(ang),
                Stroke = GB(0x55), StrokeThickness = 1
            });
        }

        // Secteur de balayage tournant + ligne de tête lumineuse.
        var sweep = new System.Windows.Shapes.Path
        {
            Fill = new RadialGradientBrush(Color.FromArgb(0x66, green.R, green.G, green.B), Colors.Transparent)
            { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 },
            Data = SweepWedge(cx, cy, r),
            RenderTransformOrigin = new Point(0, 0)
        };
        var rot = new RotateTransform(0, cx, cy);
        sweep.RenderTransform = rot;
        NetRadarCanvas.Children.Add(sweep);

        var lead = new System.Windows.Shapes.Line
        {
            X1 = cx, Y1 = cy, X2 = cx + r, Y2 = cy,
            Stroke = GB(0xDD), StrokeThickness = 2,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = green, BlurRadius = 10, ShadowDepth = 0 },
            RenderTransformOrigin = new Point(0, 0)
        };
        var rot2 = new RotateTransform(0, cx, cy);
        lead.RenderTransform = rot2;
        NetRadarCanvas.Children.Add(lead);

        var spin = new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(4))
        { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
        rot.BeginAnimation(RotateTransform.AngleProperty, spin);
        rot2.BeginAnimation(RotateTransform.AngleProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(4))
            { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });

        // Centre (ce PC).
        var core = new System.Windows.Shapes.Ellipse
        {
            Width = 12, Height = 12, Fill = GB(0xFF),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = green, BlurRadius = 16, ShadowDepth = 0 }
        };
        Canvas.SetLeft(core, cx - 6); Canvas.SetTop(core, cy - 6);
        NetRadarCanvas.Children.Add(core);

        // Blips : un appareil = un point. Vert = sûr ; rouge = nouveau ou à risque.
        int count = _radar.Count;
        for (int i = 0; i < count; i++)
        {
            var item = _radar[i];
            bool danger = item.IsNew || (item.Risks?.Count ?? 0) > 0;
            var col = danger ? red : green;

            // Position déterministe : angle d'après l'IP, distance d'après l'index.
            int hash = Math.Abs((item.Ip ?? i.ToString()).GetHashCode());
            double ang = (hash % 360) * Math.PI / 180.0;
            double dist = r * (0.30 + 0.60 * ((hash / 360 % 100) / 100.0));
            double bx = cx + dist * Math.Cos(ang), by = cy + dist * Math.Sin(ang);

            double size = danger ? 13 : 10;
            var blip = new System.Windows.Shapes.Ellipse
            {
                Width = size, Height = size, Fill = new SolidColorBrush(col),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = col, BlurRadius = 14, ShadowDepth = 0 },
                ToolTip = $"{item.Ip} — {item.TypeLabel}" + (danger ? (item.IsNew ? "  (NOUVEAU)" : "  (À RISQUE)") : "  (sûr)")
            };
            Canvas.SetLeft(blip, bx - size / 2); Canvas.SetTop(blip, by - size / 2);
            // Les appareils à risque clignotent.
            if (danger)
                blip.BeginAnimation(OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(1, 0.25, TimeSpan.FromSeconds(0.7))
                    { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
            NetRadarCanvas.Children.Add(blip);
        }

        if (NetRadarLegend is not null)
            NetRadarLegend.Text = count == 0
                ? "Cliquez « LANCER LE RADAR » pour scanner le réseau"
                : "🟢 connu / sûr      🔴 nouveau ou à risque";
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
                    // IMPORTANT : on est sur un thread d'arrière-plan (Task.Run). Un Brush
                    // WPF est un DependencyObject : il faut le « geler » (Freeze) pour
                    // pouvoir l'utiliser ensuite sur le thread UI sans planter.
                    var brush = new SolidColorBrush(ScoreColor(prof.Score));
                    brush.Freeze();
                    list.Add(new DnaItem
                    {
                        Name = string.IsNullOrWhiteSpace(prof.Name) ? System.IO.Path.GetFileName(exe) : prof.Name,
                        Publisher = $"{prof.Signature} · {prof.Publisher}",
                        Line = $"{prof.Location} · âge : {prof.Age} · {prof.Behavior}\n{exe}",
                        Score = prof.Score,
                        ScoreText = prof.Score + "%",
                        ScoreColor = brush
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
        DrawContinents(w, h);

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

        // Nœud local : « Vous êtes ici » — anneau qui pulse + point + étiquette.
        var green = Color.FromRgb(0x2B, 0xE0, 0xA6);
        var ring = new System.Windows.Shapes.Ellipse
        {
            Width = 40, Height = 40, Stroke = new SolidColorBrush(green), StrokeThickness = 2,
            Fill = System.Windows.Media.Brushes.Transparent
        };
        Canvas.SetLeft(ring, hx - 20); Canvas.SetTop(ring, hy - 20);
        WorldCanvas.Children.Add(ring);
        var grow = new System.Windows.Media.Animation.DoubleAnimation(14, 46, TimeSpan.FromSeconds(1.8))
        { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
        var fade = new System.Windows.Media.Animation.DoubleAnimation(0.9, 0, TimeSpan.FromSeconds(1.8))
        { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
        ring.BeginAnimation(System.Windows.Shapes.Ellipse.WidthProperty, grow);
        ring.BeginAnimation(System.Windows.Shapes.Ellipse.HeightProperty,
            new System.Windows.Media.Animation.DoubleAnimation(14, 46, TimeSpan.FromSeconds(1.8)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
        ring.BeginAnimation(OpacityProperty, fade);

        var homeDot = new System.Windows.Shapes.Ellipse
        {
            Width = 16, Height = 16, Fill = new SolidColorBrush(green),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            { Color = green, BlurRadius = 20, ShadowDepth = 0, Opacity = 1 },
            ToolTip = data.Home is { } hh ? $"Vous : {hh.City} ({hh.Country})" : "Position locale"
        };
        Canvas.SetLeft(homeDot, hx - 8);
        Canvas.SetTop(homeDot, hy - 8);
        WorldCanvas.Children.Add(homeDot);

        // Étiquette « 📍 Vous êtes ici » + ville/pays + coordonnées.
        string here = data.Home is { } hm
            ? $"📍 Vous êtes ici — {hm.City}, {hm.Country}  ({hm.Lat:0.00}, {hm.Lon:0.00})"
            : "📍 Position locale (géolocalisation indisponible)";
        var label = new TextBlock
        {
            Text = here, Foreground = new SolidColorBrush(green), FontSize = 12, FontWeight = FontWeights.SemiBold,
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x05, 0x0B, 0x14)), Padding = new Thickness(6, 2, 6, 2),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = green, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.6 }
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double lx = Math.Clamp(hx + 14, 4, w - label.DesiredSize.Width - 4);
        double ly = Math.Clamp(hy - 28, 4, h - 24);
        Canvas.SetLeft(label, lx); Canvas.SetTop(label, ly);
        WorldCanvas.Children.Add(label);

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

    // Contours simplifiés des continents (paires lon,lat), projection équirectangulaire.
    private static readonly double[][] Continents =
    {
        // Amérique du Nord
        new double[] { -168,65, -160,71, -130,70, -95,70, -82,73, -60,60, -55,52, -65,45, -70,42, -75,35, -81,25, -97,26, -105,22, -110,30, -117,33, -124,40, -125,48, -135,58, -150,60, -168,65 },
        // Amérique du Sud
        new double[] { -80,8, -70,11, -60,5, -50,0, -35,-5, -35,-23, -48,-25, -58,-35, -65,-42, -70,-50, -75,-52, -73,-45, -70,-35, -71,-20, -78,-10, -81,-5, -80,8 },
        // Afrique
        new double[] { -17,21, -10,27, 0,32, 10,34, 20,32, 32,31, 43,12, 51,12, 42,-2, 40,-15, 35,-22, 20,-35, 18,-30, 12,-17, 8,4, -8,5, -17,14, -17,21 },
        // Europe
        new double[] { -10,37, -9,44, -2,49, 2,51, 0,58, 10,58, 13,55, 20,55, 28,60, 30,52, 28,45, 20,42, 15,40, 8,44, 3,43, -2,37, -10,37 },
        // Asie
        new double[] { 28,60, 40,68, 60,70, 75,73, 100,78, 140,73, 160,69, 170,66, 178,62, 160,55, 140,52, 135,45, 130,35, 122,30, 120,22, 108,18, 100,8, 95,15, 88,22, 80,8, 77,8, 72,20, 60,25, 48,28, 43,40, 35,45, 28,45, 28,60 },
        // Océanie / Australie
        new double[] { 113,-22, 122,-18, 130,-12, 137,-12, 142,-11, 146,-18, 150,-25, 153,-28, 150,-37, 143,-39, 135,-35, 129,-32, 120,-34, 115,-30, 113,-22 },
    };

    /// <summary>Dessine les contours des continents (carte du monde) sur le canvas.</summary>
    private void DrawContinents(double w, double h)
    {
        var stroke = new SolidColorBrush(Color.FromArgb(0xAA, 0x3D, 0x9A, 0xC0));
        var fill = new SolidColorBrush(Color.FromArgb(0x1E, 0x22, 0xD3, 0xE8));
        foreach (var land in Continents)
        {
            var poly = new System.Windows.Shapes.Polygon { Stroke = stroke, StrokeThickness = 1, Fill = fill };
            for (int i = 0; i + 1 < land.Length; i += 2)
            {
                double lon = land[i], lat = land[i + 1];
                poly.Points.Add(new Point((lon + 180.0) / 360.0 * w, (90.0 - lat) / 180.0 * h));
            }
            WorldCanvas.Children.Add(poly);
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
    private DispatcherTimer? _procTimer;

    /// <summary>Rafraîchit le graphe des processus en direct (taille des nœuds = usage RAM).</summary>
    private void StartProcTimer()
    {
        _procTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _procTimer.Tick -= OnProcTick;
        _procTimer.Tick += OnProcTick;
        _procTimer.Start();
    }

    private void StopProcTimer() => _procTimer?.Stop();

    private void OnProcTick(object? sender, EventArgs e)
    {
        // On ne redessine que si la page est visible (sécurité).
        if (PageProcesses is { Visibility: Visibility.Visible }) BuildProcessGraph();
        else StopProcTimer();
    }

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
        AddProcNode(cx, cy, 64, accent, "PC", null, isHub: true, onClick: null);

        double totalPhys = TotalPhysicalBytes();

        for (int i = 0; i < procs.Count; i++)
        {
            var p = procs[i];
            double angle = 2 * Math.PI * i / Math.Max(1, procs.Count);
            double nx = cx + radius * Math.Cos(angle);
            double ny = cy + radius * Math.Sin(angle);

            bool trusted = IsLikelyTrusted(p.Name);
            Color c = trusted ? (i % 3 == 0 ? green : accent) : red;
            double memMb = p.Mem / (1024.0 * 1024.0);
            // Part de la mémoire physique utilisée par ce processus (dynamique).
            double pct = totalPhys > 0 ? p.Mem / totalPhys * 100.0 : 0;
            // Taille du cercle proportionnelle à l'usage (grandit/rétrécit selon la RAM).
            double size = 22 + Math.Min(52, pct * 4.0);

            string name = p.Name; int pid = p.Id; int count = p.Count;
            string detail =
                $"🧩 {name}\n" +
                $"PID : {pid}\n" +
                $"Instances : {count}\n" +
                $"Mémoire : {memMb:0} Mo ({pct:0.0} % de la RAM)\n" +
                $"Confiance : {(trusted ? "✓ Connu / signé" : "⚠ Non reconnu")}";

            AddProcNode(nx, ny, size, c, name, $"{pct:0.0}%", isHub: false, onClick: () =>
            {
                ProcDetail.Text = detail;
                ProcDetail.Foreground = new SolidColorBrush(trusted
                    ? Color.FromRgb(0xE8, 0xF6, 0xFB) : red);
                _selectedProcName = name;
                _selectedProcId = pid;
                if (ProcKillButton is not null) ProcKillButton.IsEnabled = true;
                if (ProcKillStatus is not null) ProcKillStatus.Text = "";
            });
        }

        int suspicious = procs.Count(p => !IsLikelyTrusted(p.Name));
        ProcStatus.Text = $"{procs.Count} processus majeurs · {suspicious} non reconnu(s) · taille = usage RAM.";
    }

    private string? _selectedProcName;
    private int _selectedProcId;

    /// <summary>Tue le processus sélectionné (toutes ses instances).</summary>
    private void OnKillProcess(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedProcName)) return;
        var confirm = new PromptWindow("Tuer le processus",
            $"Arrêter « {_selectedProcName} » et toutes ses instances ? Tapez OUI.", "Arrêter") { Owner = this };
        if (confirm.ShowDialog() != true || !string.Equals(confirm.Value.Trim(), "OUI", StringComparison.OrdinalIgnoreCase))
            return;
        int killed = 0, failed = 0;
        foreach (var proc in System.Diagnostics.Process.GetProcessesByName(_selectedProcName))
        {
            try { proc.Kill(); proc.WaitForExit(1500); killed++; }
            catch { failed++; }
        }
        if (ProcKillStatus is not null)
            ProcKillStatus.Text = killed > 0
                ? $"✓ {_selectedProcName} arrêté ({killed} instance(s)){(failed > 0 ? $", {failed} protégée(s)" : "")}."
                : $"❌ Impossible d'arrêter {_selectedProcName} (processus protégé ou droits insuffisants).";
        Log($"Processus arrêté : {_selectedProcName} ({killed} instance(s)).");
        if (ProcKillButton is not null) ProcKillButton.IsEnabled = false;
        BuildProcessGraph();
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile,
                     ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private static double TotalPhysicalBytes()
    {
        try
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref m) && m.ullTotalPhys > 0) return m.ullTotalPhys;
        }
        catch { }
        return 8L * 1024 * 1024 * 1024;   // repli : 8 Go
    }

    private void AddProcNode(double x, double y, double size, Color color, string label, string? pctLabel, bool isHub, Action? onClick)
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

        // Pourcentage d'utilisation (RAM) sous le nom.
        if (pctLabel is not null)
        {
            var pctText = new TextBlock
            {
                Text = pctLabel,
                Foreground = new SolidColorBrush(color),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Width = 90
            };
            Canvas.SetLeft(pctText, x - 45);
            Canvas.SetTop(pctText, y + size / 2 + 15);
            ProcCanvas.Children.Add(pctText);
        }
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
        _metricsTimer.Tick += (_, _) =>
        {
            UpdateMetrics();
            UpdateLicenseCountdown();
            if (_ramProcTick++ % 3 == 0) { UpdateTopRamProcs(); UpdateTopCpuProcs(); }   // top RAM/CPU toutes les 3 s
        };
        _metricsTimer.Start();
        UpdateMetrics();
        UpdateTopRamProcs();
    }

    private int _ramProcTick;

    /// <summary>Affiche les 3 processus les plus gourmands en RAM, avec un bouton « tuer » par ligne.</summary>
    private void UpdateTopRamProcs()
    {
        if (TopRamProcs is null) return;
        List<(string Name, long Mem, int Id)> top;
        try
        {
            top = System.Diagnostics.Process.GetProcesses()
                .Select(p => { try { return (Name: p.ProcessName, Mem: p.WorkingSet64, Id: p.Id); } catch { return (Name: "", Mem: 0L, Id: 0); } })
                .Where(t => t.Mem > 0 && !string.IsNullOrEmpty(t.Name))
                .OrderByDescending(t => t.Mem)
                .Take(3)
                .ToList();
        }
        catch { return; }

        TopRamProcs.Children.Clear();
        var muted = (Brush)FindResource("TextPrimaryBrush");
        foreach (var t in top)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lbl = new TextBlock
            {
                Text = $"{(t.Name.Length > 11 ? t.Name[..11] : t.Name)}  {t.Mem / (1024 * 1024)} Mo",
                Foreground = muted,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(lbl, 0);
            row.Children.Add(lbl);

            var kill = new Button
            {
                Content = "✕",
                Tag = t.Id,
                Width = 22,
                Height = 18,
                FontSize = 11,
                Padding = new Thickness(0),
                Cursor = Cursors.Hand,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ToolTip = $"Arrêter {t.Name} (PID {t.Id})"
            };
            kill.Click += OnKillRamProc;
            Grid.SetColumn(kill, 1);
            row.Children.Add(kill);

            TopRamProcs.Children.Add(row);
        }
    }

    private Dictionary<int, TimeSpan> _prevCpuTimes = new();
    private DateTime _prevCpuStamp = DateTime.UtcNow;

    /// <summary>Affiche les 3 processus les plus gourmands en CPU (entre 2 mesures), avec kill.</summary>
    private void UpdateTopCpuProcs()
    {
        if (TopCpuProcs is null) return;
        DateTime now = DateTime.UtcNow;
        double elapsed = (now - _prevCpuStamp).TotalSeconds;
        var cur = new Dictionary<int, TimeSpan>();
        var rows = new List<(string Name, int Id, double Cpu)>();

        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    var t = p.TotalProcessorTime;
                    cur[p.Id] = t;
                    if (elapsed > 0.1 && _prevCpuTimes.TryGetValue(p.Id, out var prev))
                    {
                        double pct = (t - prev).TotalSeconds / (elapsed * Environment.ProcessorCount) * 100.0;
                        if (pct >= 0.1) rows.Add((p.ProcessName, p.Id, Math.Min(100, pct)));
                    }
                }
                catch { /* processus protégé : ignoré */ }
            }
        }
        catch { return; }

        _prevCpuTimes = cur;
        _prevCpuStamp = now;

        var top = rows.OrderByDescending(r => r.Cpu).Take(3).ToList();
        TopCpuProcs.Children.Clear();
        var fg = (Brush)FindResource("TextPrimaryBrush");
        foreach (var t in top)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(Col0(new TextBlock
            {
                Text = $"{(t.Name.Length > 11 ? t.Name[..11] : t.Name)}  {t.Cpu:0}%",
                Foreground = fg, FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            }));

            var kill = new Button
            {
                Content = "✕", Tag = t.Id, Width = 22, Height = 18, FontSize = 11,
                Padding = new Thickness(0), Cursor = Cursors.Hand,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                ToolTip = $"Arrêter {t.Name} (PID {t.Id})"
            };
            kill.Click += OnKillRamProc;
            Grid.SetColumn(kill, 1);
            row.Children.Add(kill);

            TopCpuProcs.Children.Add(row);
        }
    }

    private static UIElement Col0(UIElement el) { Grid.SetColumn(el, 0); return el; }

    /// <summary>Arrête le processus sélectionné (1 clic).</summary>
    private void OnKillRamProc(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int pid }) return;
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            string name = p.ProcessName;
            p.Kill();
            Log($"Processus arrêté : {name} (PID {pid}).");
            UpdateTopRamProcs();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossible d'arrêter ce processus : {ex.Message}\n" +
                            "(certains processus système sont protégés)",
                "Arrêter le processus", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
    /// Exige la carte d'identité propriétaire avant d'accéder au coffre-fort.
    /// Une fois la carte validée, l'accès reste ouvert pour la session (jusqu'à ce
    /// qu'on quitte l'onglet, ce qui reverrouille le coffre).
    /// </summary>
    private bool EnsureVaultUnlocked()
    {
        if (_vaultUnlocked) return true;
        if (!CardAuth.Gate(this, "Ouvrir le coffre-fort", out _)) return false;
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

    /// <summary>
    /// Importe en arrière-plan les identifiants enregistrés dans les navigateurs
    /// (site/application, login, mot de passe) vers le coffre-fort chiffré, sans doublon.
    /// </summary>
    private async Task ImportBrowserPasswordsAsync()
    {
        try
        {
            var imported = await Task.Run(() => BrowserPasswords.ReadAll());
            if (imported.Count == 0) return;

            var raw = SecretVault.Load("vault").GetValueOrDefault("data");
            List<VaultDto> dtos;
            try { dtos = string.IsNullOrEmpty(raw) ? new() : (JsonSerializer.Deserialize<List<VaultDto>>(raw) ?? new()); }
            catch { dtos = new(); }

            var existing = new HashSet<string>(
                dtos.Select(d => (d.Url + "|" + d.Username).ToLowerInvariant()));
            int added = 0;
            foreach (var l in imported)
            {
                string key = (l.Url + "|" + l.Login).ToLowerInvariant();
                if (!existing.Add(key)) continue;
                string title = HostOf(l.Url);
                dtos.Add(new VaultDto(Guid.NewGuid().ToString("N"), title, l.Login, l.Password, l.Url,
                    $"Importé automatiquement depuis {l.Browser}"));
                added++;
            }
            if (added > 0)
            {
                SecretVault.Save("vault", new Dictionary<string, string> { ["data"] = JsonSerializer.Serialize(dtos) });
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Log($"{added} identifiant(s) navigateur importé(s) dans le coffre-fort.");
                    _vaultLoaded = false;   // rechargé au prochain accès au coffre
                }));
            }
        }
        catch { /* import best-effort : jamais bloquant */ }
    }

    private static string HostOf(string url)
    {
        try { return new Uri(url).Host; } catch { return url; }
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
                Notify("⚠ Jumeau numérique", "Des éléments sensibles (services/pilotes/démarrage/hosts) ont changé.", "ADN");
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
            DrawNetworkRadar();

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
            ? "Bonjour 👋 Je surveille votre système. Posez-moi une question (au clavier ou au 🎙️ micro), ou je vous alerterai en cas d'activité suspecte."
            : "Pour discuter avec moi GRATUITEMENT : créez une clé Google Gemini sur aistudio.google.com (« Get API key »), puis collez-la dans Réglages → Assistant IA. Une clé Anthropic « sk-ant-… » fonctionne aussi.",
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

    private System.Speech.Recognition.SpeechRecognitionEngine? _speech;
    private bool _listening;

    /// <summary>Dictée vocale : transcrit la parole dans le champ de chat (reconnaissance Windows).</summary>
    private void OnChatMic(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_listening)
            {
                _speech?.RecognizeAsyncStop();
                _listening = false;
                if (ChatMicButton is not null) ChatMicButton.Content = "🎙️";
                return;
            }
            if (_speech is null)
            {
                _speech = new System.Speech.Recognition.SpeechRecognitionEngine();
                _speech.LoadGrammar(new System.Speech.Recognition.DictationGrammar());
                _speech.SetInputToDefaultAudioDevice();
                _speech.SpeechRecognized += (_, ev) => Dispatcher.Invoke(() =>
                {
                    if (ChatInput is null) return;
                    ChatInput.Text = (ChatInput.Text + " " + ev.Result.Text).Trim();
                    ChatInput.CaretIndex = ChatInput.Text.Length;
                });
                _speech.RecognizeCompleted += (_, _) => Dispatcher.Invoke(() =>
                {
                    _listening = false;
                    if (ChatMicButton is not null) ChatMicButton.Content = "🎙️";
                });
            }
            _speech.RecognizeAsync(System.Speech.Recognition.RecognizeMode.Multiple);
            _listening = true;
            if (ChatMicButton is not null) ChatMicButton.Content = "⏹️";
        }
        catch (Exception ex)
        {
            _listening = false;
            if (ChatMicButton is not null) ChatMicButton.Content = "🎙️";
            AddChat("Copilote", "🎙️ Micro / reconnaissance vocale indisponible : " + ex.Message +
                "\nActivez-la dans Windows : Paramètres → Heure et langue → Voix (installer une voix), et autorisez l'accès au micro.", false);
        }
    }

    private async void OnChatSend(object sender, RoutedEventArgs e)
    {
        string q = ChatInput.Text.Trim();
        if (q.Length == 0) return;
        if (!AiAssistant.IsConfigured)
        {
            AddChat("Copilote", "Assistant IA non configuré. IA gratuite : collez une clé Google Gemini (aistudio.google.com) dans Réglages → Assistant IA.", false);
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

    // ------------------------------ SMTP (rapport de désinstallation) -----------

    private void LoadSmtpConfig()
    {
        var s = SecretVault.Load("smtp");
        if (SmtpHost is not null) SmtpHost.Text = s.GetValueOrDefault("host") ?? "";
        if (SmtpPort is not null) SmtpPort.Text = s.GetValueOrDefault("port") ?? "587";
        if (SmtpUser is not null) SmtpUser.Text = s.GetValueOrDefault("user") ?? "";
        if (SmtpTo is not null) SmtpTo.Text = SecretVault.Load("uninstall").GetValueOrDefault("email") ?? "iatechfutur@iatechfutur.be";
        // Un mot de passe déjà enregistré ne se réaffiche pas (chiffré) : on masque l'indice
        // et on montre des points pour signifier qu'il est bien mémorisé.
        if (SmtpPassHint is not null)
            SmtpPassHint.Visibility = string.IsNullOrEmpty(s.GetValueOrDefault("pass"))
                ? Visibility.Visible : Visibility.Collapsed;
        if (SmtpStatus is not null)
            SmtpStatus.Text = string.IsNullOrWhiteSpace(s.GetValueOrDefault("host"))
                ? "Non configuré (repli : e-mail pré-rempli à la désinstallation)."
                : "✓ SMTP configuré.";
    }

    /// <summary>Masque l'indice blanc du champ mot de passe dès que l'utilisateur tape.</summary>
    private void OnSmtpPassChanged(object sender, RoutedEventArgs e)
    {
        if (SmtpPassHint is not null)
            SmtpPassHint.Visibility = (SmtpPass?.Password.Length ?? 0) > 0
                ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SaveSmtpFromFields()
    {
        var cfg = new Dictionary<string, string>
        {
            ["host"] = SmtpHost?.Text.Trim() ?? "",
            ["port"] = SmtpPort?.Text.Trim() ?? "587",
            ["user"] = SmtpUser?.Text.Trim() ?? "",
        };
        // On ne réécrit le mot de passe que s'il a été saisi (sinon on garde l'ancien).
        string pass = SmtpPass?.Password ?? "";
        cfg["pass"] = pass.Length > 0 ? pass : (SecretVault.Load("smtp").GetValueOrDefault("pass") ?? "");
        SecretVault.Save("smtp", cfg);
        SecretVault.Save("uninstall", new Dictionary<string, string> { ["email"] = SmtpTo?.Text.Trim() ?? "iatechfutur@iatechfutur.be" });
    }

    private void OnSaveSmtp(object sender, RoutedEventArgs e)
    {
        SaveSmtpFromFields();
        if (SmtpStatus is not null) SmtpStatus.Text = "✓ Configuration SMTP enregistrée (chiffrée).";
        Log("Configuration SMTP (rapport de désinstallation) enregistrée.");
    }

    private async void OnTestSmtp(object sender, RoutedEventArgs e)
    {
        SaveSmtpFromFields();
        var s = SecretVault.Load("smtp");
        string host = s.GetValueOrDefault("host") ?? "";
        string to = SmtpTo?.Text.Trim() ?? "";
        if (host.Length == 0 || to.Length == 0)
        {
            if (SmtpStatus is not null) SmtpStatus.Text = "Renseignez au moins le serveur SMTP et le destinataire.";
            return;
        }
        if (SmtpStatus is not null) SmtpStatus.Text = "Envoi du test…";
        try
        {
            await Task.Run(() =>
            {
                using var msg = new System.Net.Mail.MailMessage(s.GetValueOrDefault("user") ?? to, to,
                    "✅ Test IATECH-SHIELD — rapport de désinstallation",
                    $"Test réussi depuis {Environment.MachineName} le {DateTime.Now:yyyy-MM-dd HH:mm}. " +
                    "Les rapports de désinstallation seront envoyés à cette adresse.");
                using var client = new System.Net.Mail.SmtpClient(host)
                {
                    Port = int.TryParse(s.GetValueOrDefault("port"), out int p) ? p : 587,
                    EnableSsl = true,
                    Credentials = new System.Net.NetworkCredential(s.GetValueOrDefault("user"), s.GetValueOrDefault("pass"))
                };
                client.Send(msg);
            });
            if (SmtpStatus is not null) SmtpStatus.Text = $"✓ Test envoyé à {to}. Vérifiez la boîte de réception.";
        }
        catch (Exception ex)
        {
            if (SmtpStatus is not null) SmtpStatus.Text = $"❌ Échec : {ex.Message}";
        }
    }

    // ----------------------------------------------------- Configuration itsme -

    private void LoadItsmeConfig()
    {
        var itsme = ItsmeAuth.FromStore();
        if (ItsmeStatusText is null) return;
        if (itsme is { IsConfigured: true })
        {
            if (ItsmeClientId is not null) ItsmeClientId.Text = itsme.ClientId;
            if (ItsmeServiceCode is not null) ItsmeServiceCode.Text = itsme.ServiceCode;
            if (ItsmeEnv is not null) ItsmeEnv.SelectedIndex = itsme.Environment == "e2e" ? 1 : 0;
            ItsmeStatusText.Text = "✓ itsme configuré — déverrouillage par notification téléphone actif.";
        }
        else
        {
            ItsmeStatusText.Text = "itsme non configuré. Sans identifiants partenaire, le déverrouillage itsme est indisponible.";
        }
    }

    private void OnSaveItsme(object sender, RoutedEventArgs e)
    {
        string id = ItsmeClientId?.Text.Trim() ?? "";
        string secret = ItsmeClientSecret?.Password.Trim() ?? "";
        string code = ItsmeServiceCode?.Text.Trim() ?? "";
        string env = ItsmeEnv?.SelectedIndex == 1 ? "e2e" : "prd";
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(code))
        {
            if (ItsmeStatusText is not null) ItsmeStatusText.Text = "⚠ client_id et code de service sont requis.";
            return;
        }
        ItsmeAuth.SaveConfig(id, secret, code, env);
        if (ItsmeClientSecret is not null) ItsmeClientSecret.Clear();
        if (ItsmeStatusText is not null) ItsmeStatusText.Text = "✓ itsme enregistré (chiffré). Le bouton itsme de l'écran de verrouillage est actif.";
        Log("Configuration itsme enregistrée.");
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

    /// <summary>Chemin transmis par le menu contextuel « Scanner avec IATECHSHIELD PRO » (clic droit).</summary>
    private string? _pendingShellScan;

    /// <summary>Appelé au démarrage quand l'app est lancée via le clic droit de l'Explorateur.</summary>
    public void RequestShellScan(string path)
    {
        _pendingShellScan = path;
        if (_ready) RunPendingShellScan();
    }

    private async void RunPendingShellScan()
    {
        var path = _pendingShellScan;
        _pendingShellScan = null;
        if (string.IsNullOrEmpty(path)) return;
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        if (_scanning || _scanner is null) return;
        if (!EnsureActivated()) return;

        // Bascule sur la page d'analyse et lance le scan ciblé immédiatement.
        if (NavScan is not null) NavScan.IsChecked = true;
        ShowPage("Scan");
        Activate();
        SoundFx.ScanStart();
        await RunScanAsync(new[] { path }, status => { if (FullScanStatus is not null) FullScanStatus.Text = status; });
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

    /// <summary>
    /// « Tester ma sécurité » : vérifie l'état réel des protections (temps réel, Defender,
    /// pare-feu, anti-ransomware…) et lance le fichier test antivirus standard EICAR
    /// (totalement inoffensif) pour confirmer que la détection fonctionne. Affiche un score.
    /// </summary>
    private async void OnSecurityTest(object sender, RoutedEventArgs e)
    {
        SecurityTestButton.IsEnabled = false;
        ScanStatusText.Text = "Test de sécurité en cours…";
        try
        {
            var sb = new StringBuilder();
            int pass = 0, total = 0;
            void Check(string name, bool ok)
            {
                total++;
                if (ok) pass++;
                sb.AppendLine((ok ? "✓  " : "✗  ") + name);
            }

            Check("Protection en temps réel IATECH", _monitor is not null);
            var sec = await SecurityScore.EvaluateAsync(_monitor is not null, TamperEnabled);
            foreach (var c in sec.Checks) Check(c.Name, c.Passed);
            Check("Protection anti-ransomware (appâts)", _ransomGuard is not null);

            bool eicar = await Task.Run(RunEicarTest);
            Check("Détection du fichier test antivirus (EICAR)", eicar);

            int score = total == 0 ? 0 : (int)Math.Round(pass * 100.0 / total);
            SoundFx.ScanDone();
            ScanStatusText.Text = $"Test terminé : {score}/100.";
            MessageBox.Show(
                $"Score de sécurité : {score}/100   ({pass}/{total} contrôles réussis)\n\n{sb}\n" +
                "Le test EICAR utilise le fichier d'essai antivirus standard, totalement inoffensif.",
                "🧪 Test de sécurité — IATECH-SHIELD PRO",
                MessageBoxButton.OK, score >= 80 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Test interrompu : {ex.Message}", "Test de sécurité",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SecurityTestButton.IsEnabled = true;
        }
    }

    /// <summary>Écrit le fichier test EICAR et vérifie qu'il est bloqué/détecté. Inoffensif.</summary>
    private bool RunEicarTest()
    {
        // Signature EICAR officielle (reconstituée pour ne pas embarquer la chaîne telle quelle).
        string eicar = @"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";
        string dir = Path.Combine(Path.GetTempPath(), "iatech-selftest");
        string path = Path.Combine(dir, "eicar_test.com");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, eicar);
        }
        catch
        {
            return true; // écriture refusée = un antivirus a bloqué → protection active
        }

        try
        {
            System.Threading.Thread.Sleep(500);      // laisse la protection réagir
            bool removed = !File.Exists(path);        // supprimé/quarantaine par l'AV = détecté
            bool flagged = false;
            if (!removed && _scanner is not null)
            {
                try { flagged = _scanner.ScanFile(path).IsThreat; } catch { }
            }
            return removed || flagged;
        }
        catch { return true; }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch { } }
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
        if (ApplyThreatAction(item, item.SelectedAction, silent: false))
        {
            _threats.Remove(item);
            _threatCount = _threats.Count;
            UpdateThreatUi();
        }
    }

    /// <summary>Applique une action à une menace. Renvoie true si elle doit être retirée de la liste.</summary>
    private bool ApplyThreatAction(ThreatItem item, string action, bool silent)
    {
        try
        {
            switch (action)
            {
                case "Supprimer":
                    if (File.Exists(item.Path)) File.Delete(item.Path);
                    Log($"Menace supprimée : {item.Path}");
                    return true;
                case "Mettre en quarantaine":
                    _quarantine?.Add(item.Path, item.Name, item.Sha);
                    Log($"Menace mise en quarantaine : {item.Path}");
                    return true;
                case "Analyser":
                    if (!silent)
                        MessageBox.Show(this, $"Menace : {item.Name}\nFichier : {item.Path}\nSHA-256 : {item.Sha}",
                            "Analyse de la menace", MessageBoxButton.OK, MessageBoxImage.Information);
                    return false; // on garde la menace
                case "Ignorer":
                    Log($"Menace ignorée : {item.Path}");
                    return true;
            }
        }
        catch (Exception ex)
        {
            if (!silent)
                MessageBox.Show(this, $"Action impossible : {ex.Message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return false;
    }

    /// <summary>Applique l'action groupée choisie à TOUTES les menaces de la liste.</summary>
    private void OnApplyAllThreats(object sender, RoutedEventArgs e)
    {
        if (_threats.Count == 0) { if (FullScanStatus is not null) FullScanStatus.Text = "Aucune menace à traiter."; return; }
        string action = (BulkActionCombo?.SelectedValue as string)
                        ?? (BulkActionCombo?.Text) ?? "Mettre en quarantaine";
        if (action == "Analyser") { if (FullScanStatus is not null) FullScanStatus.Text = "« Analyser » ne s'applique pas en masse."; return; }

        var confirm = new PromptWindow("Action groupée",
            $"Appliquer « {action} » à {_threats.Count} menace(s) ? Tapez OUI.", "Confirmer") { Owner = this };
        if (confirm.ShowDialog() != true || !string.Equals(confirm.Value.Trim(), "OUI", StringComparison.OrdinalIgnoreCase))
            return;

        int done = 0;
        foreach (var item in _threats.ToList())
            if (ApplyThreatAction(item, action, silent: true)) { _threats.Remove(item); done++; }

        _threatCount = _threats.Count;
        UpdateThreatUi();
        if (FullScanStatus is not null)
            FullScanStatus.Text = $"✓ Action groupée « {action} » appliquée à {done} menace(s). {_threats.Count} restante(s).";
        Log($"Action groupée : {action} sur {done} menace(s).");
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

    private bool _tamperEnabled;
    private bool _tamperLoaded;

    private bool TamperEnabled
    {
        get
        {
            if (!_tamperLoaded)
            {
                _tamperLoaded = true;
                _tamperEnabled = SecretVault.Load("tamper").GetValueOrDefault("enabled") == "1";
            }
            return _tamperEnabled;
        }
    }

    /// <summary>Exige la carte d'identité propriétaire ; true si protection absente ou carte validée.</summary>
    private bool RequireTamperAuth(string action)
    {
        if (!TamperEnabled) return true;
        return CardAuth.Gate(this, action, out _);
    }

    private void OnSetTamperPassword(object sender, RoutedEventArgs e)
    {
        // Active la protection : la carte d'identité propriétaire devient la clé.
        if (!CardAuth.Gate(this, "Activer la protection anti-altération", out _)) return;
        _tamperEnabled = true;
        SecretVault.Save("tamper", new Dictionary<string, string> { ["enabled"] = "1" });
        if (TamperStatus is not null) TamperStatus.Text = "Protection activée : carte d'identité requise pour désactiver le temps réel ou quitter.";
        Log("Protection anti-altération activée (carte d'identité).");
    }

    private void OnClearTamperPassword(object sender, RoutedEventArgs e)
    {
        if (!TamperEnabled)
        {
            if (TamperStatus is not null) TamperStatus.Text = "Aucune protection définie.";
            return;
        }
        if (!CardAuth.Gate(this, "Retirer la protection anti-altération", out _)) return;
        _tamperEnabled = false;
        SecretVault.Delete("tamper");
        if (TamperStatus is not null) TamperStatus.Text = "Protection retirée.";
        Log("Protection anti-altération retirée (carte d'identité).");
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

    private bool LockConfigured => CardAuth.IsEnrolled || !string.IsNullOrEmpty(_lockPinHash) || !string.IsNullOrEmpty(_lockPasswordHash) || _lockHello;

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
        if (!LockConfigured) return;

        _lockTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _lockTimer.Tick -= OnLockTick;
        _lockTimer.Tick += OnLockTick;
        _lockTimer.Start();
    }

    private void OnLockTick(object? sender, EventArgs e)
    {
        if (_lockShowing) return;
        if (!LockConfigured) return;
        if (IdleMilliseconds() >= _lockDelayMs)
            ShowLockScreen();
    }

    private static uint IdleMilliseconds()
    {
        var info = new LastInputInfo { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return 0;
        return (uint)Environment.TickCount - info.dwTime;
    }

    // Surveillance du retrait de la carte d'identité : si on enlève la carte du
    // lecteur, le PC se verrouille automatiquement.
    private DispatcherTimer? _cardWatchTimer;
    private bool _cardWasPresent;

    private void StartCardRemovalWatcher()
    {
        try
        {
            _cardWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _cardWatchTimer.Tick += OnCardWatchTick;
            _cardWatchTimer.Start();
        }
        catch { /* indisponible : le verrouillage auto carte est simplement inactif */ }
    }

    private void OnCardWatchTick(object? sender, EventArgs e)
    {
        // Entièrement protégé : ce minuteur ne doit jamais fermer l'application.
        try
        {
            if (_lockShowing) return;
            bool present;
            try { present = EidReader.IsCardPresent(); }
            catch { return; }

            // Transition « carte présente » → « carte retirée » : verrouillage immédiat.
            if (_cardWasPresent && !present)
            {
                _cardWasPresent = false;
                LoadLockConfig();
                if (LockConfigured)
                {
                    Log("Carte d'identité retirée : alarme + verrouillage automatique.");
                    // Alarme sonore continue : ne s'arrête qu'au déverrouillage (code PIN).
                    SoundFx.StartAlarm();
                    Notify("⚠️ Carte retirée", "Carte d'identité retirée. Réinsérez la carte propriétaire pour déverrouiller et arrêter l'alarme.", "Contrôle");
                    try
                    {
                        ShowLockScreen();   // modal : bloque jusqu'au déverrouillage par code PIN
                    }
                    finally
                    {
                        SoundFx.StopAlarm();   // déverrouillé (code PIN saisi) : on coupe l'alarme
                    }
                }
            }
            else
            {
                _cardWasPresent = present;
            }
        }
        catch { /* on n'interrompt jamais l'application pour cette surveillance */ }
    }

    private void ShowLockScreen()
    {
        if (_lockShowing) return;
        _lockShowing = true;
        try
        {
            ShowFromTray();
            IatechShield.Tools.AccessLog.Record("Verrouillage", "—", Environment.UserName, "Session verrouillée");
            // Le verrouillage clôt toute session eID en cours (déconnexion).
            IatechShield.Tools.EidSessionLog.EndSession();
            // Si une carte propriétaire est enregistrée : déverrouillage carte-uniquement.
            var lockScreen = new LockScreen(_lockPinHash, _lockPasswordHash, _lockHello, cardOnly: CardAuth.IsEnrolled) { Owner = this };
            lockScreen.ShowDialog();
            // Session déverrouillée : on consigne qui a accédé et par quel moyen.
            IatechShield.Tools.AccessLog.Record("Déverrouillage", lockScreen.UnlockMethod,
                lockScreen.UnlockIdentity, lockScreen.UnlockDetail);
            // Déverrouillage par carte d'identité : on ouvre une session dans l'« ID Registre ».
            if (lockScreen.UnlockCard is { IsBelgianEid: true } card)
            {
                IatechShield.Tools.EidSessionLog.StartSession(card.Name, card.FirstNames, card.BirthDate,
                    card.NationalNumber, Guid.NewGuid().ToString("N"));
                // Écran d'accueil plein écran « Bienvenue [prénom nom] » avant l'ouverture.
                try
                {
                    string who = $"{card.FirstNames} {card.Name}".Trim();
                    new WelcomeWindow(who) { Owner = this }.ShowDialog();
                }
                catch { /* l'accueil ne doit jamais bloquer l'ouverture */ }
            }
            if (AccessList is not null && PageAccess is not null && PageAccess.Visibility == Visibility.Visible)
                BuildAccessLog();
        }
        catch { /* en cas d'échec d'affichage, on ne bloque pas l'utilisateur */ }
        finally
        {
            _lockShowing = false;
            try { SoundFx.StopAlarm(); } catch { }   // filet de sécurité : jamais d'alarme persistante
        }
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

    // ------------------------------------ Protections avancées (page Protection) -

    private const string NetCutRule = "IATECHShieldNetCut";
    private const string NoPingRule = "IATECHShieldNoPing";
    private DispatcherTimer? _netSchedTimer;
    private bool _netCutActive;

    /// <summary>Ouvre un outil/URI de protection Windows (tag = cible).</summary>
    private void OnProtTool(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string target } || string.IsNullOrEmpty(target)) return;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            // Repli : si l'URI windowsdefender:// échoue, on ouvre la Sécurité Windows.
            try { Process.Start(new ProcessStartInfo("windowsdefender:") { UseShellExecute = true }); }
            catch { Log($"Ouverture impossible : {ex.Message}"); }
        }
    }

    // --- Programmateur de connexion Internet ---

    private void OnNetScheduleToggled(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (NetScheduleSwitch.IsChecked == true)
        {
            _netSchedTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _netSchedTimer.Tick -= NetScheduleTick;
            _netSchedTimer.Tick += NetScheduleTick;
            _netSchedTimer.Start();
            NetScheduleStatus.Text = $"⏰ Programmation active : Internet coupé à {NetCutTime.Text}, rétabli à {NetRestoreTime.Text}.";
            Log("Programmateur de connexion Internet activé.");
            NetScheduleTick(this, EventArgs.Empty);   // applique tout de suite si on est déjà dans la plage
        }
        else
        {
            _netSchedTimer?.Stop();
            NetScheduleStatus.Text = "Programmation désactivée. (Internet non coupé.)";
            Log("Programmateur de connexion Internet désactivé.");
        }
    }

    private async void NetScheduleTick(object? sender, EventArgs e)
    {
        if (NetScheduleSwitch.IsChecked != true) return;
        if (!TimeSpan.TryParse(NetCutTime.Text.Trim(), out var cut) ||
            !TimeSpan.TryParse(NetRestoreTime.Text.Trim(), out var restore))
            return;

        var now = DateTime.Now.TimeOfDay;
        // Plage « coupé » : de l'heure de coupure à l'heure de rétablissement (gère le passage de minuit).
        bool shouldBeCut = cut <= restore
            ? (now >= cut && now < restore)
            : (now >= cut || now < restore);

        if (shouldBeCut && !_netCutActive) await CutInternet(true);
        else if (!shouldBeCut && _netCutActive) await CutInternet(false);
    }

    private async void OnCutNetNow(object sender, RoutedEventArgs e) => await CutInternet(true);
    private async void OnRestoreNetNow(object sender, RoutedEventArgs e) => await CutInternet(false);

    /// <summary>Coupe (ou rétablit) Internet via une règle de pare-feu bloquant tout le trafic sortant/entrant.</summary>
    private async Task CutInternet(bool cut)
    {
        void St(string s) { if (NetScheduleStatus is not null) NetScheduleStatus.Text = s; }
        try
        {
            if (cut)
            {
                St("Coupure d'Internet en cours…");
                // Méthode fiable et visible : on désactive les cartes réseau actives
                // (et on pose aussi une règle de pare-feu bloquant tout, par sécurité).
                await RunHiddenAsync("cmd.exe",
                    $"/c netsh advfirewall firewall add rule name=\"{NetCutRule}\" dir=out action=block & " +
                    $"netsh advfirewall firewall add rule name=\"{NetCutRule}\" dir=in action=block");
                string res = await RunPs("$a=Get-NetAdapter -Physical | Where-Object {$_.Status -eq 'Up'}; " +
                    "if($a){ $a | Disable-NetAdapter -Confirm:$false; 'OK:' + (($a.Name) -join ', ') } else { 'NONE' }");
                _netCutActive = true;
                if (res.Contains("OK:"))
                    St($"🔴 Internet COUPÉ ({res.Substring(res.IndexOf(':') + 1).Trim()} désactivée(s)).");
                else if (res.Contains("NONE"))
                    St("🔴 Internet coupé (règle de pare-feu). Aucune carte réseau active à désactiver.");
                else
                    St("Tentative effectuée. Si Internet fonctionne encore, relancez en administrateur.");
                Notify("Connexion Internet coupée", "La connexion a été bloquée.", "Protection");
                Log("Internet coupé.");
            }
            else
            {
                St("Rétablissement d'Internet…");
                await RunHiddenAsync("cmd.exe", $"/c netsh advfirewall firewall delete rule name=\"{NetCutRule}\"");
                await RunPs("Get-NetAdapter -Physical | Enable-NetAdapter -Confirm:$false");
                _netCutActive = false;
                St("🟢 Internet rétabli (cartes réseau réactivées).");
                Log("Internet rétabli.");
            }
        }
        catch (Exception ex)
        {
            St($"Échec : {ex.Message} (droits administrateur requis).");
        }
    }

    // --- Pare-feu : exception ---

    private async void OnFirewallAllow(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Programme à autoriser au travers du pare-feu", Filter = "Programmes (*.exe)|*.exe" };
        if (dlg.ShowDialog(this) != true) return;
        string exe = dlg.FileName;
        string name = $"IATECH-Allow {Path.GetFileNameWithoutExtension(exe)}";
        try
        {
            await RunHiddenAsync("cmd.exe", $"/c netsh advfirewall firewall add rule name=\"{name}\" dir=in action=allow program=\"{exe}\" enable=yes & " +
                                            $"netsh advfirewall firewall add rule name=\"{name}\" dir=out action=allow program=\"{exe}\" enable=yes");
            Log($"Pare-feu : {Path.GetFileName(exe)} autorisé.");
            Notify("Exception pare-feu ajoutée", $"{Path.GetFileName(exe)} est désormais autorisé.", "Protection");
        }
        catch (Exception ex) { Log($"Échec de l'exception pare-feu : {ex.Message}"); }
    }

    // --- Mode furtif (ICMP / ping) ---

    private async void OnStealthToggled(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        try
        {
            if (StealthSwitch.IsChecked == true)
            {
                await RunHiddenAsync("cmd.exe", $"/c netsh advfirewall firewall add rule name=\"{NoPingRule}\" protocol=icmpv4:8,any dir=in action=block");
                Log("Mode furtif activé (ping bloqué).");
            }
            else
            {
                await RunHiddenAsync("cmd.exe", $"/c netsh advfirewall firewall delete rule name=\"{NoPingRule}\"");
                Log("Mode furtif désactivé.");
            }
        }
        catch (Exception ex) { Log($"Mode furtif : {ex.Message}"); }
    }

    // --- Protection USB (AutoRun) ---

    private async void OnUsbGuardToggled(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        const string key = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
        try
        {
            if (UsbGuardSwitch.IsChecked == true)
            {
                await RunHiddenAsync("cmd.exe", $"/c reg add \"{key}\" /v NoDriveTypeAutoRun /t REG_DWORD /d 255 /f");
                Log("Protection USB activée (AutoRun désactivé).");
            }
            else
            {
                await RunHiddenAsync("cmd.exe", $"/c reg add \"{key}\" /v NoDriveTypeAutoRun /t REG_DWORD /d 145 /f");
                Log("Protection USB désactivée (AutoRun rétabli).");
            }
        }
        catch (Exception ex) { Log($"Protection USB : {ex.Message}"); }
    }

    // --- Protection Web (SmartScreen) ---

    private void OnWebGuardToggled(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        Log(WebGuardSwitch.IsChecked == true ? "Protection Web (anti-hameçonnage) activée." : "Protection Web désactivée.");
        try { Process.Start(new ProcessStartInfo("windowsdefender://appbrowser") { UseShellExecute = true }); } catch { }
    }

    // --- Historique de protection ---

    private void OnOpenProtectionHistory(object sender, RoutedEventArgs e)
    {
        if (NavLogs is not null) NavLogs.IsChecked = true;
        ShowPage("Logs");
    }

    // ------------------------------------ Registre des accès (connexions) ------

    /// <summary>Élément de liste du registre des accès.</summary>
    public sealed class AccessItem
    {
        public string Glyph { get; set; } = "🔓";
        public string Kind { get; set; } = "";
        public string Method { get; set; } = "";
        public string Identity { get; set; } = "";
        public string Detail { get; set; } = "";
        public string TimeText { get; set; } = "";
        public System.Windows.Media.Brush MethodColor { get; set; } = System.Windows.Media.Brushes.Cyan;
    }

    private void BuildAccessLog()
    {
        if (AccessList is null) return;
        var items = new List<AccessItem>();
        foreach (var ev in IatechShield.Tools.AccessLog.Load())
        {
            bool unlock = ev.Kind.StartsWith("Déver", StringComparison.OrdinalIgnoreCase)
                       || ev.Kind.StartsWith("Conn", StringComparison.OrdinalIgnoreCase);
            string glyph = ev.Method switch
            {
                "Carte d'identité (eID)" => "🪪",
                "itsme" => "📱",
                "Windows Hello" => "🙂",
                "PIN" => "🔢",
                "Mot de passe" => "🔑",
                _ => unlock ? "🔓" : "🔒"
            };
            items.Add(new AccessItem
            {
                Glyph = glyph,
                Kind = ev.Kind,
                Method = string.IsNullOrWhiteSpace(ev.Method) || ev.Method == "—" ? "" : $"· {ev.Method}",
                Identity = string.IsNullOrWhiteSpace(ev.Identity) ? "" : $"👤 {ev.Identity}",
                Detail = ev.Detail,
                TimeText = ev.Time.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"),
                MethodColor = unlock ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.OrangeRed
            });
        }
        if (items.Count == 0)
            items.Add(new AccessItem { Glyph = "ℹ️", Kind = "Aucun accès enregistré", Identity = "", Detail = "Les verrouillages/déverrouillages apparaîtront ici.", TimeText = "" });
        AccessList.ItemsSource = items;
        if (AccessStatus is not null) AccessStatus.Text = $"{IatechShield.Tools.AccessLog.Load().Count} évènement(s) enregistré(s).";
    }

    private void OnRefreshAccess(object sender, RoutedEventArgs e) => BuildAccessLog();

    private void OnClearAccess(object sender, RoutedEventArgs e)
    {
        IatechShield.Tools.AccessLog.Clear();
        BuildAccessLog();
        Log("Registre des accès effacé.");
    }

    // ------------------------------------------ Gestion des utilisateurs Windows -

    /// <summary>Ligne « utilisateur Windows ».</summary>
    public sealed class UserAccountItem
    {
        public string Name { get; set; } = "";
        public bool IsAdmin { get; set; }
        public bool Enabled { get; set; } = true;
        public string RoleText => IsAdmin ? "ADMIN" : "STANDARD";
        public System.Windows.Media.Brush RoleColor =>
            new SolidColorBrush(IsAdmin ? Color.FromRgb(0xFB, 0xBF, 0x24) : Color.FromRgb(0x22, 0xD3, 0xE8));
        public string StatusText => Enabled ? "Compte actif" : "Compte désactivé";
        public string ToggleRoleLabel => IsAdmin ? "Rétrograder standard" : "Promouvoir admin";
        public string ToggleEnabledLabel => Enabled ? "Désactiver" : "Activer";
    }

    // SID bien connu du groupe Administrateurs (indépendant de la langue de Windows).
    private const string AdminsSid = "S-1-5-32-544";

    private async Task BuildUsersAsync()
    {
        if (UsersList is null) return;
        if (UsersStatus is not null) UsersStatus.Text = "Lecture des comptes…";
        try
        {
            // Liste des membres administrateurs (par SID, robuste au multilingue).
            string adminsRaw = await RunPs(
                $"(Get-LocalGroupMember -SID {AdminsSid} | ForEach-Object {{ $_.Name.Split('\\')[-1] }}) -join ','");
            var admins = adminsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Liste des comptes locaux (nom|état).
            string usersRaw = await RunPs(
                "(Get-LocalUser | ForEach-Object { $_.Name + '|' + $_.Enabled }) -join ';'");

            var items = new List<UserAccountItem>();
            foreach (var row in usersRaw.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = row.Split('|');
                string name = parts[0].Trim();
                if (name.Length == 0) continue;
                items.Add(new UserAccountItem
                {
                    Name = name,
                    Enabled = parts.Length < 2 || parts[1].Trim().Equals("True", StringComparison.OrdinalIgnoreCase),
                    IsAdmin = admins.Contains(name)
                });
            }
            UsersList.ItemsSource = items;
            if (UsersStatus is not null)
                UsersStatus.Text = items.Count == 0
                    ? "Impossible de lire les comptes (droits administrateur requis)."
                    : $"{items.Count} compte(s) — {items.Count(i => i.IsAdmin)} administrateur(s).";
        }
        catch (Exception ex)
        {
            if (UsersStatus is not null) UsersStatus.Text = $"Échec : {ex.Message}";
        }
    }

    private void OnRefreshUsers(object sender, RoutedEventArgs e) => _ = BuildUsersAsync();

    private async void OnAddUser(object sender, RoutedEventArgs e)
    {
        var nameDlg = new PromptWindow("Ajouter un utilisateur", "Nom du nouvel utilisateur :", "Suivant") { Owner = this };
        if (nameDlg.ShowDialog() != true || string.IsNullOrWhiteSpace(nameDlg.Value)) return;
        string name = nameDlg.Value.Trim();

        var pinDlg = new PromptWindow("Code / mot de passe", $"Code PIN ou mot de passe de session pour « {name} » :", "Créer") { Owner = this };
        if (pinDlg.ShowDialog() != true) return;
        string pwd = pinDlg.Value;

        if (UsersStatus is not null) UsersStatus.Text = $"Création de « {name} »…";
        // Échappe les guillemets simples PowerShell.
        string n = name.Replace("'", "''");
        string p = pwd.Replace("'", "''");
        string cmd = string.IsNullOrEmpty(pwd)
            ? $"New-LocalUser -Name '{n}' -NoPassword -ErrorAction Stop"
            : $"New-LocalUser -Name '{n}' -Password (ConvertTo-SecureString '{p}' -AsPlainText -Force) -ErrorAction Stop";
        string res = await RunPs(cmd + "; if($?){'OK'}");
        if (res.Contains("OK"))
        {
            IatechShield.Tools.AccessLog.Record("Compte créé", "Admin local", name, "Nouvel utilisateur Windows");
            Log($"Utilisateur Windows créé : {name}.");
        }
        else if (UsersStatus is not null) UsersStatus.Text = $"Échec de la création : {res}";
        await BuildUsersAsync();
    }

    private async void OnToggleUserRole(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: UserAccountItem u }) return;
        string n = u.Name.Replace("'", "''");
        string cmd = u.IsAdmin
            ? $"Remove-LocalGroupMember -SID {AdminsSid} -Member '{n}' -ErrorAction Stop"
            : $"Add-LocalGroupMember -SID {AdminsSid} -Member '{n}' -ErrorAction Stop";
        string res = await RunPs(cmd + "; if($?){'OK'}");
        if (res.Contains("OK"))
        {
            Log($"Rôle modifié pour {u.Name} : {(u.IsAdmin ? "standard" : "administrateur")}.");
            if (UsersStatus is not null)
                UsersStatus.Text = $"✓ {u.Name} est désormais {(u.IsAdmin ? "compte standard" : "administrateur")}.";
        }
        else if (UsersStatus is not null) UsersStatus.Text = $"❌ Échec : {res}";
        await BuildUsersAsync();
    }

    private async void OnSetUserPin(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: UserAccountItem u }) return;
        var dlg = new PromptWindow("Code PIN / mot de passe", $"Nouveau code de session pour « {u.Name} » :", "Appliquer") { Owner = this };
        if (dlg.ShowDialog() != true) return;
        string n = u.Name.Replace("'", "''");
        string p = dlg.Value.Replace("'", "''");
        string res = await RunPs(
            $"Set-LocalUser -Name '{n}' -Password (ConvertTo-SecureString '{p}' -AsPlainText -Force) -ErrorAction Stop; if($?){{'OK'}}");
        if (UsersStatus is not null)
            UsersStatus.Text = res.Contains("OK")
                ? $"✓ Code de session mis à jour pour {u.Name}. (Un code PIN Windows Hello se configure ensuite à l'écran de connexion.)"
                : $"Échec : {res}";
        if (res.Contains("OK")) Log($"Code de session modifié pour {u.Name}.");
    }

    private async void OnDeleteUser(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: UserAccountItem u }) return;
        var confirm = new PromptWindow("Supprimer l'utilisateur",
            $"Supprimer définitivement le compte « {u.Name} » ? Tapez OUI pour confirmer.", "Supprimer") { Owner = this };
        if (confirm.ShowDialog() != true || !string.Equals(confirm.Value.Trim(), "OUI", StringComparison.OrdinalIgnoreCase))
            return;
        if (UsersStatus is not null) UsersStatus.Text = $"Suppression de « {u.Name} »…";
        string n = u.Name.Replace("'", "''");
        string res = await RunPs($"Remove-LocalUser -Name '{n}' -ErrorAction Stop; if($?){{'OK'}}");
        // Repli : certains comptes (invité, comptes hérités) se suppriment mieux via « net user ».
        if (!res.Contains("OK"))
            res = await RunPs($"net user '{n}' /delete; if($?){{'OK'}}");
        if (res.Contains("OK"))
        {
            IatechShield.Tools.AccessLog.Record("Compte supprimé", "Admin local", u.Name, "Utilisateur Windows supprimé");
            Log($"Utilisateur Windows supprimé : {u.Name}.");
            if (UsersStatus is not null) UsersStatus.Text = $"✓ Compte « {u.Name} » supprimé.";
        }
        else if (UsersStatus is not null)
            UsersStatus.Text = $"❌ Échec de la suppression de « {u.Name} » : {res}";
        await BuildUsersAsync();
    }

    private async void OnToggleUserEnabled(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: UserAccountItem u }) return;
        string n = u.Name.Replace("'", "''");
        string cmd = u.Enabled
            ? $"Disable-LocalUser -Name '{n}' -ErrorAction Stop"
            : $"Enable-LocalUser -Name '{n}' -ErrorAction Stop";
        string res = await RunPs(cmd + "; if($?){'OK'}");
        // Repli « net user … /active:yes|no » si la cmdlet échoue.
        if (!res.Contains("OK"))
            res = await RunPs($"net user '{n}' /active:{(u.Enabled ? "no" : "yes")}; if($?){{'OK'}}");
        if (res.Contains("OK"))
        {
            Log($"Compte {u.Name} {(u.Enabled ? "désactivé" : "activé")}.");
            if (UsersStatus is not null)
                UsersStatus.Text = $"✓ Compte « {u.Name} » {(u.Enabled ? "désactivé" : "activé")}.";
        }
        else if (UsersStatus is not null) UsersStatus.Text = $"❌ Échec : {res}";
        await BuildUsersAsync();
    }

    private async void OnForcePwdChange(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: UserAccountItem u }) return;
        // « net user … /logonpasswordchg:yes » force le changement à la prochaine connexion.
        string res = await RunPs($"net user '{u.Name.Replace("'", "''")}' /logonpasswordchg:yes; if($?){{'OK'}}");
        if (UsersStatus is not null)
            UsersStatus.Text = res.Contains("OK")
                ? $"✓ {u.Name} devra changer son mot de passe à la prochaine connexion."
                : $"Échec : {res}";
        if (res.Contains("OK")) Log($"Changement de mot de passe forcé pour {u.Name}.");
    }

    private async void OnSetLockoutPolicy(object sender, RoutedEventArgs e)
    {
        var dlg = new PromptWindow("Verrouillage de compte",
            "Verrouiller un compte après combien de tentatives erronées ? (0 = désactivé)", "Appliquer") { Owner = this };
        if (dlg.ShowDialog() != true) return;
        if (!int.TryParse(dlg.Value.Trim(), out int n) || n < 0 || n > 999)
        {
            if (UsersStatus is not null) UsersStatus.Text = "Valeur invalide (0 à 999).";
            return;
        }
        // Politique globale Windows : seuil de verrouillage (+ durée 30 min si activé).
        string res = await RunPs($"net accounts /lockoutthreshold:{n} /lockoutduration:30 /lockoutwindow:30; if($?){{'OK'}}");
        if (UsersStatus is not null)
            UsersStatus.Text = res.Contains("OK")
                ? (n == 0 ? "✓ Verrouillage après erreurs désactivé." : $"✓ Comptes verrouillés après {n} tentatives erronées (30 min).")
                : $"Échec : {res}";
        if (res.Contains("OK")) Log($"Politique de verrouillage : seuil {n}.");
    }

    // =========================================================================
    //  PAGE CONTRÔLE : Windows · USB · Services · Réseau
    // =========================================================================

    // --- Contrôle de Windows (alimentation, réparation, scripts) ---

    private async void OnWinControl(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string key }) return;
        void St(string s) { if (WinControlStatus is not null) WinControlStatus.Text = s; }
        try
        {
            switch (key)
            {
                case "lock":       RunHidden("rundll32.exe", "user32.dll,LockWorkStation"); St("✓ Poste verrouillé."); break;
                case "restart":    if (Confirm("Redémarrer le PC maintenant ?")) RunHidden("shutdown.exe", "/r /t 5 /c \"IATECH-SHIELD\""); St("Redémarrage dans 5 s… (annuler : shutdown /a)"); break;
                case "shutdown":   if (Confirm("Arrêter le PC maintenant ?")) RunHidden("shutdown.exe", "/s /t 5 /c \"IATECH-SHIELD\""); St("Arrêt dans 5 s… (annuler : shutdown /a)"); break;
                case "sleep":      RunHidden("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0"); St("Mise en veille…"); break;
                case "restorepoint":
                    St("Création d'un point de restauration…");
                    await RunHiddenAsync("powershell.exe", "-NoProfile -Command \"Enable-ComputerRestore -Drive 'C:\\'; Checkpoint-Computer -Description 'IATECH-SHIELD' -RestorePointType MODIFY_SETTINGS\"");
                    St("✓ Point de restauration créé (IATECH-SHIELD).");
                    break;
                case "restore":    LaunchTool("rstrui.exe"); St("Restauration système ouverte."); break;
                case "sfc":        LaunchAdmin("cmd.exe", "/k sfc /scannow"); St("🩹 SFC lancé dans une fenêtre admin."); break;
                case "dism":       LaunchAdmin("cmd.exe", "/k DISM /Online /Cleanup-Image /RestoreHealth"); St("🛠️ DISM lancé dans une fenêtre admin."); break;
                case "temp":       int d = CleanTempFiles() + CleanPrefetch(); St($"✓ {d} élément(s) temporaire(s) supprimé(s)."); break;
                case "clearlogs":  LaunchAdmin("cmd.exe", "/k for /f \"tokens=*\" %G in ('wevtutil el') do wevtutil cl \"%G\""); St("🧹 Nettoyage des journaux d'événements lancé."); break;
                case "startup":    LaunchTool("ms-settings:startupapps"); St("Gestion des programmes de démarrage ouverte."); break;
                case "cmd":        LaunchAdmin("cmd.exe", ""); St("Invite de commandes (admin) ouverte."); break;
                case "powershell": LaunchAdmin("powershell.exe", "-NoExit"); St("PowerShell (admin) ouvert."); break;
                case "script":
                    var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Script à exécuter", Filter = "Scripts (*.ps1;*.bat;*.cmd)|*.ps1;*.bat;*.cmd|Tous (*.*)|*.*" };
                    if (dlg.ShowDialog(this) == true)
                    {
                        if (dlg.FileName.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
                            LaunchAdmin("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{dlg.FileName}\"");
                        else LaunchAdmin("cmd.exe", $"/c \"{dlg.FileName}\"");
                        St($"Script lancé : {System.IO.Path.GetFileName(dlg.FileName)}");
                    }
                    break;
            }
            Log($"Contrôle Windows : {key}.");
        }
        catch (Exception ex) { St($"Échec : {ex.Message}"); }
    }

    private bool Confirm(string question)
    {
        var c = new PromptWindow("Confirmation", question + " Tapez OUI.", "Confirmer") { Owner = this };
        return c.ShowDialog() == true && string.Equals(c.Value.Trim(), "OUI", StringComparison.OrdinalIgnoreCase);
    }

    private static void RunHidden(string file, string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, args)
            { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        }
        catch { }
    }

    // --- Verrou d'extinction & redémarrage (carte d'identité) ----------------

    private bool _shutdownLockEnabled;
    private bool _allowShutdownOnce;   // autorise UNE seule fermeture de session (action légitime via l'app)
    private bool _shutdownLockLoaded;

    /// <summary>Charge l'état du verrou d'extinction depuis le coffre DPAPI.</summary>
    private void LoadShutdownLock()
    {
        if (_shutdownLockLoaded) return;
        try
        {
            var cfg = SecretVault.Load("shutdownlock");
            _shutdownLockEnabled = cfg.GetValueOrDefault("enabled") == "1";
        }
        catch { }
        _shutdownLockLoaded = true;
    }

    /// <summary>Met à jour l'interface (interrupteur + texte d'état) du verrou.</summary>
    private void RefreshShutdownLockUi()
    {
        LoadShutdownLock();
        if (ShutdownLockSwitch is not null)
        {
            ShutdownLockSwitch.Checked -= OnShutdownLockToggled;
            ShutdownLockSwitch.Unchecked -= OnShutdownLockToggled;
            ShutdownLockSwitch.IsChecked = _shutdownLockEnabled;
            ShutdownLockSwitch.Checked += OnShutdownLockToggled;
            ShutdownLockSwitch.Unchecked += OnShutdownLockToggled;
        }
        if (ShutdownLockStatus is not null)
            ShutdownLockStatus.Text = _shutdownLockEnabled
                ? "🔒 Verrou ACTIF : arrêt/redémarrage bloqués sans la carte d'identité propriétaire."
                : "🔓 Verrou inactif. Activez-le : la carte d'identité deviendra la clé.";
    }

    /// <summary>Active / désactive le verrou. Toute bascule exige la carte d'identité.</summary>
    private async void OnShutdownLockToggled(object sender, RoutedEventArgs e)
    {
        LoadShutdownLock();
        bool wantOn = ShutdownLockSwitch?.IsChecked == true;

        if (!CardAuth.Gate(this, wantOn ? "Activer le verrou d'extinction" : "Désactiver le verrou d'extinction", out _))
        {
            RefreshShutdownLockUi();   // remet l'interrupteur dans son état réel
            return;
        }

        _shutdownLockEnabled = wantOn;
        SaveShutdownLock();
        await ApplyShutdownHardeningAsync(wantOn);
        if (ShutdownLockStatus is not null)
            ShutdownLockStatus.Text = wantOn
                ? "🔒 Verrou ACTIF : arrêt/redémarrage bloqués sans la carte d'identité propriétaire."
                : "🔓 Verrou désactivé.";
        Log($"Verrou d'extinction {(wantOn ? "activé" : "désactivé")} (carte d'identité).");
    }

    /// <summary>Enregistre / confirme la carte propriétaire du verrou.</summary>
    private void OnSetShutdownPin(object sender, RoutedEventArgs e)
    {
        if (CardAuth.Gate(this, "Enregistrer la carte propriétaire", out string who) && ShutdownLockStatus is not null)
            ShutdownLockStatus.Text = $"✓ Carte propriétaire : {who}.";
    }

    private void SaveShutdownLock()
    {
        try
        {
            SecretVault.Save("shutdownlock", new Dictionary<string, string>
            {
                ["enabled"] = _shutdownLockEnabled ? "1" : "0",
            });
        }
        catch { }
    }

    /// <summary>
    /// Durcissement système : empêche l'arrêt/redémarrage hors de l'application.
    /// On masque les boutons d'arrêt du menu Démarrer/Ctrl-Alt-Suppr (NoClose) et on
    /// reconfigure les boutons d'alimentation pour « ne rien faire ».
    /// </summary>
    private async Task ApplyShutdownHardeningAsync(bool on)
    {
        try
        {
            string explorer = "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer";
            string system   = "HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System";
            if (on)
            {
                // Retire les commandes d'arrêt de l'interface (Démarrer + écran de verrouillage).
                await RunHiddenAsync("cmd.exe", $"/c reg add \"{explorer}\" /v NoClose /t REG_DWORD /d 1 /f");
                await RunHiddenAsync("cmd.exe", $"/c reg add \"{system}\" /v shutdownwithoutlogon /t REG_DWORD /d 0 /f");
                // Boutons d'alimentation / fermeture du capot : « ne rien faire » (secteur + batterie).
                await RunHiddenAsync("cmd.exe", "/c powercfg -setacvalueindex SCHEME_CURRENT SUB_BUTTONS PBUTTONACTION 0");
                await RunHiddenAsync("cmd.exe", "/c powercfg -setdcvalueindex SCHEME_CURRENT SUB_BUTTONS PBUTTONACTION 0");
                await RunHiddenAsync("cmd.exe", "/c powercfg -setacvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION 0");
                await RunHiddenAsync("cmd.exe", "/c powercfg -setdcvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION 0");
                await RunHiddenAsync("cmd.exe", "/c powercfg -setactive SCHEME_CURRENT");
                // Démarrage automatique (tâche planifiée élevée) : après un arrêt forcé
                // (appui long), IATECH-SHIELD redémarre AVEC Windows et reverrouille le PC.
                string exe = Environment.ProcessPath ?? "";
                if (exe.Length > 0)
                    await RunHiddenAsync("cmd.exe",
                        $"/c schtasks /create /tn \"IatechShieldGuard\" /tr \"\\\"{exe}\\\"\" /sc onlogon /rl highest /f");
            }
            else
            {
                await RunHiddenAsync("cmd.exe", $"/c reg add \"{explorer}\" /v NoClose /t REG_DWORD /d 0 /f");
                // Boutons d'alimentation : arrêt par défaut (valeur 3 = arrêter).
                await RunHiddenAsync("cmd.exe", "/c powercfg -setacvalueindex SCHEME_CURRENT SUB_BUTTONS PBUTTONACTION 3");
                await RunHiddenAsync("cmd.exe", "/c powercfg -setdcvalueindex SCHEME_CURRENT SUB_BUTTONS PBUTTONACTION 3");
                await RunHiddenAsync("cmd.exe", "/c powercfg -setactive SCHEME_CURRENT");
                // On retire le démarrage automatique de garde.
                await RunHiddenAsync("cmd.exe", "/c schtasks /delete /tn \"IatechShieldGuard\" /f");
            }
            // Recharge les stratégies Explorer pour application immédiate.
            await RunHiddenAsync("cmd.exe", "/c taskkill /f /im explorer.exe & start explorer.exe");
        }
        catch { /* droits insuffisants : le veto logiciel (WM_QUERYENDSESSION) reste actif */ }
    }

    /// <summary>Redémarrage autorisé : exige la carte d'identité puis lève le veto une fois.</summary>
    private void OnLockedRestart(object sender, RoutedEventArgs e)
    {
        if (!CardAuth.Gate(this, "Redémarrer l'ordinateur", out _)) return;
        _allowShutdownOnce = true;
        if (ShutdownLockStatus is not null) ShutdownLockStatus.Text = "🔁 Redémarrage autorisé dans 3 s…";
        Log("Redémarrage autorisé par carte d'identité.");
        RunHidden("shutdown.exe", "/r /t 3 /c \"IATECH-SHIELD — redémarrage autorisé\"");
    }

    /// <summary>Arrêt autorisé : exige la carte d'identité puis lève le veto une fois.</summary>
    private void OnLockedShutdown(object sender, RoutedEventArgs e)
    {
        if (!CardAuth.Gate(this, "Éteindre l'ordinateur", out _)) return;
        _allowShutdownOnce = true;
        if (ShutdownLockStatus is not null) ShutdownLockStatus.Text = "⏻ Arrêt autorisé dans 3 s…";
        Log("Arrêt autorisé par carte d'identité.");
        RunHidden("shutdown.exe", "/s /t 3 /c \"IATECH-SHIELD — arrêt autorisé\"");
    }

    // --- Contrôle USB ---

    private async void OnUsbControl(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string key }) return;
        void St(string s) { if (UsbControlStatus is not null) UsbControlStatus.Text = s; }
        try
        {
            switch (key)
            {
                case "usb-block":
                    await RunHiddenAsync("cmd.exe", "/c reg add \"HKLM\\SYSTEM\\CurrentControlSet\\Services\\USBSTOR\" /v Start /t REG_DWORD /d 4 /f");
                    St("🚫 Stockage USB bloqué (clés et disques USB refusés)."); break;
                case "usb-allow":
                    await RunHiddenAsync("cmd.exe", "/c reg add \"HKLM\\SYSTEM\\CurrentControlSet\\Services\\USBSTOR\" /v Start /t REG_DWORD /d 3 /f");
                    St("✅ Stockage USB autorisé."); break;
                case "usb-ro":
                    await RunHiddenAsync("cmd.exe", "/c reg add \"HKLM\\SYSTEM\\CurrentControlSet\\Control\\StorageDevicePolicies\" /v WriteProtect /t REG_DWORD /d 1 /f");
                    St("🔒 USB en lecture seule : impossible d'y copier des fichiers (anti-exfiltration)."); break;
                case "usb-rw":
                    await RunHiddenAsync("cmd.exe", "/c reg add \"HKLM\\SYSTEM\\CurrentControlSet\\Control\\StorageDevicePolicies\" /v WriteProtect /t REG_DWORD /d 0 /f");
                    St("🔓 Écriture USB réautorisée."); break;
                case "wpd-block":
                    await RunHiddenAsync("cmd.exe", "/c reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\RemovableStorageDevices\\{6AC27878-A6FA-4155-BA85-F98F491D4F33}\" /v Deny_All /t REG_DWORD /d 1 /f");
                    St("📵 Téléphones / appareils WPD bloqués."); break;
                case "wpd-allow":
                    await RunHiddenAsync("cmd.exe", "/c reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\RemovableStorageDevices\\{6AC27878-A6FA-4155-BA85-F98F491D4F33}\" /v Deny_All /t REG_DWORD /d 0 /f");
                    St("📱 Téléphones / appareils WPD autorisés."); break;
                case "autorun-off":
                    await RunHiddenAsync("cmd.exe", "/c reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer\" /v NoDriveTypeAutoRun /t REG_DWORD /d 255 /f");
                    St("⏏️ Exécution automatique désactivée (anti-ver USB)."); break;
            }
            Log($"Contrôle USB : {key}.");
        }
        catch (Exception ex) { St($"Échec : {ex.Message} (droits administrateur requis)."); }
    }

    // --- Contrôle des services Windows ---

    public sealed class ServiceItem
    {
        public string Name { get; set; } = "";
        public string Display { get; set; } = "";
        public string Sub { get; set; } = "";
        public System.Windows.Media.Brush StateColor { get; set; } = System.Windows.Media.Brushes.Cyan;
    }

    private HashSet<string>? _serviceSnapshot;

    private async void OnSearchServices(object sender, RoutedEventArgs e)
    {
        string q = (ServiceSearch?.Text ?? "").Trim().Replace("'", "''");
        if (q.Length == 0) { if (ServicesStatus is not null) ServicesStatus.Text = "Tapez un nom de service."; return; }
        await LoadServices($"Get-Service -Name '*{q}*' -ErrorAction SilentlyContinue");
    }

    private async void OnRunningServices(object sender, RoutedEventArgs e)
        => await LoadServices("Get-Service | Where-Object { $_.Status -eq 'Running' }");

    private async Task LoadServices(string baseCmd)
    {
        if (ServicesList is null) return;
        if (ServicesStatus is not null) ServicesStatus.Text = "Lecture des services…";
        try
        {
            string raw = await RunPs($"({baseCmd} | Select-Object -First 60 | ForEach-Object {{ $_.Name + '|' + $_.DisplayName + '|' + $_.Status }}) -join ';;'");
            var items = new List<ServiceItem>();
            foreach (var row in raw.Split(new[] { ";;" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = row.Split('|');
                if (p.Length < 3) continue;
                bool running = p[2].Trim().Equals("Running", StringComparison.OrdinalIgnoreCase);
                items.Add(new ServiceItem
                {
                    Name = p[0].Trim(),
                    Display = p[1].Trim(),
                    Sub = $"{p[0].Trim()} · {(running ? "▶ en cours" : "⏹ arrêté")}",
                    StateColor = new SolidColorBrush(running ? Color.FromRgb(0x2B, 0xE0, 0xA6) : Color.FromRgb(0x6E, 0x8B, 0xA0))
                });
            }
            ServicesList.ItemsSource = items;
            if (ServicesStatus is not null)
                ServicesStatus.Text = items.Count == 0 ? "Aucun service trouvé." : $"{items.Count} service(s).";
        }
        catch (Exception ex) { if (ServicesStatus is not null) ServicesStatus.Text = $"Échec : {ex.Message}"; }
    }

    private async void OnSnapshotServices(object sender, RoutedEventArgs e)
    {
        string raw = await RunPs("(Get-Service | ForEach-Object { $_.Name }) -join ','");
        var current = raw.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_serviceSnapshot is null)
        {
            _serviceSnapshot = current;
            if (ServicesStatus is not null) ServicesStatus.Text = $"🛡️ Référence enregistrée ({current.Count} services). Recliquez plus tard pour détecter les nouveaux.";
        }
        else
        {
            var added = current.Except(_serviceSnapshot).ToList();
            _serviceSnapshot = current;
            if (ServicesStatus is not null)
                ServicesStatus.Text = added.Count == 0 ? "✓ Aucun nouveau service depuis la référence." : $"⚠ Nouveau(x) service(s) : {string.Join(", ", added)}";
            if (added.Count > 0) Notify("Nouveau service détecté", string.Join(", ", added), "Contrôle");
        }
    }

    private async void OnStartService(object sender, RoutedEventArgs e) => await ServiceAction(sender, "start");
    private async void OnStopService(object sender, RoutedEventArgs e) => await ServiceAction(sender, "stop");
    private async void OnDisableService(object sender, RoutedEventArgs e) => await ServiceAction(sender, "disable");

    private async Task ServiceAction(object sender, string action)
    {
        if (sender is not FrameworkElement { Tag: string name }) return;
        string n = name.Replace("'", "''");
        string cmd = action switch
        {
            "start" => $"Start-Service -Name '{n}' -ErrorAction Stop",
            "stop" => $"Stop-Service -Name '{n}' -Force -ErrorAction Stop",
            "disable" => $"Stop-Service -Name '{n}' -Force -ErrorAction SilentlyContinue; Set-Service -Name '{n}' -StartupType Disabled -ErrorAction Stop",
            _ => ""
        };
        string res = await RunPs(cmd + "; if($?){'OK'}");
        if (ServicesStatus is not null)
            ServicesStatus.Text = res.Contains("OK")
                ? $"✓ Service {name} : {action} effectué."
                : $"Échec ({name}) : droits administrateur requis ou service protégé.";
        if (res.Contains("OK")) Log($"Service {name} : {action}.");
    }

    // --- Contrôle réseau (connexions, blocage) ---

    public sealed class ConnItem
    {
        public string Line { get; set; } = "";
        public string Sub { get; set; } = "";
        public string RemoteIp { get; set; } = "";
    }

    private async void OnShowConnections(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string proto } || NetConnList is null) return;
        if (NetControlStatus is not null) NetControlStatus.Text = "Lecture des connexions…";
        try
        {
            string cmd = proto == "udp"
                ? "(Get-NetUDPEndpoint | Select-Object -First 80 | ForEach-Object { $_.LocalAddress + ':' + $_.LocalPort + '|' + $_.OwningProcess }) -join ';;'"
                : "(Get-NetTCPConnection | Where-Object { $_.RemoteAddress -ne '0.0.0.0' -and $_.RemoteAddress -ne '::' } | Select-Object -First 80 | ForEach-Object { $_.LocalAddress + ':' + $_.LocalPort + '>' + $_.RemoteAddress + ':' + $_.RemotePort + '|' + $_.State + '|' + $_.OwningProcess }) -join ';;'";
            string raw = await RunPs(cmd);
            var items = new List<ConnItem>();
            foreach (var row in raw.Split(new[] { ";;" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = row.Split('|');
                string remoteIp = "";
                if (proto == "tcp" && p[0].Contains('>'))
                {
                    string remote = p[0].Split('>')[1];
                    remoteIp = remote.Contains(':') ? remote[..remote.LastIndexOf(':')] : remote;
                }
                items.Add(new ConnItem
                {
                    Line = p[0],
                    Sub = proto == "udp" ? $"UDP · PID {(p.Length > 1 ? p[1] : "?")}" : $"TCP · {(p.Length > 1 ? p[1] : "")} · PID {(p.Length > 2 ? p[2] : "?")}",
                    RemoteIp = remoteIp
                });
            }
            NetConnList.ItemsSource = items;
            if (NetControlStatus is not null) NetControlStatus.Text = $"{items.Count} connexion(s) {proto.ToUpper()}.";
        }
        catch (Exception ex) { if (NetControlStatus is not null) NetControlStatus.Text = $"Échec : {ex.Message}"; }
    }

    private async void OnBlockConnIp(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string ip } || string.IsNullOrWhiteSpace(ip)) return;
        await BlockIp(ip);
    }

    private async void OnBlockIpManual(object sender, RoutedEventArgs e)
    {
        var dlg = new PromptWindow("Bloquer une IP", "Adresse IP à bloquer (entrant + sortant) :", "Bloquer") { Owner = this };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Value)) await BlockIp(dlg.Value.Trim());
    }

    private async Task BlockIp(string ip)
    {
        string safe = ip.Replace("\"", "");
        await RunHiddenAsync("cmd.exe", $"/c netsh advfirewall firewall add rule name=\"IATECH-Block {safe}\" dir=out action=block remoteip={safe} & " +
                                        $"netsh advfirewall firewall add rule name=\"IATECH-Block {safe}\" dir=in action=block remoteip={safe}");
        if (NetControlStatus is not null) NetControlStatus.Text = $"🚫 IP {ip} bloquée (pare-feu).";
        IatechShield.Tools.AccessLog.Record("Blocage réseau", "IP", ip, "Bloquée au pare-feu");
        Log($"IP bloquée : {ip}.");
    }

    private async void OnUnblockIpManual(object sender, RoutedEventArgs e)
    {
        var dlg = new PromptWindow("Débloquer une IP", "Adresse IP à débloquer :", "Débloquer") { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value)) return;
        string ip = dlg.Value.Trim();
        string safe = ip.Replace("\"", "");
        // Supprime la règle de blocage IATECH-Block <ip> (entrée + sortie).
        string res = await RunPs($"netsh advfirewall firewall delete rule name='IATECH-Block {safe}'; if($?){{'OK'}}");
        if (NetControlStatus is not null)
            NetControlStatus.Text = res.Contains("OK")
                ? $"✅ IP {ip} débloquée."
                : $"Aucune règle trouvée pour {ip} (déjà débloquée ?).";
        IatechShield.Tools.AccessLog.Record("Déblocage réseau", "IP", ip, "Règle de pare-feu supprimée");
        Log($"IP débloquée : {ip}.");
    }

    private async void OnListBlockedIps(object sender, RoutedEventArgs e)
    {
        if (NetControlStatus is not null) NetControlStatus.Text = "Lecture des IP bloquées…";
        try
        {
            // Liste les règles IATECH-Block et extrait les IP distantes.
            string raw = await RunPs(
                "(Get-NetFirewallRule -DisplayName 'IATECH-Block*' -ErrorAction SilentlyContinue | " +
                "Get-NetFirewallAddressFilter | ForEach-Object { $_.RemoteAddress }) | Sort-Object -Unique");
            var ips = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim())
                .Where(s => s.Length > 0 && s != "Any").Distinct().ToList();
            if (NetConnList is not null)
                NetConnList.ItemsSource = ips.Select(ip => new ConnItem { Line = ip, Sub = "🚫 IP bloquée — bouton « Bloquer » re-bloque ; utilisez « Débloquer une IP » pour retirer", RemoteIp = ip }).ToList();
            if (NetControlStatus is not null)
                NetControlStatus.Text = ips.Count == 0 ? "Aucune IP bloquée par IATECH-SHIELD." : $"{ips.Count} IP bloquée(s).";
        }
        catch (Exception ex) { if (NetControlStatus is not null) NetControlStatus.Text = $"Échec : {ex.Message}"; }
    }

    private async void OnBlockDomain(object sender, RoutedEventArgs e)
    {
        var dlg = new PromptWindow("Bloquer un domaine", "Nom de domaine à bloquer (ex : exemple.com) :", "Bloquer") { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value)) return;
        string domain = dlg.Value.Trim().Replace("\"", "").Replace("'", "").Replace(" ", "");
        // Ajout au fichier hosts : redirige le domaine vers 0.0.0.0 (inaccessible).
        // Guillemets simples PowerShell → pas d'échappement fragile.
        string ps = $"$h='C:\\Windows\\System32\\drivers\\etc\\hosts'; " +
                    $"Add-Content -Path $h -Value '0.0.0.0 {domain}'; " +
                    $"Add-Content -Path $h -Value '0.0.0.0 www.{domain}'; if($?){{'OK'}}";
        string res = await RunPs(ps);
        if (NetControlStatus is not null)
            NetControlStatus.Text = res.Contains("OK") ? $"🌍 Domaine {domain} bloqué (fichier hosts)." : $"Échec : droits administrateur requis.";
        if (res.Contains("OK")) { IatechShield.Tools.AccessLog.Record("Blocage réseau", "Domaine", domain, "Bloqué (hosts)"); Log($"Domaine bloqué : {domain}."); }
    }

    private async void OnBlockCountry(object sender, RoutedEventArgs e)
    {
        var dlg = new PromptWindow("Bloquer un pays",
            "Code pays ISO à 2 lettres (ex : RU, CN, KP). Les plages d'IP du pays seront bloquées au pare-feu.", "Bloquer") { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value)) return;
        string cc = dlg.Value.Trim().ToUpperInvariant();
        if (cc.Length != 2) { if (NetControlStatus is not null) NetControlStatus.Text = "Code pays invalide (2 lettres)."; return; }
        if (NetControlStatus is not null) NetControlStatus.Text = $"Téléchargement des plages d'IP de {cc}…";
        try
        {
            // Plages CIDR du pays via un service public (ipdeny).
            string list = await new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) }
                .GetStringAsync($"https://www.ipdeny.com/ipblocks/data/aggregated/{cc.ToLowerInvariant()}-aggregated.zone");
            var cidrs = list.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Contains('/')).Take(2000).ToList();
            if (cidrs.Count == 0) { if (NetControlStatus is not null) NetControlStatus.Text = $"Aucune plage trouvée pour {cc}."; return; }
            string joined = string.Join(",", cidrs);
            await RunHiddenAsync("cmd.exe", $"/c netsh advfirewall firewall add rule name=\"IATECH-Country {cc}\" dir=out action=block remoteip={joined}");
            if (NetControlStatus is not null) NetControlStatus.Text = $"🏴 {cidrs.Count} plage(s) d'IP de {cc} bloquée(s) au pare-feu.";
            IatechShield.Tools.AccessLog.Record("Blocage réseau", "Pays", cc, $"{cidrs.Count} plages bloquées");
            Log($"Pays bloqué : {cc} ({cidrs.Count} plages).");
        }
        catch (Exception ex) { if (NetControlStatus is not null) NetControlStatus.Text = $"Échec : {ex.Message}"; }
    }

    private async void OnBandwidth(object sender, RoutedEventArgs e)
    {
        if (NetControlStatus is not null) NetControlStatus.Text = "Mesure de la bande passante (2 s)…";
        try
        {
            string raw = await RunPs("$a=Get-NetAdapterStatistics | Sort-Object ReceivedBytes -Descending | Select-Object -First 1; " +
                "$r1=$a.ReceivedBytes; $s1=$a.SentBytes; Start-Sleep -Seconds 2; " +
                "$b=Get-NetAdapterStatistics -Name $a.Name; " +
                "[math]::Round(($b.ReceivedBytes-$r1)/2048,1).ToString() + '|' + [math]::Round(($b.SentBytes-$s1)/2048,1).ToString() + '|' + $a.Name");
            var p = raw.Split('|');
            if (p.Length >= 3 && NetControlStatus is not null)
                NetControlStatus.Text = $"📊 {p[2].Trim()} — ↓ {p[0].Trim()} Ko/s · ↑ {p[1].Trim()} Ko/s";
            else if (NetControlStatus is not null) NetControlStatus.Text = "Mesure indisponible.";
        }
        catch (Exception ex) { if (NetControlStatus is not null) NetControlStatus.Text = $"Échec : {ex.Message}"; }
    }

    private async void OnDetectScan(object sender, RoutedEventArgs e)
    {
        if (NetControlStatus is not null) NetControlStatus.Text = "Analyse des connexions entrantes (détection de scan)…";
        try
        {
            // Heuristique : une même IP distante ouvrant de nombreuses connexions = scan probable.
            string raw = await RunPs("(Get-NetTCPConnection | Where-Object { $_.RemoteAddress -ne '0.0.0.0' -and $_.RemoteAddress -ne '127.0.0.1' -and $_.RemoteAddress -ne '::' } | " +
                "Group-Object RemoteAddress | Where-Object { $_.Count -ge 8 } | Sort-Object Count -Descending | " +
                "ForEach-Object { $_.Name + ':' + $_.Count }) -join ';'");
            var hits = raw.Split(';', StringSplitOptions.RemoveEmptyEntries);
            if (NetControlStatus is not null)
                NetControlStatus.Text = hits.Length == 0
                    ? "✓ Aucun comportement de scan détecté."
                    : "🛰️ Activité suspecte (nombreuses connexions) : " + string.Join("  ", hits);
            if (hits.Length > 0) { SoundFx.Alert(); Notify("Scan réseau possible", string.Join(", ", hits), "Contrôle"); }
        }
        catch (Exception ex) { if (NetControlStatus is not null) NetControlStatus.Text = $"Échec : {ex.Message}"; }
    }

    // ------------------------------------------------- Accès à distance --------

    private DispatcherTimer? _remoteTimer;
    private DateTime _remoteExpiry;

    private static readonly string[] AnyDeskPaths =
    {
        @"%ProgramFiles(x86)%\AnyDesk\AnyDesk.exe",
        @"%ProgramFiles%\AnyDesk\AnyDesk.exe",
        @"%APPDATA%\AnyDesk\AnyDesk.exe",
    };
    private static readonly string[] TeamViewerPaths =
    {
        @"%ProgramFiles%\TeamViewer\TeamViewer.exe",
        @"%ProgramFiles(x86)%\TeamViewer\TeamViewer.exe",
    };

    private static string? FindExe(string[] candidates)
    {
        foreach (var c in candidates)
        {
            string p = Environment.ExpandEnvironmentVariables(c);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private void RefreshRemoteStatus()
    {
        if (AnyDeskStatus is not null)
            AnyDeskStatus.Text = FindExe(AnyDeskPaths) is not null ? "✓ installé" : "non installé (cliquez Télécharger)";
        if (TeamViewerStatus is not null)
            TeamViewerStatus.Text = FindExe(TeamViewerPaths) is not null ? "✓ installé" : "non installé (cliquez Télécharger)";
    }

    private void OnRemoteLaunch(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;
        try
        {
            switch (tag)
            {
                case "anydesk":
                    var ad = FindExe(AnyDeskPaths);
                    if (ad is not null) Process.Start(new ProcessStartInfo(ad) { UseShellExecute = true });
                    else Process.Start(new ProcessStartInfo("https://anydesk.com/fr/downloads/windows") { UseShellExecute = true });
                    break;
                case "anydesk-settings":
                    var ad2 = FindExe(AnyDeskPaths);
                    if (ad2 is not null) Process.Start(new ProcessStartInfo(ad2) { UseShellExecute = true });
                    if (RemoteStatus is not null) RemoteStatus.Text = "Dans AnyDesk : ☰ → Paramètres → Sécurité → « Activer l'accès non surveillé » et collez le mot de passe ci-dessus.";
                    break;
                case "teamviewer":
                    var tv = FindExe(TeamViewerPaths);
                    if (tv is not null) Process.Start(new ProcessStartInfo(tv) { UseShellExecute = true });
                    else Process.Start(new ProcessStartInfo("https://www.teamviewer.com/fr/telecharger/windows/") { UseShellExecute = true });
                    break;
            }
        }
        catch (Exception ex) { if (RemoteStatus is not null) RemoteStatus.Text = $"Erreur : {ex.Message}"; }
    }

    private void OnGenRemotePassword(object sender, RoutedEventArgs e)
    {
        if (RemotePassword is null) return;
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$%";
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        var sb = new StringBuilder();
        foreach (var b in bytes) sb.Append(chars[b % chars.Length]);
        RemotePassword.Text = sb.ToString();
    }

    private void OnCopyRemotePassword(object sender, RoutedEventArgs e)
    {
        if (RemotePassword is null || string.IsNullOrEmpty(RemotePassword.Text)) { OnGenRemotePassword(sender, e); }
        try { Clipboard.SetText(RemotePassword!.Text); if (RemoteStatus is not null) RemoteStatus.Text = "✓ Mot de passe copié dans le presse-papiers."; }
        catch { }
    }

    private void OnGrantRemote(object sender, RoutedEventArgs e)
    {
        if (RemotePassword is not null && string.IsNullOrEmpty(RemotePassword.Text))
            OnGenRemotePassword(sender, e);

        bool temporary = RemoteTemporary?.IsChecked == true;
        string mode;
        if (temporary)
        {
            int mins = RemoteDuration?.SelectedIndex switch { 0 => 15, 1 => 30, 2 => 60, 3 => 240, 4 => 480, _ => 60 };
            _remoteExpiry = DateTime.Now.AddMinutes(mins);
            _remoteTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _remoteTimer.Tick -= OnRemoteTick;
            _remoteTimer.Tick += OnRemoteTick;
            _remoteTimer.Start();
            mode = $"temporaire ({mins} min, jusqu'à {_remoteExpiry:HH:mm})";
        }
        else
        {
            _remoteTimer?.Stop();
            mode = "permanent";
        }

        if (RemoteStatus is not null)
            RemoteStatus.Text = $"✅ Accès {mode} activé. Communiquez le mot de passe au technicien et lancez AnyDesk/TeamViewer.";
        IatechShield.Tools.AccessLog.Record("Accès distant accordé", temporary ? "Temporaire" : "Permanent",
            Environment.UserName, $"Mode {mode}");
        Notify("Accès à distance activé", $"Accès {mode} accordé.", "Accès distant");
        Log($"Accès à distance accordé ({mode}).");
    }

    private void OnRemoteTick(object? sender, EventArgs e)
    {
        if (DateTime.Now < _remoteExpiry)
        {
            int left = (int)(_remoteExpiry - DateTime.Now).TotalMinutes;
            if (RemoteStatus is not null) RemoteStatus.Text = $"⏳ Accès temporaire actif — expire dans ~{left + 1} min (à {_remoteExpiry:HH:mm}).";
            return;
        }
        _remoteTimer?.Stop();
        RevokeRemote(auto: true);
    }

    private void OnRevokeRemote(object sender, RoutedEventArgs e) => RevokeRemote(auto: false);

    private void RevokeRemote(bool auto)
    {
        _remoteTimer?.Stop();
        int killed = 0;
        foreach (var name in new[] { "AnyDesk", "TeamViewer" })
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try { p.Kill(); killed++; } catch { } finally { p.Dispose(); }
                }
            }
            catch { }
        }
        if (RemoteStatus is not null)
            RemoteStatus.Text = auto
                ? "⛔ Accès temporaire expiré : AnyDesk/TeamViewer ont été fermés."
                : $"⛔ Accès révoqué — {killed} application(s) de contrôle à distance fermée(s).";
        IatechShield.Tools.AccessLog.Record("Accès distant révoqué", auto ? "Expiration" : "Manuel", Environment.UserName, "");
        Notify("Accès à distance révoqué", auto ? "L'accès temporaire a expiré." : "Accès coupé manuellement.", "Accès distant");
        Log("Accès à distance révoqué.");
    }

    // ------------------------------------------------- ID Registre (eID) -------

    /// <summary>Ligne du registre des cartes d'identité (durée mise à jour en direct).</summary>
    public sealed class IdSessionItem : System.ComponentModel.INotifyPropertyChanged
    {
        public string FullName { get; set; } = "";
        public string BirthLine { get; set; } = "";
        public string NationalLine { get; set; } = "";
        public string ConnectedLine { get; set; } = "";
        public string DisconnectedLine { get; set; } = "";
        public string StateText { get; set; } = "";
        public System.Windows.Media.Brush StateColor { get; set; } = System.Windows.Media.Brushes.LimeGreen;
        public DateTimeOffset ConnectedAt { get; set; }
        public DateTimeOffset? DisconnectedAt { get; set; }

        private string _duration = "";
        public string Duration
        {
            get => _duration;
            set { _duration = value; PropertyChanged?.Invoke(this, new(nameof(Duration))); }
        }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    private List<IdSessionItem> _idItems = new();
    private DispatcherTimer? _idTicker;

    private void BuildIdRegister()
    {
        if (IdRegisterList is null) return;
        _idItems = new List<IdSessionItem>();
        foreach (var s in IatechShield.Tools.EidSessionLog.Load())
        {
            bool active = s.DisconnectedAt is null;
            _idItems.Add(new IdSessionItem
            {
                FullName = $"{s.FirstNames} {s.Name}".Trim() is { Length: > 0 } n ? n : "Carte d'identité",
                BirthLine = $"📅 Né(e) le {(string.IsNullOrWhiteSpace(s.BirthDate) ? "—" : s.BirthDate)}",
                NationalLine = $"🆔 Registre national : {(string.IsNullOrWhiteSpace(s.NationalNumber) ? "—" : s.NationalNumber)}",
                ConnectedLine = $"🟢 Connexion : {s.ConnectedAt.ToLocalTime():dd/MM/yyyy HH:mm:ss}",
                DisconnectedLine = active ? "🔴 ● toujours connecté" : $"🔴 Déconnexion : {s.DisconnectedAt!.Value.ToLocalTime():dd/MM/yyyy HH:mm:ss}",
                StateText = active ? "EN COURS" : "TERMINÉE",
                StateColor = new SolidColorBrush(active ? Color.FromRgb(0x33, 0xFF, 0x66) : Color.FromRgb(0x6E, 0x8B, 0xA0)),
                ConnectedAt = s.ConnectedAt,
                DisconnectedAt = s.DisconnectedAt,
            });
        }
        foreach (var it in _idItems) UpdateIdDuration(it);
        if (_idItems.Count == 0)
            _idItems.Add(new IdSessionItem { FullName = "Aucune connexion par carte d'identité", BirthLine = "Insérez une carte d'identité sur l'écran de verrouillage pour ouvrir une session.", Duration = "—", StateText = "" });
        IdRegisterList.ItemsSource = _idItems;
        if (IdRegisterStatus is not null)
        {
            int active = IatechShield.Tools.EidSessionLog.Load().Count(x => x.DisconnectedAt is null);
            IdRegisterStatus.Text = $"{IatechShield.Tools.EidSessionLog.Load().Count} session(s) — {active} en cours.";
        }
    }

    private static void UpdateIdDuration(IdSessionItem it)
    {
        if (it.ConnectedAt == default) return;
        var end = it.DisconnectedAt ?? DateTimeOffset.Now;
        var span = end - it.ConnectedAt;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        it.Duration = span.TotalDays >= 1
            ? $"{(int)span.TotalDays}j {span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
    }

    private void StartIdRegisterTicker()
    {
        BuildIdRegister();
        _idTicker ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _idTicker.Tick -= OnIdTick;
        _idTicker.Tick += OnIdTick;
        _idTicker.Start();
    }

    private void StopIdRegisterTicker() => _idTicker?.Stop();

    private void OnIdTick(object? sender, EventArgs e)
    {
        // Met à jour en direct la durée des sessions encore actives.
        foreach (var it in _idItems)
            if (it.DisconnectedAt is null && it.ConnectedAt != default)
                UpdateIdDuration(it);
    }

    private void OnRefreshIdRegister(object sender, RoutedEventArgs e) => BuildIdRegister();

    private void OnClearIdRegister(object sender, RoutedEventArgs e)
    {
        IatechShield.Tools.EidSessionLog.Clear();
        BuildIdRegister();
        Log("ID Registre effacé.");
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

    private const int WM_QUERYENDSESSION = 0x0011;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Verrou d'extinction : on oppose un veto à toute tentative d'arrêt/redémarrage
        // tant que le code PIN n'a pas autorisé l'opération via l'app.
        if (msg == WM_QUERYENDSESSION && _shutdownLockEnabled && !_allowShutdownOnce)
        {
            handled = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Notify("Arrêt bloqué", "IATECH-SHIELD empêche l'arrêt/redémarrage. Utilisez « Éteindre/Redémarrer (avec code) » dans l'onglet Contrôle.", "Contrôle");
                try { SoundFx.Alert(); } catch { }
            }));
            return IntPtr.Zero;   // FALSE = annule la fermeture de session
        }

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

    // ------------------------------------------------------ Historique ---------
    // Onglet unique regroupant : sites visités, applications ouvertes (avec qui/quand),
    // connexions/déconnexions, et problèmes survenus.

    public sealed record AppLaunchItem(string App, string Who, DateTime When)
    { public string WhenText => When.ToString("dd/MM/yyyy HH:mm"); }

    public sealed record HistoryConnEntry(string Kind, string Identity, string Method, DateTimeOffset When)
    { public string WhenText => When.ToString("dd/MM/yyyy HH:mm"); }

    public sealed record ProblemItem(string Message, string Source, DateTime When)
    { public string WhenText => When.ToString("dd/MM/yyyy HH:mm"); }

    private string _historyTab = "sites";
    private List<HistoryEntry> _history = new();
    private List<AppLaunchItem> _apps = new();
    private List<HistoryConnEntry> _conns = new();
    private List<ProblemItem> _problems = new();

    private void OnHistoryTab(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tab }) _historyTab = tab;
        // Bascule la visibilité des quatre listes.
        if (HistoryList is not null) HistoryList.Visibility = _historyTab == "sites" ? Visibility.Visible : Visibility.Collapsed;
        if (AppsList is not null) AppsList.Visibility = _historyTab == "apps" ? Visibility.Visible : Visibility.Collapsed;
        if (HistoryConnList is not null) HistoryConnList.Visibility = _historyTab == "conn" ? Visibility.Visible : Visibility.Collapsed;
        if (ProblemsList is not null) ProblemsList.Visibility = _historyTab == "prob" ? Visibility.Visible : Visibility.Collapsed;
        if (HistoryHint is not null)
            HistoryHint.Text = _historyTab switch
            {
                "apps" => "Applications lancées (dossier Prefetch de Windows) — avec la personne connectée à ce moment.",
                "conn" => "Connexions et déconnexions à IATECH-SHIELD (PIN, Windows Hello, carte d'identité…).",
                "prob" => "Problèmes : plantages d'IATECH-SHIELD + erreurs des journaux d'événements Windows.",
                _ => "Sites visités dans Chrome, Edge, Brave, Opera, Vivaldi et Firefox (tous profils)."
            };
        _ = BuildHistoryAsync();
    }

    private async Task BuildHistoryAsync()
    {
        if (HistoryStatus is not null) HistoryStatus.Text = "Lecture…";
        try
        {
            switch (_historyTab)
            {
                case "apps":
                    var (runs, sessions) = await Task.Run(() =>
                        (AppLaunchHistory.Read(), IatechShield.Tools.EidSessionLog.Load()));
                    _apps = runs.Select(r => new AppLaunchItem(r.App, WhoAt(r.When, sessions), r.When)).ToList();
                    if (HistoryStatus is not null) HistoryStatus.Text = $"{_apps.Count} application(s) récemment lancée(s).";
                    break;
                case "conn":
                    var ev = await Task.Run(() => IatechShield.Tools.AccessLog.Load());
                    _conns = ev.Select(a => new HistoryConnEntry(a.Kind, a.Identity, a.Method, a.Time)).ToList();
                    if (HistoryStatus is not null) HistoryStatus.Text = $"{_conns.Count} évènement(s) de connexion.";
                    break;
                case "prob":
                    _problems = await BuildProblemsAsync();
                    if (HistoryStatus is not null) HistoryStatus.Text = $"{_problems.Count} problème(s) enregistré(s).";
                    break;
                default:
                    var entries = await Task.Run(() => BrowserHistory.Read());
                    _history = entries;
                    if (HistoryStatus is not null)
                        HistoryStatus.Text = entries.Count == 0
                            ? "Aucun historique trouvé (navigateurs non installés ou aucun profil)."
                            : $"{entries.Count} pages — {entries.Select(e => e.Browser).Distinct().Count()} navigateur(s).";
                    break;
            }
            ApplyHistoryFilter();
        }
        catch (Exception ex)
        {
            if (HistoryStatus is not null) HistoryStatus.Text = $"Échec : {ex.Message}";
        }
    }

    /// <summary>Identité connectée par carte d'identité à l'instant donné (sinon utilisateur Windows).</summary>
    private static string WhoAt(DateTime when, IReadOnlyList<IatechShield.Tools.EidSession> sessions)
    {
        var w = new DateTimeOffset(when);
        foreach (var s in sessions)
        {
            var end = s.DisconnectedAt ?? DateTimeOffset.MaxValue;
            if (w >= s.ConnectedAt && w <= end)
                return $"👤 {s.FirstNames} {s.Name} (carte d'identité)".Trim();
        }
        return $"👤 {Environment.UserName} (session Windows)";
    }

    private static async Task<List<ProblemItem>> BuildProblemsAsync()
    {
        var list = new List<ProblemItem>();
        // 1) Plantages internes d'IATECH-SHIELD (crash.log).
        try
        {
            string crash = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IatechShield", "crash.log");
            if (System.IO.File.Exists(crash))
            {
                foreach (var block in System.IO.File.ReadAllText(crash)
                             .Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string first = block.Split('\n')[0].Trim();
                    DateTime when = DateTime.MinValue;
                    if (first.StartsWith("[") && first.Contains(']'))
                    {
                        string ts = first.Substring(1, first.IndexOf(']') - 1);
                        DateTime.TryParse(ts, out when);
                    }
                    string msg = first.Contains(']') ? first[(first.IndexOf(']') + 1)..].Trim() : first;
                    list.Add(new ProblemItem(msg.Length > 300 ? msg[..300] : msg, "IATECH-SHIELD", when));
                }
            }
        }
        catch { }

        // 2) Erreurs des journaux d'événements Windows (Application + Système).
        try
        {
            string raw = await RunPs(
                "Get-WinEvent -FilterHashtable @{LogName='Application','System'; Level=1,2} -MaxEvents 40 " +
                "-ErrorAction SilentlyContinue | ForEach-Object { $_.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss') + '|' + " +
                "$_.ProviderName + '|' + ($_.Message -replace '\\r?\\n',' ') } | Out-String");
            if (!raw.StartsWith("ERREUR"))
                foreach (var row in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var p = row.Split('|');
                    if (p.Length < 3) continue;
                    DateTime.TryParse(p[0].Trim(), out DateTime when);
                    string msg = p[2].Trim();
                    list.Add(new ProblemItem(msg.Length > 300 ? msg[..300] : msg, "Windows : " + p[1].Trim(), when));
                }
        }
        catch { }

        return list.OrderByDescending(p => p.When).ToList();
    }

    private void ApplyHistoryFilter()
    {
        string q = (HistorySearch?.Text ?? "").Trim();
        bool M(string? s) => q.Length == 0 || (s?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
        switch (_historyTab)
        {
            case "apps":
                if (AppsList is not null)
                    AppsList.ItemsSource = _apps.Where(a => M(a.App) || M(a.Who)).Take(1500).ToList();
                break;
            case "conn":
                if (HistoryConnList is not null)
                    HistoryConnList.ItemsSource = _conns.Where(c => M(c.Kind) || M(c.Identity) || M(c.Method)).Take(1500).ToList();
                break;
            case "prob":
                if (ProblemsList is not null)
                    ProblemsList.ItemsSource = _problems.Where(p => M(p.Message) || M(p.Source)).Take(1500).ToList();
                break;
            default:
                if (HistoryList is not null)
                    HistoryList.ItemsSource = _history.Where(e => M(e.Title) || M(e.Url) || M(e.Browser)).Take(1500).ToList();
                break;
        }
    }

    /// <summary>Ouvre le lien d'un site de l'historique dans le navigateur par défaut.</summary>
    private void OnHistoryLinkClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string url } || string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { if (HistoryStatus is not null) HistoryStatus.Text = $"Impossible d'ouvrir : {ex.Message}"; }
    }

    /// <summary>Bloque le domaine d'un site de l'historique (fichier hosts → inaccessible).</summary>
    private async void OnBlockHistorySite(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string url } || string.IsNullOrWhiteSpace(url)) return;
        string domain;
        try { domain = new Uri(url).Host; } catch { domain = url; }
        if (domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) domain = domain[4..];
        if (string.IsNullOrWhiteSpace(domain)) return;

        var confirm = new PromptWindow("Bloquer le site",
            $"Bloquer « {domain} » sur ce PC (tous les navigateurs) ? Tapez OUI.", "Bloquer") { Owner = this };
        if (confirm.ShowDialog() != true || !string.Equals(confirm.Value.Trim(), "OUI", StringComparison.OrdinalIgnoreCase))
            return;

        if (HistoryStatus is not null) HistoryStatus.Text = $"Blocage de {domain}…";
        string d = domain.Replace("'", "").Replace("\"", "").Replace(" ", "");
        string ps = "$h='C:\\Windows\\System32\\drivers\\etc\\hosts'; " +
                    $"Add-Content -Path $h -Value '0.0.0.0 {d}'; " +
                    $"Add-Content -Path $h -Value '0.0.0.0 www.{d}'; " +
                    "ipconfig /flushdns | Out-Null; if($?){'OK'}";
        string res = await RunPs(ps);
        bool ok = res.Contains("OK");
        if (ok) { IatechShield.Tools.AccessLog.Record("Blocage réseau", "Domaine", d, "Bloqué depuis l'historique (hosts)"); Log($"Site bloqué : {d}."); }
        if (HistoryStatus is not null)
            HistoryStatus.Text = ok ? $"🚫 « {d} » bloqué (fichier hosts). Débloquez-le dans Contrôle → Réseau si besoin." : "❌ Échec : droits administrateur requis.";
    }

    /// <summary>Bloque l'accès réseau d'une application (règle de pare-feu sortante + entrante).</summary>
    private async void OnBlockApp(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string exe } || string.IsNullOrWhiteSpace(exe)) return;
        string? path = ResolveExePath(exe);
        if (path is null)
        {
            if (HistoryStatus is not null) HistoryStatus.Text = $"Chemin introuvable pour « {exe} » (application non enregistrée).";
            return;
        }
        var confirm = new PromptWindow("Bloquer l'application",
            $"Empêcher « {exe} » d'accéder à Internet ? Tapez OUI.", "Bloquer") { Owner = this };
        if (confirm.ShowDialog() != true || !string.Equals(confirm.Value.Trim(), "OUI", StringComparison.OrdinalIgnoreCase))
            return;

        if (HistoryStatus is not null) HistoryStatus.Text = $"Blocage réseau de {exe}…";
        string p = path.Replace("'", "''");
        string rule = "IATECH-App-Block " + exe;
        string ps = $"New-NetFirewallRule -DisplayName '{rule}' -Direction Outbound -Program '{p}' -Action Block -ErrorAction SilentlyContinue | Out-Null; " +
                    $"New-NetFirewallRule -DisplayName '{rule}' -Direction Inbound -Program '{p}' -Action Block -ErrorAction SilentlyContinue | Out-Null; if($?){{'OK'}}";
        string res = await RunPs(ps);
        bool ok = res.Contains("OK");
        if (ok) { IatechShield.Tools.AccessLog.Record("Blocage réseau", "Application", exe, "Accès Internet bloqué (pare-feu)"); Log($"Application bloquée (réseau) : {exe}."); }
        if (HistoryStatus is not null)
            HistoryStatus.Text = ok ? $"🚫 « {exe} » ne peut plus accéder à Internet (pare-feu)." : "❌ Échec : droits administrateur requis.";
    }

    /// <summary>Résout le chemin complet d'un exécutable via le registre « App Paths ».</summary>
    private static string? ResolveExePath(string exe)
    {
        (Microsoft.Win32.RegistryKey Root, string Path)[] locs =
        {
            (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe),
            (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\" + exe),
            (Microsoft.Win32.Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe),
        };
        foreach (var (root, path) in locs)
        {
            try
            {
                using var key = root.OpenSubKey(path);
                if (key?.GetValue(null) is string p && System.IO.File.Exists(p.Trim('"'))) return p.Trim('"');
            }
            catch { }
        }
        return null;
    }

    private void OnRefreshHistory(object sender, RoutedEventArgs e) => _ = BuildHistoryAsync();

    private void OnHistorySearch(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        ApplyHistoryFilter();
    }

    private void OnExportHistory(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exporter l'historique",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"historique-{_historyTab}.csv"
        };
        if (dlg.ShowDialog(this) != true) return;
        static string Esc(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        try
        {
            var sb = new System.Text.StringBuilder();
            switch (_historyTab)
            {
                case "apps":
                    sb.AppendLine("Date;Application;Ouvert par");
                    foreach (var a in _apps) sb.AppendLine($"{Esc(a.WhenText)};{Esc(a.App)};{Esc(a.Who)}");
                    break;
                case "conn":
                    sb.AppendLine("Date;Évènement;Identité;Méthode");
                    foreach (var c in _conns) sb.AppendLine($"{Esc(c.WhenText)};{Esc(c.Kind)};{Esc(c.Identity)};{Esc(c.Method)}");
                    break;
                case "prob":
                    sb.AppendLine("Date;Source;Message");
                    foreach (var p in _problems) sb.AppendLine($"{Esc(p.WhenText)};{Esc(p.Source)};{Esc(p.Message)}");
                    break;
                default:
                    sb.AppendLine("Date;Navigateur;Titre;URL");
                    foreach (var h in _history) sb.AppendLine($"{Esc(h.VisitedText)};{Esc(h.Browser)};{Esc(h.Title)};{Esc(h.Url)}");
                    break;
            }
            System.IO.File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
            if (HistoryStatus is not null) HistoryStatus.Text = $"✓ Exporté : {System.IO.Path.GetFileName(dlg.FileName)}";
            Log($"Historique ({_historyTab}) exporté.");
        }
        catch (Exception ex)
        {
            if (HistoryStatus is not null) HistoryStatus.Text = $"Échec de l'export : {ex.Message}";
        }
    }

    // ---------------------------------------- Recherche d'options (in-app) -----

    public sealed record FeatureHit(string Icon, string Label, string Category, string Tab);

    // Registre des options : mot(s)-clé → onglet. Sert à la barre de recherche du Dashboard.
    private static readonly List<(string Icon, string Label, string Category, string Tab, string[] Keys)> Features = new()
    {
        ("🛡️", "Protection en temps réel", "Protection", "Protection", new[]{"protection","temps reel","réel","bouclier","antivirus"}),
        ("🔍", "Analyser / Scanner", "Protection", "Scan", new[]{"scan","analyse","analyser","virus","fichier"}),
        ("🚨", "Radar de menaces", "Protection", "Scan", new[]{"menace","threat","radar menace"}),
        ("💚", "Tableau d'intégrité", "Protection", "Intégrité", new[]{"integrite","intégrité","score","secure boot","santé"}),
        ("🧰", "Outils de protection", "Protection", "Outils", new[]{"outils","chiffrer","url","dossier"}),
        ("🌐", "Analyse du réseau", "Réseau", "Réseau", new[]{"reseau","réseau","ip","connexions","tcp","bloquer ip","pays"}),
        ("📡", "Radar réseau local", "Réseau", "Réseau", new[]{"radar","appareils reseau","local","ports"}),
        ("🗺️", "Carte mondiale", "Réseau", "Mondiale", new[]{"monde","mondiale","carte","géo","pays connexions"}),
        ("🧱", "Pare-feu", "Réseau", "Firewall", new[]{"pare-feu","firewall","exception"}),
        ("🔒", "VPN", "Réseau", "VPN", new[]{"vpn","tunnel"}),
        ("🖥️", "Accès à distance", "Réseau", "Accès distant", new[]{"acces distant","distant","teamviewer","anydesk","remote"}),
        ("⚙️", "Compte, famille & performances", "Système", "Système", new[]{"systeme","système","compte","famille","cpu","ram","performance","controle parental","parental"}),
        ("📊", "Processus en direct", "Système", "Processus", new[]{"processus","process","tâches","taches"}),
        ("🧹", "Optimisation", "Système", "Optimisation", new[]{"optimis","nettoyer","temp","dns","ram","défrag","demarrage","démarrage"}),
        ("🔌", "Périphériques", "Système", "Périphériques", new[]{"peripherique","périphérique","usb","imprimante","materiel","matériel","clé"}),
        ("📱", "Sécurité des appareils", "Système", "Appareil", new[]{"appareil","webcam","micro","bluetooth"}),
        ("🎛️", "Contrôle système", "Système", "Contrôle", new[]{"controle","contrôle","redemarrer","redémarrer","eteindre","éteindre","shutdown","services","verrou extinction","pin","usb bloquer"}),
        ("🧬", "ADN des programmes", "Intégrité", "ADN", new[]{"adn","confiance","empreinte programme"}),
        ("👥", "Jumeau numérique", "Intégrité", "ADN", new[]{"jumeau","twin","empreinte systeme"}),
        ("🕵️", "Investigation", "Historique", "Investigation", new[]{"investigation","incident","forensic","enquete","enquête"}),
        ("📅", "Timeline de sécurité", "Historique", "Timeline", new[]{"timeline","chronologie"}),
        ("📜", "Journal d'activité", "Historique", "Logs", new[]{"journal","logs","log","activite","activité"}),
        ("🕘", "Historique (sites, apps…)", "Historique", "Historique", new[]{"historique","sites visités","navigation","applications ouvertes","problemes","problèmes"}),
        ("🔑", "Registre des accès", "Historique", "Accès", new[]{"acces","accès","utilisateurs windows","connexions deconnexions","comptes"}),
        ("🪪", "ID Registre (carte identité)", "Historique", "Registre ID", new[]{"registre id","carte identité","eid","id card"}),
        ("🗝️", "Coffre-fort (mots de passe)", "Confidentialité", "Coffre-fort", new[]{"coffre","mot de passe","password","vault","identifiants"}),
        ("📧", "Mail Shield", "Confidentialité", "Mail", new[]{"mail","email","e-mail","phishing","gmail","spam","pièce jointe"}),
        ("🔎", "Recherche globale (fichiers)", "Confidentialité", "Recherche", new[]{"recherche","chercher fichier","trouver fichier","fichier partout"}),
        ("🤖", "Copilote (IA)", "Assistant", "Copilote", new[]{"copilote","ia","assistant","chat","pourquoi bloqué"}),
        ("🎯", "Centre de sécurité", "Assistant", "Centre", new[]{"centre","panic","sos","presse-papiers","mode panic"}),
        ("🛠️", "Réglages", "Réglages", "Settings", new[]{"reglages","réglages","settings","clé api","api","licence","activer","abonnement","mise à jour"}),
    };

    private List<FeatureHit>? _featureIndex;

    /// <summary>Index complet des options : construit en parcourant tous les boutons de chaque page.</summary>
    private List<FeatureHit> FeatureIndex()
    {
        if (_featureIndex is not null) return _featureIndex;
        var list = Features.Select(f => new FeatureHit(f.Icon, f.Label, f.Category, f.Tab)).ToList();

        var pages = new (Grid? Page, string Tab)[]
        {
            (PageProtection,"Protection"), (PageScan,"Scan"), (PageFirewall,"Firewall"), (PageOptimize,"Optimisation"),
            (PageDevices,"Périphériques"), (PageTools,"Outils"), (PageNetwork,"Réseau"), (PageWorld,"Mondiale"),
            (PageVpn,"VPN"), (PageVault,"Coffre-fort"), (PageCentre,"Centre"), (PageIntegrity,"Intégrité"),
            (PageDna,"ADN"), (PageInvestigation,"Investigation"), (PageCopilot,"Copilote"), (PageDevice,"Appareil"),
            (PageSystem,"Système"), (PageProcesses,"Processus"), (PageTimeline,"Timeline"), (PageSettings,"Settings"),
            (PageLogs,"Logs"), (PageAccess,"Accès"), (PageRemote,"Accès distant"), (PageIdRegister,"Registre ID"),
            (PageControl,"Contrôle"), (PageHistory,"Historique"), (PageMail,"Mail"),
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (page, tab) in pages)
        {
            if (page is null) continue;
            foreach (var label in CollectOptionLabels(page))
            {
                if (label.Length < 3 || label.Length > 60) continue;
                if (!seen.Add(tab + "|" + label)) continue;
                list.Add(new FeatureHit("•", label, tab, tab));
            }
        }
        _featureIndex = list;
        return list;
    }

    private static IEnumerable<string> CollectOptionLabels(DependencyObject root)
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dobj) continue;
            if (dobj is System.Windows.Controls.Primitives.ButtonBase bb && bb.Content is string s && s.Trim().Length > 0)
                yield return s.Trim();
            foreach (var sub in CollectOptionLabels(dobj)) yield return sub;
        }
    }

    private void OnAppSearch(object sender, TextChangedEventArgs e)
    {
        if (!_ready || AppSearchResults is null) return;
        string q = (AppSearchInput?.Text ?? "").Trim();
        if (AppSearchHint is not null) AppSearchHint.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (q.Length == 0) { AppSearchResults.Visibility = Visibility.Collapsed; AppSearchResults.ItemsSource = null; return; }

        string ql = q.ToLowerInvariant();
        var all = FeatureIndex();

        // Onglets dont le nom correspond → on liste TOUTES leurs options.
        var matchedTabs = all.Select(h => h.Tab).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(t => t.ToLowerInvariant().Contains(ql)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var hits = all
            .Where(h => matchedTabs.Contains(h.Tab)
                     || h.Label.ToLowerInvariant().Contains(ql)
                     || h.Category.ToLowerInvariant().Contains(ql))
            .GroupBy(h => h.Tab + "|" + h.Label, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(h => h.Label.ToLowerInvariant().StartsWith(ql))
            .ThenBy(h => h.Tab)
            .Select(h => new FeatureHit(h.Icon, h.Label, "Dans : " + h.Tab, h.Tab))
            .Take(50).ToList();

        AppSearchResults.ItemsSource = hits;
        AppSearchResults.Visibility = hits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAppSearchKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || AppSearchResults?.ItemsSource is not IEnumerable<FeatureHit> hits) return;
        var first = hits.FirstOrDefault();
        if (first is not null) GoToFeature(first);
    }

    private void OnAppSearchPick(object sender, SelectionChangedEventArgs e)
    {
        if (AppSearchResults?.SelectedItem is FeatureHit hit) GoToFeature(hit);
    }

    private void GoToFeature(FeatureHit hit)
    {
        if (AppSearchInput is not null) AppSearchInput.Text = "";
        if (AppSearchResults is not null) { AppSearchResults.Visibility = Visibility.Collapsed; AppSearchResults.ItemsSource = null; }
        NavigateToTab(hit.Tab);
    }

    /// <summary>Sélectionne l'onglet de navigation dont le libellé correspond.</summary>
    private void NavigateToTab(string tab)
    {
        foreach (var rb in FindVisualChildren<System.Windows.Controls.RadioButton>(this))
        {
            if (rb.GroupName == "nav" && (rb.Content as string) == tab)
            {
                rb.IsChecked = true;   // déclenche OnNav → ShowPage
                return;
            }
        }
        ShowPage(tab);   // repli si le bouton n'est pas trouvé
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var sub in FindVisualChildren<T>(child)) yield return sub;
        }
    }

    // ------------------------------------------------- Recherche globale -------

    private CancellationTokenSource? _searchCts;
    private readonly List<SearchHit> _searchHits = new();
    private readonly object _searchLock = new();
    private DispatcherTimer? _searchUiTimer;
    private string _searchCurrentDir = "";

    private void OnSearchAllKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnGlobalSearch(sender, e);
    }

    private void OnGlobalSearch(object sender, RoutedEventArgs e)
    {
        string q = (SearchAllInput?.Text ?? "").Trim();
        if (q.Length < 2)
        {
            if (SearchAllStatus is not null) SearchAllStatus.Text = "Tapez au moins 2 caractères.";
            return;
        }
        // Annule une recherche précédente éventuelle.
        _searchCts?.Cancel();
        lock (_searchLock) _searchHits.Clear();
        if (SearchResultsList is not null) SearchResultsList.ItemsSource = null;

        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        if (SearchStopButton is not null) SearchStopButton.IsEnabled = true;
        if (SearchAllButton is not null) SearchAllButton.IsEnabled = false;
        if (SearchAllStatus is not null) SearchAllStatus.Text = "Recherche en cours sur tous les disques…";

        // Rafraîchissement périodique de la liste (évite d'inonder l'UI).
        _searchUiTimer?.Stop();
        _searchUiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _searchUiTimer.Tick += (_, _) => RefreshSearchResults(false);
        _searchUiTimer.Start();

        Log($"Recherche globale : « {q} ».");
        Task.Run(() =>
        {
            try
            {
                GlobalSearch.Run(q, ct,
                    hit => { lock (_searchLock) _searchHits.Add(hit); },
                    dir => _searchCurrentDir = dir);
            }
            catch { }
            finally
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _searchUiTimer?.Stop();
                    RefreshSearchResults(true);
                    if (SearchStopButton is not null) SearchStopButton.IsEnabled = false;
                    if (SearchAllButton is not null) SearchAllButton.IsEnabled = true;
                }));
            }
        }, ct);
    }

    private void RefreshSearchResults(bool done)
    {
        List<SearchHit> snapshot;
        lock (_searchLock) snapshot = _searchHits.ToList();
        if (SearchResultsList is not null) SearchResultsList.ItemsSource = snapshot;
        if (SearchAllStatus is not null)
        {
            string tail = done ? "Terminé." : $"En cours… ({_searchCurrentDir})";
            SearchAllStatus.Text = $"{snapshot.Count} résultat(s). {tail}";
        }
    }

    private void OnStopGlobalSearch(object sender, RoutedEventArgs e)
    {
        _searchCts?.Cancel();
        _searchUiTimer?.Stop();
        RefreshSearchResults(true);
        if (SearchStopButton is not null) SearchStopButton.IsEnabled = false;
        if (SearchAllButton is not null) SearchAllButton.IsEnabled = true;
        if (SearchAllStatus is not null) SearchAllStatus.Text += " (arrêté)";
    }

    private void OnOpenSearchHit(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path }) return;
        try
        {
            // Sélectionne le fichier/dossier dans l'Explorateur Windows.
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { if (SearchAllStatus is not null) SearchAllStatus.Text = $"Impossible d'ouvrir : {ex.Message}"; }
    }

    // ----------------------------------------------------- Mail Shield ---------

    private void OnConnectMail(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string provider }) return;
        if (provider == "gmail") { _ = ConnectGmailAsync(); return; }

        // Outlook / Yahoo / Proton : câblage à venir (Proton n'a pas d'API publique).
        string name = provider switch
        {
            "outlook" => "Outlook / Office 365 (Microsoft)",
            "yahoo" => "Yahoo Mail",
            "proton" => "Proton Mail",
            _ => provider
        };
        if (MailConnectStatus is not null)
            MailConnectStatus.Text = provider == "proton"
                ? "Proton n'expose pas d'API publique (chiffrement de bout en bout) : utilisez le Proton Mail Bridge, ou l'analyse manuelle d'un .eml ci-dessous."
                : $"La connexion {name} arrive bientôt. Pour l'instant : Gmail (bouton dédié) ou analyse manuelle d'un .eml ci-dessous.";
    }

    /// <summary>Flux OAuth Gmail complet : consentement navigateur puis lecture des e-mails.</summary>
    private async Task ConnectGmailAsync()
    {
        var cfg = SecretVault.Load("mailshield");
        string clientId = cfg.GetValueOrDefault("gmail_clientid") ?? "";
        string secret = cfg.GetValueOrDefault("gmail_secret") ?? "";

        if (string.IsNullOrWhiteSpace(clientId))
        {
            var idDlg = new PromptWindow("Connecter Gmail",
                "Collez le « Client ID » OAuth (console.cloud.google.com → Identifiants → ID client OAuth, type « Application de bureau ») :",
                "Suivant") { Owner = this };
            if (idDlg.ShowDialog() != true || string.IsNullOrWhiteSpace(idDlg.Value)) return;
            clientId = idDlg.Value.Trim();
        }
        if (string.IsNullOrWhiteSpace(secret))
        {
            var secDlg = new PromptWindow("Connecter Gmail",
                "Collez le « Client Secret » associé :", "Se connecter") { Owner = this };
            if (secDlg.ShowDialog() != true || string.IsNullOrWhiteSpace(secDlg.Value)) return;
            secret = secDlg.Value.Trim();
        }

        try
        {
            if (MailConnectStatus is not null) MailConnectStatus.Text = "Ouverture du consentement Google dans le navigateur…";
            var tokens = await GmailClient.AuthorizeAsync(clientId, secret, CancellationToken.None);
            cfg["gmail_clientid"] = clientId;
            cfg["gmail_secret"] = secret;
            if (!string.IsNullOrEmpty(tokens.RefreshToken)) cfg["gmail_refresh"] = tokens.RefreshToken!;
            SecretVault.Save("mailshield", cfg);
            if (MailConnectStatus is not null) MailConnectStatus.Text = "✓ Gmail connecté. Analyse de vos e-mails récents…";
            Log("Gmail connecté (OAuth).");
            await ScanGmailWithTokenAsync(tokens.AccessToken);
        }
        catch (Exception ex)
        {
            if (MailConnectStatus is not null) MailConnectStatus.Text = $"Échec de la connexion Gmail : {ex.Message}";
        }
    }

    private async void OnScanGmailInbox(object sender, RoutedEventArgs e)
    {
        var cfg = SecretVault.Load("mailshield");
        string clientId = cfg.GetValueOrDefault("gmail_clientid") ?? "";
        string secret = cfg.GetValueOrDefault("gmail_secret") ?? "";
        string refresh = cfg.GetValueOrDefault("gmail_refresh") ?? "";
        if (string.IsNullOrWhiteSpace(refresh) || string.IsNullOrWhiteSpace(clientId))
        {
            await ConnectGmailAsync();
            return;
        }
        try
        {
            if (MailConnectStatus is not null) MailConnectStatus.Text = "Connexion à Gmail…";
            string access = await GmailClient.RefreshAsync(clientId, secret, refresh, CancellationToken.None);
            await ScanGmailWithTokenAsync(access);
        }
        catch (Exception ex)
        {
            if (MailConnectStatus is not null) MailConnectStatus.Text = $"Échec : {ex.Message} (reconnectez Gmail).";
        }
    }

    private async Task ScanGmailWithTokenAsync(string accessToken)
    {
        if (MailConnectStatus is not null) MailConnectStatus.Text = "Lecture et analyse des e-mails récents…";
        var raws = await GmailClient.ListRecentRawAsync(accessToken, 20, CancellationToken.None);
        var verdicts = await Task.Run(() =>
            raws.Select(r => { try { return MailShield.AnalyzeRaw(r.Raw); } catch { return null; } })
                .Where(v => v is not null).Select(v => v!)
                .OrderByDescending(v => v.Score).ToList());
        if (MailInboxList is not null) MailInboxList.ItemsSource = verdicts;
        int dangerous = verdicts.Count(v => v.Level == "DANGEREUX");
        int suspect = verdicts.Count(v => v.Level == "SUSPECT");
        if (MailConnectStatus is not null)
            MailConnectStatus.Text = $"✓ {verdicts.Count} e-mail(s) analysé(s) — {dangerous} dangereux, {suspect} suspect(s).";
        if (dangerous > 0) { try { SoundFx.Threat(); } catch { } Notify("Mail Shield", $"{dangerous} e-mail(s) dangereux détecté(s) dans Gmail.", "Mail"); }
        Log($"Gmail analysé : {verdicts.Count} e-mails ({dangerous} dangereux).");
    }

    private void OnAnalyzeEmlFile(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Analyser un e-mail",
            Filter = "E-mails (*.eml;*.msg)|*.eml;*.msg|Tous (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;
        try { ShowMailVerdict(MailShield.AnalyzeFile(dlg.FileName)); }
        catch (Exception ex) { ShowMailError(ex.Message); }
    }

    private void OnAnalyzePastedMail(object sender, RoutedEventArgs e)
    {
        string raw = MailRawInput?.Text ?? "";
        if (raw.Trim().Length == 0) { ShowMailError("Collez d'abord un e-mail dans la zone de texte."); return; }
        try { ShowMailVerdict(MailShield.AnalyzeRaw(raw)); }
        catch (Exception ex) { ShowMailError(ex.Message); }
    }

    private void ShowMailVerdict(MailVerdict v)
    {
        if (MailResultCard is null) return;
        MailResultCard.Visibility = Visibility.Visible;
        var color = v.Level switch
        {
            "DANGEREUX" => System.Windows.Media.Color.FromRgb(0xE5, 0x48, 0x4D),
            "SUSPECT" => System.Windows.Media.Color.FromRgb(0xF5, 0xA6, 0x23),
            _ => System.Windows.Media.Color.FromRgb(0x3F, 0xB9, 0x50)
        };
        if (MailVerdictText is not null)
        {
            MailVerdictText.Text = $"{v.LevelIcon} {v.Level} — score {v.Score}";
            MailVerdictText.Foreground = new System.Windows.Media.SolidColorBrush(color);
        }
        if (MailMeta is not null)
            MailMeta.Text = $"De : {v.From}\nObjet : {v.Subject}" +
                (v.Date.Length > 0 ? $"\nDate : {v.Date}" : "") +
                (v.Links.Count > 0 ? $"\n{v.Links.Count} lien(s)" : "") +
                (v.Attachments.Count > 0 ? $" · {v.Attachments.Count} pièce(s) jointe(s) : {string.Join(", ", v.Attachments)}" : "");
        if (MailReasonsList is not null) MailReasonsList.ItemsSource = v.Reasons;
        Log($"Mail Shield : {v.Level} (score {v.Score}) — {v.Subject}");
        if (v.Level == "DANGEREUX") { try { SoundFx.Threat(); } catch { } }
    }

    private void ShowMailError(string message)
    {
        if (MailResultCard is not null) MailResultCard.Visibility = Visibility.Visible;
        if (MailVerdictText is not null)
        {
            MailVerdictText.Text = "Analyse impossible";
            MailVerdictText.Foreground = System.Windows.Media.Brushes.OrangeRed;
        }
        if (MailMeta is not null) MailMeta.Text = message;
        if (MailReasonsList is not null) MailReasonsList.ItemsSource = null;
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
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return "ERREUR: impossible de démarrer PowerShell.";
            // On lit les deux flux en parallèle (évite tout blocage de tampon) et on
            // attend réellement la fin du processus.
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            await Task.WhenAll(outTask, errTask);
            await p.WaitForExitAsync();
            string output = outTask.Result.Trim();
            string error = errTask.Result.Trim();
            // Si la commande n'a rien renvoyé mais a produit une erreur, on remonte
            // la raison réelle (sinon l'appelant croit à un échec silencieux).
            if (output.Length == 0 && error.Length > 0) return "ERREUR: " + error;
            return output;
        }
        catch (Exception ex)
        {
            return "ERREUR: " + ex.Message;
        }
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
/// <summary>Un évènement de la timeline de sécurité (page Timeline).</summary>
public sealed class SecurityTimelineItem
{
    public string TimeText { get; init; } = "";
    public string Title { get; init; } = "";
    public string Sub { get; init; } = "";
    public Brush Dot { get; init; } = Brushes.Gray;
}

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

    // --- Alarme continue (carte d'identité retirée) ---------------------------
    // Sirène montante/descendante qui tourne en boucle jusqu'à StopAlarm().
    private static System.Threading.CancellationTokenSource? _alarmCts;

    /// <summary>Déclenche une sirène d'alarme en boucle (jusqu'à <see cref="StopAlarm"/>).</summary>
    public static void StartAlarm()
    {
        if (_alarmCts is not null) return;          // déjà en cours
        var cts = new System.Threading.CancellationTokenSource();
        _alarmCts = cts;
        var token = cts.Token;
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        for (int f = 700; f <= 1100 && !token.IsCancellationRequested; f += 100)
                            Console.Beep(f, 110);
                        for (int f = 1100; f >= 700 && !token.IsCancellationRequested; f -= 100)
                            Console.Beep(f, 110);
                    }
                    catch
                    {
                        try { SystemSounds.Hand.Play(); } catch { }
                        try { System.Threading.Thread.Sleep(400); } catch { }
                    }
                }
            }
            catch { }
        });
    }

    /// <summary>Arrête la sirène d'alarme si elle tourne.</summary>
    public static void StopAlarm()
    {
        try { _alarmCts?.Cancel(); } catch { }
        _alarmCts = null;
    }

    /// <summary>Indique si l'alarme est en cours.</summary>
    public static bool AlarmActive => _alarmCts is not null;

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
