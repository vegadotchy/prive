namespace IatechShield.Engine;

/// <summary>
/// Type de détection d'une signature.
/// </summary>
public enum SignatureType
{
    /// <summary>Correspondance exacte sur le hash SHA-256 du fichier entier.</summary>
    Sha256,

    /// <summary>Recherche d'une suite d'octets (motif) à l'intérieur du fichier.
    /// Le motif est exprimé en hexadécimal, ex: "4D5A9000".</summary>
    HexPattern,

    /// <summary>Recherche d'une chaîne de texte à l'intérieur du fichier.</summary>
    TextPattern
}

/// <summary>
/// Une signature de menace : la « carte d'identité » d'un fichier malveillant connu.
/// </summary>
public sealed class Signature
{
    /// <summary>Nom de la menace, ex: "EICAR-Test-File".</summary>
    public string Name { get; init; } = "";

    /// <summary>Niveau de gravité indicatif (low / medium / high).</summary>
    public string Severity { get; init; } = "medium";

    /// <summary>Type de correspondance.</summary>
    public SignatureType Type { get; init; } = SignatureType.Sha256;

    /// <summary>
    /// Valeur recherchée :
    ///  - Sha256      -> hash hexadécimal (64 caractères)
    ///  - HexPattern  -> octets en hexadécimal, ex: "504B0304"
    ///  - TextPattern -> chaîne de texte brute
    /// </summary>
    public string Value { get; init; } = "";
}
