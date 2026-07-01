using System.Collections.Generic;
using System.IO;
using System.Linq;

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

    public static List<AppRun> Read(int limit = 300)
    {
        var runs = new List<AppRun>();
        string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        try
        {
            string prefetch = Path.Combine(windir, "Prefetch");
            if (!Directory.Exists(prefetch)) return runs;

            foreach (var file in new DirectoryInfo(prefetch).GetFiles("*.pf"))
            {
                // Nom du fichier : « CHROME.EXE-1A2B3C4D.pf » → « CHROME.EXE ».
                string name = file.Name;
                int dash = name.LastIndexOf('-');
                string exe = dash > 0 ? name.Substring(0, dash) : name;

                if (SystemExe.Contains(exe)) continue;               // processus système connu
                if (IsWindowsBinary(exe, windir)) continue;          // exécutable présent dans Windows\System32…

                runs.Add(new AppRun(exe, file.LastWriteTime));
            }
        }
        catch { /* dossier inaccessible : liste vide */ }

        return runs
            .OrderByDescending(r => r.When)
            .Take(limit)
            .ToList();
    }

    /// <summary>Vrai si l'exécutable se trouve dans un dossier système Windows (donc à masquer).</summary>
    private static bool IsWindowsBinary(string exe, string windir)
    {
        foreach (var sub in new[] { "System32", "SysWOW64", "" })
        {
            try
            {
                string candidate = string.IsNullOrEmpty(sub)
                    ? Path.Combine(windir, exe)
                    : Path.Combine(windir, sub, exe);
                if (File.Exists(candidate)) return true;
            }
            catch { }
        }
        return false;
    }
}
