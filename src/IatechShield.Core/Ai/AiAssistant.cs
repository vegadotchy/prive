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
            return "Assistant IA non configuré. Pour une IA GRATUITE : créez une clé Google Gemini sur " +
                   "aistudio.google.com (bouton « Get API key ») et collez-la dans Réglages → Assistant IA. " +
                   "Une clé Anthropic (« sk-ant-… »), OpenAI/ChatGPT (« sk-… »), Groq, xAI ou Mistral fonctionne aussi.";

        // Détection automatique du fournisseur selon le préfixe de la clé, pour accepter
        // la clé de n'importe quelle IA sans réglage supplémentaire.
        apiKey = apiKey.Trim();

        // Google Gemini (gratuit) : « AIza… ».
        if (apiKey.StartsWith("AIza", StringComparison.Ordinal))
            return await AskGeminiAsync(apiKey, prompt, cancel);

        // Anthropic (Claude) : « sk-ant-… » → SDK officiel.
        if (apiKey.StartsWith("sk-ant-", StringComparison.Ordinal))
            return await AskAnthropicAsync(apiKey, prompt, cancel);

        // Fournisseurs compatibles OpenAI (même format d'API) : on route sur le bon serveur
        // et un modèle par défaut selon le préfixe de la clé.
        if (apiKey.StartsWith("sk-or-", StringComparison.Ordinal))
            return await AskOpenAiCompatibleAsync("OpenRouter", "https://openrouter.ai/api/v1/chat/completions", "openai/gpt-4o-mini", apiKey, prompt, cancel);
        if (apiKey.StartsWith("gsk_", StringComparison.Ordinal))
            return await AskOpenAiCompatibleAsync("Groq", "https://api.groq.com/openai/v1/chat/completions", "llama-3.3-70b-versatile", apiKey, prompt, cancel);
        if (apiKey.StartsWith("xai-", StringComparison.Ordinal))
            return await AskOpenAiCompatibleAsync("xAI (Grok)", "https://api.x.ai/v1/chat/completions", "grok-2-latest", apiKey, prompt, cancel);
        if (apiKey.StartsWith("sk-", StringComparison.Ordinal))
            return await AskOpenAiCompatibleAsync("OpenAI (ChatGPT)", "https://api.openai.com/v1/chat/completions", "gpt-4o-mini", apiKey, prompt, cancel);

        // Sinon (ex. clé Mistral sans préfixe reconnaissable) : on tente le serveur Mistral,
        // lui aussi compatible OpenAI.
        return await AskOpenAiCompatibleAsync("Mistral", "https://api.mistral.ai/v1/chat/completions", "mistral-small-latest", apiKey, prompt, cancel);
    }

    /// <summary>Appelle Claude (Anthropic) via le SDK officiel.</summary>
    private static async Task<string> AskAnthropicAsync(string apiKey, string prompt, CancellationToken cancel)
    {
        // Le SDK lit ANTHROPIC_API_KEY : on l'alimente avec la clé enregistrée dans l'app.
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", apiKey);

        try
        {
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
        catch (Exception ex)
        {
            return FriendlyError(ex.Message);
        }
    }

    private static readonly System.Net.Http.HttpClient GeminiHttp = new();

    /// <summary>Appelle l'API gratuite Google Gemini (clé « AIza… ») et renvoie la réponse.</summary>
    private static async Task<string> AskGeminiAsync(string apiKey, string prompt, CancellationToken cancel)
    {
        try
        {
            string url = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key="
                         + Uri.EscapeDataString(apiKey);
            string body = System.Text.Json.JsonSerializer.Serialize(new
            {
                contents = new[] { new { parts = new[] { new { text = $"{Persona}\n\n---\n\n{prompt}" } } } }
            });
            using var content = new System.Net.Http.StringContent(body, Encoding.UTF8, "application/json");
            using var resp = await GeminiHttp.PostAsync(url, content, cancel);
            string json = await resp.Content.ReadAsStringAsync(cancel);
            if (!resp.IsSuccessStatusCode)
            {
                string jl = json.ToLowerInvariant();
                if (jl.Contains("api_key_invalid") || jl.Contains("api key not valid") || resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    return "⚠ Clé Google Gemini invalide. Créez-en une gratuitement sur aistudio.google.com → « Get API key » (elle commence par « AIza… »).";
                if (jl.Contains("quota") || jl.Contains("rate") || (int)resp.StatusCode == 429)
                    return "⚠ Limite gratuite Gemini atteinte pour le moment. Patientez une minute puis réessayez.";
                return "⚠ Gemini indisponible : " + Short(json);
            }
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var sb = new StringBuilder();
            if (doc.RootElement.TryGetProperty("candidates", out var cands))
                foreach (var cand in cands.EnumerateArray())
                    if (cand.TryGetProperty("content", out var cnt) && cnt.TryGetProperty("parts", out var parts))
                        foreach (var part in parts.EnumerateArray())
                            if (part.TryGetProperty("text", out var t)) sb.Append(t.GetString());
            string answer = sb.ToString().Trim();
            return answer.Length > 0 ? answer : "(aucune réponse)";
        }
        catch (Exception ex) { return "⚠ Gemini : " + Short(ex.Message); }
    }

    private static readonly System.Net.Http.HttpClient OpenAiHttp = new();

    /// <summary>
    /// Appelle n'importe quel fournisseur compatible OpenAI (OpenAI/ChatGPT, Groq, xAI/Grok,
    /// OpenRouter, Mistral…). Même format de requête « chat/completions ».
    /// </summary>
    private static async Task<string> AskOpenAiCompatibleAsync(
        string provider, string endpoint, string model, string apiKey, string prompt, CancellationToken cancel)
    {
        try
        {
            string body = System.Text.Json.JsonSerializer.Serialize(new
            {
                model,
                max_tokens = 1024,
                messages = new[]
                {
                    new { role = "system", content = Persona },
                    new { role = "user", content = prompt }
                }
            });
            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, endpoint);
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
            req.Content = new System.Net.Http.StringContent(body, Encoding.UTF8, "application/json");
            using var resp = await OpenAiHttp.SendAsync(req, cancel);
            string json = await resp.Content.ReadAsStringAsync(cancel);
            if (!resp.IsSuccessStatusCode)
            {
                string jl = json.ToLowerInvariant();
                if (jl.Contains("invalid api key") || jl.Contains("invalid_api_key") || jl.Contains("incorrect api key")
                    || resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    return $"⚠ Clé {provider} invalide ou révoquée. Vérifiez-la dans Réglages → Assistant IA.";
                if (jl.Contains("quota") || jl.Contains("insufficient") || jl.Contains("billing") || jl.Contains("credit"))
                    return $"⚠ Crédit/quota {provider} épuisé. Rechargez votre compte ou utilisez une clé Google Gemini gratuite (« AIza… »).";
                if (jl.Contains("rate") || (int)resp.StatusCode == 429)
                    return $"⚠ Trop de requêtes {provider} pour le moment. Patientez puis réessayez.";
                return $"⚠ {provider} indisponible : " + Short(json);
            }
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var msg = choices[0].GetProperty("message");
                if (msg.TryGetProperty("content", out var c))
                {
                    string answer = (c.GetString() ?? "").Trim();
                    return answer.Length > 0 ? answer : "(aucune réponse)";
                }
            }
            return "(aucune réponse)";
        }
        catch (Exception ex) { return $"⚠ {provider} : " + Short(ex.Message); }
    }

    /// <summary>Traduit les erreurs de l'API en messages clairs (crédits, clé, quota…).</summary>
    private static string FriendlyError(string raw)
    {
        string m = raw.ToLowerInvariant();

        if (m.Contains("credit balance") || m.Contains("too low") || m.Contains("billing") || m.Contains("payment"))
            return "⚠ Votre compte API Anthropic n'a pas assez de crédits.\n\n" +
                   "L'API est facturée à l'usage, séparément de l'abonnement Claude.ai. " +
                   "Ajoutez quelques crédits sur console.anthropic.com → « Plans & Billing » (≈ 5 $ suffisent et durent longtemps), " +
                   "puis réessayez. Toutes les autres fonctions de l'antivirus marchent sans crédit.";

        if (m.Contains("authentication") || m.Contains("invalid x-api-key") || m.Contains("invalid api key") || m.Contains("unauthorized") || m.Contains("401"))
            return "⚠ Clé API Anthropic invalide ou révoquée. Vérifiez-la dans Réglages → Assistant IA " +
                   "(elle commence par « sk-ant-… »), ou créez-en une nouvelle sur console.anthropic.com.";

        if (m.Contains("rate limit") || m.Contains("429") || m.Contains("overloaded"))
            return "⚠ Trop de requêtes pour le moment (limite de débit). Patientez quelques secondes puis réessayez.";

        if (m.Contains("model") && (m.Contains("not found") || m.Contains("does not exist") || m.Contains("not_found")))
            return "⚠ Le modèle IA n'est pas disponible sur votre compte. Vérifiez l'accès au modèle dans la console Anthropic.";

        return "⚠ L'assistant IA est temporairement indisponible : " + Short(raw);
    }

    private static string Short(string s) => s.Length > 220 ? s[..220].Trim() + "…" : s.Trim();
}
