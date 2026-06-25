using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using IatechShield.Engine;

namespace IatechShield.Gui;

public partial class MainWindow : Window
{
    private SignatureDatabase? _db;
    private Scanner? _scanner;
    private Quarantine? _quarantine;
    private RealtimeMonitor? _monitor;
    private RansomwareGuard? _ransomGuard;
    private readonly ProcessCuller _culler = new();

    private int _threatCount;
    private bool _scanning;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => InitEngine();
    }

    // Branche l'écoute des événements matériels (clés USB) une fois la fenêtre prête.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            _knownDrives = new HashSet<string>(GetReadyRemovableDrives(), StringComparer.OrdinalIgnoreCase);
            source.AddHook(WndProc);
        }
    }

    // --------------------------------------------------------------- moteur ---

    private void InitEngine()
    {
        try
        {
            string dbPath = Path.Combine(AppContext.BaseDirectory, "signatures.json");
            _db = SignatureDatabase.LoadFromFile(dbPath);
            _scanner = new Scanner(_db);
            _quarantine = new Quarantine(DefaultQuarantineDir());

            SignatureCountText.Text = $"{_db.Signatures.Count} signatures chargées";
            UpdatesDateText.Text = $"Dernière vérification : {DateTime.Now:dd/MM/yyyy}";
        }
        catch (Exception ex)
        {
            ScanStatusText.Text = $"Erreur moteur : {ex.Message}";
            ScanButton.IsEnabled = false;
        }
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

    // -------------------------------------------------------------- analyse ---

    private async void OnQuickScan(object sender, RoutedEventArgs e)
    {
        if (_scanning || _scanner is null)
            return;

        var dialog = new OpenFolderDialog
        {
            Title = "Choisir le dossier à analyser",
            InitialDirectory = DefaultScanFolder()
        };
        if (dialog.ShowDialog(this) != true)
            return;

        string target = dialog.FolderName;
        _scanning = true;
        ScanButton.IsEnabled = false;
        ResetThreats();

        var service = new ScanService(_scanner, _quarantine);
        int filesScanned = 0;

        try
        {
            var report = await Task.Run(() => service.Scan(target, result =>
            {
                filesScanned++;
                // Mise à jour fluide de l'UI depuis le thread de scan.
                if (filesScanned % 25 == 0 || result.IsThreat)
                {
                    Dispatcher.Invoke(() =>
                    {
                        ScanStatusText.Text = $"Analyse… {filesScanned} fichiers";
                        if (result.IsThreat)
                            RegisterThreat();
                    });
                }
            }));

            ScanStatusText.Text =
                $"Terminé : {report.FilesScanned} fichiers, {report.Threats.Count} menace(s).";
            _threatCount = report.Threats.Count;
            UpdateThreatUi();
        }
        catch (Exception ex)
        {
            ScanStatusText.Text = $"Échec de l'analyse : {ex.Message}";
        }
        finally
        {
            _scanning = false;
            ScanButton.IsEnabled = true;
        }
    }

    private void OnScanNav(object sender, RoutedEventArgs e) => OnQuickScan(sender, e);

    // ----------------------------------------------------------- temps réel ---

    private void OnRealtimeToggled(object sender, RoutedEventArgs e)
    {
        if (_scanner is null)
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
            }
            catch (Exception ex)
            {
                ScanStatusText.Text = $"Impossible d'activer le temps réel : {ex.Message}";
                RealtimeSwitch.IsChecked = false;
            }
        }
        else
        {
            _monitor?.Dispose();
            _monitor = null;
            ScanStatusText.Text = "Surveillance temps réel désactivée.";
        }
    }

    private void OnRealtimeThreat(FileScanResult result)
    {
        Dispatcher.Invoke(() =>
        {
            // Quarantaine immédiate du fichier détecté en temps réel.
            try
            {
                string sha = Scanner.ComputeSha256(result.Path);
                _quarantine?.Add(result.Path, result.Match!.Name, sha);
            }
            catch { /* fichier verrouillé : on rapporte quand même */ }

            RegisterThreat();
            UpdateThreatUi();
            ScanStatusText.Text = $"Menace bloquée : {Path.GetFileName(result.Path)}";
        });
    }

    // -------------------------------------------------------- anti-ransomware -

    private void OnRansomwareToggled(object sender, RoutedEventArgs e)
    {
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
                ScanStatusText.Text = "Bouclier anti-ransomware actif (Documents, Images, Bureau).";
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
        }
    }

    private void OnRansomwareAlert(RansomwareAlert alert)
    {
        Dispatcher.Invoke(() =>
        {
            // Réaction : on cible et arrête le processus le plus actif en écriture.
            var culprit = _culler.FindTopWriter();
            string action = "aucun processus dominant identifié";
            if (culprit is not null && _culler.Kill(culprit.Pid))
                action = $"processus {culprit.Name} (PID {culprit.Pid}) arrêté";

            RegisterThreat();
            UpdateThreatUi();
            ScanStatusText.Text = $"⚠ RANSOMWARE : {alert.Reason} — {action}.";
            _ransomGuard?.Rearm();
        });
    }

    // -------------------------------------------------------------- USB --------

    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private HashSet<string> _knownDrives = new(StringComparer.OrdinalIgnoreCase);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DEVICECHANGE && wParam.ToInt32() == DBT_DEVICEARRIVAL)
        {
            // Un périphérique vient d'être branché : on cherche le nouveau lecteur.
            foreach (string drive in GetReadyRemovableDrives())
            {
                if (_knownDrives.Add(drive))
                    AutoScanDrive(drive);
            }
        }
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
        if (_scanner is null)
            return;

        ScanStatusText.Text = $"Clé USB détectée ({drive}) — analyse automatique…";
        var service = new ScanService(_scanner, _quarantine);
        try
        {
            var report = await Task.Run(() => service.Scan(drive));
            if (report.Threats.Count > 0)
            {
                _threatCount += report.Threats.Count;
                UpdateThreatUi();
            }
            ScanStatusText.Text =
                $"USB {drive} : {report.FilesScanned} fichiers, {report.Threats.Count} menace(s).";
        }
        catch (Exception ex)
        {
            ScanStatusText.Text = $"Analyse USB échouée : {ex.Message}";
        }
    }

    // ------------------------------------------------------------- affichage --

    private void ResetThreats()
    {
        _threatCount = 0;
        UpdateThreatUi();
    }

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
    }

    // -------------------------------------------------------- fenêtre / chrome -

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnClose(object sender, RoutedEventArgs e)
    {
        _monitor?.Dispose();
        _ransomGuard?.RemoveCanaries();
        _ransomGuard?.Dispose();
        Close();
    }
}
