using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using IatechShield.Licensing;

// =============================================================================
//  IATECH-SHIELD — Serveur d'activation en ligne (implémentation de référence)
// -----------------------------------------------------------------------------
//  Reçoit une « clé d'achat » (order key) + l'empreinte machine, vérifie l'achat
//  dans un registre de commandes, puis renvoie une licence SIGNÉE (ECDSA P-256)
//  que l'application vérifie hors-ligne avec sa clé publique embarquée.
//
//  La cryptographie reste la source de vérité : ce serveur ne fait qu'AUTORISER
//  l'émission. Il ne peut rien forger sans la clé privée, et un client ne peut
//  pas contourner la vérification de signature côté application.
//
//  Configuration (variables d'environnement) :
//    IATECH_PRIVATE_KEY_PEM   chemin du PKCS#8 privé (généré par IatechShield.KeyGen)
//    IATECH_ORDERS_FILE       chemin d'un orders.json { "ORDERKEY": { "tier": "Yearly" } }
// =============================================================================

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

string privateKeyPath = Environment.GetEnvironmentVariable("IATECH_PRIVATE_KEY_PEM")
                        ?? "keys/private.pem";
string ordersPath = Environment.GetEnvironmentVariable("IATECH_ORDERS_FILE")
                    ?? "orders.json";

app.MapGet("/", () => "IATECH-SHIELD Activation Server — POST /activate");

app.MapPost("/activate", (ActivationRequestDto req) =>
{
    if (string.IsNullOrWhiteSpace(req.Key))
        return Results.Ok(new ActivationResponseDto(false, null, "Clé d'achat manquante."));

    // 1) Recherche la commande correspondant à la clé d'achat.
    var orders = LoadOrders(ordersPath);
    if (!orders.TryGetValue(req.Key.Trim(), out var order))
        return Results.Ok(new ActivationResponseDto(false, null, "Clé d'achat inconnue."));

    if (order.Activated && !string.Equals(order.MachineId, req.MachineId, StringComparison.OrdinalIgnoreCase))
        return Results.Ok(new ActivationResponseDto(false, null,
            "Cette clé est déjà activée sur un autre poste."));

    // 2) Émet une licence signée pour le palier acheté.
    if (!Enum.TryParse<LicenseTier>(order.Tier, ignoreCase: true, out var tier))
        tier = LicenseTier.Yearly;

    string licenseKey;
    try
    {
        licenseKey = IssueSignedLicense(tier, privateKeyPath);
    }
    catch (Exception ex)
    {
        return Results.Ok(new ActivationResponseDto(false, null,
            $"Erreur serveur (signature) : {ex.Message}"));
    }

    // 3) Marque la commande comme activée pour ce poste (anti-partage simple).
    order.Activated = true;
    order.MachineId = req.MachineId;
    SaveOrders(ordersPath, orders);

    return Results.Ok(new ActivationResponseDto(true, licenseKey, "Activation réussie. Merci !"));
});

app.Run();

// ---------------------------------------------------------------- helpers ---

static string IssueSignedLicense(LicenseTier tier, string privateKeyPath)
{
    if (!File.Exists(privateKeyPath))
        throw new FileNotFoundException($"Clé privée introuvable : {privateKeyPath}");

    var license = License.Create(tier, DateTimeOffset.UtcNow);
    byte[] payload = LicenseCodec.BuildPayload(license);

    using var ec = ECDsa.Create();
    ec.ImportFromPem(File.ReadAllText(privateKeyPath));
    byte[] signature = ec.SignData(payload, HashAlgorithmName.SHA256,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    return LicenseCodec.Encode(payload, signature);
}

static Dictionary<string, OrderRecord> LoadOrders(string path)
{
    if (!File.Exists(path)) return new(StringComparer.OrdinalIgnoreCase);
    var data = JsonSerializer.Deserialize<Dictionary<string, OrderRecord>>(File.ReadAllText(path));
    return new Dictionary<string, OrderRecord>(data ?? new(), StringComparer.OrdinalIgnoreCase);
}

static void SaveOrders(string path, Dictionary<string, OrderRecord> orders) =>
    File.WriteAllText(path, JsonSerializer.Serialize(orders,
        new JsonSerializerOptions { WriteIndented = true }));

// ------------------------------------------------------------------ DTOs ---

public sealed record ActivationRequestDto(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("machineId")] string MachineId,
    [property: JsonPropertyName("product")] string? Product,
    [property: JsonPropertyName("version")] string? Version);

public sealed record ActivationResponseDto(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("licenseKey")] string? LicenseKey,
    [property: JsonPropertyName("message")] string? Message);

public sealed class OrderRecord
{
    [JsonPropertyName("tier")] public string Tier { get; set; } = "Yearly";
    [JsonPropertyName("activated")] public bool Activated { get; set; }
    [JsonPropertyName("machineId")] public string? MachineId { get; set; }
}
