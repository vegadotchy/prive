using System.Text;
using Anthropic;
using Anthropic.Models.Messages;

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

    /// <summary>Vrai si la clé API est configurée.</summary>
    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));

    /// <summary>Envoie une question/un contexte à Claude et renvoie la réponse texte.</summary>
    public async Task<string> AskAsync(string prompt, CancellationToken cancel = default)
    {
        if (!IsConfigured)
            return "Assistant IA non configuré : définissez la variable d'environnement " +
                   "ANTHROPIC_API_KEY avec votre clé Anthropic.";

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
