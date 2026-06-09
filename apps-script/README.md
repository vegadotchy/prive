# Vérification quotidienne des doublons d'agenda

Détecte automatiquement, **chaque jour**, les rendez-vous **en double** créés par
la synchronisation croisée de vos deux agendas, et (au choix) **déplace** le
doublon vers un autre créneau libre.

```
Doctena   ⇄   Google Agenda   ⇄   Doctoranytime
                    ▲
            le script vérifie ICI
```

Google Agenda est le point central des deux plateformes : c'est là que les
doublons apparaissent, donc c'est là qu'on les détecte.

---

## Ce que fait le script

1. Chaque matin (~7h), il parcourt vos rendez-vous (hier → 60 jours à venir).
2. Il regroupe les RDV par **nom de patient** (en ignorant les préfixes type
   « Doctena », « RDV », etc.).
3. Si un **même patient** a 2 RDV à moins de **72h** d'écart → c'est un doublon.
4. Selon le mode choisi, il :
   - **`report`** (par défaut) : se contente de **lister les doublons par email**, ne modifie rien ;
   - `move` : déplace le doublon vers le prochain créneau libre ;
   - `delete` : supprime le doublon (garde le plus ancien).
5. Il vous **envoie un email** récapitulatif à `eurocare.agendas@gmail.com`.

> 🔒 **Sécurité** : le script démarre en mode **SIMULATION** (`DRY_RUN = true`).
> Il vous envoie les déplacements *proposés* **sans rien modifier**. Vérifiez
> quelques rapports, puis passez `DRY_RUN = false` pour activer les actions réelles.

---

## Installation (5 minutes, sans serveur, gratuit)

1. Allez sur **https://script.google.com** (connecté avec le compte qui possède
   l'agenda, ici `eurocare.agendas@gmail.com`).
2. Cliquez **Nouveau projet**.
3. Supprimez le contenu de `Code.gs` et **collez** le contenu de
   [`Code.gs`](./Code.gs) de ce dossier.
4. (Optionnel mais recommandé) Activez le manifeste : roue dentée
   **Paramètres du projet → cochez « Afficher le fichier manifeste appsscript.json »**,
   puis collez le contenu de [`appsscript.json`](./appsscript.json).
5. En haut, sélectionnez la fonction **`installDailyTrigger`** puis cliquez
   **▶ Exécuter**. Autorisez les accès demandés (Agenda + Envoi d'email) au
   premier lancement.
6. C'est tout : la vérification tournera chaque jour vers 7h.

### Tester tout de suite

Sélectionnez la fonction **`runNow`** et cliquez **▶ Exécuter** : vous recevez
immédiatement un email de test avec les doublons éventuels.

---

## Réglages (en haut de `Code.gs`, objet `CONFIG`)

| Réglage | Rôle | Défaut |
|---|---|---|
| `EMAIL` | Destinataire du rapport | `eurocare.agendas@gmail.com` |
| `RESOLUTION_MODE` | `report` / `move` / `delete` | `report` |
| `DRY_RUN` | `true` = simulation (ne modifie rien) | `true` |
| `DUP_WINDOW_HOURS` | Écart max pour considérer 2 RDV comme doublons | `72` |
| `LOOKAHEAD_DAYS` | Nombre de jours analysés vers le futur | `60` |
| `PREFIXES_TO_STRIP` | Mots à retirer du titre pour isoler le nom | liste |
| `WORK.START_HOUR` / `END_HOUR` | Plage horaire de consultation | `9` → `18` |
| `WORK.SLOT_MINUTES` | Pas de recherche de créneau libre | `15` |
| `WORK.INCLUDE_SATURDAY` / `SUNDAY` | Travailler le week-end ? | `false` |
| `ALWAYS_NOTIFY` | Email même si zéro doublon | `false` |

---

## Limites importantes à connaître

- **Disponibilité réelle** : Google Agenda ne connaît pas vos horaires Doctena /
  Doctoranytime (pauses, congés, types de consultation). Le mode `move` se base
  uniquement sur les heures configurées dans `WORK` + les RDV existants. Vérifiez
  toujours en simulation avant d'automatiser.
- **Prévenir le patient** : déplacer un RDV ne prévient pas automatiquement le
  patient. Gardez de préférence le mode simulation ou `report` si vous voulez
  garder la main sur les notifications.
- **Sens de la synchronisation** : selon la configuration de Doctena /
  Doctoranytime, une modification faite côté Google Agenda peut ou non être
  renvoyée vers les plateformes. Validez ce comportement avant `DRY_RUN = false`.
- **Détection par nom** : si les deux plateformes écrivent les noms très
  différemment (ex. initiales d'un côté, nom complet de l'autre), ajustez
  `PREFIXES_TO_STRIP` ou repassez en mode `report` le temps d'affiner.
