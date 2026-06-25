using System.Diagnostics;
using System.Text.Json;

namespace IatechShield.Tools;

/// <summary>Un élément de l'empreinte système (catégorie, clé, valeur/hash).</summary>
public sealed record FingerprintItem(string Category, string Key, string Value);

/// <summary>Une différence détectée entre l'empreinte de référence et l'actuelle.</summary>
public sealed record FingerprintDiff(string Category, string Key, string Kind, string Before, string After);

/// <summary>
/// « Jumeau numérique » du PC + « ADN des logiciels ». Capture une empreinte
/// complète du système (applications + versions, services, pilotes, points de
/// démarrage, tâches planifiées, fichier hosts) et la compare à un état de
/// référence pour révéler toute modification anormale — même sans signature.
/// </summary>
public static class SystemFingerprint
{
    private static string BaselinePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IatechShield", "fingerprint.json");

    public static bool HasBaseline => File.Exists(BaselinePath);

    /// <summary>Capture l'empreinte courante du système.</summary>
    public static async Task<List<FingerprintItem>> CaptureAsync()
    {
        const string script =
            "$ErrorActionPreference='SilentlyContinue';" +
            // Applications (HKLM + HKCU) : nom|version => ADN logiciel
            "$paths=@('HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*'," +
            "'HKLM:\\SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*'," +
            "'HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*');" +
            "foreach($p in $paths){Get-ItemProperty $p | Where-Object {$_.DisplayName} | ForEach-Object {" +
            "Write-Output (\"APP`t{0}`t{1}\" -f $_.DisplayName, $_.DisplayVersion)}};" +
            // Services : nom|startmode|état
            "Get-CimInstance Win32_Service | ForEach-Object {" +
            "Write-Output (\"SERVICE`t{0}`t{1}/{2}\" -f $_.Name, $_.StartMode, $_.State)};" +
            // Pilotes : nom|état
            "Get-CimInstance Win32_SystemDriver | ForEach-Object {" +
            "Write-Output (\"DRIVER`t{0}`t{1}\" -f $_.Name, $_.State)};" +
            // Points de démarrage
            "Get-CimInstance Win32_StartupCommand | ForEach-Object {" +
            "Write-Output (\"STARTUP`t{0}`t{1}\" -f $_.Name, $_.Command)};" +
            // Tâches planifiées
            "Get-ScheduledTask | ForEach-Object {" +
            "Write-Output (\"TASK`t{0}`t{1}\" -f ($_.TaskPath+$_.TaskName), $_.State)};" +
            // Fichier hosts (hash) : cible classique des malwares
            "$h=Get-FileHash -Algorithm SHA256 \"$env:windir\\System32\\drivers\\etc\\hosts\";" +
            "if($h){Write-Output (\"FILE`thosts`t{0}\" -f $h.Hash)};";

        var items = new List<FingerprintItem>();
        try
        {
            string output = await RunPowerShellAsync(script);
            foreach (var line in output.Split('\n'))
            {
                var parts = line.TrimEnd('\r').Split('\t');
                if (parts.Length == 3 && parts[0].Length > 0 && parts[1].Length > 0)
                    items.Add(new FingerprintItem(parts[0], parts[1], parts[2]));
            }
        }
        catch { /* capture partielle tolérée */ }
        return items;
    }

    /// <summary>Compare deux empreintes et renvoie les différences (ajouts, suppressions, modifications).</summary>
    public static List<FingerprintDiff> Compare(List<FingerprintItem> baseline, List<FingerprintItem> current)
    {
        string K(FingerprintItem i) => $"{i.Category}{i.Key}";
        var baseMap = baseline.GroupBy(K).ToDictionary(g => g.Key, g => g.First());
        var curMap = current.GroupBy(K).ToDictionary(g => g.Key, g => g.First());

        var diffs = new List<FingerprintDiff>();
        foreach (var (k, cur) in curMap)
        {
            if (!baseMap.TryGetValue(k, out var b))
                diffs.Add(new FingerprintDiff(cur.Category, cur.Key, "Ajouté", "", cur.Value));
            else if (!string.Equals(b.Value, cur.Value, StringComparison.OrdinalIgnoreCase))
                diffs.Add(new FingerprintDiff(cur.Category, cur.Key, "Modifié", b.Value, cur.Value));
        }
        foreach (var (k, b) in baseMap)
            if (!curMap.ContainsKey(k))
                diffs.Add(new FingerprintDiff(b.Category, b.Key, "Supprimé", b.Value, ""));

        // Priorise les catégories sensibles (services/pilotes/démarrage/fichiers).
        static int Rank(string cat) => cat switch
        {
            "FILE" => 0, "DRIVER" => 1, "SERVICE" => 2, "STARTUP" => 3, "TASK" => 4, _ => 5
        };
        return diffs.OrderBy(d => Rank(d.Category)).ThenBy(d => d.Key).ToList();
    }

    public static void SaveBaseline(List<FingerprintItem> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BaselinePath)!);
        File.WriteAllText(BaselinePath, JsonSerializer.Serialize(items));
    }

    public static List<FingerprintItem> LoadBaseline()
    {
        try
        {
            return File.Exists(BaselinePath)
                ? JsonSerializer.Deserialize<List<FingerprintItem>>(File.ReadAllText(BaselinePath)) ?? new()
                : new();
        }
        catch { return new(); }
    }

    private static async Task<string> RunPowerShellAsync(string command)
    {
        var psi = new ProcessStartInfo("powershell",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        string output = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        return output;
    }
}
