using System.Diagnostics;

namespace IatechShield.Tools;

/// <summary>Un critère évalué pour le score de sécurité.</summary>
public sealed record SecurityCheck(string Name, bool Passed, int Weight, string Recommendation);

/// <summary>Résultat global du score de sécurité (note /100 + critères).</summary>
public sealed record SecurityScoreResult(int Score, IReadOnlyList<SecurityCheck> Checks)
{
    public string Grade => Score switch
    {
        >= 90 => "Excellent",
        >= 75 => "Bon",
        >= 50 => "Moyen",
        _ => "À renforcer"
    };
}

/// <summary>
/// Calcule une note de sécurité du PC sur 100 à partir de vérifications réelles
/// (Defender, pare-feu, BitLocker, UAC, mises à jour des définitions…) combinées
/// à l'état propre de l'application (temps réel, protection par mot de passe).
/// </summary>
public static class SecurityScore
{
    public static async Task<SecurityScoreResult> EvaluateAsync(bool appRealtimeOn, bool tamperSet)
    {
        var sys = await ProbeSystemAsync();

        var checks = new List<SecurityCheck>
        {
            new("Protection en temps réel (IATECH-SHIELD)", appRealtimeOn, 18,
                "Activez la surveillance en temps réel dans l'onglet Protection."),
            new("Antivirus Windows Defender actif", sys.GetValueOrDefault("defender"), 16,
                "Activez la protection en temps réel de Windows Defender."),
            new("Pare-feu activé", sys.GetValueOrDefault("firewall"), 16,
                "Activez le pare-feu Windows (au moins un profil)."),
            new("Définitions antivirus à jour", sys.GetValueOrDefault("sigs"), 12,
                "Mettez à jour les définitions antivirus (moins de 7 jours)."),
            new("Chiffrement du disque (BitLocker)", sys.GetValueOrDefault("bitlocker"), 12,
                "Activez BitLocker sur le disque système pour protéger vos données."),
            new("Contrôle de compte (UAC) activé", sys.GetValueOrDefault("uac"), 10,
                "Réactivez l'UAC pour bloquer les modifications non autorisées."),
            new("Protection par mot de passe de l'app", tamperSet, 8,
                "Définissez un mot de passe de protection (Réglages)."),
            new("Pas de redémarrage en attente", sys.GetValueOrDefault("noreboot", true), 8,
                "Redémarrez pour appliquer les mises à jour de sécurité en attente."),
        };

        int max = checks.Sum(c => c.Weight);
        int got = checks.Where(c => c.Passed).Sum(c => c.Weight);
        int score = max == 0 ? 0 : (int)Math.Round(got * 100.0 / max);
        return new SecurityScoreResult(score, checks);
    }

    private static async Task<Dictionary<string, bool>> ProbeSystemAsync()
    {
        // Un seul appel PowerShell renvoie « clé=valeur » par ligne.
        const string script =
            "$ErrorActionPreference='SilentlyContinue';" +
            "$d=Get-MpComputerStatus;" +
            "Write-Output ('defender=' + [bool]$d.RealTimeProtectionEnabled);" +
            "Write-Output ('sigs=' + ($d.AntivirusSignatureAge -ne $null -and $d.AntivirusSignatureAge -lt 7));" +
            "$fw=(Get-NetFirewallProfile | Where-Object {$_.Enabled -eq 'True'}).Count;" +
            "Write-Output ('firewall=' + ($fw -ge 1));" +
            "$bl=(Get-BitLockerVolume -MountPoint $env:SystemDrive).ProtectionStatus;" +
            "Write-Output ('bitlocker=' + ($bl -eq 'On' -or $bl -eq 1));" +
            "$uac=(Get-ItemProperty 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System').EnableLUA;" +
            "Write-Output ('uac=' + ($uac -eq 1));" +
            "$rb=Test-Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\WindowsUpdate\\Auto Update\\RebootRequired';" +
            "Write-Output ('noreboot=' + (-not $rb));";

        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string output = await RunPowerShellAsync(script);
            foreach (var line in output.Split('\n'))
            {
                var parts = line.Trim().Split('=', 2);
                if (parts.Length == 2 && bool.TryParse(parts[1].Trim(), out bool v))
                    result[parts[0].Trim()] = v;
            }
        }
        catch { /* en cas d'échec, les critères manquants comptent comme non validés */ }
        return result;
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
