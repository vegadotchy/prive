# IATECH-SHIELD PRO 0.1

Antivirus pédagogique et fonctionnel pour **Windows**, écrit en **C# / .NET 8**.

> ⚠️ **Statut : 0.1 (fondation)**. Le moteur fonctionne réellement (détection,
> quarantaine, temps réel), mais la base de signatures est minimale. Ce n'est
> **pas** un remplacement d'un antivirus commercial. À utiliser en complément,
> pour apprendre et expérimenter.

## Fonctionnalités

| Fonction | État | Description |
|----------|------|-------------|
| Scan par signatures | ✅ | Hash SHA-256, motifs hexadécimaux et motifs texte. |
| Quarantaine | ✅ | Isole et neutralise les fichiers détectés (réversible). |
| Surveillance temps réel | ✅ | Analyse automatique des fichiers créés/modifiés. |
| Installateur `.exe` | ✅ | Script Inno Setup (`installer/iatech-shield.iss`). |
| Mise à jour des signatures | ⏳ | À venir (récupération depuis une source distante). |
| Analyse heuristique | ⏳ | À venir. |

## Compilation

Prérequis : [.NET SDK 8](https://dotnet.microsoft.com/download).

```bash
cd src/IatechShield
dotnet build -c Release
```

L'exécutable est produit dans `bin/Release/net8.0/iatech-shield.exe`.

## Produire un `.exe` installable

Le projet se distribue comme un **installateur Windows `.exe`** unique. Deux étapes :

### 1. Publier un exécutable autonome (sans .NET requis sur la machine cible)

```bash
cd src/IatechShield
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Résultat : `bin/Release/net8.0/win-x64/publish/iatech-shield.exe` (+ `signatures.json`).

### 2. Construire l'installateur avec Inno Setup

1. Installez [Inno Setup](https://jrsoftware.org/isdl.php) (gratuit).
2. Ouvrez `installer/iatech-shield.iss` dans Inno Setup, ou en ligne de commande :

   ```bash
   iscc installer\iatech-shield.iss
   ```

3. L'installateur est généré dans `installer/Output/IatechShield-Setup-0.1.0.exe`.

Cet `.exe` installe le programme dans `Program Files`, l'ajoute au `PATH`, et crée
une entrée dans « Ajouter/Supprimer des programmes » pour la désinstallation.

> 💡 Le script `build-installer.bat` à la racine enchaîne automatiquement les
> étapes 1 et 2.

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
├── src/IatechShield/
│   ├── Program.cs              # Interface en ligne de commande
│   ├── signatures.json         # Base de signatures (modifiable)
│   └── Engine/
│       ├── Signature.cs        # Modèle d'une signature
│       ├── SignatureDatabase.cs# Chargement de la base JSON
│       ├── Scanner.cs          # Moteur de détection (hash + motifs)
│       ├── Quarantine.cs       # Isolation / restauration des fichiers
│       └── RealtimeMonitor.cs  # Surveillance temps réel (FileSystemWatcher)
├── installer/
│   └── iatech-shield.iss       # Script d'installateur Inno Setup
└── build-installer.bat         # Publie + construit le .exe d'installation
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
