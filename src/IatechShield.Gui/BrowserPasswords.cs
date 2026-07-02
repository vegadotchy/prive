using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace IatechShield.Gui;

/// <summary>Un identifiant enregistré récupéré d'un navigateur.</summary>
public sealed record BrowserLogin(string Browser, string Url, string Login, string Password);

/// <summary>
/// Récupère, en arrière-plan, les identifiants enregistrés par les navigateurs
/// Chromium (Chrome, Edge, Brave, Vivaldi, Opera) pour les verser dans le coffre-fort :
/// site/application, login et mot de passe. Les mots de passe sont déchiffrés localement
/// (clé AES protégée par DPAPI de l'utilisateur courant). Les versions récentes de Chrome
/// utilisant le chiffrement « app-bound » (préfixe v20) ne sont pas déchiffrables et sont
/// ignorées sans erreur.
/// </summary>
public static class BrowserPasswords
{
    public static List<BrowserLogin> ReadAll()
    {
        var logins = new List<BrowserLogin>();
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var roots = new (string Name, string Path)[]
        {
            ("Chrome",  Path.Combine(local, "Google", "Chrome", "User Data")),
            ("Edge",    Path.Combine(local, "Microsoft", "Edge", "User Data")),
            ("Brave",   Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data")),
            ("Vivaldi", Path.Combine(local, "Vivaldi", "User Data")),
            ("Opera",   Path.Combine(roaming, "Opera Software", "Opera Stable")),
            ("Opera GX",Path.Combine(roaming, "Opera Software", "Opera GX Stable")),
        };

        foreach (var (name, root) in roots)
        {
            if (!Directory.Exists(root)) continue;
            byte[]? aesKey = TryLoadAesKey(Path.Combine(root, "Local State"));
            foreach (string dbPath in FindLoginDbs(root))
            {
                try { logins.AddRange(ReadDb(name, dbPath, aesKey)); }
                catch { /* profil illisible : on continue */ }
            }
        }
        // Déduplique (même site + login).
        return logins
            .GroupBy(l => l.Url + "|" + l.Login, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static IEnumerable<string> FindLoginDbs(string userDataRoot)
    {
        string[] dirs;
        try { dirs = Directory.GetDirectories(userDataRoot); } catch { yield break; }
        foreach (var dir in dirs)
        {
            string db = Path.Combine(dir, "Login Data");
            if (File.Exists(db)) yield return db;
        }
        string root = Path.Combine(userDataRoot, "Login Data");
        if (File.Exists(root)) yield return root;
    }

    private static byte[]? TryLoadAesKey(string localStatePath)
    {
        try
        {
            if (!File.Exists(localStatePath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(localStatePath));
            string b64 = doc.RootElement.GetProperty("os_crypt").GetProperty("encrypted_key").GetString() ?? "";
            byte[] blob = Convert.FromBase64String(b64);
            if (blob.Length < 5) return null;
            byte[] encryptedKey = blob[5..];   // retire le préfixe « DPAPI »
            return DpapiUnprotect(encryptedKey);
        }
        catch { return null; }
    }

    private static List<BrowserLogin> ReadDb(string browser, string dbPath, byte[]? aesKey)
    {
        var result = new List<BrowserLogin>();
        string temp = Path.Combine(Path.GetTempPath(), "iatech_login_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            File.Copy(dbPath, temp, overwrite: true);
            using var cn = new SqliteConnection($"Data Source={temp};Mode=ReadOnly;Cache=Private");
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT origin_url, username_value, password_value FROM logins";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string url = r.IsDBNull(0) ? "" : r.GetString(0);
                string user = r.IsDBNull(1) ? "" : r.GetString(1);
                byte[] enc = r.IsDBNull(2) ? Array.Empty<byte>() : (byte[])r[2];
                string pwd = DecryptPassword(enc, aesKey);
                if (url.Length == 0 || (user.Length == 0 && pwd.Length == 0)) continue;
                result.Add(new BrowserLogin(browser, url, user, pwd));
            }
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        return result;
    }

    private static string DecryptPassword(byte[] enc, byte[]? aesKey)
    {
        if (enc.Length == 0) return "";
        try
        {
            // v10 / v11 : AES-256-GCM avec la clé du Local State.
            if (enc.Length > 15 && (enc[0] == 'v') && (enc[1] == '1') && aesKey is not null)
            {
                byte[] nonce = enc[3..15];
                byte[] tag = enc[^16..];
                byte[] cipher = enc[15..^16];
                byte[] plain = new byte[cipher.Length];
                using var gcm = new AesGcm(aesKey, 16);
                gcm.Decrypt(nonce, cipher, tag, plain);
                return Encoding.UTF8.GetString(plain);
            }
            // v20 : chiffrement « app-bound » non déchiffrable ici → ignoré.
            if (enc.Length > 3 && enc[0] == 'v' && enc[1] == '2') return "";
            // Ancien format : DPAPI direct.
            byte[]? dp = DpapiUnprotect(enc);
            return dp is null ? "" : Encoding.UTF8.GetString(dp);
        }
        catch { return ""; }
    }

    // --- DPAPI (CryptUnprotectData) ---

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB { public int cbData; public IntPtr pbData; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    private static byte[]? DpapiUnprotect(byte[] data)
    {
        var inBlob = new DATA_BLOB();
        var outBlob = new DATA_BLOB();
        try
        {
            inBlob.pbData = Marshal.AllocHGlobal(data.Length);
            inBlob.cbData = data.Length;
            Marshal.Copy(data, 0, inBlob.pbData, data.Length);
            if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref outBlob))
                return null;
            byte[] outData = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, outData, 0, outBlob.cbData);
            return outData;
        }
        catch { return null; }
        finally
        {
            if (inBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.pbData);
            if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
