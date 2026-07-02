# IATECH-SHIELD PRO 0.1

Antivirus pédagogique et fonctionnel pour **Windows**, écrit en **C# / .NET 8**.

> ⚠️ **Statut : 0.1 (fondation)**. Le moteur fonctionne réellement (détection,
> quarantaine, temps réel), mais la base de signatures est minimale. Ce n'est
> **pas** un remplacement d'un antivirus commercial. À utiliser en complément,
> pour apprendre et expérimenter.

## Fonctionnalités

| Fonction | État | Description |
|----------|------|-------------|
| Interface graphique (WPF) | ✅ | Tableau de bord néon : bouclier « PROTÉGÉ », scan, temps réel. |
| Scan par signatures | ✅ | Hash SHA-256, motifs hexadécimaux et motifs texte. |
| Quarantaine | ✅ | Isole et neutralise les fichiers détectés (réversible). |
| Surveillance temps réel | ✅ | Analyse automatique des fichiers créés/modifiés. |
| **Score de confiance des apps** | ✅ | Note /100 (signature Authenticode, éditeur, emplacement). |
| **Bouclier anti-ransomware** | ✅ | Canaris + détection de modifications massives + arrêt du processus. |
| **Auto-scan des clés USB** | ✅ | Analyse automatique à l'insertion d'un périphérique amovible. |
| **Droits administrateur** | ✅ | L'interface démarre élevée (UAC) pour agir sur le système. |
| **Inspection du registre** | ✅ | Liste/analyse les programmes au démarrage (persistance malware). |
| **Assistant IA (Claude)** | ✅ | Analyse les menaces et recommande des actions (SDK Anthropic). |
| Installateur `.exe` | ✅ | Script Inno Setup (`installer/iatech-shield.iss`). |
| Détection d'anomalie / ML local | ⏳ | À venir (voir `ARCHITECTURE.md`). |
| Module noyau (mémoire, anti-exploit) | 🔬 | Recherche — nécessite un pilote signé (voir `ARCHITECTURE.md`). |

La vision complète « EDR piloté par IA » et la faisabilité de chaque couche sont
détaillées dans **[`ARCHITECTURE.md`](ARCHITECTURE.md)**.

Le projet fournit **deux exécutables** :
- `iatech-shield-gui.exe` — l'**interface graphique** (le tableau de bord).
- `iatech-shield.exe` — la **ligne de commande** (automatisation, scripts).

## Compilation

Prérequis : [.NET SDK 8](https://dotnet.microsoft.com/download).

```bash
dotnet build -c Release src/IatechShield.Gui     # interface graphique (+ moteur)
dotnet build -c Release src/IatechShield          # ligne de commande (+ moteur)
```

Pour lancer l'interface graphique en développement :

```bash
dotnet run --project src/IatechShield.Gui
```

## Produire un `.exe` installable

Le projet se distribue comme un **installateur Windows `.exe`** unique qui contient
l'interface graphique **et** la CLI.

### Méthode automatique (recommandée)

Installez [Inno Setup](https://jrsoftware.org/isdl.php) (gratuit), puis depuis la
racine du dépôt :

```bash
build-installer.bat
```

Ce script publie l'interface graphique + la CLI dans `dist/`, puis génère
l'installateur dans `installer/Output/IatechShield-Setup-0.1.0.exe`.

### Sans rien installer : via GitHub Actions

À chaque `push`, GitHub compile automatiquement l'installateur sur une machine
Windows. Allez dans l'onglet **Actions** du dépôt, ouvrez le dernier build, et
téléchargez l'artefact **`IatechShield-Setup`** (ou **`iatech-shield-app-win-x64`**
pour l'application sans installateur).

L'installateur place le programme dans `Program Files`, crée des raccourcis
(Bureau + menu Démarrer), ajoute la CLI au `PATH`, et s'inscrit dans
« Ajouter/Supprimer des programmes » pour la désinstallation.

## Utilisation

```bash
# Analyser un dossier (récursif)
iatech-shield scan C:\Users\Moi\Downloads

# Analyser et mettre les menaces en quarantaine
iatech-shield scan C:\Users\Moi\Downloads --quarantine

# Surveillance temps réel d'un dossier
iatech-shield watch C:\Users\Moi\Downloads -q

# Calculer le score de confiance d'un programme
iatech-shield trust "C:\Program Files\App\app.exe"

# Bouclier anti-ransomware (dossiers sensibles par défaut si non précisés)
iatech-shield guard
iatech-shield guard C:\Donnees\Important

# Gérer la quarantaine
iatech-shield quarantine list
iatech-shield quarantine restore <id>     # en cas de faux positif
iatech-shield quarantine delete <id>

# Programmes au démarrage automatique (persistance)
iatech-shield autoruns

# Assistant de sécurité IA (nécessite ANTHROPIC_API_KEY)
iatech-shield ai "ce fichier facture.pdf.exe est-il dangereux ?"
```

### Assistant IA (Claude)

L'assistant utilise le **SDK officiel Anthropic** (modèle `claude-opus-4-8`). Définissez
votre clé avant de lancer l'application ou la CLI :

```bash
setx ANTHROPIC_API_KEY "votre-clé-anthropic"   # Windows (nouvelle session)
```

Dans l'interface, le bouton **« Assistant IA »** envoie l'état du système à Claude et
affiche un avis de sécurité + les actions recommandées.

## Tester la détection (sans danger)

Le projet détecte le **fichier de test EICAR**, un fichier standard et 100 %
inoffensif conçu précisément pour tester les antivirus.

1. Créez un fichier `eicar.com` contenant la chaîne de test EICAR
   (disponible sur <https://www.eicar.org/download-anti-malware-testfile/>).
2. Lancez `iatech-shield scan <dossier>` : le fichier doit être signalé comme
   `EICAR-Test-File`.

Vous pouvez aussi tester le motif texte de démonstration en créant un fichier
contenant `IATECH_SHIELD_DEMO_MALWARE_MARKER`.

## Architecture

```
prive/
├── src/
│   ├── IatechShield.Core/          # Moteur partagé (bibliothèque)
│   │   ├── signatures.json         # Base de signatures (modifiable)
│   │   └── Engine/
│   │       ├── Signature.cs        # Modèle d'une signature
│   │       ├── SignatureDatabase.cs# Chargement de la base JSON
│   │       ├── Scanner.cs          # Moteur de détection (hash + motifs)
│   │       ├── ScanService.cs      # Orchestration d'un scan de dossier
│   │       ├── Quarantine.cs       # Isolation / restauration des fichiers
│   │       └── RealtimeMonitor.cs  # Surveillance temps réel (FileSystemWatcher)
│   ├── IatechShield/               # Application en ligne de commande
│   │   └── Program.cs
│   └── IatechShield.Gui/           # Interface graphique (WPF)
│       ├── App.xaml                # Thème néon (couleurs, styles)
│       ├── MainWindow.xaml         # Tableau de bord
│       └── MainWindow.xaml.cs      # Logique branchée au moteur
├── installer/
│   └── iatech-shield.iss           # Script d'installateur Inno Setup
└── build-installer.bat             # Publie + construit le .exe d'installation
```

## Licences (essai 15 jours + activation)

L'application fonctionne **15 jours gratuitement**, puis demande une licence.

| Formule | Prix |
|---------|------|
| Mensuel | **5 €/mois** |
| Annuel | **50 €/an** (2 mois offerts) |
| À vie | **249,99 €** (paiement unique) |

La sécurité repose sur une **signature cryptographique (ECDSA P-256)** :
- l'application embarque uniquement la **clé publique** et vérifie les licences ;
- **vous** (l'éditeur) détenez la **clé privée**, dans le générateur `iatech-keygen`.
  Sans cette clé privée, personne ne peut forger de licence valide.

### Générer des clés (outil éditeur `iatech-keygen`)

```bash
# 1. (une seule fois) créer votre paire de clés
iatech-keygen init
#    -> keys/private.pem  (SECRÈTE, ne jamais partager ni committer)
#    -> keys/public_key.pem
#    Copiez public_key.pem dans src/IatechShield.Core/Licensing/ puis recompilez.

# 2. émettre des licences
iatech-keygen issue --tier monthly
iatech-keygen issue --tier yearly
iatech-keygen issue --tier lifetime
```

Le client colle la clé obtenue dans **Activer** (interface) ou via
`iatech-shield license activate <clé>`.

> ⚠️ La clé privée (`keys/`) est exclue du dépôt par `.gitignore`. Conservez-la
> hors ligne : quiconque la possède peut générer des licences.

## Ajouter vos propres signatures

Éditez `signatures.json`. Trois types sont supportés :

```jsonc
[
  { "name": "Ma-Menace",  "severity": "high", "type": "sha256",
    "value": "<hash SHA-256 en hexadécimal>" },

  { "name": "Motif-Octets", "severity": "medium", "type": "hexPattern",
    "value": "4D5A9000" },

  { "name": "Motif-Texte", "severity": "low", "type": "textPattern",
    "value": "chaîne à rechercher" }
]
```

## Avertissement légal

Cet outil est fourni à des fins éducatives et de sécurité défensive. Ne l'utilisez
que sur des systèmes et des fichiers qui vous appartiennent ou pour lesquels vous
avez une autorisation explicite.

## Feuille de route (0.2+)

- [ ] Mise à jour des signatures depuis une source distante (HTTPS).
- [ ] Analyse heuristique (entropie, sections PE suspectes).
- [ ] Journalisation dans un fichier + rapport HTML.
- [ ] Service Windows pour la surveillance permanente.
- [ ] Interface graphique (WPF / WinUI).
