using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IatechShield.Tools;

/// <summary>
/// Petit coffre local chiffré via DPAPI (clé liée à l'utilisateur Windows).
/// Sert à mémoriser des secrets (identifiants VPN, PIN…) sans les écrire en clair.
/// </summary>
public static class SecretVault
{
    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "IatechShield", "secrets");

    /// <summary>Enregistre un dictionnaire de valeurs sous un nom donné (chiffré DPAPI).</summary>
    public static void Save(string name, IReadOnlyDictionary<string, string> values)
    {
        Directory.CreateDirectory(Dir);
        byte[] json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values));
        byte[] enc = ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(Path.Combine(Dir, name + ".bin"), enc);
    }

    /// <summary>Recharge les valeurs enregistrées, ou un dictionnaire vide.</summary>
    public static Dictionary<string, string> Load(string name)
    {
        try
        {
            string path = Path.Combine(Dir, name + ".bin");
            if (!File.Exists(path)) return new();
            byte[] dec = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(dec)) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public static void Delete(string name)
    {
        try
        {
            string path = Path.Combine(Dir, name + ".bin");
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* ignoré */ }
    }
}
