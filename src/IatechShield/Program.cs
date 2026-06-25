using IatechShield.Engine;

namespace IatechShield;

internal static class Program
{
    private const string Product = "IATECH-SHIELD PRO";
    private const string VersionString = "0.1.0";

    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintBanner();
            PrintUsage();
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        string[] rest = args.Skip(1).ToArray();

        try
        {
            return command switch
            {
                "scan"       => CmdScan(rest),
                "watch"      => CmdWatch(rest),
                "quarantine" => CmdQuarantine(rest),
                "version"    => CmdVersion(),
                _            => Unknown(command)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Erreur : {ex.Message}");
            return 1;
        }
    }

    // ---------------------------------------------------------------- scan ---

    private static int CmdScan(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage : iatech-shield scan <chemin> [--quarantine]");
            return 2;
        }

        string target = args[0];
        bool doQuarantine = args.Contains("--quarantine") || args.Contains("-q");

        var db = LoadDatabase();
        var scanner = new Scanner(db);
        var quarantine = doQuarantine ? new Quarantine(DefaultQuarantineDir()) : null;

        PrintBanner();
        Console.WriteLine($"Base de signatures : {db.Signatures.Count} entrées");
        Console.WriteLine($"Cible             : {target}");
        Console.WriteLine($"Quarantaine       : {(doQuarantine ? "activée" : "désactivée")}");
        Console.WriteLine(new string('-', 60));

        int scanned = 0, threats = 0, errors = 0;

        foreach (string file in ScanService.EnumerateFiles(target))
        {
            var result = scanner.ScanFile(file);
            scanned++;

            if (result.Error is not null)
            {
                errors++;
                Console.WriteLine($"  [!] {file} — {result.Error}");
            }
            else if (result.IsThreat)
            {
                threats++;
                Console.WriteLine($"  [MENACE] {file}");
                Console.WriteLine($"           → {result.Match!.Name} (gravité : {result.Match.Severity})");

                if (quarantine is not null)
                {
                    string sha = Scanner.ComputeSha256(file);
                    var entry = quarantine.Add(file, result.Match.Name, sha);
                    Console.WriteLine($"           → mis en quarantaine (id {entry.Id})");
                }
            }
        }

        Console.WriteLine(new string('-', 60));
        Console.WriteLine($"Terminé : {scanned} fichiers analysés, {threats} menace(s), {errors} erreur(s).");
        return threats > 0 ? 1 : 0;
    }

    // --------------------------------------------------------------- watch ---

    private static int CmdWatch(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage : iatech-shield watch <dossier> [--quarantine]");
            return 2;
        }

        string folder = args[0];
        if (!Directory.Exists(folder))
        {
            Console.Error.WriteLine($"Dossier introuvable : {folder}");
            return 2;
        }

        bool doQuarantine = args.Contains("--quarantine") || args.Contains("-q");
        var db = LoadDatabase();
        var scanner = new Scanner(db);
        var quarantine = doQuarantine ? new Quarantine(DefaultQuarantineDir()) : null;

        PrintBanner();
        Console.WriteLine($"Surveillance temps réel de : {folder}");
        Console.WriteLine($"Quarantaine : {(doQuarantine ? "activée" : "désactivée")}");
        Console.WriteLine("Appuyez sur Ctrl+C pour arrêter.");
        Console.WriteLine(new string('-', 60));

        using var monitor = new RealtimeMonitor(scanner, folder);

        monitor.ThreatDetected += result =>
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] MENACE : {result.Path}");
            Console.WriteLine($"          → {result.Match!.Name} (gravité : {result.Match.Severity})");
            if (quarantine is not null)
            {
                try
                {
                    string sha = Scanner.ComputeSha256(result.Path);
                    var entry = quarantine.Add(result.Path, result.Match.Name, sha);
                    Console.WriteLine($"          → mis en quarantaine (id {entry.Id})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"          → échec quarantaine : {ex.Message}");
                }
            }
        };

        monitor.Start();

        // Bloque jusqu'à Ctrl+C.
        var done = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.Set(); };
        done.Wait();

        Console.WriteLine("Surveillance arrêtée.");
        return 0;
    }

    // ---------------------------------------------------------- quarantine ---

    private static int CmdQuarantine(string[] args)
    {
        var quarantine = new Quarantine(DefaultQuarantineDir());
        string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

        switch (sub)
        {
            case "list":
                var entries = quarantine.List();
                if (entries.Count == 0)
                {
                    Console.WriteLine("Quarantaine vide.");
                    return 0;
                }
                Console.WriteLine($"{entries.Count} fichier(s) en quarantaine :");
                foreach (var e in entries)
                {
                    Console.WriteLine($"  {e.Id}  {e.ThreatName}");
                    Console.WriteLine($"      origine : {e.OriginalPath}");
                    Console.WriteLine($"      date    : {e.QuarantinedAt:yyyy-MM-dd HH:mm:ss}");
                }
                return 0;

            case "restore":
                if (args.Length < 2) { Console.Error.WriteLine("Usage : quarantine restore <id>"); return 2; }
                return quarantine.Restore(args[1])
                    ? Ok($"Fichier {args[1]} restauré.")
                    : Fail($"Aucune entrée pour l'id {args[1]}.");

            case "delete":
                if (args.Length < 2) { Console.Error.WriteLine("Usage : quarantine delete <id>"); return 2; }
                return quarantine.Delete(args[1])
                    ? Ok($"Fichier {args[1]} supprimé définitivement.")
                    : Fail($"Aucune entrée pour l'id {args[1]}.");

            default:
                Console.Error.WriteLine("Sous-commandes : list | restore <id> | delete <id>");
                return 2;
        }
    }

    private static int CmdVersion()
    {
        Console.WriteLine($"{Product} {VersionString}");
        return 0;
    }

    // --------------------------------------------------------------- utils ---

    private static SignatureDatabase LoadDatabase()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "signatures.json");
        return SignatureDatabase.LoadFromFile(path);
    }

    private static string DefaultQuarantineDir()
    {
        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "IatechShield", "quarantine");
    }

    private static bool IsHelp(string a) =>
        a is "-h" or "--help" or "help" or "/?";

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Commande inconnue : {command}");
        PrintUsage();
        return 2;
    }

    private static int Ok(string msg)   { Console.WriteLine(msg); return 0; }
    private static int Fail(string msg) { Console.Error.WriteLine(msg); return 1; }

    private static void PrintBanner()
    {
        Console.WriteLine($"=== {Product} {VersionString} ===");
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            Usage : iatech-shield <commande> [options]

            Commandes :
              scan <chemin> [--quarantine]    Analyse un fichier ou un dossier (récursif).
              watch <dossier> [--quarantine]  Surveillance temps réel d'un dossier.
              quarantine list                 Liste les fichiers en quarantaine.
              quarantine restore <id>         Restaure un fichier (faux positif).
              quarantine delete <id>          Supprime définitivement un fichier.
              version                         Affiche la version.
              help                            Affiche cette aide.

            Exemples :
              iatech-shield scan C:\Users\Moi\Downloads --quarantine
              iatech-shield watch C:\Users\Moi\Downloads -q
              iatech-shield quarantine list
            """);
    }
}
