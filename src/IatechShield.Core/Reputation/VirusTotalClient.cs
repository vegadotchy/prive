using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace IatechShield.Reputation;

/// <summary>Verdict de réputation d'un fichier d'après VirusTotal.</summary>
public sealed record VtReport(
    string Sha256,
    int Malicious,
    int Suspicious,
    int Harmless,
    int Undetected,
    int TotalEngines,
    string? PopularName,
    bool Found)
{
    /// <summary>Réputation lisible : « 12/70 moteurs détectent une menace ».</summary>
    public string Summary => Found
        ? $"{Malicious + Suspicious}/{TotalEngines} moteurs signalent une menace"
        : "Inconnu de VirusTotal";

    public bool IsMalicious => Malicious + Suspicious > 0;
}

/// <summary>
/// Client minimal de l'API VirusTotal v3. Interroge la réputation d'un fichier
/// par son hash SHA-256 (pas d'envoi du fichier : seul le hash transite, ce qui
/// préserve la confidentialité). Nécessite une clé API (gratuite).
/// </summary>
public sealed class VirusTotalClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public const string ApiKeyEnvVar = "VIRUSTOTAL_API_KEY";

    public VirusTotalClient(string apiKey, HttpClient? http = null)
    {
        _apiKey = apiKey;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    /// <summary>Clé lue depuis la variable d'environnement, si présente.</summary>
    public static string? KeyFromEnvironment =>
        Environment.GetEnvironmentVariable(ApiKeyEnvVar);

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(KeyFromEnvironment);

    /// <summary>Interroge VirusTotal pour un hash SHA-256 (64 caractères hexadécimaux).</summary>
    public async Task<VtReport> LookupAsync(string sha256, CancellationToken cancel = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://www.virustotal.com/api/v3/files/{sha256}");
        req.Headers.Add("x-apikey", _apiKey);

        using var resp = await _http.SendAsync(req, cancel);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return new VtReport(sha256, 0, 0, 0, 0, 0, null, Found: false);

        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(cancel);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancel);

        var attr = doc.RootElement.GetProperty("data").GetProperty("attributes");

        int mal = 0, susp = 0, harm = 0, undet = 0;
        if (attr.TryGetProperty("last_analysis_stats", out var stats))
        {
            mal = GetInt(stats, "malicious");
            susp = GetInt(stats, "suspicious");
            harm = GetInt(stats, "harmless");
            undet = GetInt(stats, "undetected");
        }

        string? popular = null;
        if (attr.TryGetProperty("popular_threat_classification", out var ptc) &&
            ptc.TryGetProperty("suggested_threat_label", out var lbl))
            popular = lbl.GetString();

        int total = mal + susp + harm + undet;
        return new VtReport(sha256, mal, susp, harm, undet, total, popular, Found: true);
    }

    private static int GetInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
}
