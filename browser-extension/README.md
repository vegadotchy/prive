# Extension navigateur — IATECH-SHIELD PRO

Propose d'enregistrer automatiquement dans le **coffre-fort IATECH-SHIELD PRO**
les identifiants que vous saisissez dans le navigateur.

## Confidentialité

Le canal est **strictement local** : l'extension envoie l'identifiant capturé à
l'application **uniquement** via `http://127.0.0.1:38217` (votre PC). **Rien ne
transite par Internet.** À chaque nouvel identifiant, l'application **vous
demande** si vous souhaitez l'enregistrer.

## Installation (Chrome / Edge)

1. Lancez **IATECH-SHIELD PRO** (le pont local démarre automatiquement).
2. Ouvrez `chrome://extensions` (ou `edge://extensions`).
3. Activez le **Mode développeur** (en haut à droite).
4. Cliquez **« Charger l'extension non empaquetée »** et sélectionnez ce dossier
   `browser-extension`.
5. C'est prêt : connectez-vous à un site, IATECH-SHIELD vous proposera d'enregistrer
   l'identifiant.

> Le dossier `browser-extension` est fourni avec l'application (menu Réglages →
> « Ouvrir le dossier de l'extension »).

## Fichiers

- `manifest.json` — déclaration de l'extension (Manifest V3)
- `content.js` — détecte la saisie identifiant/mot de passe dans les pages
- `background.js` — relaie au pont local de l'application
