using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MimeKit;

namespace IatechShield.Gui;

/// <summary>Résultat d'analyse d'un e-mail par le Mail Shield.</summary>
public sealed class MailVerdict
{
    public string From { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Date { get; set; } = "";
    public int Score { get; set; }               // 0 = sûr, >100 = très dangereux
    public string Level { get; set; } = "SÛR";   // SÛR / SUSPECT / DANGEREUX
    public List<string> Reasons { get; } = new();
    public List<string> Links { get; } = new();
    public List<string> Attachments { get; } = new();

    public string LevelIcon => Level switch { "DANGEREUX" => "⛔", "SUSPECT" => "⚠️", _ => "✅" };
    public string LevelHex => Level switch { "DANGEREUX" => "#E5484D", "SUSPECT" => "#F5A623", _ => "#3FB950" };
    public string ScoreLine => $"{LevelIcon} {Level} · score {Score}";
    public string ReasonsSummary => string.Join("   ·   ", Reasons.Take(3));
}

/// <summary>
/// Mail Shield : analyse un e-mail (fichier .eml ou texte brut) et calcule un score de
/// risque. Détecte : échec SPF/DKIM/DMARC, usurpation d'identité (display name / Reply-To),
/// liens malveillants (raccourcisseurs, IP, punycode, faux-semblants), pièces jointes
/// dangereuses (exécutables, macros, double extension), fraude au président (BEC) et
/// diffusion de rançongiciels.
/// </summary>
public static class MailShield
{
    private static readonly string[] DangerousExt =
    {
        ".exe", ".scr", ".com", ".pif", ".bat", ".cmd", ".js", ".jse", ".vbs", ".vbe",
        ".wsf", ".wsh", ".hta", ".jar", ".ps1", ".msi", ".msc", ".cpl", ".lnk", ".reg",
        ".iso", ".img", ".vhd"
    };
    private static readonly string[] MacroExt = { ".docm", ".xlsm", ".pptm", ".dotm", ".xlam" };
    private static readonly string[] ArchiveExt = { ".zip", ".rar", ".7z", ".gz", ".ace", ".cab" };

    private static readonly string[] UrlShorteners =
    {
        "bit.ly", "tinyurl.com", "goo.gl", "t.co", "ow.ly", "is.gd", "buff.ly",
        "cutt.ly", "rebrand.ly", "shorturl.at", "rb.gy", "t.ly"
    };

    // Mots-clés typiques de fraude au virement / au président (BEC).
    private static readonly string[] BecKeywords =
    {
        "virement", "wire transfer", "iban", "rib", "bank transfer", "changer de banque",
        "coordonnées bancaires", "gift card", "carte cadeau", "paiement urgent", "facture",
        "invoice", "bon de commande", "confidentiel", "reste discret", "je suis en réunion"
    };
    private static readonly string[] UrgencyKeywords =
    {
        "urgent", "immédiat", "immediately", "aujourd'hui", "asap", "dès que possible",
        "action requise", "dernier avertissement", "expire", "suspendu", "verrouillé", "vérifiez"
    };
    private static readonly string[] BrandKeywords =
    {
        "microsoft", "outlook", "office365", "google", "gmail", "apple", "icloud", "paypal",
        "amazon", "netflix", "belfius", "bnp", "ing", "kbc", "bpost", " overheid", "febelfin",
        "dhl", "fedex", "ups", "facebook", "instagram", "whatsapp", "banque", "bank"
    };
    private static readonly string[] FreeMailDomains =
    {
        "gmail.com", "outlook.com", "hotmail.com", "yahoo.com", "yahoo.fr", "live.com",
        "icloud.com", "proton.me", "protonmail.com", "gmx.com", "aol.com"
    };

    public static MailVerdict AnalyzeFile(string path)
    {
        using var stream = File.OpenRead(path);
        var msg = MimeMessage.Load(stream);
        return Analyze(msg);
    }

    public static MailVerdict AnalyzeRaw(string raw)
    {
        var bytes = Encoding.UTF8.GetBytes(raw);
        using var stream = new MemoryStream(bytes);
        try
        {
            var msg = MimeMessage.Load(stream);
            // Si le collage n'a pas d'en-têtes MIME, on traite tout comme un corps de texte.
            if (msg.From.Count == 0 && string.IsNullOrEmpty(msg.Subject) && msg.Body is null)
                return AnalyzeBodyOnly(raw);
            return Analyze(msg);
        }
        catch
        {
            return AnalyzeBodyOnly(raw);
        }
    }

    private static MailVerdict Analyze(MimeMessage msg)
    {
        var v = new MailVerdict();
        var from = msg.From.Mailboxes.FirstOrDefault();
        string fromAddr = from?.Address ?? "";
        string fromName = from?.Name ?? "";
        string fromDomain = DomainOf(fromAddr);
        v.From = string.IsNullOrEmpty(fromName) ? fromAddr : $"{fromName} <{fromAddr}>";
        v.Subject = msg.Subject ?? "";
        v.Date = msg.Date == default ? "" : msg.Date.LocalDateTime.ToString("dd/MM/yyyy HH:mm");

        // --- Authentification (SPF / DKIM / DMARC) ---
        string auth = (msg.Headers["Authentication-Results"] ?? "") + " " +
                      (msg.Headers["Received-SPF"] ?? "") + " " +
                      (msg.Headers["ARC-Authentication-Results"] ?? "");
        auth = auth.ToLowerInvariant();
        if (auth.Length == 0)
            Add(v, 10, "Aucune information d'authentification (SPF/DKIM/DMARC) présente.");
        else
        {
            if (auth.Contains("spf=fail") || auth.Contains("spf=softfail")) Add(v, 25, "SPF échoué : l'expéditeur n'est pas autorisé par le domaine.");
            if (auth.Contains("dkim=fail")) Add(v, 20, "DKIM échoué : la signature du message est invalide.");
            if (auth.Contains("dmarc=fail")) Add(v, 30, "DMARC échoué : forte présomption d'usurpation du domaine.");
        }

        // --- Usurpation d'identité (display name / Reply-To / Return-Path) ---
        string nameLower = fromName.ToLowerInvariant();
        // Le nom affiché contient une adresse e-mail d'un autre domaine.
        var mailInName = Regex.Match(fromName, @"[\w\.\-]+@([\w\.\-]+)");
        if (mailInName.Success && !string.Equals(mailInName.Groups[1].Value, fromDomain, StringComparison.OrdinalIgnoreCase))
            Add(v, 30, $"Le nom affiché imite une adresse ({mailInName.Value}) d'un autre domaine que l'expéditeur réel ({fromDomain}).");
        // Le nom affiché cite une marque mais le domaine n'y correspond pas.
        foreach (var brand in BrandKeywords)
            if (nameLower.Contains(brand) && fromDomain.Length > 0 && !fromDomain.Contains(brand))
            {
                Add(v, 20, $"Le nom affiché évoque « {brand} » mais le domaine réel est « {fromDomain} ».");
                break;
            }
        // Reply-To d'un autre domaine que From.
        var replyTo = msg.ReplyTo.Mailboxes.FirstOrDefault();
        if (replyTo is not null && DomainOf(replyTo.Address) is { Length: > 0 } rd &&
            !string.Equals(rd, fromDomain, StringComparison.OrdinalIgnoreCase))
            Add(v, 15, $"L'adresse de réponse ({rd}) diffère du domaine expéditeur ({fromDomain}).");
        // Return-Path différent.
        string returnPath = msg.Headers["Return-Path"] ?? "";
        if (returnPath.Length > 0 && fromDomain.Length > 0 &&
            !returnPath.ToLowerInvariant().Contains(fromDomain.ToLowerInvariant()))
            Add(v, 10, "Le chemin de retour (Return-Path) ne correspond pas au domaine expéditeur.");

        // --- Corps : texte + liens ---
        string text = (msg.TextBody ?? "") + "\n" + StripHtml(msg.HtmlBody ?? "");
        AnalyzeLinks(v, msg.HtmlBody ?? "", text, fromDomain);
        AnalyzeContent(v, text, fromDomain);

        // --- Pièces jointes ---
        foreach (var att in msg.Attachments)
        {
            string fname = att.ContentDisposition?.FileName ?? att.ContentType?.Name ?? "pièce jointe";
            v.Attachments.Add(fname);
            AnalyzeAttachment(v, fname);
        }

        Finalize(v);
        return v;
    }

    private static MailVerdict AnalyzeBodyOnly(string raw)
    {
        var v = new MailVerdict { Subject = "(texte collé)" };
        AnalyzeLinks(v, raw, raw, "");
        AnalyzeContent(v, raw, "");
        Finalize(v);
        return v;
    }

    private static void AnalyzeLinks(MailVerdict v, string html, string text, string fromDomain)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(text, @"https?://[^\s""'<>\)]+")) urls.Add(m.Value);
        // Liens HTML avec texte affiché (détection du faux-semblant href/texte).
        foreach (Match m in Regex.Matches(html, "<a\\s[^>]*href\\s*=\\s*[\"']([^\"']+)[\"'][^>]*>(.*?)</a>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string href = m.Groups[1].Value.Trim();
            string shown = StripHtml(m.Groups[2].Value).Trim();
            urls.Add(href);
            var shownDomain = Regex.Match(shown, @"([\w\-]+\.)+[\w\-]{2,}");
            string hrefDomain = DomainOf(href);
            if (shownDomain.Success && hrefDomain.Length > 0 &&
                !hrefDomain.EndsWith(shownDomain.Value, StringComparison.OrdinalIgnoreCase) &&
                !shownDomain.Value.EndsWith(hrefDomain, StringComparison.OrdinalIgnoreCase))
                Add(v, 25, $"Lien trompeur : le texte affiche « {shownDomain.Value} » mais pointe vers « {hrefDomain} ».");
        }

        foreach (var url in urls.Take(40))
        {
            v.Links.Add(url);
            string d = DomainOf(url);
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                Add(v, 8, $"Lien non chiffré (http) : {d}");
            if (UrlShorteners.Any(s => d.Equals(s, StringComparison.OrdinalIgnoreCase)))
                Add(v, 15, $"Lien raccourci masquant la destination : {d}");
            if (Regex.IsMatch(d, @"^\d{1,3}(\.\d{1,3}){3}$"))
                Add(v, 25, $"Lien pointant vers une adresse IP brute : {d}");
            if (d.StartsWith("xn--", StringComparison.OrdinalIgnoreCase) || d.Contains(".xn--"))
                Add(v, 20, $"Domaine en punycode (caractères trompeurs) : {d}");
            if (Regex.IsMatch(url, @"@", RegexOptions.None) && url.IndexOf('@') < url.IndexOf('/', 8 < url.Length ? 8 : 0) + 1 && url.Contains("://") && url.Split('/')[2].Contains('@'))
                Add(v, 20, $"URL contenant un « @ » (redirection trompeuse) : {d}");
        }
    }

    private static void AnalyzeContent(MailVerdict v, string text, string fromDomain)
    {
        string t = text.ToLowerInvariant();
        bool urgency = UrgencyKeywords.Any(k => t.Contains(k));
        int becHits = BecKeywords.Count(k => t.Contains(k));

        if (becHits >= 2 && urgency)
            Add(v, 30, "Sign-aux de fraude au virement (BEC) : demande financière + ton d'urgence.");
        else if (becHits >= 1 && urgency)
            Add(v, 15, "Vocabulaire financier associé à un ton d'urgence.");

        // Demande de secret + urgence = fraude au président classique.
        if ((t.Contains("confidentiel") || t.Contains("reste discret") || t.Contains("ne parle")) && urgency)
            Add(v, 20, "Demande de confidentialité inhabituelle associée à l'urgence (fraude au président).");

        // Prétend être un dirigeant depuis une messagerie gratuite.
        if ((t.Contains("ceo") || t.Contains("directeur") || t.Contains("président") || t.Contains("patron")) &&
            FreeMailDomains.Any(d => d.Equals(fromDomain, StringComparison.OrdinalIgnoreCase)))
            Add(v, 20, "Se présente comme un dirigeant tout en écrivant depuis une messagerie gratuite.");

        // Demande d'identifiants.
        if (t.Contains("mot de passe") || t.Contains("password") || t.Contains("identifiant") ||
            t.Contains("carte de crédit") || t.Contains("numéro de carte"))
            Add(v, 12, "Demande d'informations sensibles (identifiants / carte).");
    }

    private static void AnalyzeAttachment(MailVerdict v, string fname)
    {
        string lower = fname.ToLowerInvariant();
        string ext = Path.GetExtension(lower);

        if (DangerousExt.Contains(ext))
            Add(v, 40, $"Pièce jointe exécutable dangereuse : {fname}");
        else if (MacroExt.Contains(ext))
            Add(v, 30, $"Document Office à macros (vecteur fréquent de rançongiciel) : {fname}");
        else if (ArchiveExt.Contains(ext))
            Add(v, 12, $"Archive compressée (contenu à vérifier) : {fname}");

        // Double extension (ex. « facture.pdf.exe »).
        var parts = lower.Split('.');
        if (parts.Length >= 3)
        {
            string secondExt = "." + parts[^2];
            if (new[] { ".pdf", ".doc", ".docx", ".jpg", ".png", ".xls", ".txt" }.Contains(secondExt) &&
                (DangerousExt.Contains(ext) || MacroExt.Contains(ext)))
                Add(v, 25, $"Double extension trompeuse : {fname}");
        }
    }

    private static void Finalize(MailVerdict v)
    {
        v.Level = v.Score >= 60 ? "DANGEREUX" : v.Score >= 25 ? "SUSPECT" : "SÛR";
        if (v.Reasons.Count == 0)
            v.Reasons.Add("Aucun signe de danger détecté par les heuristiques.");
    }

    private static void Add(MailVerdict v, int points, string reason)
    {
        v.Score += points;
        v.Reasons.Add(reason);
    }

    private static string DomainOf(string addrOrUrl)
    {
        if (string.IsNullOrWhiteSpace(addrOrUrl)) return "";
        try
        {
            if (addrOrUrl.Contains("://")) return new Uri(addrOrUrl).Host.ToLowerInvariant();
            int at = addrOrUrl.LastIndexOf('@');
            return at >= 0 ? addrOrUrl[(at + 1)..].Trim().TrimEnd('>').ToLowerInvariant() : "";
        }
        catch { return ""; }
    }

    private static string StripHtml(string html) =>
        string.IsNullOrEmpty(html) ? "" : Regex.Replace(html, "<[^>]+>", " ");
}
