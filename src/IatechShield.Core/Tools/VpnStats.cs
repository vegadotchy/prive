using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace IatechShield.Tools;

/// <summary>Mesure instantanée de l'adaptateur VPN.</summary>
public sealed record VpnLiveStats(bool Up, string AdapterName, string LocalIp, long BytesIn, long BytesOut);

/// <summary>
/// Lit en direct l'état et le trafic de l'interface VPN (adaptateur PPP créé par
/// le client VPN Windows). Permet d'afficher débit entrant/sortant et volume.
/// </summary>
public static class VpnStats
{
    public static VpnLiveStats? Read(string profileName)
    {
        NetworkInterface? best = null;
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;

            bool nameMatch = ni.Name.Contains(profileName, StringComparison.OrdinalIgnoreCase)
                          || ni.Description.Contains(profileName, StringComparison.OrdinalIgnoreCase);
            bool typeMatch = ni.NetworkInterfaceType == NetworkInterfaceType.Ppp;
            bool descMatch = ni.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase)
                          || ni.Description.Contains("WAN Miniport", StringComparison.OrdinalIgnoreCase)
                          || ni.Description.Contains("agile", StringComparison.OrdinalIgnoreCase);

            if (nameMatch) { best = ni; break; }      // priorité au nom du profil
            if (typeMatch || descMatch) best ??= ni;   // sinon, premier candidat VPN/PPP
        }

        if (best is null) return null;

        var stats = best.GetIPv4Statistics();
        string ip = best.GetIPProperties().UnicastAddresses
            .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "—";

        return new VpnLiveStats(true, best.Name, ip, stats.BytesReceived, stats.BytesSent);
    }

    /// <summary>Formate un débit en o/s vers une unité lisible (Ko/s, Mo/s).</summary>
    public static string FormatRate(double bytesPerSec)
    {
        if (bytesPerSec >= 1024 * 1024) return $"{bytesPerSec / (1024 * 1024):0.0} Mo/s";
        if (bytesPerSec >= 1024) return $"{bytesPerSec / 1024:0.0} Ko/s";
        return $"{bytesPerSec:0} o/s";
    }

    /// <summary>Formate un volume d'octets vers une unité lisible.</summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):0.00} Go";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.0} Mo";
        if (bytes >= 1024) return $"{bytes / 1024.0:0.0} Ko";
        return $"{bytes} o";
    }
}
