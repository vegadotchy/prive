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
            // Assistant : on invite l'utilisateur à insérer sa carte, puis on relit à chaque
            // clic sur « Réessayer ». Dès qu'une carte autorisée est détectée, la désinstallation
            // démarre. « Annuler » interrompt la désinstallation.
            CardInfo? authorized = null;
            while (true)
            {
                var card = EidReader.Read();
                bool ok = card.CardPresent && card.IsBelgianEid
                          && (!CardAuth.IsEnrolled || CardAuth.IsAuthorized(card));
                if (ok) { authorized = card; break; }

                string why = !card.CardPresent
                    ? "Aucune carte détectée dans le lecteur."
                    : !card.IsBelgianEid
                        ? "La carte insérée n'est pas une carte d'identité belge (eID)."
                        : "Cette carte n'est pas autorisée pour ce poste.";

                var again = MessageBox.Show(
                    "Pour désinstaller IATECH-SHIELD PRO, insérez votre carte d'identité (eID) " +
                    "dans le lecteur.\n\n" + why + "\n\n" +
                    "➡ Insérez la carte, puis cliquez sur « Réessayer » pour lancer la désinstallation.\n" +
                    "➡ « Annuler » abandonne la désinstallation.",
                    "IATECH-SHIELD PRO — Insérez votre carte d'identité",
                    MessageBoxButton.OKCancel, MessageBoxImage.Information);

                if (again != MessageBoxResult.OK)
                {
                    // L'utilisateur renonce : on bloque la désinstallation.
                    Shutdown(1);
                    return;
                }
            }

            // Autorisé : on enregistre la carte (si première fois) et on envoie le rapport.
            CardAuth.Register(authorized!, out _);
            try { UninstallReport.BuildAndSendAsync(authorized!).GetAwaiter().GetResult(); } catch { }
            MessageBox.Show(
                $"Carte reconnue — {authorized!.DisplayIdentity}.\n\n" +
                "La désinstallation va démarrer. Un rapport (poste, localisation, identité) " +
                "a été transmis à IATECHFUTUR.",
                "IATECH-SHIELD PRO — Désinstallation autorisée",
                MessageBoxButton.OK, MessageBoxImage.Information);
            code = 0;
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
