using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace IatechShield.Tools;

/// <summary>Un point de connexion géolocalisé (serveur distant contacté par le PC).</summary>
public sealed record GeoConnection(string Ip, string Country, string City, double Lat, double Lon, int Count);

/// <summary>Position « maison » (IP publique de l'utilisateur) sur la carte.</summary>
public sealed record GeoHome(string Country, string City, double Lat, double Lon);

/// <summary>Synthèse pour la carte mondiale : position locale + connexions distantes.</summary>
public sealed record WorldMapData(GeoHome? Home, IReadOnlyList<GeoConnection> Connections);

/// <summary>
/// Cartographie les connexions réseau réelles du PC : récupère les connexions TCP
/// actives, garde les adresses publiques distinctes, puis les géolocalise en un seul
/// appel groupé (ip-api.com). Échoue proprement hors-ligne (renvoie une liste vide).
/// </summary>
public static class WorldConnections
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public static async Task<WorldMapData> GetAsync(CancellationToken cancel = default)
    {
        var remote = CollectPublicRemoteIps();
        GeoHome? home = await LookupHomeAsync(cancel);

        if (remote.Count == 0)
            return new WorldMapData(home, Array.Empty<GeoConnection>());

        var connections = await GeolocateBatchAsync(remote, cancel);
        return new WorldMapData(home, connections);
    }

    /// <summary>IP publiques distinctes contactées (avec le nombre de connexions).</summary>
    private static Dictionary<string, int> CollectPublicRemoteIps()
    {
        var map = new Dictionary<string, int>();
        try
        {
            var conns = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections();
            foreach (var c in conns)
            {
                var ep = c.RemoteEndPoint;
                if (ep is null) continue;
                if (ep.AddressFamily != AddressFamily.InterNetwork) continue; // IPv4
                if (IsPrivate(ep.Address)) continue;
                string ip = ep.Address.ToString();
                map[ip] = map.TryGetValue(ip, out int n) ? n + 1 : 1;
            }
        }
        catch { /* indisponible : liste vide */ }
        return map;
    }

    private static bool IsPrivate(IPAddress addr)
    {
        byte[] b = addr.GetAddressBytes();
        if (b.Length != 4) return true;
        if (b[0] == 10) return true;                         // 10/8
        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true; // 172.16/12
        if (b[0] == 192 && b[1] == 168) return true;         // 192.168/16
        if (b[0] == 127) return true;                        // loopback
        if (b[0] == 169 && b[1] == 254) return true;         // link-local
        if (b[0] == 0) return true;
        if (b[0] >= 224) return true;                        // multicast/réservé
        return false;
    }

    private static async Task<GeoHome?> LookupHomeAsync(CancellationToken cancel)
    {
        try
        {
            string json = await Http.GetStringAsync(
                "http://ip-api.com/json/?fields=status,country,city,lat,lon", cancel);
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.GetProperty("status").GetString() != "success") return null;
            return new GeoHome(
                Str(r, "country"), Str(r, "city"),
                Num(r, "lat"), Num(r, "lon"));
        }
        catch { return null; }
    }

    private static async Task<List<GeoConnection>> GeolocateBatchAsync(Dictionary<string, int> ips, CancellationToken cancel)
    {
        var result = new List<GeoConnection>();
        try
        {
            // ip-api : 100 max par lot. On en prend au plus 60.
            var list = ips.Keys.Take(60).ToList();
            string body = JsonSerializer.Serialize(list);
            using var req = new HttpRequestMessage(HttpMethod.Post,
                "http://ip-api.com/batch?fields=status,country,city,lat,lon,query")
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            using var resp = await Http.SendAsync(req, cancel);
            if (!resp.IsSuccessStatusCode) return result;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancel));
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.TryGetProperty("status", out var st) && st.GetString() != "success") continue;
                string ip = Str(e, "query");
                result.Add(new GeoConnection(
                    ip, Str(e, "country"), Str(e, "city"),
                    Num(e, "lat"), Num(e, "lon"),
                    ips.TryGetValue(ip, out int n) ? n : 1));
            }
        }
        catch { /* hors-ligne / quota : ce qu'on a déjà */ }
        return result;
    }

    private static string Str(JsonElement e, string k)
        => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double Num(JsonElement e, string k)
        => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}
