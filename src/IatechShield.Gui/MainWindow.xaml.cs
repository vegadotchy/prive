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

    private bool _activated = true;
    private bool _ready;
    private int _threatCount;
    private bool _scanning;
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
            _ready = true;
            ShowPage("Dashboard");
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
        PageDevice.Visibility = Visibility.Collapsed;
        PageSystem.Visibility = Visibility.Collapsed;
        PageSettings.Visibility = Visibility.Collapsed;
        PageLogs.Visibility = Visibility.Collapsed;

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
            "Appareil" => PageDevice,
            "Système" => PageSystem,
            "Settings" => PageSettings,
            "Logs" => PageLogs,
            _ => PageDashboard
        };
        page.Visibility = Visibility.Visible;

        ApplyTabAccent(name);
        AnimatePageIn(page);

        if (page == PageSettings)
        {
            ApiKeyStatusText.Text = AiAssistant.IsConfigured
                ? "Clé ANTHROPIC_API_KEY détectée — assistant IA actif."
                : "Aucune clé détectée. Définissez ANTHROPIC_API_KEY pour activer l'assistant IA.";
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
            LoadVault();
        }
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
        ["Coffre-fort"] = Color.FromRgb(0xFB, 0xBF, 0x24), // or
        ["Centre"]     = Color.FromRgb(0xEF, 0x44, 0x44), // rouge sécurité
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
                Notify("Mise à jour disponible", $"IATECH-SHIELD {check.Latest.Tag} est disponible.");
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

    private void SetVpnUi(bool connected)
    {
        _vpnConnected = connected;
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
                Notify("VPN", $"Connexion sécurisée établie ({profile.Server}).");
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

    private sealed record VaultDto(string Id, string Title, string Username, string Password, string Url, string Notes);

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
        Notify("🚨 Mode Panic", "Réseau coupé, USB bloqué, session verrouillée.");
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
            Notify("⚠ Presse-papiers compromis", "Une adresse crypto copiée a été modifiée. Vérifiez avant d'envoyer des fonds !");
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
                Notify("VirusTotal — menace", $"{Path.GetFileName(path)} : {report.Summary}");
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

        switch (status.State)
        {
            case LicenseState.Licensed:
                LicenseStatusText.Text = status.License!.IsLifetime ? "Licence à vie" : $"Licence {status.License.TierLabel}";
                LicenseStatusText.Foreground = green;
                LicenseBadge.BorderBrush = green;
                ActivateButton.Visibility = Visibility.Collapsed;
                break;
            case LicenseState.TrialActive:
                LicenseStatusText.Text = $"Essai — {status.TrialDaysRemaining} j";
                LicenseStatusText.Foreground = accent;
                LicenseBadge.BorderBrush = accent;
                ActivateButton.Visibility = Visibility.Visible;
                break;
            default:
                LicenseStatusText.Text = "Essai expiré";
                LicenseStatusText.Foreground = alert;
                LicenseBadge.BorderBrush = alert;
                ActivateButton.Visibility = Visibility.Visible;
                break;
        }

        ScanButton.IsEnabled = _activated && _scanner is not null;
        if (FullScanButton is not null)
            FullScanButton.IsEnabled = _activated && _scanner is not null;
    }

    private void OnOpenLicense(object sender, RoutedEventArgs e)
    {
        var status = _license.GetStatus(DateTimeOffset.UtcNow);
        var dialog = new LicenseWindow(_license, status) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Activated)
            RefreshLicense();
    }

    private void OnDeactivateLicense(object sender, RoutedEventArgs e)
    {
        _license.Deactivate();
        RefreshLicense();
        Log("Licence désactivée (retour en mode essai).");
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

        var dialog = new OpenFolderDialog { Title = "Choisir le dossier à analyser", InitialDirectory = DefaultScanFolder() };
        if (dialog.ShowDialog(this) != true)
            return;

        await RunScanAsync(new[] { dialog.FolderName }, status => ScanStatusText.Text = status);
        if (_threats.Count > 0)
            ScanStatusText.Text = $"{_threats.Count} menace(s) — voir l'onglet Scan pour agir.";
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
        ScanButton.IsEnabled = false;
        if (FullScanButton is not null) FullScanButton.IsEnabled = false;
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
            report("Préparation : inventaire des fichiers…");
            var allFiles = await Task.Run(() =>
            {
                var list = new List<string>();
                foreach (string target in targets)
                {
                    try { list.AddRange(ScanService.EnumerateFiles(target)); }
                    catch { /* cible inaccessible : ignorée */ }
                }
                return list;
            });

            int total = allFiles.Count;
            Log($"{total} fichier(s) à analyser sur {Environment.ProcessorCount} cœur(s).");
            SetProgress(0, indeterminate: total == 0, visible: true);

            // 2) Analyse parallèle (multi-cœurs) avec progression.
            int lastPercent = -1;
            await Task.Run(() =>
            {
                service.ScanFilesParallel(allFiles, (result, scanned) =>
                {
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
                            Notify("Menace détectée", $"{name}\n{path}");
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
                });
            });

            SetProgress(100, indeterminate: false, visible: false);
            report($"Terminé : {total} fichiers analysés, {threats} menace(s).");
            _threatCount = _threats.Count;
            UpdateThreatUi();
            Log($"Analyse terminée : {total} fichiers, {threats} menace(s).");
            SoundFx.ScanDone();
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
            RefreshLicense();
        }
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
    private int _lockDelayMs = 5 * 60 * 1000;
    private bool _lockShowing;
    private bool _lockSettingsLoaded;

    private void StartLockWatcher()
    {
        var cfg = SecretVault.Load("lock");
        _lockPinHash = cfg.GetValueOrDefault("pin");
        if (cfg.TryGetValue("delay", out var d) && int.TryParse(d, out int min) && min > 0)
            _lockDelayMs = min * 60 * 1000;
        if (string.IsNullOrEmpty(_lockPinHash)) return;

        _lockTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _lockTimer.Tick -= OnLockTick;
        _lockTimer.Tick += OnLockTick;
        _lockTimer.Start();
    }

    private void OnLockTick(object? sender, EventArgs e)
    {
        if (_lockShowing || string.IsNullOrEmpty(_lockPinHash)) return;
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
        if (_lockShowing || string.IsNullOrEmpty(_lockPinHash)) return;
        _lockShowing = true;
        try
        {
            ShowFromTray();
            var lockScreen = new LockScreen(_lockPinHash) { Owner = this };
            lockScreen.ShowDialog();
        }
        catch { /* en cas d'échec d'affichage, on ne bloque pas l'utilisateur */ }
        finally { _lockShowing = false; }
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
            Notify("Menace bloquée (temps réel)", Path.GetFileName(result.Path));
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
            Notify("⚠ Ransomware bloqué", $"{alert.Reason} — {action}");
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

    private async void OnAiAssistant(object sender, RoutedEventArgs e)
    {
        if (!AiAssistant.IsConfigured)
        {
            MessageBox.Show(this,
                "L'assistant IA nécessite une clé Anthropic.\n\nDéfinissez ANTHROPIC_API_KEY puis relancez l'application.",
                "Assistant IA — configuration requise", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int suspect = _registry.ListAutoRuns().Count(a => a.Suspicious);
        string context =
            $"État du système : {_threatCount} menace(s) détectée(s), " +
            $"{suspect} programme(s) suspect(s) au démarrage. " +
            "Donne un avis de sécurité et les prochaines actions recommandées.";

        AiButton.IsEnabled = false;
        AiButton.Content = "Analyse…";
        try
        {
            string answer = await new AiAssistant().AskAsync(context);
            Log("Assistant IA consulté.");
            MessageBox.Show(this, answer, "Assistant IA — IATECH-SHIELD PRO", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Assistant IA indisponible : {ex.Message}", "Assistant IA", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            AiButton.IsEnabled = true;
            AiButton.Content = "Assistant IA";
        }
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

    // Le bouton X ne ferme pas l'app : il la réduit dans la barre des tâches.
    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // ---------------------------------------------------- barre des tâches ----

    private System.Windows.Forms.NotifyIcon? _tray;
    private bool _reallyExit;

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
        }
        catch { /* la barre des tâches n'est pas critique */ }
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true; Topmost = false;
    }

    /// <summary>Notification système (bulle dans la barre des tâches).</summary>
    private void Notify(string title, string message)
    {
        try { _tray?.ShowBalloonTip(4000, title, message, System.Windows.Forms.ToolTipIcon.Warning); }
        catch { }
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

    public static void ScanStart() => Safe(() => SystemSounds.Asterisk.Play());
    public static void ScanDone() => Safe(() => SystemSounds.Asterisk.Play());
    public static void Threat() => Safe(() => SystemSounds.Exclamation.Play());
    public static void Danger() => Safe(() => SystemSounds.Hand.Play());

    private static void Safe(Action a) { if (Muted) return; try { a(); } catch { } }
}
