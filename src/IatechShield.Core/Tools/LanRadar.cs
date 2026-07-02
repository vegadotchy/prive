using System.Collections.Concurrent;
using System.Net.Sockets;

namespace IatechShield.Tools;

/// <summary>Type d'appareil déduit des ports ouverts et du nom.</summary>
public enum LanDeviceType { Inconnu, Routeur, CameraIp, Nas, Imprimante, Ordinateur, Objet, Telephone }

/// <summary>Un port TCP ouvert sur un appareil.</summary>
public sealed record RadarPort(int Number, string Service);

/// <summary>Un appareil vu par le radar, enrichi (ports, type, risques).</summary>
public sealed record RadarDevice(
    string Ip,
    string Name,
    string Mac,
    LanDeviceType Type,
    IReadOnlyList<RadarPort> OpenPorts,
    IReadOnlyList<string> Risks);

/// <summary>
/// Radar du réseau local : découvre les appareils, scanne leurs ports les plus
/// courants, déduit leur type (routeur, caméra IP, NAS, imprimante, objet
/// connecté…) et signale les risques (Telnet/FTP en clair, RDP/SMB exposés,
/// interface d'administration par défaut…).
/// </summary>
public sealed class LanRadar
{
    private static readonly (int Port, string Service)[] CommonPorts =
    {
        (21, "FTP"), (22, "SSH"), (23, "Telnet"), (53, "DNS"), (80, "HTTP"),
        (139, "NetBIOS"), (443, "HTTPS"), (445, "SMB"), (515, "Imprimante"),
        (554, "RTSP"), (631, "IPP"), (1883, "MQTT"), (3389, "RDP"),
        (5000, "UPnP/NAS"), (8080, "HTTP-alt"), (8443, "HTTPS-alt"),
        (9100, "Imprimante"), (32400, "Plex"), (37777, "DVR/Caméra"), (62078, "iPhone"),
    };

    private readonly NetworkScanner _discovery = new();

    public async Task<IReadOnlyList<RadarDevice>> ScanAsync(
        Action<string>? progress = null, CancellationToken cancel = default)
    {
        progress?.Invoke("Découverte des appareils…");
        var devices = await _discovery.ScanAsync(progress, cancel);

        var result = new List<RadarDevice>();
        int done = 0;
        foreach (var dev in devices)
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Invoke($"Analyse des ports… {++done}/{devices.Count} ({dev.Ip})");

            var ports = await ScanPortsAsync(dev.Ip, cancel);
            var type = Classify(dev.Name, dev.Ip, ports);
            var risks = AssessRisks(type, ports);
            result.Add(new RadarDevice(dev.Ip, dev.Name, dev.Mac, type, ports, risks));
        }

        progress?.Invoke($"{result.Count} appareil(s) — radar terminé.");
        return result;
    }

    private static async Task<List<RadarPort>> ScanPortsAsync(string ip, CancellationToken cancel)
    {
        var open = new ConcurrentBag<RadarPort>();
        using var gate = new SemaphoreSlim(16);
        var tasks = CommonPorts.Select(async p =>
        {
            await gate.WaitAsync(cancel);
            try
            {
                using var client = new TcpClient();
                var connect = client.ConnectAsync(ip, p.Port);
                var finished = await Task.WhenAny(connect, Task.Delay(450, cancel));
                if (finished == connect && client.Connected)
                    open.Add(new RadarPort(p.Port, p.Service));
            }
            catch { /* port fermé / filtré */ }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);
        return open.OrderBy(p => p.Number).ToList();
    }

    private static LanDeviceType Classify(string name, string ip, List<RadarPort> ports)
    {
        string n = name.ToLowerInvariant();
        bool Has(int port) => ports.Any(p => p.Number == port);
        bool lastOctetGateway = ip.EndsWith(".1") || ip.EndsWith(".254");

        if (n.Contains("synology") || n.Contains("qnap") || n.Contains("nas") ||
            (Has(5000) && (Has(445) || Has(139)))) return LanDeviceType.Nas;
        if (Has(554) || Has(37777) || n.Contains("cam") || n.Contains("ipc")) return LanDeviceType.CameraIp;
        if (Has(631) || Has(9100) || Has(515) || n.Contains("printer") || n.Contains("hp") || n.Contains("epson"))
            return LanDeviceType.Imprimante;
        if (Has(62078) || n.Contains("iphone") || n.Contains("android") || n.Contains("phone")) return LanDeviceType.Telephone;
        if (lastOctetGateway && (Has(80) || Has(443))) return LanDeviceType.Routeur;
        if (Has(3389) || Has(445) || Has(139)) return LanDeviceType.Ordinateur;
        if (Has(1883) || Has(80)) return LanDeviceType.Objet;
        return LanDeviceType.Inconnu;
    }

    private static List<string> AssessRisks(LanDeviceType type, List<RadarPort> ports)
    {
        var risks = new List<string>();
        bool Has(int port) => ports.Any(p => p.Number == port);

        if (Has(23)) risks.Add("Telnet (23) ouvert — protocole non chiffré, très exposé.");
        if (Has(21)) risks.Add("FTP (21) ouvert — identifiants en clair.");
        if (Has(3389)) risks.Add("Bureau à distance (RDP) exposé sur le réseau.");
        if (Has(445)) risks.Add("Partage de fichiers SMB (445) exposé.");
        if (type == LanDeviceType.CameraIp && (Has(80) || Has(554)))
            risks.Add("Caméra IP accessible — changez le mot de passe par défaut et coupez l'accès Internet.");
        if (type == LanDeviceType.Nas && Has(80))
            risks.Add("Interface d'administration du NAS exposée — activez HTTPS et le 2FA.");
        if (type == LanDeviceType.Objet && Has(80))
            risks.Add("Objet connecté avec interface web — vérifiez les mises à jour du firmware.");
        return risks;
    }

    public static string TypeLabel(LanDeviceType t) => t switch
    {
        LanDeviceType.Routeur => "Routeur / box",
        LanDeviceType.CameraIp => "Caméra IP",
        LanDeviceType.Nas => "NAS / stockage",
        LanDeviceType.Imprimante => "Imprimante",
        LanDeviceType.Ordinateur => "Ordinateur",
        LanDeviceType.Objet => "Objet connecté",
        LanDeviceType.Telephone => "Téléphone",
        _ => "Inconnu"
    };
}
