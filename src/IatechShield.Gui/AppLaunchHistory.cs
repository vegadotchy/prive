using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace IatechShield.Gui;

/// <summary>Un lancement d'application détecté (nom de l'exécutable + dernière exécution).</summary>
public sealed record AppRun(string App, DateTime When);

/// <summary>
/// Historique des applications ouvertes, reconstruit à partir du dossier Prefetch de
/// Windows (C:\Windows\Prefetch\*.pf). Chaque fichier .pf correspond à un programme
/// exécuté ; sa date de dernière modification correspond à la dernière exécution.
/// (Nécessite les droits administrateur — l'application les possède déjà.)
/// </summary>
public static class AppLaunchHistory
{
    // Processus système Windows à masquer (on ne veut que les vraies applications).
    private static readonly HashSet<string> SystemExe = new(StringComparer.OrdinalIgnoreCase)
    {
        "SVCHOST.EXE", "RUNTIMEBROKER.EXE", "BACKGROUNDTASKHOST.EXE", "SEARCHPROTOCOLHOST.EXE",
        "SEARCHFILTERHOST.EXE", "SEARCHINDEXER.EXE", "DLLHOST.EXE", "CONHOST.EXE", "TASKHOSTW.EXE",
        "SIHOST.EXE", "CTFMON.EXE", "SMARTSCREEN.EXE", "WMIPRVSE.EXE", "MOUSOCOREWORKER.EXE",
        "AUDIODG.EXE", "FONTDRVHOST.EXE", "DWM.EXE", "CSRSS.EXE", "WININIT.EXE", "SERVICES.EXE",
        "LSASS.EXE", "SMSS.EXE", "SPOOLSV.EXE", "TASKHOST.EXE", "TASKENG.EXE", "RUNDLL32.EXE",
        "WERFAULT.EXE", "WERFAULTSECURE.EXE", "CONSENT.EXE", "SECURITYHEALTHSERVICE.EXE",
        "SECURITYHEALTHSYSTRAY.EXE", "TRUSTEDINSTALLER.EXE", "TIWORKER.EXE", "MSMPENG.EXE",
        "NISSRV.EXE", "MPCMDRUN.EXE", "USERINIT.EXE", "LOGONUI.EXE", "SYSTEMSETTINGS.EXE",
        "APPLICATIONFRAMEHOST.EXE", "SHELLEXPERIENCEHOST.EXE", "STARTMENUEXPERIENCEHOST.EXE",
        "TEXTINPUTHOST.EXE", "PHONEEXPERIENCEHOST.EXE", "GAMEBARFTSERVER.EXE", "WIDGETS.EXE",
        "COMPPKGSRV.EXE", "DASHOST.EXE", "DEVICECENSUS.EXE", "COMPATTELRUNNER.EXE",
        "WUAUCLT.EXE", "USOCLIENT.EXE", "SPPSVC.EXE", "MSDTC.EXE", "DISM.EXE", "SFC.EXE",
        "REGSVR32.EXE", "WSCRIPT.EXE", "CSCRIPT.EXE", "MMC.EXE", "SETTINGSYNCHOST.EXE"
    };

    // Sous-processus « d'application » à masquer même s'ils sont dans Program Files
    // (ce ne sont pas des applications lancées par l'utilisateur).
    private static readonly HashSet<string> HelperExe = new(StringComparer.OrdinalIgnoreCase)
    {
        "MSEDGEWEBVIEW2.EXE", "FULLTRUSTNOTIFIER.EXE", "SEARCHHOST.EXE", "CRASHPAD_HANDLER.EXE",
        "ELEVATION_SERVICE.EXE", "NOTIFICATION_HELPER.EXE", "GOOGLECRASHHANDLER.EXE",
        "GOOGLECRASHHANDLER64.EXE", "SETUP.EXE", "UPDATE.EXE", "SQUIRREL.EXE", "VCREDIST.EXE",
        "UNINS000.EXE", "INSTALLER.EXE", "IDENTITY_HELPER.EXE", "MSEDGE_PROXY.EXE"
    };

    public static List<AppRun> Read(int limit = 300)
    {
        string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        // Ensemble des applications réellement installées (registre « App Paths »).
        var installed = InstalledAppExes();

        var byExe = new Dictionary<string, AppRun>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string prefetch = Path.Combine(windir, "Prefetch");
            if (!Directory.Exists(prefetch)) return new();

            foreach (var file in new DirectoryInfo(prefetch).GetFiles("*.pf"))
            {
                // Nom du fichier : « CHROME.EXE-1A2B3C4D.pf » → « CHROME.EXE ».
                string name = file.Name;
                int dash = name.LastIndexOf('-');
                string exe = dash > 0 ? name.Substring(0, dash) : name;

                if (SystemExe.Contains(exe) || HelperExe.Contains(exe)) continue;
                // On ne garde que les applications installées (présentes dans « App Paths »).
                if (installed.Count > 0 && !installed.Contains(exe)) continue;

                // Déduplication : une seule entrée par application (la plus récente).
                if (!byExe.TryGetValue(exe, out var existing) || file.LastWriteTime > existing.When)
                    byExe[exe] = new AppRun(exe, file.LastWriteTime);
            }
        }
        catch { /* dossier inaccessible */ }

        return byExe.Values.OrderByDescending(r => r.When).Take(limit).ToList();
    }

    /// <summary>Noms des exécutables des applications installées (clés « App Paths » du registre).</summary>
    private static HashSet<string> InstalledAppExes()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        (RegistryKey Root, string Path)[] locations =
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths"),
            (Registry.CurrentUser,  @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
        };
        foreach (var (root, path) in locations)
        {
            try
            {
                using var key = root.OpenSubKey(path);
                if (key is null) continue;
                foreach (var sub in key.GetSubKeyNames())
                    if (sub.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        set.Add(sub);
            }
            catch { }
        }
        return set;
    }
}
