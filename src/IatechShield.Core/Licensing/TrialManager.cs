using System.Security.Cryptography;
using System.Text;

namespace IatechShield.Licensing;

/// <summary>
/// Gère la période d'essai gratuite (15 jours). La date de premier lancement est
/// stockée localement avec une empreinte anti-altération simple.
///
/// Limite assumée : en mode utilisateur, un utilisateur déterminé peut effacer le
/// marqueur d'essai. Une protection forte nécessiterait un composant serveur
/// d'activation (prévu dans la feuille de route).
/// </summary>
public sealed class TrialManager
{
    public const int TrialDays = 15;

    private readonly string _path;
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("IATECH-SHIELD-TRIAL-v1");

    public TrialManager(string storageDir)
    {
        Directory.CreateDirectory(storageDir);
        _path = Path.Combine(storageDir, "trial.dat");
    }

    /// <summary>Renvoie la date de début d'essai, en l'initialisant au besoin.</summary>
    public DateTimeOffset GetOrStartTrial(DateTimeOffset now)
    {
        var existing = ReadTrialStart();
        if (existing is not null)
            return existing.Value;

        WriteTrialStart(now);
        return now;
    }

    /// <summary>Nombre de jours d'essai restants (0 si terminé).</summary>
    public int DaysRemaining(DateTimeOffset now)
    {
        var start = GetOrStartTrial(now);
        int used = (int)(now - start).TotalDays;
        return Math.Max(0, TrialDays - used);
    }

    private DateTimeOffset? ReadTrialStart()
    {
        try
        {
            if (!File.Exists(_path))
                return null;

            string[] parts = File.ReadAllText(_path).Split('|');
            if (parts.Length != 2)
                return null;

            long unix = long.Parse(parts[0]);
            // Vérifie l'empreinte : si quelqu'un édite la date, elle ne correspond plus.
            if (Hash(parts[0]) != parts[1])
                return null;

            return DateTimeOffset.FromUnixTimeSeconds(unix);
        }
        catch
        {
            return null;
        }
    }

    private void WriteTrialStart(DateTimeOffset now)
    {
        string unix = now.ToUnixTimeSeconds().ToString();
        File.WriteAllText(_path, $"{unix}|{Hash(unix)}");
    }

    private static string Hash(string value)
    {
        byte[] data = Encoding.UTF8.GetBytes(value);
        byte[] combined = new byte[data.Length + Salt.Length];
        Buffer.BlockCopy(data, 0, combined, 0, data.Length);
        Buffer.BlockCopy(Salt, 0, combined, data.Length, Salt.Length);
        return Convert.ToHexString(SHA256.HashData(combined));
    }
}
