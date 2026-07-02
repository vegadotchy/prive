using System.Security.Cryptography;
using IatechShield.Licensing;

namespace IatechShield.KeyGen;

/// <summary>
/// Générateur de licences — OUTIL ÉDITEUR (vendeur). Détient la clé privée et
/// produit des clés de licence signées. NE JAMAIS distribuer la clé privée ni
/// cet outil aux clients.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "init"   => CmdInit(args.Skip(1).ToArray()),
                "issue"  => CmdIssue(args.Skip(1).ToArray()),
                "verify" => CmdVerify(args.Skip(1).ToArray()),
                _        => Unknown(args[0])
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Erreur : {ex.Message}");
            return 1;
        }
    }

    // ---------------------------------------------------------------- init ---

    private static int CmdInit(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : "keys";
        Directory.CreateDirectory(outDir);

        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string privatePem = ec.ExportPkcs8PrivateKeyPem();
        string publicPem = ec.ExportSubjectPublicKeyInfoPem();

        string privPath = Path.Combine(outDir, "private.pem");
        string pubPath = Path.Combine(outDir, "public_key.pem");
        File.WriteAllText(privPath, privatePem);
        File.WriteAllText(pubPath, publicPem);

        Console.WriteLine("Nouvelle paire de clés générée.");
        Console.WriteLine($"  Clé privée  : {privPath}   (SECRÈTE — ne jamais partager ni committer)");
        Console.WriteLine($"  Clé publique: {pubPath}");
        Console.WriteLine();
        Console.WriteLine("Pour activer ces clés dans l'application :");
        Console.WriteLine("  copiez le contenu de public_key.pem dans");
        Console.WriteLine("  src/IatechShield.Core/Licensing/public_key.pem, puis recompilez l'app.");
        return 0;
    }

    // --------------------------------------------------------------- issue ---

    private static int CmdIssue(string[] args)
    {
        var opts = ParseOptions(args);
        string tierStr = opts.GetValueOrDefault("tier", "lifetime");
        string keyPath = opts.GetValueOrDefault("key", Path.Combine("keys", "private.pem"));

        if (!File.Exists(keyPath))
        {
            Console.Error.WriteLine($"Clé privée introuvable : {keyPath}");
            Console.Error.WriteLine("Lancez d'abord : iatech-keygen init");
            return 2;
        }

        LicenseTier tier = tierStr.ToLowerInvariant() switch
        {
            "monthly" or "mensuel" or "mois" => LicenseTier.Monthly,
            "yearly" or "annuel" or "an"     => LicenseTier.Yearly,
            "lifetime" or "avie" or "vie"    => LicenseTier.Lifetime,
            "trial" or "essai"               => LicenseTier.Trial,
            _ => throw new ArgumentException($"Tier inconnu : {tierStr} (monthly|yearly|lifetime)")
        };

        var now = DateTimeOffset.UtcNow;
        License license = License.Create(tier, now);

        // Durée personnalisée optionnelle (--days N) pour les cas spéciaux.
        if (opts.TryGetValue("days", out string? daysStr) && int.TryParse(daysStr, out int days))
        {
            license = new License
            {
                Tier = tier,
                IssuedUtc = now,
                ExpiresUtc = now.AddDays(days),
                LicenseId = license.LicenseId
            };
        }

        using var ec = ECDsa.Create();
        ec.ImportFromPem(File.ReadAllText(keyPath));

        byte[] payload = LicenseCodec.BuildPayload(license);
        byte[] signature = ec.SignData(payload, HashAlgorithmName.SHA256,
                                       DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        string licenseKey = LicenseCodec.Encode(payload, signature);

        Console.WriteLine($"Licence  : {license.TierLabel}");
        Console.WriteLine($"Émise le : {license.IssuedUtc:yyyy-MM-dd}");
        Console.WriteLine($"Expire   : {(license.IsLifetime ? "jamais (à vie)" : license.ExpiresUtc?.ToString("yyyy-MM-dd"))}");
        Console.WriteLine($"ID       : {license.LicenseId}");
        Console.WriteLine();
        Console.WriteLine("CLÉ DE LICENCE (à remettre au client) :");
        Console.WriteLine();
        Console.WriteLine("  " + licenseKey);
        return 0;
    }

    // -------------------------------------------------------------- verify ---

    private static int CmdVerify(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage : iatech-keygen verify <clé>");
            return 2;
        }

        var check = new LicenseVerifier().Verify(args[0], DateTimeOffset.UtcNow);
        Console.WriteLine(check.Valid ? "VALIDE" : "INVALIDE");
        Console.WriteLine(check.Message);
        if (check.License is { } l)
            Console.WriteLine($"  {l.TierLabel}, expire : {(l.IsLifetime ? "jamais" : l.ExpiresUtc?.ToString("yyyy-MM-dd"))}");
        return check.Valid ? 0 : 1;
    }

    // --------------------------------------------------------------- utils ---

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--") && i + 1 < args.Length)
            {
                opts[args[i][2..]] = args[i + 1];
                i++;
            }
        }
        return opts;
    }

    private static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"Commande inconnue : {cmd}");
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            IATECH-SHIELD PRO — Générateur de licences (outil éditeur)

            Commandes :
              init [dossier]                       Génère une paire de clés (privée + publique).
              issue --tier <monthly|yearly|lifetime> [--days N] [--key chemin]
                                                   Produit une clé de licence signée.
              verify <clé>                         Vérifie une clé avec la clé publique embarquée.

            Exemples :
              iatech-keygen init
              iatech-keygen issue --tier lifetime
              iatech-keygen issue --tier monthly
              iatech-keygen issue --tier yearly

            ATTENTION : gardez keys/private.pem SECRÈTE. Quiconque la possède peut
            générer des licences valides. Ne la committez jamais.
            """);
    }
}
