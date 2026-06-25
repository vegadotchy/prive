using System.Text.RegularExpressions;

namespace IatechShield.Tools;

/// <summary>Type d'adresse de cryptomonnaie reconnu dans le presse-papiers.</summary>
public enum CryptoKind { None, Bitcoin, Ethereum, Litecoin, TronOrBnb }

/// <summary>
/// Logique de détection des « clipboard hijackers » : malwares qui remplacent
/// silencieusement une adresse de cryptomonnaie copiée par celle de l'attaquant.
/// Cette classe ne fait que classifier/comparer ; l'écoute du presse-papiers est
/// gérée côté interface (message Windows WM_CLIPBOARDUPDATE).
/// </summary>
public static partial class ClipboardGuard
{
    [GeneratedRegex(@"^(bc1[a-z0-9]{20,80}|[13][a-km-zA-HJ-NP-Z1-9]{25,39})$")]
    private static partial Regex Bitcoin();

    [GeneratedRegex(@"^0x[a-fA-F0-9]{40}$")]
    private static partial Regex Ethereum();

    [GeneratedRegex(@"^(ltc1[a-z0-9]{20,80}|[LM3][a-km-zA-HJ-NP-Z1-9]{26,33})$")]
    private static partial Regex Litecoin();

    [GeneratedRegex(@"^(T[1-9A-HJ-NP-Za-km-z]{33}|bnb[a-z0-9]{38,})$")]
    private static partial Regex TronBnb();

    /// <summary>Reconnaît une adresse de cryptomonnaie isolée dans un texte.</summary>
    public static CryptoKind Classify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return CryptoKind.None;
        string t = text.Trim();
        if (t.Length is < 26 or > 100 || t.Contains(' ') || t.Contains('\n')) return CryptoKind.None;

        if (Bitcoin().IsMatch(t)) return CryptoKind.Bitcoin;
        if (Ethereum().IsMatch(t)) return CryptoKind.Ethereum;
        if (Litecoin().IsMatch(t)) return CryptoKind.Litecoin;
        if (TronBnb().IsMatch(t)) return CryptoKind.TronOrBnb;
        return CryptoKind.None;
    }

    public static string Label(CryptoKind kind) => kind switch
    {
        CryptoKind.Bitcoin => "Bitcoin",
        CryptoKind.Ethereum => "Ethereum",
        CryptoKind.Litecoin => "Litecoin",
        CryptoKind.TronOrBnb => "Tron/BNB",
        _ => ""
    };
}
