# IATECH-SHIELD PRO — Architecture cible (vision EDR)

Ce document décrit la vision complète : faire évoluer IATECH-SHIELD d'un antivirus
par signatures vers une **plateforme de défense pilotée par comportement et IA**
(de type EDR/XDR). Il sert de feuille de route honnête : il distingue ce qui est
**réalisable en mode utilisateur** (et donc livré progressivement) de ce qui exige
un **pilote noyau signé** (effort majeur, structure dédiée).

## Principe directeur

> Ne pas chercher *ce qui est connu comme malveillant*, mais détecter *ce qui se
> comporte anormalement* — même pour une menace jamais vue.

Trois piliers complémentaires :

1. **Réputation** — à quel point fait-on confiance à ce programme ? (signature, éditeur, âge, emplacement)
2. **Comportement** — que fait ce programme, et est-ce normal pour cette machine ?
3. **Réaction** — contenir, tuer, restaurer, isoler dès qu'un seuil est franchi.

## Les couches et leur faisabilité

| # | Couche | Mode | Faisabilité solo | Statut |
|---|--------|------|------------------|--------|
| 1 | Détection comportementale (process/réseau/registre) | User (ETW) | 🟡 Partielle | Planifié |
| 2 | **Anti-ransomware** (canaris, kill, rollback) | User | 🟢 Élevée | **v0.2** |
| 3 | Micro-sandbox automatique | User (AppContainer / Windows Sandbox) | 🟡 Partielle | Planifié |
| 4 | Protection mémoire (injection, hollowing, shellcode) | **Noyau** | 🔴 Faible | Recherche |
| 5 | Détection réseau (DNS, C2, exfiltration) | User | 🟢 Moyenne | Planifié |
| 6 | ML local (exe/comportement/doc) | User (ONNX) | 🟢 Moyenne | Planifié |
| 7 | Protection navigateur (phishing) | User (extension + listes) | 🟢 Moyenne | Planifié |
| 8 | **Protection clés USB** (auto-scan) | User (WM_DEVICECHANGE / WMI) | 🟢 Élevée | **v0.2** |
| 9 | Anti-exploit (ROP, buffer overflow) | **Noyau / OS** | 🔴 Faible | Hors périmètre |
| 10 | Auto-protection inviolable | **Noyau + PPL signé Microsoft** | 🔴 Nulle (solo) | Hors périmètre |
| ⭐ | **Score de confiance des applications** | User (Authenticode) | 🟢 Élevée | **v0.2** |
| ⭐ | **Détection d'anomalie** (modèle normal) | User | 🟢 Moyenne | v0.3 |

### Pourquoi certaines couches exigent le noyau

Les couches 4, 9 et 10 reposent sur des mécanismes que Windows **réserve** au code
noyau et/ou aux antivirus officiellement enregistrés :

- **ETW-TI** (Threat Intelligence provider), callbacks `ObRegisterCallbacks`,
  `PsSetCreateProcessNotifyRoutine` — nécessitent un pilote.
- Un pilote tiers doit être **signé** (certificat EV) puis **attesté par Microsoft**
  (portail de signature de pilotes). C'est payant, long, et soumis à audit.
- L'auto-protection « même un administrateur ne peut pas l'arrêter » suppose un
  processus **PPL (Protected Process Light)** de niveau antimalware, signé par
  Microsoft via le programme **ELAM** — inaccessible à un projet individuel.

Conclusion : on construit une **architecture EDR-lite robuste en mode utilisateur**,
conçue pour accueillir plus tard un module noyau si le projet se structure.

## Architecture logicielle

```
                ┌─────────────────────────────────────────────┐
                │          IatechShield.Gui (WPF)             │
                │   Tableau de bord, alertes, scores, toggles │
                └───────────────▲─────────────────────────────┘
                                │ événements / commandes
                ┌───────────────┴─────────────────────────────┐
                │            IatechShield.Core                 │
                │                                              │
                │  Détection            Réputation            │
                │  ├─ Scanner (signatures)   ├─ AppTrustScorer │
                │  ├─ RealtimeMonitor        │  (Authenticode) │
                │  ├─ RansomwareGuard        │                 │
                │  └─ (ETW BehaviorMonitor)  Réaction          │
                │                            ├─ Quarantine     │
                │                            └─ ProcessCuller  │
                └───────────────▲─────────────────────────────┘
                                │ (futur)
                ┌───────────────┴─────────────────────────────┐
                │   IatechShield.Driver (pilote noyau, futur)  │
                │   callbacks process/mémoire, auto-protection │
                └──────────────────────────────────────────────┘
```

## Score de confiance (modèle)

Chaque exécutable reçoit une note **/100** combinant des facteurs pondérés :

| Facteur | Poids | Exemple |
|---------|-------|---------|
| Signature Authenticode valide | +40 | Chrome, Office |
| Éditeur connu / de confiance | +25 | Microsoft, Google |
| Emplacement « sain » (Program Files) | +15 | vs `Temp`, `Downloads` |
| Ancienneté / déjà observé | +10 | nouveau = suspect |
| Aucune signature | −40 | crack, script |
| Emplacement suspect / extension trompeuse | −25 | `facture.pdf.exe` |

Bandes : **80–100** = fiable · **40–79** = à surveiller · **0–39** = dangereux.

## Anti-ransomware (mécanisme)

1. **Canaris** : des fichiers leurres (`~IATECH_CANARY_*.docx`) sont déposés dans
   les dossiers sensibles. Toute modification d'un canari = signal quasi certain.
2. **Modifications massives** : fenêtre glissante ; si > N fichiers modifiés en
   < T secondes dans une zone protégée → alerte.
3. **Réaction** : identification du processus le plus actif en écriture disque
   (`ProcessCuller`) → arrêt + alerte. Rollback depuis snapshots (étape suivante).

## Feuille de route

- **v0.2** : score de confiance, bouclier anti-ransomware (canaris + kill), auto-scan USB.
- **v0.3** : détection d'anomalie (baseline process/réseau), rollback par snapshots.
- **v0.4** : BehaviorMonitor ETW (arbre de processus, réseau), ML local (ONNX).
- **v0.5** : extension navigateur anti-phishing, micro-sandbox (AppContainer).
- **vNext** : module noyau (recherche) — nécessite certificat EV + attestation Microsoft.

## Limites assumées

IATECH-SHIELD PRO est un projet **éducatif et défensif**. En mode utilisateur, il
**complète** mais ne **remplace** pas une suite de sécurité certifiée. Les couches
noyau (mémoire, anti-exploit, auto-protection inviolable) sont documentées comme
objectifs de recherche, pas comme fonctionnalités livrées.
