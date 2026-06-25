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

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            main.Show();
            splash.Close();
        };
        timer.Start();
    }
}
