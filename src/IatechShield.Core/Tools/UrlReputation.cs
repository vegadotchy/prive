namespace IatechShield.Tools;

/// <summary>Verdict de réputation d'une URL.</summary>
public enum UrlBand { Safe, Suspicious, Dangerous }

/// <summary>Résultat de l'analyse d'une URL.</summary>
public sealed class UrlVerdict
{
    public string Url { get; init; } = "";
    public int Score { get; init; }          // 0 (dangereux) .. 100 (fiable)
    public UrlBand Band { get; init; }
    public List<string> Reasons { get; } = new();

    public string BandLabel => Band switch
    {
        UrlBand.Safe => "FIABLE",
        UrlBand.Suspicious => "À SURVEILLER",
        _ => "DANGEREUX"
    };
}

/// <summary>
/// Évalue le risque d'une URL par heuristiques (pas d'appel réseau) : protocole,
/// IP littérale, punycode, TLD à risque, marques usurpées (typosquatting), etc.
/// </summary>
public static class UrlReputation
{
    private static readonly string[] RiskyTlds =
    { ".zip", ".mov", ".tk", ".ml", ".ga", ".cf", ".gq", ".top", ".xyz", ".click", ".country", ".kim" };

    private static readonly string[] Brands =
    { "paypal", "microsoft", "apple", "google", "amazon", "netflix", "steam", "facebook",
      "instagram", "bank", "banque", "office365", "outlook", "impots" };

    public static UrlVerdict Analyze(string input)
    {
        var reasons = new List<string>();
        int score = 100;

        string raw = input.Trim();
        if (!raw.Contains("://"))
            raw = "http://" + raw;

        if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri))
        {
            return new UrlVerdict { Url = input, Score = 0, Band = UrlBand.Dangerous }
                .With(reasons, "URL invalide / non analysable (−100)");
        }

        string host = uri.Host.ToLowerInvariant();
        string fullLower = raw.ToLowerInvariant();

        if (uri.Scheme == "http")
        {
            score -= 20;
            reasons.Add("Connexion non chiffrée (http) (−20)");
        }

        if (System.Net.IPAddress.TryParse(host, out _))
        {
            score -= 35;
            reasons.Add("Adresse IP brute au lieu d'un nom de domaine (−35)");
        }

        if (host.StartsWith("xn--") || host.Contains(".xn--"))
        {
            score -= 30;
            reasons.Add("Domaine en punycode (risque d'homographe) (−30)");
        }

        if (raw.Contains('@'))
        {
            score -= 30;
            reasons.Add("Caractère '@' dans l'URL (technique de tromperie) (−30)");
        }

        if (RiskyTlds.Any(t => host.EndsWith(t)))
        {
            score -= 25;
            reasons.Add("Extension de domaine à risque (−25)");
        }

        int dots = host.Count(c => c == '.');
        if (dots >= 4)
        {
            score -= 15;
            reasons.Add("Trop de sous-domaines (−15)");
        }

        if (host.Length > 40)
        {
            score -= 10;
            reasons.Add("Nom d'hôte anormalement long (−10)");
        }

        // Marque connue présente, mais pas dans le domaine enregistrable -> typosquat probable.
        string registrable = RegistrableDomain(host);
        foreach (string brand in Brands)
        {
            if (fullLower.Contains(brand) && !registrable.Contains(brand))
            {
                score -= 30;
                reasons.Add($"Marque « {brand} » usurpée hors du vrai domaine (−30)");
                break;
            }
        }

        if (reasons.Count == 0)
            reasons.Add("Aucun signal de risque détecté.");

        score = Math.Clamp(score, 0, 100);
        return new UrlVerdict
        {
            Url = input,
            Score = score,
            Band = score >= 80 ? UrlBand.Safe : score >= 45 ? UrlBand.Suspicious : UrlBand.Dangerous
        }.With(reasons);
    }

    private static string RegistrableDomain(string host)
    {
        var parts = host.Split('.');
        return parts.Length >= 2 ? $"{parts[^2]}.{parts[^1]}" : host;
    }

    private static UrlVerdict With(this UrlVerdict v, List<string> reasons, string? extra = null)
    {
        if (extra is not null) reasons.Add(extra);
        v.Reasons.AddRange(reasons);
        return v;
    }
}
