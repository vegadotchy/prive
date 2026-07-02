using System.Security.Cryptography.X509Certificates;

namespace IatechShield.Tools;

/// <summary>Profil « ADN » d'un programme : indice de confiance + facteurs expliqués.</summary>
public sealed record TrustProfile(
    string Name,
    string Path,
    int Score,
    string Publisher,
    string Signature,
    string Age,
    string Location,
    string Behavior);

/// <summary>
/// Calcule un « indice de confiance » (0-100) pour un programme à partir de signaux
/// réels : signature Authenticode (éditeur), emplacement du fichier, âge, démarrage auto.
/// </summary>
public static class TrustIndex
{
    public static TrustProfile Evaluate(string name, string path, bool autostart)
    {
        int score = 50;
        string publisher = "Inconnu";
        string signature = "Non signé";
        string age = "—";
        string location;

        // Signature numérique (Authenticode) : présence + éditeur.
        if (File.Exists(path))
        {
            try
            {
                var cert = X509Certificate.CreateFromSignedFile(path);
                publisher = ParseCn(cert.Subject);
                signature = "✓ Signé";
                score += 28;
            }
            catch
            {
                signature = "Non signé";
                score -= 18;
            }

            try
            {
                var info = new FileInfo(path);
                int days = (int)(DateTime.Now - info.CreationTime).TotalDays;
                age = days <= 0 ? "récent (aujourd'hui)" : days < 30 ? $"{days} j" : $"{days / 30} mois";
                if (days < 3) score -= 8;       // tout neuf = un peu plus suspect
                else if (days > 180) score += 8; // ancien et stable
            }
            catch { }
        }
        else
        {
            signature = "Fichier introuvable";
            score -= 10;
        }

        string p = path.ToLowerInvariant();
        if (p.Contains(@"\windows\") || p.Contains(@"\program files"))
        {
            location = "Système / éditeur";
            score += 18;
        }
        else if (p.Contains(@"\temp\") || p.Contains(@"\appdata\local\temp") || p.Contains(@"\downloads"))
        {
            location = "Dossier temporaire / téléchargements";
            score -= 22;
        }
        else if (p.Contains(@"\appdata\"))
        {
            location = "Données utilisateur (AppData)";
            score -= 6;
        }
        else
        {
            location = "Autre emplacement";
        }

        string behavior = autostart ? "Démarrage automatique avec Windows" : "Lancement manuel";
        if (autostart && score < 60) score -= 4;

        return new TrustProfile(name, path, Math.Clamp(score, 5, 100),
            publisher, signature, age, location, behavior);
    }

    private static string ParseCn(string subject)
    {
        // subject ex. "CN=Microsoft Corporation, O=..., L=..."
        foreach (var part in subject.Split(','))
        {
            string t = part.Trim();
            if (t.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                return t[3..].Trim();
        }
        return string.IsNullOrWhiteSpace(subject) ? "Inconnu" : subject;
    }
}
