'use strict';

// Chat IA intégré. Utilise une API compatible OpenAI (chat/completions).
// La clé et le modèle sont configurables dans l'onglet Réglages.

async function chatCompletion(settings, payload) {
  const ai = (settings && settings.ai) || {};
  const apiKey = ai.apiKey;
  if (!apiKey) {
    return {
      ok: false,
      error:
        "Aucune clé d'API configurée. Ouvrez Réglages et renseignez votre clé OpenAI (ou compatible)."
    };
  }

  const baseUrl = (ai.baseUrl || 'https://api.openai.com/v1').replace(/\/$/, '');
  const model = ai.model || 'gpt-4o-mini';
  const history = Array.isArray(payload && payload.messages) ? payload.messages : [];

  const messages = [];
  if (ai.systemPrompt) {
    messages.push({ role: 'system', content: ai.systemPrompt });
  }
  for (const m of history) {
    if (m && (m.role === 'user' || m.role === 'assistant') && typeof m.content === 'string') {
      messages.push({ role: m.role, content: m.content });
    }
  }

  try {
    const res = await fetch(`${baseUrl}/chat/completions`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${apiKey}`
      },
      body: JSON.stringify({ model, messages, temperature: 0.4 })
    });

    if (!res.ok) {
      const text = await res.text().catch(() => '');
      return { ok: false, error: `Erreur API (${res.status}) : ${text.slice(0, 500)}` };
    }

    const data = await res.json();
    const content =
      data && data.choices && data.choices[0] && data.choices[0].message
        ? data.choices[0].message.content
        : '';
    return { ok: true, content: content || '(réponse vide)' };
  } catch (err) {
    return { ok: false, error: `Impossible de contacter l'API : ${err.message}` };
  }
}

module.exports = { chatCompletion };
