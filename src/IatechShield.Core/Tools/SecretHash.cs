using System.Security.Cryptography;
using System.Text;

namespace IatechShield.Tools;

/// <summary>
/// Hachage salé (PBKDF2-SHA256) pour stocker mots de passe / PIN de protection
/// sans jamais conserver la valeur en clair. Vérification à temps constant.
/// </summary>
public static class SecretHash
{
    private const int Iterations = 100_000;
    private const int SaltLen = 16;
    private const int HashLen = 32;

    /// <summary>Produit une représentation « base64(salt).base64(hash) ».</summary>
    public static string Hash(string secret)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltLen);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(secret), salt, Iterations, HashAlgorithmName.SHA256, HashLen);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    /// <summary>Vérifie un secret face à une empreinte produite par <see cref="Hash"/>.</summary>
    public static bool Verify(string secret, string stored)
    {
        try
        {
            var parts = stored.Split('.');
            if (parts.Length != 2) return false;
            byte[] salt = Convert.FromBase64String(parts[0]);
            byte[] expected = Convert.FromBase64String(parts[1]);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(secret), salt, Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }
}
