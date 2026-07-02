using System.Collections.Concurrent;

namespace IatechShield.Engine;

/// <summary>Détail d'une alerte ransomware.</summary>
public sealed class RansomwareAlert
{
    public string Reason { get; init; } = "";
    public int FilesAffected { get; init; }
    public string? CanaryPath { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.Now;
}

/// <summary>
/// Bouclier anti-ransomware en mode utilisateur :
///  - dépose des fichiers « canaris » dans les dossiers sensibles ;
///  - surveille les modifications ; toute touche à un canari = signal fort ;
///  - compte les modifications massives dans une fenêtre glissante.
/// Quand un seuil est franchi, déclenche <see cref="Alert"/> (le consommateur
/// décide de la réaction : tuer le processus, restaurer, alerter).
/// </summary>
public sealed class RansomwareGuard : IDisposable
{
    private const string CanaryPrefix = "~IATECH_CANARY_";

    private readonly string[] _folders;
    private readonly int _massThreshold;
    private readonly TimeSpan _window;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly ConcurrentQueue<DateTime> _recentChanges = new();
    private readonly List<string> _canaries = new();
    private volatile bool _triggered;

    /// <summary>Déclenché lorsqu'une activité de type ransomware est détectée.</summary>
    public event Action<RansomwareAlert>? Alert;

    public RansomwareGuard(IEnumerable<string> protectedFolders,
                           int massModificationThreshold = 40,
                           TimeSpan? window = null)
    {
        _folders = protectedFolders.Where(Directory.Exists).ToArray();
        _massThreshold = massModificationThreshold;
        _window = window ?? TimeSpan.FromSeconds(5);
    }

    /// <summary>Déploie les canaris et démarre la surveillance.</summary>
    public void Start()
    {
        DeployCanaries();

        foreach (string folder in _folders)
        {
            var w = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024
            };
            w.Changed += OnChanged;
            w.Created += OnChanged;
            w.Renamed += OnChanged;
            w.Deleted += OnChanged;
            w.EnableRaisingEvents = true;
            _watchers.Add(w);
        }
    }

    private void DeployCanaries()
    {
        // Contenu inoffensif ; un nom alphabétique précoce pour être traité tôt.
        byte[] content = System.Text.Encoding.UTF8.GetBytes(
            "Fichier de protection IATECH-SHIELD. Ne pas modifier ni supprimer.");

        foreach (string folder in _folders)
        {
            try
            {
                string canary = Path.Combine(folder, CanaryPrefix + "AAAA.docx");
                if (!File.Exists(canary))
                {
                    File.WriteAllBytes(canary, content);
                    File.SetAttributes(canary, FileAttributes.Hidden);
                }
                _canaries.Add(canary);
            }
            catch { /* dossier en lecture seule : on continue */ }
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (_triggered)
            return;

        string name = Path.GetFileName(e.FullPath);

        // 1) Un canari a été touché => quasi certain.
        if (name.StartsWith(CanaryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            Trigger(new RansomwareAlert
            {
                Reason = "Fichier canari modifié — comportement de chiffrement détecté",
                FilesAffected = 1,
                CanaryPath = e.FullPath
            });
            return;
        }

        // 2) Modifications massives dans la fenêtre glissante.
        var now = DateTime.UtcNow;
        _recentChanges.Enqueue(now);

        while (_recentChanges.TryPeek(out var oldest) && now - oldest > _window)
            _recentChanges.TryDequeue(out _);

        if (_recentChanges.Count >= _massThreshold)
        {
            Trigger(new RansomwareAlert
            {
                Reason = $"Modification massive : {_recentChanges.Count} fichiers en " +
                         $"{_window.TotalSeconds:0}s",
                FilesAffected = _recentChanges.Count
            });
        }
    }

    private void Trigger(RansomwareAlert alert)
    {
        if (_triggered)
            return;
        _triggered = true;
        Alert?.Invoke(alert);
    }

    /// <summary>Réarme le bouclier après une alerte traitée.</summary>
    public void Rearm()
    {
        _triggered = false;
        while (_recentChanges.TryDequeue(out _)) { }
    }

    /// <summary>Retire les fichiers canaris du disque.</summary>
    public void RemoveCanaries()
    {
        foreach (string c in _canaries)
        {
            try { if (File.Exists(c)) File.Delete(c); } catch { /* ignore */ }
        }
    }

    public void Dispose()
    {
        foreach (var w in _watchers)
            w.Dispose();
        _watchers.Clear();
    }
}
