using System.Text;
using Anthropic;
using Anthropic.Models.Messages;
using IatechShield.Tools;

namespace IatechShield.Ai;

/// <summary>
/// Assistant de sécurité propulsé par Claude (Anthropic). Analyse des menaces,
/// des rapports de scan ou des questions de sécurité et propose des actions.
///
/// Nécessite une clé API dans la variable d'environnement ANTHROPIC_API_KEY.
/// </summary>
public sealed class AiAssistant
{
    private const string Persona =
        "Tu es l'assistant de sécurité intégré à IATECH-SHIELD PRO, un antivirus. " +
        "Réponds en français, de façon concise et actionnable. Donne des recommandations " +
        "concrètes (mettre en quarantaine, analyser, ignorer) et explique le risque " +
        "simplement. Ne fournis jamais d'aide à la création de logiciels malveillants.";

    /// <summary>Clé enregistrée dans l'application (chiffrée DPAPI), ou variable d'environnement.</summary>
    public static string? ResolveApiKey()
    {
        string? stored = SecretVault.Load("ai").GetValueOrDefault("key");
        if (!string.IsNullOrWhiteSpace(stored)) return stored.Trim();
        string? env = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        return string.IsNullOrWhiteSpace(env) ? null : env.Trim();
    }

    /// <summary>Enregistre la clé API dans le coffre local chiffré (DPAPI).</summary>
    public static void SaveApiKey(string key)
        => SecretVault.Save("ai", new Dictionary<string, string> { ["key"] = key.Trim() });

    public static void ClearApiKey() => SecretVault.Delete("ai");

    /// <summary>Vrai si la clé API est configurée (app ou variable d'environnement).</summary>
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ResolveApiKey());

    /// <summary>#1 — Explique une menace en langage clair (rôle, risque, danger, action).</summary>
    public Task<string> ExplainThreatAsync(string name, string path, string details, CancellationToken cancel = default)
        => AskAsync(
            "Explique cette détection à un utilisateur non technique. Structure ta réponse :\n" +
            "1) Ce que fait probablement ce fichier ;\n" +
            "2) Niveau de risque (Faible / Moyen / Élevé / Critique) ;\n" +
            "3) Pourquoi c'est dangereux ;\n" +
            "4) Action recommandée.\n" +
            "Sois bref.\n\n" +
            $"Nom de la détection : {name}\nChemin : {path}\nIndices techniques : {details}", cancel);

    /// <summary>#7 — Analyse un e-mail / SMS / URL pour détecter le phishing ou l'arnaque.</summary>
    public Task<string> AnalyzeScamAsync(string content, CancellationToken cancel = default)
        => AskAsync(
            "Analyse ce contenu (e-mail, SMS ou page web/URL) pour détecter le phishing ou l'arnaque. " +
            "Commence par une ligne « VERDICT : Sûr / Suspect / Dangereux », puis liste les signaux " +
            "détectés (liens trompeurs, urgence, fautes, demande d'informations…) et donne un conseil clair.\n\n" +
            $"Contenu à analyser :\n\"\"\"\n{content}\n\"\"\"", cancel);

    /// <summary>#10 — Estime une probabilité de malveillance à partir de caractéristiques (sans signature).</summary>
    public Task<string> AssessFileAsync(string metadata, CancellationToken cancel = default)
        => AskAsync(
            "À partir de ces caractéristiques techniques d'un fichier (aucune signature connue ne correspond), " +
            "estime une PROBABILITÉ de malveillance en pourcentage (commence par « Probabilité : X % ») " +
            "puis explique en deux ou trois points les facteurs qui justifient cette estimation.\n\n" +
            $"Caractéristiques :\n{metadata}", cancel);

    /// <summary>Envoie une question/un contexte à Claude et renvoie la réponse texte.</summary>
    public async Task<string> AskAsync(string prompt, CancellationToken cancel = default)
    {
        string? apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            return "Assistant IA non configuré : collez votre clé Anthropic dans Réglages → Assistant IA " +
                   "(ou définissez la variable d'environnement ANTHROPIC_API_KEY).";

        // Le SDK lit ANTHROPIC_API_KEY : on l'alimente avec la clé enregistrée dans l'app.
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", apiKey);
        var client = new AnthropicClient();

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = Model.ClaudeOpus4_8,
            MaxTokens = 1024,
            Messages =
            [
                new()
                {
                    Role = Role.User,
                    Content = $"{Persona}\n\n---\n\n{prompt}"
                }
            ]
        });

        var sb = new StringBuilder();
        foreach (var text in response.Content.Select(b => b.Value).OfType<TextBlock>())
            sb.AppendLine(text.Text);

        string answer = sb.ToString().Trim();
        return answer.Length > 0 ? answer : "(aucune réponse)";
    }
}
