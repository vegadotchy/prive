using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace IatechShield.Gui;

/// <summary>Un résultat de recherche globale (fichier ou dossier).</summary>
public sealed record SearchHit(string Name, string FullPath, string Kind, string SizeText);

/// <summary>
/// Recherche globale sur tout l'ordinateur : parcourt tous les disques prêts (interne,
/// externe, USB) et l'intégralité de leurs sous-dossiers — y compris les fichiers
/// système — en cherchant le terme dans les noms de fichiers et de dossiers.
/// Le parcours est protégé dossier par dossier (les accès refusés sont ignorés) et
/// peut être interrompu à tout moment.
/// </summary>
public static class GlobalSearch
{
    public static void Run(string query, CancellationToken ct,
        Action<SearchHit> onHit, Action<string> onProgress, int maxResults = 5000)
    {
        if (string.IsNullOrWhiteSpace(query)) return;

        // Points de départ : la racine de chaque disque prêt.
        var stack = new Stack<string>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try { if (d.IsReady) stack.Push(d.RootDirectory.FullName); } catch { }
        }

        int found = 0;
        while (stack.Count > 0)
        {
            if (ct.IsCancellationRequested) return;
            string dir = stack.Pop();
            onProgress(dir);

            // Sous-dossiers.
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    if (ct.IsCancellationRequested) return;
                    string name = Path.GetFileName(sub);
                    if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        onHit(new SearchHit(name, sub, "Dossier", ""));
                        if (++found >= maxResults) return;
                    }
                    stack.Push(sub);
                }
            }
            catch { /* dossier inaccessible : on continue */ }

            // Fichiers.
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    if (ct.IsCancellationRequested) return;
                    string name = Path.GetFileName(file);
                    if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        string size = "";
                        string ext = Path.GetExtension(name).TrimStart('.').ToUpperInvariant();
                        try { size = HumanSize(new FileInfo(file).Length); } catch { }
                        onHit(new SearchHit(name, file, ext.Length > 0 ? $"Fichier {ext}" : "Fichier", size));
                        if (++found >= maxResults) return;
                    }
                }
            }
            catch { }
        }
    }

    private static string HumanSize(long bytes)
    {
        string[] units = { "o", "Ko", "Mo", "Go", "To" };
        double v = bytes; int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v:0.#} {units[u]}";
    }
}
