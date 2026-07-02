using System.Collections.Concurrent;

namespace IatechShield.Engine;

/// <summary>
/// Surveillance temps réel d'un dossier : chaque fichier créé ou modifié est
/// analysé automatiquement. Les détections sont signalées via l'événement
/// <see cref="ThreatDetected"/>.
/// </summary>
public sealed class RealtimeMonitor : IDisposable
{
    private readonly Scanner _scanner;
    private readonly FileSystemWatcher _watcher;
    private readonly BlockingCollection<string> _queue = new(new ConcurrentQueue<string>());
    private readonly Thread _worker;
    private readonly HashSet<string> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private volatile bool _running = true;

    /// <summary>Déclenché quand une menace est détectée pendant la surveillance.</summary>
    public event Action<FileScanResult>? ThreatDetected;

    /// <summary>Déclenché à chaque fichier analysé (menace ou non).</summary>
    public event Action<FileScanResult>? FileScanned;

    public RealtimeMonitor(Scanner scanner, string path, bool includeSubdirectories = true)
    {
        _scanner = scanner;

        _watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = includeSubdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024
        };
        _watcher.Created += OnChanged;
        _watcher.Changed += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnError;

        _worker = new Thread(ProcessQueue) { IsBackground = true, Name = "iatech-shield-rt" };
    }

    /// <summary>Démarre la surveillance.</summary>
    public void Start()
    {
        _worker.Start();
        _watcher.EnableRaisingEvents = true;
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => Enqueue(e.FullPath);
    private void OnRenamed(object sender, RenamedEventArgs e) => Enqueue(e.FullPath);

    private void OnError(object sender, ErrorEventArgs e)
    {
        // Le buffer interne peut déborder en cas de forte activité disque.
        Console.Error.WriteLine($"[temps réel] avertissement : {e.GetException().Message}");
    }

    private void Enqueue(string fullPath)
    {
        if (Directory.Exists(fullPath)) // on ignore les dossiers
            return;

        // Anti-doublon : un même fichier génère souvent plusieurs événements.
        lock (_lock)
        {
            if (!_inFlight.Add(fullPath))
                return;
        }
        _queue.Add(fullPath);
    }

    private void ProcessQueue()
    {
        foreach (string path in _queue.GetConsumingEnumerable())
        {
            if (!_running)
                break;

            // Petit délai : laisse le temps à l'écriture du fichier de se terminer.
            Thread.Sleep(150);

            FileScanResult result;
            try
            {
                result = _scanner.ScanFile(path);
            }
            catch (Exception ex)
            {
                result = FileScanResult.Failed(path, ex.Message);
            }
            finally
            {
                lock (_lock) { _inFlight.Remove(path); }
            }

            FileScanned?.Invoke(result);
            if (result.IsThreat)
                ThreatDetected?.Invoke(result);
        }
    }

    public void Dispose()
    {
        _running = false;
        _watcher.EnableRaisingEvents = false;
        _queue.CompleteAdding();
        _watcher.Dispose();
    }
}
