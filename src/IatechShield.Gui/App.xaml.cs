using System.Windows;
using System.Windows.Threading;

namespace IatechShield.Gui;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
}
