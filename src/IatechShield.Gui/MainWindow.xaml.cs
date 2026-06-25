using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
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

    private int _threatCount;
    private bool _scanning;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => InitEngine();
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
        Close();
    }
}
