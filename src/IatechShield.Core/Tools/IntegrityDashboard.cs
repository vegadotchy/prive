using System.Diagnostics;

namespace IatechShield.Tools;

/// <summary>Un pilier de santé du PC (score 0-100, ou -1 = non disponible).</summary>
public sealed record HealthPillar(string Name, int Score, string Detail)
{
    public bool Available => Score >= 0;
}

/// <summary>Synthèse de l'état d'intégrité du PC (plusieurs piliers).</summary>
public sealed record IntegrityReport(IReadOnlyList<HealthPillar> Pillars)
{
    /// <summary>Moyenne des piliers disponibles.</summary>
    public int Overall
    {
        get
        {
            var ok = Pillars.Where(p => p.Available).ToList();
            return ok.Count == 0 ? 0 : (int)Math.Round(ok.Average(p => p.Score));
        }
    }
}

/// <summary>
/// Tableau de bord d'intégrité du PC : agrège en temps réel des indicateurs de
/// Sécurité, Performances, Confidentialité, Vulnérabilités, Température,
/// Santé du SSD et Batterie à partir de sources Windows réelles (WMI/CIM).
/// </summary>
public static class IntegrityDashboard
{
    public static async Task<IntegrityReport> EvaluateAsync(int securityScore)
    {
        var raw = await ProbeAsync();
        var pillars = new List<HealthPillar>
        {
            new("Sécurité", securityScore, $"{securityScore}/100 (voir Centre de sécurité)"),
            Performance(raw),
            Privacy(raw),
            Vulnerabilities(raw),
            Temperature(raw),
            SsdHealth(raw),
            Battery(raw),
        };
        return new IntegrityReport(pillars);
    }

    private static HealthPillar Performance(Dictionary<string, string> r)
    {
        double cpu = Num(r, "cpu", 0);
        double ramFree = Num(r, "ramfree", 50);
        int score = (int)Math.Clamp(Math.Round(((100 - cpu) + ramFree) / 2.0), 0, 100);
        return new HealthPillar("Performances", score, $"CPU {cpu:0}% · RAM libre {ramFree:0}%");
    }

    private static HealthPillar Privacy(Dictionary<string, string> r)
    {
        bool adId = r.GetValueOrDefault("advid") == "1";
        int score = adId ? 65 : 95;
        return new HealthPillar("Confidentialité", score,
            adId ? "ID de publicité activé — désactivable" : "ID de publicité désactivé");
    }

    private static HealthPillar Vulnerabilities(Dictionary<string, string> r)
    {
        bool reboot = r.GetValueOrDefault("reboot").Equals("True", StringComparison.OrdinalIgnoreCase);
        double sigAge = Num(r, "sigage", 0);
        int score = 100;
        if (reboot) score -= 35;
        if (sigAge >= 7) score -= 25;
        score = Math.Clamp(score, 0, 100);
        string detail = reboot ? "Redémarrage de sécurité en attente" :
            sigAge >= 7 ? "Définitions antivirus anciennes" : "Aucune vulnérabilité système évidente";
        return new HealthPillar("Vulnérabilités", score, detail);
    }

    private static HealthPillar Temperature(Dictionary<string, string> r)
    {
        if (!r.TryGetValue("temp", out var t) || !double.TryParse(t, out double tenthK) || tenthK <= 0)
            return new HealthPillar("Température", -1, "Capteur non disponible");
        double c = tenthK / 10.0 - 273.15;
        int score = (int)Math.Clamp(Math.Round(110 - c * 1.1), 0, 100); // ~40°C≈66, ~70°C≈33
        return new HealthPillar("Température", score, $"{c:0} °C");
    }

    private static HealthPillar SsdHealth(Dictionary<string, string> r)
    {
        string s = r.GetValueOrDefault("ssd", "");
        if (string.IsNullOrEmpty(s)) return new HealthPillar("Santé du SSD", -1, "Non disponible");
        bool healthy = s.Equals("Healthy", StringComparison.OrdinalIgnoreCase) || s == "0";
        return new HealthPillar("Santé du SSD", healthy ? 100 : 40,
            healthy ? "Disque sain" : $"État : {s}");
    }

    private static HealthPillar Battery(Dictionary<string, string> r)
    {
        if (!r.TryGetValue("battery", out var b) || !double.TryParse(b, out double pct) || pct < 0)
            return new HealthPillar("Batterie", -1, "PC fixe / non détectée");
        return new HealthPillar("Batterie", (int)Math.Clamp(pct, 0, 100), $"{pct:0} % de charge");
    }

    private static double Num(Dictionary<string, string> r, string key, double fallback)
        => r.TryGetValue(key, out var v) && double.TryParse(v, out double d) ? d : fallback;

    private static async Task<Dictionary<string, string>> ProbeAsync()
    {
        const string script =
            "$ErrorActionPreference='SilentlyContinue';" +
            "$cpu=(Get-CimInstance Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average; Write-Output \"cpu=$cpu\";" +
            "$os=Get-CimInstance Win32_OperatingSystem;" +
            "if($os){$rf=[math]::Round($os.FreePhysicalMemory/$os.TotalVisibleMemorySize*100); Write-Output \"ramfree=$rf\"};" +
            "$ssd=(Get-PhysicalDisk | Select-Object -First 1).HealthStatus; Write-Output \"ssd=$ssd\";" +
            "$bat=(Get-CimInstance Win32_Battery | Select-Object -First 1).EstimatedChargeRemaining; Write-Output \"battery=$bat\";" +
            "$tz=(Get-CimInstance -Namespace root/wmi MSAcpi_ThermalZoneTemperature | Select-Object -First 1).CurrentTemperature; Write-Output \"temp=$tz\";" +
            "$adv=(Get-ItemProperty 'HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo').Enabled; Write-Output \"advid=$adv\";" +
            "$rb=Test-Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\WindowsUpdate\\Auto Update\\RebootRequired'; Write-Output \"reboot=$rb\";" +
            "$sa=(Get-MpComputerStatus).AntivirusSignatureAge; Write-Output \"sigage=$sa\";";

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var psi = new ProcessStartInfo("powershell",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            string output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            foreach (var line in output.Split('\n'))
            {
                var kv = line.Trim().Split('=', 2);
                if (kv.Length == 2) dict[kv[0].Trim()] = kv[1].Trim();
            }
        }
        catch { /* indicateurs manquants → piliers en N/A */ }
        return dict;
    }
}
