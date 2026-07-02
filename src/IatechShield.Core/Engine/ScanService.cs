namespace IatechShield.Engine;

/// <summary>Synthèse d'une analyse complète d'un dossier.</summary>
public sealed class ScanReport
{
    public int FilesScanned { get; set; }
    public int Errors { get; set; }
    public List<FileScanResult> Threats { get; } = new();
}

/// <summary>
/// Orchestration de haut niveau : parcourt un dossier, analyse chaque fichier
/// et (optionnellement) met les menaces en quarantaine. Pensé pour être appelé
/// aussi bien depuis la CLI que depuis l'interface graphique.
/// </summary>
public sealed class ScanService
{
    private readonly Scanner _scanner;
    private readonly Quarantine? _quarantine;

    public ScanService(Scanner scanner, Quarantine? quarantine = null)
    {
        _scanner = scanner;
        _quarantine = quarantine;
    }

    /// <summary>
    /// Analyse un fichier ou un dossier (récursif).
    /// </summary>
    /// <param name="onProgress">Rappel optionnel pour chaque fichier analysé (chemin courant, total connu).</param>
    /// <param name="cancel">Jeton d'annulation.</param>
    public ScanReport Scan(string target, Action<FileScanResult>? onProgress = null,
                           CancellationToken cancel = default)
    {
        var report = new ScanReport();

        foreach (string file in EnumerateFiles(target))
        {
            cancel.ThrowIfCancellationRequested();

            var result = _scanner.ScanFile(file);
            report.FilesScanned++;

            if (result.Error is not null)
            {
                report.Errors++;
            }
            else if (result.IsThreat)
            {
                report.Threats.Add(result);
                if (_quarantine is not null)
                {
                    try
                    {
                        string sha = Scanner.ComputeSha256(file);
                        _quarantine.Add(file, result.Match!.Name, sha);
                    }
                    catch
                    {
                        // La mise en quarantaine peut échouer (fichier verrouillé) ;
                        // la détection reste rapportée.
                    }
                }
            }

            onProgress?.Invoke(result);
        }

        return report;
    }

    /// <summary>
    /// Analyse une liste de fichiers en parallèle (plusieurs cœurs CPU).
    /// La liste étant connue à l'avance, la progression est déterminée (pourcentage).
    /// </summary>
    /// <param name="files">Fichiers à analyser (déjà énumérés).</param>
    /// <param name="onProgress">Rappel thread-safe : (résultat, nbAnalysésJusqu'ici).</param>
    /// <param name="maxDegreeOfParallelism">0 ou négatif = laisser le runtime décider.</param>
    /// <param name="cancel">Jeton d'annulation.</param>
    public ScanReport ScanFilesParallel(
        IReadOnlyList<string> files,
        Action<FileScanResult, int>? onProgress = null,
        int maxDegreeOfParallelism = 0,
        CancellationToken cancel = default)
    {
        var report = new ScanReport();
        int scanned = 0;
        var sync = new object();

        var options = new ParallelOptions
        {
            CancellationToken = cancel,
            MaxDegreeOfParallelism = maxDegreeOfParallelism > 0
                ? maxDegreeOfParallelism
                : Math.Max(1, Environment.ProcessorCount - 1)
        };

        Parallel.ForEach(files, options, file =>
        {
            cancel.ThrowIfCancellationRequested();

            var result = _scanner.ScanFile(file);
            int n = Interlocked.Increment(ref scanned);

            lock (sync)
            {
                report.FilesScanned++;
                if (result.Error is not null)
                {
                    report.Errors++;
                }
                else if (result.IsThreat)
                {
                    report.Threats.Add(result);
                    if (_quarantine is not null)
                    {
                        try
                        {
                            string sha = Scanner.ComputeSha256(file);
                            _quarantine.Add(file, result.Match!.Name, sha);
                        }
                        catch { /* fichier verrouillé : détection conservée */ }
                    }
                }
            }

            onProgress?.Invoke(result, n);
        });

        return report;
    }

    /// <summary>Énumère récursivement les fichiers d'une cible (fichier unique ou dossier).</summary>
    public static IEnumerable<string> EnumerateFiles(string target)
    {
        if (File.Exists(target))
        {
            yield return target;
            yield break;
        }

        if (!Directory.Exists(target))
            throw new FileNotFoundException($"Cible introuvable : {target}");

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (string file in Directory.EnumerateFiles(target, "*", options))
            yield return file;
    }
}
