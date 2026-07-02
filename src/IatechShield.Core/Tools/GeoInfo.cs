using System.Net.Http;
using System.Text.Json;

namespace IatechShield.Tools;

/// <summary>Informations de géolocalisation de l'adresse IP publique.</summary>
public sealed record GeoResult(string Ip, string Country, string Region, string City, string Isp);

/// <summary>
/// Récupère l'IP publique vue depuis Internet et son pays/opérateur. Sous VPN,
/// cela reflète le point de sortie (utile pour confirmer le pays de connexion).
/// Utilise un service public gratuit ; échoue proprement hors-ligne.
/// </summary>
public static class GeoInfo
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public static async Task<GeoResult?> LookupAsync(CancellationToken cancel = default)
    {
        try
        {
            using var stream = await Http.GetStreamAsync(
                "http://ip-api.com/json/?fields=status,country,regionName,city,isp,query", cancel);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancel);
            var root = doc.RootElement;
            if (root.TryGetProperty("status", out var st) && st.GetString() != "success")
                return null;

            string Get(string k) => root.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";
            return new GeoResult(Get("query"), Get("country"), Get("regionName"), Get("city"), Get("isp"));
        }
        catch
        {
            return null;
        }
    }
}
