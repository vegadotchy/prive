using System.Windows;
using System.Windows.Threading;

namespace IatechShield.Gui;

public partial class App : Application
{
    private bool _dialogOpen;
    private DateTime _lastDialog = DateTime.MinValue;

    private static string CrashLogPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IatechShield", "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Mode « garde de désinstallation » : appelé par le désinstallateur. On exige la
        // carte d'identité ; si autorisée, on envoie le rapport à IATECHFUTUR et on renvoie
        // le code 0 (désinstallation permise). Sinon code 1 (désinstallation bloquée).
        foreach (var a in e.Args)
        {
            if (string.Equals(a, "--uninstall-auth", StringComparison.OrdinalIgnoreCase))
            {
                RunUninstallGuard();
                return;
            }
        }

        // Filet de sécurité global : on évite que l'application se ferme
        // silencieusement sur une exception non gérée. On journalise et on
        // continue lorsque c'est possible (au lieu de planter).
        DispatcherUnhandledException += (_, ex) =>
        {
            LogCrash("UI", ex.Exception);
            // Anti-spam : on n'affiche qu'une boîte à la fois et au plus une toutes
            // les 5 s (sinon une erreur répétée empilerait des dizaines de fenêtres).
            if (!_dialogOpen && (DateTime.UtcNow - _lastDialog).TotalSeconds > 5)
            {
                _dialogOpen = true;
                _lastDialog = DateTime.UtcNow;
                try
                {
                    MessageBox.Show(
                        "Une erreur est survenue mais IATECH-SHIELD reste ouvert.\n\n" +
                        ex.Exception.Message + "\n\nDétails enregistrés dans :\n" + CrashLogPath,
                        "IATECH-SHIELD PRO", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch { }
                finally { _dialogOpen = false; }
            }
            ex.Handled = true;   // on ne ferme pas l'application
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            LogCrash("Fatal", ex.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            LogCrash("Task", ex.Exception);
            ex.SetObserved();
        };

        // Écran de démarrage doré, puis apparition du tableau de bord.
        var splash = new SplashWindow();
        splash.Show();

        var main = new MainWindow();

        // Lancé via le menu contextuel « Scanner avec IATECHSHIELD PRO » (clic droit
        // sur un fichier, dossier, disque ou clé USB) : on récupère le chemin ciblé.
        string? shellScanPath = null;
        foreach (var arg in e.Args)
        {
            if (string.Equals(arg, "--scan", StringComparison.OrdinalIgnoreCase)) continue;
            if (System.IO.File.Exists(arg) || System.IO.Directory.Exists(arg)) { shellScanPath = arg; break; }
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            main.Show();
            splash.Close();
            if (shellScanPath is not null)
                main.RequestShellScan(shellScanPath);
        };
        timer.Start();
    }

    /// <summary>
    /// Garde de désinstallation : exige la carte d'identité propriétaire, envoie le rapport,
    /// puis quitte avec le code 0 (autorisé) ou 1 (refusé). Le désinstallateur lit ce code.
    /// </summary>
    private void RunUninstallGuard()
    {
        int code = 1;
        try
        {
            var card = EidReader.Read();
            bool ok = false;
            if (card.CardPresent && card.IsBelgianEid)
                ok = !CardAuth.IsEnrolled || CardAuth.IsAuthorized(card);

            if (!ok)
            {
                MessageBox.Show(
                    "Désinstallation refusée.\n\nInsérez une carte d'identité autorisée dans le lecteur, " +
                    "puis relancez la désinstallation.",
                    "IATECH-SHIELD PRO — Carte d'identité requise",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                // Autorisé : on enregistre la carte (si première fois) et on envoie le rapport.
                CardAuth.Register(card, out _);
                try { UninstallReport.BuildAndSendAsync(card).GetAwaiter().GetResult(); } catch { }
                MessageBox.Show(
                    $"Désinstallation autorisée par {card.DisplayIdentity}.\n\n" +
                    "Un rapport (poste, localisation, identité) a été transmis à IATECHFUTUR.",
                    "IATECH-SHIELD PRO", MessageBoxButton.OK, MessageBoxImage.Information);
                code = 0;
            }
        }
        catch { code = 1; }
        Shutdown(code);
    }

    private static void LogCrash(string kind, Exception? ex)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CrashLogPath)!);
            System.IO.File.AppendAllText(CrashLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {kind} : {ex}\n\n");
        }
        catch { }
    }
}
