using System.Reflection;
using System.Security.Cryptography;

namespace IatechShield.Licensing;

/// <summary>Résultat de la vérification d'une clé de licence.</summary>
public sealed record LicenseCheck(bool Valid, License? License, string Message)
{
    public static LicenseCheck Ok(License l) => new(true, l, "Licence valide.");
    public static LicenseCheck Fail(string msg) => new(false, null, msg);
}

/// <summary>
/// Vérifie l'authenticité d'une clé de licence à l'aide de la clé publique
/// embarquée. Seul le détenteur de la clé privée (le générateur) peut produire
/// des clés acceptées ici.
/// </summary>
public sealed class LicenseVerifier
{
    private readonly string _publicKeyPem;

    public LicenseVerifier()
    {
        _publicKeyPem = LoadEmbeddedPublicKey();
    }

    /// <summary>Vérifie la signature ET la validité temporelle de la clé.</summary>
    public LicenseCheck Verify(string licenseKey, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
            return LicenseCheck.Fail("Clé vide.");

        byte[] payload, signature;
        try
        {
            (payload, signature) = LicenseCodec.Decode(licenseKey.Trim());
        }
        catch (Exception ex)
        {
            return LicenseCheck.Fail($"Format de clé invalide : {ex.Message}");
        }

        // Vérifie la signature cryptographique.
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(_publicKeyPem);
            bool ok = ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256,
                                       DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            if (!ok)
                return LicenseCheck.Fail("Signature de licence non valide (clé contrefaite ou altérée).");
        }
        catch (Exception ex)
        {
            return LicenseCheck.Fail($"Échec de vérification : {ex.Message}");
        }

        License license;
        try
        {
            license = LicenseCodec.ParsePayload(payload);
        }
        catch (Exception ex)
        {
            return LicenseCheck.Fail($"Contenu de licence illisible : {ex.Message}");
        }

        if (license.IsExpired(now))
            return LicenseCheck.Fail($"Licence expirée le {license.ExpiresUtc:yyyy-MM-dd}.");

        return LicenseCheck.Ok(license);
    }

    private static string LoadEmbeddedPublicKey()
    {
        var asm = typeof(LicenseVerifier).Assembly;
        string? name = asm.GetManifestResourceNames()
                          .FirstOrDefault(n => n.EndsWith("public_key.pem", StringComparison.OrdinalIgnoreCase));
        if (name is null)
            throw new InvalidOperationException("Clé publique de licence introuvable dans l'assembly.");

        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
