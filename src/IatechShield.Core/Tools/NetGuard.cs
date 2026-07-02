using System.Diagnostics;

namespace IatechShield.Tools;

/// <summary>
/// Isole un appareil du réseau local depuis ce PC en créant des règles de
/// pare-feu Windows (entrée + sortie) bloquant son adresse IP. Réversible.
/// </summary>
public static class NetGuard
{
    private static string RuleName(string ip) => $"IATECH-SHIELD Block {ip}";

    public static async Task<bool> BlockIpAsync(string ip)
    {
        string name = RuleName(ip);
        bool ok1 = await NetshAsync(
            $"advfirewall firewall add rule name=\"{name}\" dir=out action=block remoteip={ip}");
        bool ok2 = await NetshAsync(
            $"advfirewall firewall add rule name=\"{name}\" dir=in action=block remoteip={ip}");
        return ok1 || ok2;
    }

    public static async Task<bool> UnblockIpAsync(string ip)
    {
        return await NetshAsync($"advfirewall firewall delete rule name=\"{RuleName(ip)}\"");
    }

    private static async Task<bool> NetshAsync(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
