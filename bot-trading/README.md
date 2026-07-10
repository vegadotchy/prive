# IATech Trading Bot

Bot de trading Binance Spot avec interface graphique en français.

## Fonctionnalités

- **Deux modes** : 🧪 Paper (simulation, zéro risque) et 💰 Réel (ordres au marché sur Binance).
- **Tableau de bord en direct** : prix, équité, gain du jour, nombre de trades, taux de réussite, tendance, graphique animé avec ligne d'entrée de position.
- **📋 Journal d'activité** : chaque décision du bot est visible (démarrage, signaux, ordres exécutés, **erreurs d'ordre explicites** — plus jamais de « rien ne se passe » silencieux).
- **Stratégie** : croisement SMA rapide/lente (bougies 1 min) + take-profit, stop-loss, trailing stop, cooldown entre trades.
- **Garde-fous** : limite de perte journalière (le bot cesse d'acheter), vérification du minimum Binance (~10 USDT) avant chaque achat, confirmation avant démarrage en réel.
- **💵 Fermer la position** : vente au marché en un clic.
- **🧾 Historique** : liste des trades avec P&L + export CSV.
- **🔐 Code d'accès** : les clés API sont chiffrées sur disque (PBKDF2, 200 000 itérations) et déverrouillées par ton code au lancement.

## Lancer / construire

- Depuis les sources : `py main.py` (Python 3.10+, aucune dépendance externe — tout est en bibliothèque standard).
- Construire l'exe localement : double-clique `build.bat` → `dist\IATechTradingBot.exe`.
- Ou récupère l'exe construit automatiquement par GitHub Actions : onglet **Actions** du dépôt → dernier run « Build IATech Trading Bot (Windows) » → artefact `IATechTradingBot-windows`.

## Premiers pas

1. Lance l'application, choisis un code d'accès (il protège tes clés).
2. Onglet **⚙️ Configuration** : colle tes clés API Binance (sans espaces), « Tester la connexion », puis « Enregistrer ».
3. Mets une **Mise / trade ≥ 12 USDT** (minimum Binance ≈ 10).
4. Démarre d'abord en **Paper** pour voir le bot vivre sans risque, puis passe en Réel.

## Notes techniques

- Le thread du bot ne touche jamais à tkinter : la configuration est figée dans un dict au démarrage et toutes les mises à jour passent par une `queue` lue par le thread principal (`root.after`). C'est la correction définitive du crash `RuntimeError: main thread is not in main loop`.
- Les clés API sont `.strip()`ées partout (correction de l'erreur Binance `-1022` causée par des espaces collés à la clé).
- L'horloge est synchronisée avec le serveur Binance avant les requêtes signées (évite l'erreur `-1021`).
