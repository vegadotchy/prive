using System.Security.Cryptography;
using System.Text;

namespace IatechShield.Engine;

/// <summary>Résultat de l'analyse d'un seul fichier.</summary>
public sealed record FileScanResult(string Path, bool IsThreat, Signature? Match, string? Error)
{
    public static FileScanResult Clean(string path) => new(path, false, null, null);
    public static FileScanResult Threat(string path, Signature match) => new(path, true, match, null);
    public static FileScanResult Failed(string path, string error) => new(path, false, null, error);
}

/// <summary>
/// Moteur d'analyse : compare le contenu d'un fichier aux signatures connues.
/// </summary>
public sealed class Scanner
{
    private readonly SignatureDatabase _db;

    // Au-delà de cette taille on ne fait que le hash (pas de recherche de motif en mémoire).
    private const long MaxPatternScanBytes = 64L * 1024 * 1024; // 64 Mo

    public Scanner(SignatureDatabase db) => _db = db;

    /// <summary>Analyse un fichier et renvoie le résultat.</summary>
    public FileScanResult ScanFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return FileScanResult.Failed(path, "fichier introuvable");

            var info = new FileInfo(path);

            // 1) Signatures par hash : on calcule le SHA-256 une seule fois.
            string sha256 = ComputeSha256(path);
            foreach (var sig in _db.Signatures)
            {
                if (sig.Type == SignatureType.Sha256 &&
                    string.Equals(sig.Value, sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return FileScanResult.Threat(path, sig);
                }
            }

            // 2) Signatures par motif (octets / texte) : on lit le contenu si le fichier
            //    n'est pas trop volumineux.
            bool hasPatternSig = _db.Signatures.Any(s => s.Type is SignatureType.HexPattern or SignatureType.TextPattern);
            if (hasPatternSig && info.Length <= MaxPatternScanBytes)
            {
                byte[] content = File.ReadAllBytes(path);
                foreach (var sig in _db.Signatures)
                {
                    byte[]? needle = sig.Type switch
                    {
                        SignatureType.HexPattern => TryParseHex(sig.Value),
                        SignatureType.TextPattern => Encoding.UTF8.GetBytes(sig.Value),
                        _ => null
                    };

                    if (needle is { Length: > 0 } && IndexOf(content, needle) >= 0)
                        return FileScanResult.Threat(path, sig);
                }
            }

            return FileScanResult.Clean(path);
        }
        catch (UnauthorizedAccessException)
        {
            return FileScanResult.Failed(path, "accès refusé");
        }
        catch (IOException ex)
        {
            return FileScanResult.Failed(path, $"erreur d'E/S : {ex.Message}");
        }
    }

    /// <summary>Calcule le hash SHA-256 d'un fichier en streaming (mémoire constante).</summary>
    public static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash); // majuscules, sans tiret
    }

    /// <summary>Convertit une chaîne hexadécimale (ex: "4D5A") en tableau d'octets.</summary>
    private static byte[]? TryParseHex(string hex)
    {
        hex = hex.Replace(" ", "").Replace("-", "");
        if (hex.Length == 0 || hex.Length % 2 != 0)
            return null;
        try { return Convert.FromHexString(hex); }
        catch (FormatException) { return null; }
    }

    /// <summary>Recherche naïve d'un motif d'octets dans un buffer.</summary>
    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length)
            return -1;

        int limit = haystack.Length - needle.Length;
        for (int i = 0; i <= limit; i++)
        {
            int j = 0;
            while (j < needle.Length && haystack[i + j] == needle[j])
                j++;
            if (j == needle.Length)
                return i;
        }
        return -1;
    }
}
