using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using IatechShield.Tools;

namespace IatechShield.Gui;

/// <summary>
/// Génère et transmet un rapport lorsqu'une désinstallation d'IATECH-SHIELD est autorisée
/// par carte d'identité : références du PC, géolocalisation (IP), identité de la carte et
/// horodatage, avec un avertissement. Envoi best-effort : serveur (webhook) ou SMTP si
/// configuré, sinon ouverture d'un e-mail pré-rempli dans le client de messagerie.
/// </summary>
public static class UninstallReport
{
    // Destinataire par défaut (modifiable via le coffre : clé « uninstall » → « email »).
    private const string DefaultEmail = "iatechfutur@iatechfutur.be";

    public static async Task<string> BuildAndSendAsync(CardInfo card)
    {
        string report = await BuildAsync(card);

        // Journalise localement (preuve conservée sur la machine).
        try
        {
            string path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IatechShield", "uninstall-report.txt");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, report);
        }
        catch { }
        try { AccessLog.Record("Désinstallation autorisée", "Carte d'identité (eID)", card.DisplayIdentity, "Rapport envoyé à IATECHFUTUR"); } catch { }

        var cfg = SecretVault.Load("uninstall");
        string email = cfg.GetValueOrDefault("email") ?? DefaultEmail;

        // 1) Webhook serveur si configuré (envoi silencieux).
        string? webhook = cfg.GetValueOrDefault("webhook");
        if (!string.IsNullOrWhiteSpace(webhook))
        {
            try
            {
                using var http = new HttpClient();
                using var content = new StringContent(
                    JsonSerializer.Serialize(new { subject = "IATECH-SHIELD désinstallé", body = report }),
                    Encoding.UTF8, "application/json");
                await http.PostAsync(webhook, content);
                return report;
            }
            catch { /* on tente le repli e-mail */ }
        }

        // 2) SMTP si configuré (host/port/user/pass).
        var smtp = SecretVault.Load("smtp");
        if (!string.IsNullOrWhiteSpace(smtp.GetValueOrDefault("host")))
        {
            try
            {
                using var msg = new System.Net.Mail.MailMessage(
                    smtp.GetValueOrDefault("user") ?? email, email,
                    "⚠ IATECH-SHIELD désinstallé", report);
                using var client = new System.Net.Mail.SmtpClient(smtp["host"])
                {
                    Port = int.TryParse(smtp.GetValueOrDefault("port"), out int p) ? p : 587,
                    EnableSsl = true,
                    Credentials = new System.Net.NetworkCredential(
                        smtp.GetValueOrDefault("user"), smtp.GetValueOrDefault("pass"))
                };
                await client.SendMailAsync(msg);
                return report;
            }
            catch { /* on tente le repli mailto */ }
        }

        // 3) Repli : ouvre un e-mail pré-rempli dans le client de messagerie par défaut.
        try
        {
            string subject = Uri.EscapeDataString("⚠ IATECH-SHIELD désinstallé");
            string body = Uri.EscapeDataString(report.Length > 1800 ? report[..1800] : report);
            Process.Start(new ProcessStartInfo($"mailto:{email}?subject={subject}&body={body}") { UseShellExecute = true });
        }
        catch { }
        return report;
    }

    private static async Task<string> BuildAsync(CardInfo card)
    {
        string geo = await GeolocateAsync();
        var sb = new StringBuilder();
        sb.AppendLine("⚠ AVERTISSEMENT — IATECH-SHIELD PRO a été DÉSINSTALLÉ de ce poste.");
        sb.AppendLine("La désinstallation a été autorisée par insertion d'une carte d'identité.");
        sb.AppendLine();
        sb.AppendLine($"Date / heure : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("— Personne (carte d'identité) —");
        sb.AppendLine($"Nom : {card.Name}");
        sb.AppendLine($"Prénoms : {card.FirstNames}");
        sb.AppendLine($"Date de naissance : {card.BirthDate}");
        sb.AppendLine($"Numéro national : {card.NationalNumber}");
        sb.AppendLine($"Nationalité : {card.Nationality}");
        sb.AppendLine();
        sb.AppendLine("— Ordinateur —");
        sb.AppendLine($"Nom du PC : {Environment.MachineName}");
        sb.AppendLine($"Utilisateur Windows : {Environment.UserName}");
        sb.AppendLine($"Système : {Environment.OSVersion}");
        try
        {
            string host = System.Net.Dns.GetHostName();
            foreach (var ip in System.Net.Dns.GetHostAddresses(host))
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    sb.AppendLine($"IP locale : {ip}");
        }
        catch { }
        sb.AppendLine();
        sb.AppendLine("— Localisation (approximative, via IP) —");
        sb.AppendLine(geo);
        return sb.ToString();
    }

    private static async Task<string> GeolocateAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            string json = await http.GetStringAsync("http://ip-api.com/json/");
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            string Get(string k) => r.TryGetProperty(k, out var v) ? (v.GetString() ?? "") : "";
            double lat = r.TryGetProperty("lat", out var la) ? la.GetDouble() : 0;
            double lon = r.TryGetProperty("lon", out var lo) ? lo.GetDouble() : 0;
            return $"IP publique : {Get("query")}\nPays : {Get("country")}\nRégion : {Get("regionName")}\n" +
                   $"Ville : {Get("city")}\nCode postal : {Get("zip")}\nFournisseur : {Get("isp")}\n" +
                   $"Coordonnées : {lat}, {lon}\nCarte : https://maps.google.com/?q={lat},{lon}";
        }
        catch { return "Localisation indisponible (pas de connexion Internet)."; }
    }
}
