using System.Text.Json;

namespace IatechShield.Engine;

/// <summary>Métadonnées d'un fichier mis en quarantaine.</summary>
public sealed class QuarantineEntry
{
    public string Id { get; init; } = "";
    public string OriginalPath { get; init; } = "";
    public string ThreatName { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public DateTimeOffset QuarantinedAt { get; init; }
}

/// <summary>
/// Gère le dossier de quarantaine : déplace les fichiers détectés dans un endroit
/// isolé et « neutralisé » (octets inversés via XOR), et conserve un index pour
/// pouvoir restaurer un faux positif.
/// </summary>
public sealed class Quarantine
{
    // Clé XOR appliquée à chaque octet : empêche l'exécution accidentelle du
    // fichier en quarantaine sans détruire l'information (réversible).
    private const byte XorKey = 0xAA;

    private readonly string _root;
    private readonly string _storeDir;
    private readonly string _indexPath;

    public Quarantine(string root)
    {
        _root = root;
        _storeDir = Path.Combine(root, "store");
        _indexPath = Path.Combine(root, "index.json");
        Directory.CreateDirectory(_storeDir);
    }

    /// <summary>
    /// Déplace un fichier détecté en quarantaine. Renvoie l'entrée créée.
    /// </summary>
    public QuarantineEntry Add(string filePath, string threatName, string sha256)
    {
        string id = Guid.NewGuid().ToString("N");
        string dest = Path.Combine(_storeDir, id + ".q");

        // On lit, on neutralise (XOR), on écrit dans la quarantaine, puis on
        // supprime l'original. On évite File.Move pour garantir la neutralisation.
        byte[] data = File.ReadAllBytes(filePath);
        for (int i = 0; i < data.Length; i++)
            data[i] ^= XorKey;
        File.WriteAllBytes(dest, data);
        File.Delete(filePath);

        var entry = new QuarantineEntry
        {
            Id = id,
            OriginalPath = Path.GetFullPath(filePath),
            ThreatName = threatName,
            Sha256 = sha256,
            QuarantinedAt = DateTimeOffset.Now
        };

        var index = LoadIndex();
        index.Add(entry);
        SaveIndex(index);
        return entry;
    }

    /// <summary>Liste les fichiers actuellement en quarantaine.</summary>
    public IReadOnlyList<QuarantineEntry> List() => LoadIndex();

    /// <summary>
    /// Restaure un fichier en quarantaine vers son emplacement d'origine
    /// (ou un autre dossier). À utiliser en cas de faux positif.
    /// </summary>
    public bool Restore(string id, string? destinationOverride = null)
    {
        var index = LoadIndex();
        var entry = index.FirstOrDefault(e => e.Id == id);
        if (entry is null)
            return false;

        string source = Path.Combine(_storeDir, id + ".q");
        if (!File.Exists(source))
            return false;

        string dest = destinationOverride ?? entry.OriginalPath;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        // On inverse le XOR pour retrouver le contenu original.
        byte[] data = File.ReadAllBytes(source);
        for (int i = 0; i < data.Length; i++)
            data[i] ^= XorKey;
        File.WriteAllBytes(dest, data);
        File.Delete(source);

        index.Remove(entry);
        SaveIndex(index);
        return true;
    }

    /// <summary>Supprime définitivement un fichier en quarantaine.</summary>
    public bool Delete(string id)
    {
        var index = LoadIndex();
        var entry = index.FirstOrDefault(e => e.Id == id);
        if (entry is null)
            return false;

        string source = Path.Combine(_storeDir, id + ".q");
        if (File.Exists(source))
            File.Delete(source);

        index.Remove(entry);
        SaveIndex(index);
        return true;
    }

    private List<QuarantineEntry> LoadIndex()
    {
        if (!File.Exists(_indexPath))
            return new List<QuarantineEntry>();
        string json = File.ReadAllText(_indexPath);
        return JsonSerializer.Deserialize<List<QuarantineEntry>>(json) ?? new List<QuarantineEntry>();
    }

    private void SaveIndex(List<QuarantineEntry> index)
    {
        string json = JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_indexPath, json);
    }
}
