// IATECH-SHIELD PRO — service worker
// Relaie les identifiants capturés vers l'application locale (127.0.0.1).
// Une déduplication simple évite les envois répétés identiques.

const ENDPOINT = "http://127.0.0.1:38217/save";
let lastSent = "";

chrome.runtime.onMessage.addListener(function (msg) {
  if (!msg || msg.type !== "iatech-cred" || !msg.data) return;

  const key = msg.data.url + "|" + msg.data.username + "|" + msg.data.password;
  if (key === lastSent) return; // évite les doublons immédiats
  lastSent = key;

  fetch(ENDPOINT, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(msg.data)
  }).catch(function () {
    // Application fermée ou pont indisponible : on ignore silencieusement.
  });
});
