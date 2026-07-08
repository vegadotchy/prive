# Prive — tableau de bord du cabinet (Windows)

Application de bureau **Windows** (`.exe`) regroupant, sous forme d'onglets et de
boutons, tous les outils du cabinet dans une seule fenêtre. Construite avec
[Electron](https://www.electronjs.org/).

## Fonctionnalités

| Onglet / bouton | Description |
|---|---|
| **Gmail** | Boîte de réception Gmail intégrée |
| **Envoyer un mail** | Composition avec **liste déroulante de modèles** ; ouvre dans Gmail ou le logiciel de messagerie |
| **Chat IA** | Chat intégré (API compatible OpenAI/ChatGPT, clé configurable) |
| **Doctena** | Accès à Doctena |
| **Doctoranytime** | Accès au CRM Doctoranytime |
| **Shyfter** | Tableau de bord Shyfter |
| **CareConnect** | Bouton qui **lance l'application CareConnect** installée sur le poste |
| **InBody** | Site InBody + bouton **Imprimer le test affiché** |
| **IBC Lab online** | Labo en ligne (LHUB-ULB) |
| **Examens du jour** | Serveur interne (`https://192.168.1.220/eoWEB/…`, certificat auto-signé accepté) |
| **Médicaments (CBIP)** | Champ de recherche qui interroge le CBIP et affiche le résultat dans l'app |
| **ClearFacts / Kyte** | Facturation ClearFacts |
| **Medipost** | Boutique Medipost |
| **Recherche fichiers** | Recherche de dossiers/fichiers (par nom, et option contenu) partout sur le PC |
| **Test de vitesse** | Mesure **permanente** de la latence et du débit (barre d'état, en bas) |
| **Réglages** | Clé d'API IA, chemin CareConnect, URL examens, hôtes à certificat auto-signé |

## Prérequis

- [Node.js](https://nodejs.org/) 18 ou supérieur (LTS recommandé)
- Windows 10/11 pour générer et exécuter le `.exe`

## Développement (lancer sans compiler)

```bash
npm install
npm start
```

## Générer l'installateur `.exe` (sur Windows)

```bash
npm install
npm run dist
```

L'installateur NSIS est produit dans le dossier `dist/`
(par ex. `dist/Prive Setup 1.0.0.exe`). Double-cliquez dessus pour installer
l'application, qui crée un raccourci sur le bureau et dans le menu Démarrer.

> Astuce : `npm run pack` produit une version décompressée dans `dist/win-unpacked/`
> (pratique pour tester rapidement sans passer par l'installateur).

## Configuration

Au premier lancement, ouvrez l'onglet **Réglages** pour renseigner :

- **Clé d'API du Chat IA** — clé OpenAI (`sk-…`) ou toute API compatible
  `/chat/completions`. La clé est stockée localement dans le profil utilisateur
  (`%APPDATA%/Prive/settings.json`) et **n'est jamais versionnée**.
- **Chemin de CareConnect** — l'exécutable à lancer, par ex.
  `C:\Program Files\CareConnect\CareConnect.exe`.
- **URL des examens du jour** et **hôtes à certificat auto-signé autorisés**
  (par défaut `192.168.1.220`).

Les **modèles de mail** peuvent être ajoutés/modifiés directement dans le
fichier `settings.json` (clé `emailTemplates`).

## Notes techniques

- Les sessions de connexion aux sites (Gmail, Doctena, Shyfter, …) sont
  **conservées** entre les lancements (partition persistante Electron).
- Le certificat auto-signé du serveur interne n'est accepté **que** pour les
  hôtes explicitement listés dans les réglages — jamais globalement.
- `contextIsolation` activé, `nodeIntegration` désactivé, pont IPC minimal via
  `preload.js` (bonnes pratiques de sécurité Electron).

## Structure

```
src/
  main.js         Processus principal (fenêtre, IPC, certificats, lanceurs)
  preload.js      Pont sécurisé exposé à l'interface
  settings.js     Chargement/sauvegarde des réglages (%APPDATA%/Prive)
  fileSearch.js   Recherche de fichiers récursive
  speedtest.js    Test de vitesse permanent (Cloudflare)
  chat.js         Appel à l'API de chat (compatible OpenAI)
  renderer/
    index.html    Interface (onglets, formulaires)
    styles.css    Thème sombre
    renderer.js   Logique de l'interface
```
