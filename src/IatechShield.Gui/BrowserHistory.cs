using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace IatechShield.Gui;

/// <summary>Une entrée d'historique de navigation (tous navigateurs confondus).</summary>
public sealed record HistoryEntry(string Browser, string Title, string Url, DateTime Visited)
{
    public string VisitedText => Visited.ToString("dd/MM/yyyy HH:mm");
    public string Host
    {
        get
        {
            try { return new Uri(Url).Host; } catch { return Url; }
        }
    }
}

/// <summary>
/// Lit l'historique de navigation directement dans les bases SQLite des navigateurs
/// installés (Chrome, Edge, Brave, Opera, Vivaldi = Chromium ; Firefox). Les fichiers
/// sont copiés dans un dossier temporaire avant lecture (la base est verrouillée quand
/// le navigateur est ouvert).
/// </summary>
public static class BrowserHistory
{
    // Époque Chromium : microsecondes depuis le 1er janvier 1601 (UTC).
    private static readonly DateTime ChromeEpoch = new(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    // Époque Firefox : microsecondes depuis le 1er janvier 1970 (UTC).
    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Lit jusqu'à <paramref name="limitPerBrowser"/> entrées récentes par profil.</summary>
    public static List<HistoryEntry> Read(int limitPerBrowser = 400)
    {
        var all = new List<HistoryEntry>();
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        // --- Navigateurs Chromium : dossiers « User Data » avec des profils. ---
        var chromiumRoots = new (string Name, string Path)[]
        {
            ("Chrome",  Path.Combine(local, "Google", "Chrome", "User Data")),
            ("Edge",    Path.Combine(local, "Microsoft", "Edge", "User Data")),
            ("Brave",   Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data")),
            ("Vivaldi", Path.Combine(local, "Vivaldi", "User Data")),
            ("Opera",   Path.Combine(roaming, "Opera Software", "Opera Stable")),
            ("Opera GX",Path.Combine(roaming, "Opera Software", "Opera GX Stable")),
        };
        foreach (var (name, root) in chromiumRoots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (string dbPath in FindChromiumHistoryFiles(root))
            {
                try { all.AddRange(ReadChromium(name, dbPath, limitPerBrowser)); }
                catch { /* profil illisible : on ignore et on continue */ }
            }
        }

        // --- Firefox : profils sous « Mozilla\Firefox\Profiles ». ---
        string ffProfiles = Path.Combine(roaming, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(ffProfiles))
        {
            foreach (string profile in SafeDirs(ffProfiles))
            {
                string db = Path.Combine(profile, "places.sqlite");
                if (!File.Exists(db)) continue;
                try { all.AddRange(ReadFirefox("Firefox", db, limitPerBrowser)); }
                catch { }
            }
        }

        // Tri décroissant par date + déduplication (même URL à la même minute).
        return all
            .OrderByDescending(e => e.Visited)
            .GroupBy(e => e.Browser + "|" + e.Url + "|" + e.Visited.ToString("yyyyMMddHHmm"))
            .Select(g => g.First())
            .ToList();
    }

    private static IEnumerable<string> FindChromiumHistoryFiles(string userDataRoot)
    {
        // Le fichier « History » se trouve dans chaque profil (Default, Profile 1, …).
        foreach (string dir in SafeDirs(userDataRoot))
        {
            string db = Path.Combine(dir, "History");
            if (File.Exists(db)) yield return db;
        }
        // Certains montages « Opera » stockent History à la racine.
        string rootDb = Path.Combine(userDataRoot, "History");
        if (File.Exists(rootDb)) yield return rootDb;
    }

    private static IEnumerable<string> SafeDirs(string root)
    {
        string[] dirs;
        try { dirs = Directory.GetDirectories(root); } catch { yield break; }
        foreach (var d in dirs) yield return d;
    }

    private static List<HistoryEntry> ReadChromium(string browser, string dbPath, int limit)
    {
        var result = new List<HistoryEntry>();
        string temp = CopyForRead(dbPath);
        try
        {
            using var cn = new SqliteConnection($"Data Source={temp};Mode=ReadOnly;Cache=Private");
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText =
                "SELECT url, title, last_visit_time FROM urls " +
                "WHERE last_visit_time > 0 ORDER BY last_visit_time DESC LIMIT $lim";
            cmd.Parameters.AddWithValue("$lim", limit);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string url = r.IsDBNull(0) ? "" : r.GetString(0);
                if (url.Length == 0) continue;
                string title = r.IsDBNull(1) ? "" : r.GetString(1);
                long t = r.IsDBNull(2) ? 0 : r.GetInt64(2);
                DateTime visited = t > 0 ? ChromeEpoch.AddTicks(t * 10).ToLocalTime() : DateTime.MinValue;
                result.Add(new HistoryEntry(browser, title, url, visited));
            }
        }
        finally { TryDelete(temp); }
        return result;
    }

    private static List<HistoryEntry> ReadFirefox(string browser, string dbPath, int limit)
    {
        var result = new List<HistoryEntry>();
        string temp = CopyForRead(dbPath);
        try
        {
            using var cn = new SqliteConnection($"Data Source={temp};Mode=ReadOnly;Cache=Private");
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText =
                "SELECT url, title, last_visit_date FROM moz_places " +
                "WHERE last_visit_date IS NOT NULL ORDER BY last_visit_date DESC LIMIT $lim";
            cmd.Parameters.AddWithValue("$lim", limit);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string url = r.IsDBNull(0) ? "" : r.GetString(0);
                if (url.Length == 0) continue;
                string title = r.IsDBNull(1) ? "" : r.GetString(1);
                long t = r.IsDBNull(2) ? 0 : r.GetInt64(2);
                DateTime visited = t > 0 ? UnixEpoch.AddTicks(t * 10).ToLocalTime() : DateTime.MinValue;
                result.Add(new HistoryEntry(browser, title, url, visited));
            }
        }
        finally { TryDelete(temp); }
        return result;
    }

    /// <summary>Copie la base (et ses journaux WAL/SHM) dans un fichier temporaire lisible.</summary>
    private static string CopyForRead(string dbPath)
    {
        string temp = Path.Combine(Path.GetTempPath(),
            "iatech_hist_" + Guid.NewGuid().ToString("N") + ".db");
        File.Copy(dbPath, temp, overwrite: true);
        foreach (string suffix in new[] { "-wal", "-shm" })
        {
            string src = dbPath + suffix;
            if (File.Exists(src))
            {
                try { File.Copy(src, temp + suffix, overwrite: true); } catch { }
            }
        }
        return temp;
    }

    private static void TryDelete(string path)
    {
        foreach (string p in new[] { path, path + "-wal", path + "-shm" })
        {
            try { if (File.Exists(p)) File.Delete(p); } catch { }
        }
    }
}
