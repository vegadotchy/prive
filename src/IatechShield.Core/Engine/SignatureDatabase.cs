using System.Text.Json;
using System.Text.Json.Serialization;

namespace IatechShield.Engine;

/// <summary>
/// Charge et expose la base de signatures depuis un fichier JSON.
/// </summary>
public sealed class SignatureDatabase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public IReadOnlyList<Signature> Signatures { get; }

    private SignatureDatabase(IReadOnlyList<Signature> signatures) => Signatures = signatures;

    /// <summary>
    /// Charge la base depuis un fichier JSON. Lève une exception explicite en cas de souci.
    /// </summary>
    public static SignatureDatabase LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Base de signatures introuvable : {path}");

        string json = File.ReadAllText(path);
        List<Signature>? signatures;
        try
        {
            signatures = JsonSerializer.Deserialize<List<Signature>>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Base de signatures invalide ({path}) : {ex.Message}", ex);
        }

        if (signatures is null || signatures.Count == 0)
            throw new InvalidDataException($"La base de signatures est vide : {path}");

        // Validation minimale de chaque entrée.
        foreach (var s in signatures)
        {
            if (string.IsNullOrWhiteSpace(s.Name) || string.IsNullOrWhiteSpace(s.Value))
                throw new InvalidDataException("Une signature a un 'name' ou 'value' manquant.");
        }

        return new SignatureDatabase(signatures);
    }
}
