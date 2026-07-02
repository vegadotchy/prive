using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace IatechShield.Engine;

/// <summary>Bande de confiance d'une application.</summary>
public enum TrustBand
{
    Dangerous,   // 0-39
    Watch,       // 40-79
    Trusted      // 80-100
}

/// <summary>Résultat de l'évaluation de confiance d'un exécutable.</summary>
public sealed class TrustScore
{
    public string Path { get; init; } = "";
    public int Score { get; init; }
    public TrustBand Band { get; init; }
    public string? Publisher { get; init; }
    public bool SignatureValid { get; init; }
    public List<string> Reasons { get; } = new();
}

/// <summary>
/// Attribue à chaque exécutable une note de confiance /100 combinant la validité
/// de la signature Authenticode, l'éditeur, l'emplacement et quelques heuristiques.
/// </summary>
public sealed class AppTrustScorer
{
    // Éditeurs largement reconnus (liste de départ, extensible).
    private static readonly string[] TrustedPublishers =
    {
        "Microsoft", "Google", "Mozilla", "Apple", "Adobe", "Oracle",
        "Intel", "NVIDIA", "Valve", "Dropbox", "Cisco", "Canonical"
    };

    public TrustScore Evaluate(string path)
    {
        var reasons = new List<string>();
        int score = 50; // neutre au départ
        string? publisher = null;
        bool sigValid = false;

        // 1) Signature Authenticode.
        var sig = Authenticode.Verify(path, out publisher);
        switch (sig)
        {
            case SignatureStatus.Valid:
                sigValid = true;
                score += 40;
                reasons.Add("Signature numérique valide (+40)");
                break;
            case SignatureStatus.Invalid:
                score -= 35;
                reasons.Add("Signature présente mais INVALIDE (−35)");
                break;
            case SignatureStatus.Unsigned:
                score -= 40;
                reasons.Add("Aucune signature numérique (−40)");
                break;
            default:
                reasons.Add("Vérification de signature impossible");
                break;
        }

        // 2) Éditeur connu.
        if (publisher is not null &&
            TrustedPublishers.Any(p => publisher.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            score += 25;
            reasons.Add($"Éditeur reconnu : {publisher} (+25)");
        }
        else if (publisher is not null)
        {
            reasons.Add($"Éditeur : {publisher}");
        }

        // 3) Emplacement.
        string lower = path.ToLowerInvariant();
        if (lower.Contains(@"\program files") || lower.Contains(@"\windows\"))
        {
            score += 15;
            reasons.Add("Emplacement système sain (+15)");
        }
        else if (lower.Contains(@"\temp\") || lower.Contains(@"\downloads\") ||
                 lower.Contains(@"\appdata\local\temp"))
        {
            score -= 20;
            reasons.Add("Emplacement temporaire/téléchargement (−20)");
        }

        // 4) Extension trompeuse (double extension type "facture.pdf.exe").
        string name = Path.GetFileName(lower);
        if (name.Contains(".pdf.exe") || name.Contains(".doc.exe") ||
            name.Contains(".jpg.exe") || name.Contains(".scr"))
        {
            score -= 25;
            reasons.Add("Nom de fichier trompeur (−25)");
        }

        // 5) Nouveauté du fichier (récemment créé = légèrement suspect).
        try
        {
            var created = File.GetCreationTime(path);
            if ((DateTime.Now - created).TotalDays < 2)
            {
                score -= 8;
                reasons.Add("Fichier très récent (−8)");
            }
        }
        catch { /* ignore */ }

        score = Math.Clamp(score, 0, 100);

        return new TrustScore
        {
            Path = path,
            Score = score,
            Band = score >= 80 ? TrustBand.Trusted : score >= 40 ? TrustBand.Watch : TrustBand.Dangerous,
            Publisher = publisher,
            SignatureValid = sigValid
        }.WithReasons(reasons);
    }
}

internal static class TrustScoreExtensions
{
    public static TrustScore WithReasons(this TrustScore s, List<string> reasons)
    {
        s.Reasons.AddRange(reasons);
        return s;
    }
}

internal enum SignatureStatus { Valid, Invalid, Unsigned, Error }

/// <summary>
/// Vérifie la signature Authenticode embarquée d'un fichier via WinVerifyTrust
/// (Windows uniquement). Sur les autres systèmes, renvoie Error.
/// </summary>
internal static class Authenticode
{
    public static SignatureStatus Verify(string path, out string? publisher)
    {
        publisher = null;

        if (!OperatingSystem.IsWindows())
            return SignatureStatus.Error;

        // Récupère l'éditeur depuis le certificat signataire (si présent).
        try
        {
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            publisher = cert.GetNameInfo(X509NameType.SimpleName, false);
        }
        catch
        {
            // Pas de certificat embarqué => non signé.
            return SignatureStatus.Unsigned;
        }

        // Vérifie la chaîne de confiance complète via WinVerifyTrust.
        try
        {
            uint result = WinVerify(path);
            return result == 0 ? SignatureStatus.Valid : SignatureStatus.Invalid;
        }
        catch
        {
            return SignatureStatus.Error;
        }
    }

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private static uint WinVerify(string path)
    {
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = path,
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero
        };

        IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fileInfo, pFile, false);

            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = 2,            // WTD_UI_NONE
                fdwRevocationChecks = 0,   // WTD_REVOKE_NONE
                dwUnionChoice = 1,         // WTD_CHOICE_FILE
                pFile = pFile,
                dwStateAction = 0,
                dwProvFlags = 0x00000010   // WTD_SAFER_FLAG
            };

            return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, data);
        }
        finally
        {
            Marshal.FreeHGlobal(pFile);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
                                              WINTRUST_DATA data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
    }
}
