using System.Diagnostics;
using System.Text;

namespace IatechShield.Tools;

/// <summary>
/// Mode « Panic » : réponse d'urgence en un clic. Coupe le réseau, bloque les
/// périphériques USB de stockage, arrête les processus suspects (lancés depuis
/// les dossiers temporaires/Téléchargements) et verrouille la session.
/// Toutes les actions sont réversibles via <see cref="DisengageAsync"/>.
/// </summary>
public static class PanicMode
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    public static async Task<string> EngageAsync(bool killSuspicious = true)
    {
        var log = new StringBuilder();

        // 1) Couper toutes les cartes réseau.
        await PsAsync("Disable-NetAdapter -Name '*' -Confirm:$false", log, "Réseau coupé");

        // 2) Bloquer les périphériques de stockage USB (USBSTOR Start=4).
        await PsAsync(
            "Set-ItemProperty 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\USBSTOR' -Name Start -Value 4",
            log, "Stockage USB bloqué");

        // 3) Arrêter les processus suspects (dossiers temporaires / Téléchargements).
        if (killSuspicious)
            log.AppendLine(KillSuspiciousProcesses());

        // 4) Verrouiller la session.
        try { LockWorkStation(); log.AppendLine("• Session verrouillée"); }
        catch { /* best-effort */ }

        return log.ToString();
    }

    public static async Task<string> DisengageAsync()
    {
        var log = new StringBuilder();
        await PsAsync("Enable-NetAdapter -Name '*' -Confirm:$false", log, "Réseau rétabli");
        await PsAsync(
            "Set-ItemProperty 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\USBSTOR' -Name Start -Value 3",
            log, "Stockage USB rétabli");
        return log.ToString();
    }

    private static string KillSuspiciousProcesses()
    {
        string temp = Path.GetTempPath().TrimEnd('\\');
        string downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        string[] watched = { temp, downloads };

        int killed = 0;
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                string? path = proc.MainModule?.FileName;
                if (path is null) continue;
                if (watched.Any(w => path.StartsWith(w, StringComparison.OrdinalIgnoreCase)))
                {
                    proc.Kill(entireProcessTree: true);
                    killed++;
                }
            }
            catch { /* processus système / accès refusé : ignoré */ }
        }
        return $"• Processus suspects arrêtés : {killed}";
    }

    private static async Task PsAsync(string command, StringBuilder log, string okLabel)
    {
        try
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
            string err = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            log.AppendLine(p.ExitCode == 0 ? $"• {okLabel}" : $"• {okLabel} — échec ({err.Trim()})");
        }
        catch (Exception ex)
        {
            log.AppendLine($"• {okLabel} — erreur : {ex.Message}");
        }
    }
}
