using System.Text.Json;

namespace IatechShield.Tools;

/// <summary>Gravité d'un évènement de sécurité.</summary>
public enum IncidentSeverity { Info, Warning, Critical }

/// <summary>Un évènement enregistré dans la chronologie d'investigation.</summary>
public sealed record IncidentEvent(
    DateTimeOffset Time,
    string Category,
    IncidentSeverity Severity,
    string Title,
    string Detail);

/// <summary>
/// Journal d'incidents persistant (« Centre d'investigation »). Enregistre les
/// évènements de sécurité (menaces, temps réel, ransomware, mode panic, radar,
/// jumeau numérique, webcam…) avec horodatage, pour reconstituer la chronologie
/// d'une attaque et l'exporter (rapport).
/// </summary>
public static class IncidentLog
{
    private static readonly object Sync = new();
    private const int MaxEvents = 2000;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IatechShield", "incidents.json");

    /// <summary>Enregistre un évènement (horodaté à l'instant présent).</summary>
    public static void Record(string category, IncidentSeverity severity, string title, string detail, DateTimeOffset now)
    {
        try
        {
            lock (Sync)
            {
                var events = LoadInternal();
                events.Add(new IncidentEvent(now, category, severity, title, detail));
                if (events.Count > MaxEvents)
                    events.RemoveRange(0, events.Count - MaxEvents);
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(events));
            }
        }
        catch { /* journalisation best-effort */ }
    }

    /// <summary>Charge la chronologie, du plus récent au plus ancien.</summary>
    public static IReadOnlyList<IncidentEvent> Load()
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

    private static List<IncidentEvent> LoadInternal()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<IncidentEvent>>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch { return new(); }
    }
}
