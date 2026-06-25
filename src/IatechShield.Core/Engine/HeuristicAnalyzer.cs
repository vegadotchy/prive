using System.Buffers.Binary;

namespace IatechShield.Engine;

/// <summary>Résultat d'une analyse heuristique.</summary>
public sealed record HeuristicResult(bool Suspicious, string Reason, double Entropy);

/// <summary>
/// Détection heuristique (sans signature) : repère les exécutables potentiellement
/// malveillants par leur entropie (fichiers compressés/chiffrés = souvent des
/// malwares « packés ») et quelques marqueurs structurels du format PE.
///
/// Conçu pour limiter les faux positifs : ne s'applique qu'aux exécutables.
/// </summary>
public static class HeuristicAnalyzer
{
    private const long MaxBytes = 8L * 1024 * 1024;   // échantillon max pour l'entropie
    private const double PackedEntropyThreshold = 7.2; // 0..8 ; >7.2 ≈ compressé/chiffré

    private static readonly string[] ExecutableExtensions =
    { ".exe", ".dll", ".scr", ".com", ".sys", ".ocx", ".cpl" };

    public static HeuristicResult Analyze(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0)
                return new HeuristicResult(false, "", 0);

            bool extExe = ExecutableExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

            byte[] sample = ReadSample(path, MaxBytes);
            bool isPe = sample.Length >= 2 && sample[0] == 0x4D && sample[1] == 0x5A; // 'MZ'

            if (!extExe && !isPe)
                return new HeuristicResult(false, "", 0);

            double entropy = ShannonEntropy(sample);

            if (entropy >= PackedEntropyThreshold)
                return new HeuristicResult(true,
                    $"exécutable compressé/chiffré (entropie {entropy:0.0}/8)", entropy);

            // Exécutable dont l'extension ne correspond pas à un en-tête PE.
            if (extExe && !isPe)
                return new HeuristicResult(true, "extension exécutable sans en-tête PE valide", entropy);

            return new HeuristicResult(false, "", entropy);
        }
        catch
        {
            return new HeuristicResult(false, "", 0);
        }
    }

    private static byte[] ReadSample(string path, long max)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        int len = (int)Math.Min(max, fs.Length);
        var buffer = new byte[len];
        int read = 0;
        while (read < len)
        {
            int n = fs.Read(buffer, read, len - read);
            if (n == 0) break;
            read += n;
        }
        return read == len ? buffer : buffer[..read];
    }

    private static double ShannonEntropy(byte[] data)
    {
        if (data.Length == 0) return 0;
        Span<int> counts = stackalloc int[256];
        foreach (byte b in data) counts[b]++;

        double entropy = 0;
        foreach (int c in counts)
        {
            if (c == 0) continue;
            double p = (double)c / data.Length;
            entropy -= p * Math.Log2(p);
        }
        return entropy;
    }
}
