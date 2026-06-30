using System.Text.Json;

namespace IatechShield.Tools;

/// <summary>Un évènement d'accès (verrouillage / déverrouillage / connexion / déconnexion).</summary>
public sealed record AccessEvent(
    DateTimeOffset Time,
    string Kind,        // « Déverrouillage », « Verrouillage », « Connexion », « Déconnexion »
    string Method,      // « PIN », « Mot de passe », « Windows Hello », « itsme », « Carte d'identité (eID) »
    string Identity,    // nom / numéro national / compte itsme / utilisateur Windows
    string Detail);     // informations complémentaires (ATR de la carte, etc.)

/// <summary>
/// Registre persistant des connexions et déconnexions à la session IATECH-SHIELD.
/// Trace qui a déverrouillé le poste, quand et par quel moyen (PIN, Windows Hello,
/// itsme, carte d'identité eID). Permet de savoir, en cas de besoin, qui a accédé
/// à la machine.
/// </summary>
public static class AccessLog
{
    private static readonly object Sync = new();
    private const int MaxEvents = 5000;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IatechShield", "access.json");

    /// <summary>Enregistre un évènement d'accès (horodaté à l'instant présent).</summary>
    public static void Record(string kind, string method, string identity, string detail = "")
    {
        try
        {
            lock (Sync)
            {
                var events = LoadInternal();
                events.Add(new AccessEvent(DateTimeOffset.Now, kind, method, identity, detail));
                if (events.Count > MaxEvents)
                    events.RemoveRange(0, events.Count - MaxEvents);
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(events));
            }
        }
        catch { /* journalisation best-effort */ }
    }

    /// <summary>Charge le registre, du plus récent au plus ancien.</summary>
    public static IReadOnlyList<AccessEvent> Load()
    {
        lock (Sync)
        {
            var events = LoadInternal();
            events.Reverse();
            return events;
        }
    }

    public static void Clear()
    {
        try { lock (Sync) { if (File.Exists(FilePath)) File.Delete(FilePath); } }
        catch { }
    }

    private static List<AccessEvent> LoadInternal()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<AccessEvent>>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch { return new(); }
    }
}
