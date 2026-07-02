using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace IatechShield.Licensing;

/// <summary>Identifiant stable de la machine (ne contient aucune donnée personnelle).</summary>
public static class MachineId
{
    /// <summary>
    /// Empreinte déterministe de la machine : hachage du nom de machine, de
    /// l'utilisateur et de la version d'OS. Permet de lier une activation à un
    /// poste sans transmettre d'information identifiable en clair.
    /// </summary>
    public static string Get()
    {
        string raw = string.Join('|',
            Environment.MachineName,
            Environment.UserName,
            Environment.OSVersion.Platform,
            Environment.ProcessorCount);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..32]; // 128 bits suffisent
    }
}

/// <summary>Requête d'activation envoyée au serveur.</summary>
public sealed class ActivationRequest
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("machineId")] public string MachineId { get; set; } = "";
    [JsonPropertyName("product")] public string Product { get; set; } = "iatech-shield";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
}

/// <summary>Réponse du serveur d'activation.</summary>
public sealed class ActivationResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    /// <summary>Clé de licence signée renvoyée par le serveur (vérifiée hors-ligne).</summary>
    [JsonPropertyName("licenseKey")] public string? LicenseKey { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

/// <summary>Résultat consolidé d'une activation en ligne.</summary>
public sealed record OnlineActivationResult(bool Success, string Message, string? LicenseKey);

/// <summary>
/// Client d'activation en ligne. Envoie la clé saisie + l'empreinte machine au
/// serveur d'activation ; celui-ci valide l'achat et renvoie une licence
/// **signée** que l'application vérifie ensuite hors-ligne (la cryptographie
/// reste la source de vérité : un serveur compromis ne peut pas forger de licence
/// sans la clé privée). L'URL du serveur est configurable.
/// </summary>
public sealed class OnlineActivationClient
{
    public const string EndpointEnvVar = "IATECH_ACTIVATION_URL";

    private readonly HttpClient _http;
    private readonly string _endpoint;

    public OnlineActivationClient(string? endpoint = null, HttpClient? http = null)
    {
        _endpoint = endpoint ?? EndpointFromEnvironment ?? "";
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    public static string? EndpointFromEnvironment =>
        Environment.GetEnvironmentVariable(EndpointEnvVar);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_endpoint);

    public async Task<OnlineActivationResult> ActivateAsync(string key, string version,
        CancellationToken cancel = default)
    {
        if (!IsConfigured)
            return new OnlineActivationResult(false,
                $"Serveur d'activation non configuré (variable {EndpointEnvVar}).", null);

        var request = new ActivationRequest
        {
            Key = key.Trim(),
            MachineId = MachineId.Get(),
            Version = version
        };

        try
        {
            using var resp = await _http.PostAsJsonAsync(_endpoint, request, cancel);
            if (!resp.IsSuccessStatusCode)
                return new OnlineActivationResult(false,
                    $"Serveur d'activation : HTTP {(int)resp.StatusCode}.", null);

            var body = await resp.Content.ReadFromJsonAsync<ActivationResponse>(cancellationToken: cancel);
            if (body is null)
                return new OnlineActivationResult(false, "Réponse du serveur illisible.", null);

            if (body.Ok && !string.IsNullOrWhiteSpace(body.LicenseKey))
                return new OnlineActivationResult(true,
                    body.Message ?? "Activation réussie.", body.LicenseKey);

            return new OnlineActivationResult(false,
                body.Message ?? "Activation refusée par le serveur.", null);
        }
        catch (Exception ex)
        {
            return new OnlineActivationResult(false, $"Connexion au serveur impossible : {ex.Message}", null);
        }
    }
}
