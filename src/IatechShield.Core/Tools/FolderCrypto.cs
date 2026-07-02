using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace IatechShield.Tools;

/// <summary>
/// Chiffrement/déchiffrement d'un dossier protégé par mot de passe.
///
/// Le dossier est d'abord compressé (ZIP) puis chiffré en AES-256 ; la clé est
/// dérivée du mot de passe via PBKDF2 (SHA-256, 200 000 itérations). Le fichier
/// produit porte l'extension « .iasx » et contient :
///   [magic 4][salt 16][IV 16][données chiffrées].
/// </summary>
public static class FolderCrypto
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("IASX");
    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const int KeySize = 32;          // AES-256
    private const int Iterations = 200_000;
    public const string Extension = ".iasx";

    /// <summary>
    /// Chiffre un dossier vers un fichier .iasx. Renvoie le chemin du fichier créé.
    /// </summary>
    public static string EncryptFolder(string folderPath, string password, bool deleteOriginal = false)
    {
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"Dossier introuvable : {folderPath}");
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Mot de passe requis.", nameof(password));

        string output = folderPath.TrimEnd(Path.DirectorySeparatorChar) + Extension;
        string tempZip = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");

        try
        {
            ZipFile.CreateFromDirectory(folderPath, tempZip, CompressionLevel.Optimal, includeBaseDirectory: false);

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Key = DeriveKey(password, salt);
            aes.GenerateIV();

            using (var outFs = new FileStream(output, FileMode.Create, FileAccess.Write))
            {
                outFs.Write(Magic);
                outFs.Write(salt);
                outFs.Write(aes.IV);
                using var crypto = new CryptoStream(outFs, aes.CreateEncryptor(), CryptoStreamMode.Write);
                using var zipFs = new FileStream(tempZip, FileMode.Open, FileAccess.Read);
                zipFs.CopyTo(crypto);
            }

            if (deleteOriginal)
                Directory.Delete(folderPath, recursive: true);

            return output;
        }
        finally
        {
            if (File.Exists(tempZip)) File.Delete(tempZip);
        }
    }

    /// <summary>
    /// Déchiffre un fichier .iasx vers un dossier. Renvoie le dossier restauré.
    /// </summary>
    public static string DecryptFolder(string iasxPath, string password, string? destinationDir = null)
    {
        if (!File.Exists(iasxPath))
            throw new FileNotFoundException($"Fichier introuvable : {iasxPath}");

        string tempZip = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var inFs = new FileStream(iasxPath, FileMode.Open, FileAccess.Read))
            {
                var magic = new byte[Magic.Length];
                inFs.ReadExactly(magic);
                if (!magic.SequenceEqual(Magic))
                    throw new InvalidDataException("Fichier .iasx invalide.");

                var salt = new byte[SaltSize];
                var iv = new byte[IvSize];
                inFs.ReadExactly(salt);
                inFs.ReadExactly(iv);

                using var aes = Aes.Create();
                aes.KeySize = 256;
                aes.Key = DeriveKey(password, salt);
                aes.IV = iv;

                using var crypto = new CryptoStream(inFs, aes.CreateDecryptor(), CryptoStreamMode.Read);
                using var zipFs = new FileStream(tempZip, FileMode.Create, FileAccess.Write);
                try
                {
                    crypto.CopyTo(zipFs);
                }
                catch (CryptographicException)
                {
                    throw new CryptographicException("Mot de passe incorrect ou fichier corrompu.");
                }
            }

            string dest = destinationDir ??
                          Path.Combine(Path.GetDirectoryName(iasxPath)!,
                                       Path.GetFileNameWithoutExtension(iasxPath) + "_dechiffre");
            Directory.CreateDirectory(dest);
            ZipFile.ExtractToDirectory(tempZip, dest, overwriteFiles: true);
            return dest;
        }
        finally
        {
            if (File.Exists(tempZip)) File.Delete(tempZip);
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        using var kdf = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
        return kdf.GetBytes(KeySize);
    }
}
