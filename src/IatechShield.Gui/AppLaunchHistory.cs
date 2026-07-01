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
    public static List<AppRun> Read(int limit = 300)
    {
        var runs = new List<AppRun>();
        try
        {
            string prefetch = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
            if (!Directory.Exists(prefetch)) return runs;

            foreach (var file in new DirectoryInfo(prefetch).GetFiles("*.pf"))
            {
                // Nom du fichier : « CHROME.EXE-1A2B3C4D.pf » → « CHROME.EXE ».
                string name = file.Name;
                int dash = name.LastIndexOf('-');
                string exe = dash > 0 ? name.Substring(0, dash) : name;
                runs.Add(new AppRun(exe, file.LastWriteTime));
            }
        }
        catch { /* dossier inaccessible : liste vide */ }

        return runs
            .OrderByDescending(r => r.When)
            .Take(limit)
            .ToList();
    }
}
