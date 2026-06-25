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
| Installateur `.exe` | ✅ | Script Inno Setup (`installer/iatech-shield.iss`). |
| Mise à jour des signatures | ⏳ | À venir (récupération depuis une source distante). |
| Analyse heuristique | ⏳ | À venir. |

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

# Gérer la quarantaine
iatech-shield quarantine list
iatech-shield quarantine restore <id>     # en cas de faux positif
iatech-shield quarantine delete <id>
```

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
