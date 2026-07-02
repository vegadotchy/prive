using System.Diagnostics;
using System.Text;

namespace IatechShield.Tools;

/// <summary>Type de tunnel VPN (correspond aux options du serveur VPN Synology).</summary>
public enum VpnType { L2tp, Sstp, Pptp, Automatic }

/// <summary>État courant d'une connexion VPN.</summary>
public sealed record VpnState(bool Connected, string? AssignedIp, string Message);

/// <summary>Paramètres d'une connexion VPN.</summary>
public sealed class VpnProfile
{
    public string Name { get; set; } = "IATECH-SHIELD VPN";
    public string Server { get; set; } = "";
    public VpnType Type { get; set; } = VpnType.L2tp;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    /// <summary>Clé pré-partagée (L2TP/IPSec uniquement).</summary>
    public string PreSharedKey { get; set; } = "";
}

/// <summary>
/// Pilote le client VPN intégré à Windows (RAS) via PowerShell + rasdial.
/// Compatible avec le serveur VPN d'un NAS Synology (L2TP/IPSec, SSTP, PPTP) :
/// aucun logiciel tiers à installer. Crée/met à jour un profil VPN nommé puis
/// le connecte/déconnecte.
/// </summary>
public sealed class VpnManager
{
    /// <summary>Crée ou met à jour le profil VPN Windows, puis établit la connexion.</summary>
    public async Task<VpnState> ConnectAsync(VpnProfile profile, CancellationToken cancel = default)
    {
        if (string.IsNullOrWhiteSpace(profile.Server))
            return new VpnState(false, null, "Adresse du serveur manquante.");

        // 1) (Ré)enregistre le profil avec PowerShell (Add-VpnConnection).
        string ps = BuildProvisionScript(profile);
        var prov = await RunAsync("powershell",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"{ps}\"", cancel);
        if (prov.ExitCode != 0)
            return new VpnState(false, null, $"Échec de configuration : {Short(prov.Error)}");

        // 2) Connexion via rasdial (gère l'invite d'identifiants).
        string args = $"\"{profile.Name}\" {Quote(profile.Username)} {Quote(profile.Password)}";
        var dial = await RunAsync("rasdial", args, cancel);
        if (dial.ExitCode != 0)
            return new VpnState(false, null, $"Connexion refusée : {Short(dial.Output + dial.Error)}");

        string? ip = ExtractAssignedIp(dial.Output);
        return new VpnState(true, ip, "Connecté.");
    }

    /// <summary>Ferme la connexion VPN.</summary>
    public async Task<VpnState> DisconnectAsync(string name = "IATECH-SHIELD VPN", CancellationToken cancel = default)
    {
        var res = await RunAsync("rasdial", $"\"{name}\" /disconnect", cancel);
        return new VpnState(false, null, res.ExitCode == 0 ? "Déconnecté." : "Aucune connexion active.");
    }

    /// <summary>Indique si le profil est actuellement connecté (via rasdial sans argument).</summary>
    public async Task<bool> IsConnectedAsync(string name = "IATECH-SHIELD VPN", CancellationToken cancel = default)
    {
        var res = await RunAsync("rasdial", "", cancel);
        return res.Output.Contains(name, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildProvisionScript(VpnProfile p)
    {
        string tunnel = p.Type switch
        {
            VpnType.L2tp => "L2tp",
            VpnType.Sstp => "Sstp",
            VpnType.Pptp => "Pptp",
            _ => "Automatic"
        };

        var sb = new StringBuilder();
        // Recrée proprement le profil à chaque connexion (idempotent).
        sb.Append($"Remove-VpnConnection -Name '{Esc(p.Name)}' -Force -ErrorAction SilentlyContinue; ");
        sb.Append($"Add-VpnConnection -Name '{Esc(p.Name)}' -ServerAddress '{Esc(p.Server)}' ");
        sb.Append($"-TunnelType {tunnel} -AuthenticationMethod MSChapv2 ");
        sb.Append("-EncryptionLevel Required -RememberCredential -Force ");
        if (p.Type == VpnType.L2tp && !string.IsNullOrEmpty(p.PreSharedKey))
            sb.Append($"-L2tpPsk '{Esc(p.PreSharedKey)}' ");
        sb.Append("-PassThru | Out-Null");
        return sb.ToString();
    }

    private static string? ExtractAssignedIp(string rasdialOutput)
    {
        foreach (var line in rasdialOutput.Split('\n'))
        {
            string l = line.Trim();
            // rasdial n'affiche pas toujours l'IP ; on tente une heuristique simple.
            if (l.Count(c => c == '.') == 3 && l.Any(char.IsDigit) &&
                System.Net.IPAddress.TryParse(l, out var ip))
                return ip.ToString();
        }
        return null;
    }

    private static string Esc(string s) => s.Replace("'", "''");
    private static string Quote(string s) => string.IsNullOrEmpty(s) ? "\"\"" : $"\"{s.Replace("\"", "")}\"";
    private static string Short(string s) => s.Length > 200 ? s[..200].Trim() : s.Trim();

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string file, string args, CancellationToken cancel)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        string output = await proc.StandardOutput.ReadToEndAsync(cancel);
        string error = await proc.StandardError.ReadToEndAsync(cancel);
        await proc.WaitForExitAsync(cancel);
        return (proc.ExitCode, output, error);
    }
}
