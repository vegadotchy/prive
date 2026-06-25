using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace IatechShield.Tools;

/// <summary>Un appareil découvert sur le réseau local.</summary>
public sealed record NetworkDevice(string Ip, string Name, string Mac);

/// <summary>
/// Découverte des appareils du réseau local (façon « Fing ») : balayage ping du
/// sous-réseau /24, résolution des noms (DNS inverse) et des adresses MAC (ARP).
/// </summary>
public sealed class NetworkScanner
{
    /// <summary>
    /// Analyse le réseau local et renvoie les appareils répondant au ping.
    /// </summary>
    public async Task<IReadOnlyList<NetworkDevice>> ScanAsync(
        Action<string>? progress = null, CancellationToken cancel = default)
    {
        string? localIp = GetLocalIPv4();
        if (localIp is null)
            return Array.Empty<NetworkDevice>();

        string prefix = localIp[..(localIp.LastIndexOf('.') + 1)]; // ex: "192.168.1."
        progress?.Invoke($"Sous-réseau {prefix}0/24 — balayage…");

        var alive = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var gate = new SemaphoreSlim(64);

        var tasks = new List<Task>();
        for (int i = 1; i <= 254; i++)
        {
            string ip = prefix + i;
            tasks.Add(PingOne(ip, alive, gate, cancel));
        }
        await Task.WhenAll(tasks);

        // Table ARP (IP -> MAC) une seule fois après le balayage.
        var arp = ReadArpTable();

        var devices = new List<NetworkDevice>();
        foreach (string ip in alive)
        {
            string name = await ResolveNameAsync(ip);
            arp.TryGetValue(ip, out string? mac);
            devices.Add(new NetworkDevice(ip, name, mac ?? "—"));
        }

        devices.Sort((a, b) => CompareIp(a.Ip, b.Ip));
        progress?.Invoke($"{devices.Count} appareil(s) trouvé(s).");
        return devices;
    }

    private static async Task PingOne(string ip, System.Collections.Concurrent.ConcurrentBag<string> alive,
                                      SemaphoreSlim gate, CancellationToken cancel)
    {
        await gate.WaitAsync(cancel);
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, 600);
            if (reply.Status == IPStatus.Success)
                alive.Add(ip);
        }
        catch { /* hôte injoignable */ }
        finally { gate.Release(); }
    }

    private static async Task<string> ResolveNameAsync(string ip)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(ip);
            return string.IsNullOrWhiteSpace(entry.HostName) ? "(inconnu)" : entry.HostName;
        }
        catch
        {
            return "(inconnu)";
        }
    }

    private static string? GetLocalIPv4()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up ||
                ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            foreach (var addr in ni.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(addr.Address))
                    return addr.Address.ToString();
            }
        }
        return null;
    }

    private static Dictionary<string, string> ReadArpTable()
    {
        var map = new Dictionary<string, string>();
        try
        {
            var psi = new ProcessStartInfo("arp", "-a")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return map;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            // Lignes type : "  192.168.1.10     aa-bb-cc-dd-ee-ff     dynamique"
            foreach (Match m in Regex.Matches(output,
                @"(\d+\.\d+\.\d+\.\d+)\s+([0-9a-fA-F]{2}(?:[-:][0-9a-fA-F]{2}){5})"))
            {
                map[m.Groups[1].Value] = m.Groups[2].Value.ToUpperInvariant();
            }
        }
        catch { /* arp indisponible */ }
        return map;
    }

    private static int CompareIp(string a, string b)
    {
        int la = int.TryParse(a[(a.LastIndexOf('.') + 1)..], out int x) ? x : 0;
        int lb = int.TryParse(b[(b.LastIndexOf('.') + 1)..], out int y) ? y : 0;
        return la.CompareTo(lb);
    }
}
