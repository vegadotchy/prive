using System.Text.Json;

namespace IatechShield.Tools;

/// <summary>Une session de connexion ouverte par carte d'identité (eID).</summary>
public sealed record EidSession(
    string Id,
    string Name,
    string FirstNames,
    string BirthDate,
    string NationalNumber,
    DateTimeOffset ConnectedAt,
    DateTimeOffset? DisconnectedAt);

/// <summary>
/// « ID Registre » : registre des connexions ouvertes par carte d'identité (eID).
/// Chaque insertion de carte qui déverrouille le poste est enregistrée avec les
/// détails de la carte (nom, prénoms, date de naissance, numéro national) ainsi que
/// l'heure de connexion et de déconnexion, pour calculer la durée de présence.
/// </summary>
public static class EidSessionLog
{
    private static readonly object Sync = new();
    private const int MaxSessions = 5000;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IatechShield", "id-register.json");

    /// <summary>Ouvre une nouvelle session eID et retourne son identifiant.</summary>
    public static string StartSession(string name, string firstNames, string birthDate,
        string nationalNumber, string id)
    {
        try
        {
            lock (Sync)
            {
                var list = LoadInternal();
                list.Add(new EidSession(id, name, firstNames, birthDate, nationalNumber,
                    DateTimeOffset.Now, null));
                if (list.Count > MaxSessions) list.RemoveRange(0, list.Count - MaxSessions);
                Save(list);
            }
        }
        catch { }
        return id;
    }

    /// <summary>Clôt la dernière session ouverte (déconnexion) et fixe l'heure de fin.</summary>
    public static void EndSession(string? id = null)
    {
        try
        {
            lock (Sync)
            {
                var list = LoadInternal();
                EidSession? target = null;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i].DisconnectedAt is null && (id is null || list[i].Id == id))
                    { target = list[i]; break; }
                }
                if (target is null) return;
                int idx = list.IndexOf(target);
                list[idx] = target with { DisconnectedAt = DateTimeOffset.Now };
                Save(list);
            }
        }
        catch { }
    }

    /// <summary>Charge le registre, de la session la plus récente à la plus ancienne.</summary>
    public static IReadOnlyList<EidSession> Load()
    {
        lock (Sync)
        {
            var list = LoadInternal();
            list.Reverse();
            return list;
        }
    }

    public static void Clear()
    {
        try { lock (Sync) { if (File.Exists(FilePath)) File.Delete(FilePath); } }
        catch { }
    }

    private static void Save(List<EidSession> list)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(list));
    }

    private static List<EidSession> LoadInternal()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<EidSession>>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch { return new(); }
    }
}
