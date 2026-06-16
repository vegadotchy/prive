# Téléchargement des factures Amazon

Script pour télécharger en masse les **factures et notes de crédit** Amazon
d'une année donnée (par défaut **2026**).

> ⚠️ **À exécuter sur VOTRE machine**, pas dans un environnement cloud.
> Le script a besoin d'un vrai navigateur et de votre session Amazon.

## Installation

```bash
pip install playwright
playwright install chromium
```

## Utilisation

```bash
# Année 2026 sur amazon.com.be (valeurs par défaut)
python download_amazon_invoices.py

# Autre année / autre domaine
python download_amazon_invoices.py --year 2025 --domain amazon.fr
```

1. Au **premier lancement**, une fenêtre Chromium s'ouvre. Connectez-vous
   **manuellement** à Amazon (e-mail, mot de passe, 2FA si demandé).
2. Le script attend que vous soyez connecté, puis parcourt l'historique des
   commandes de l'année et télécharge tous les PDF dans `factures_<année>/`.
3. La session est mémorisée dans `.amazon_profile/` : les fois suivantes vous
   n'aurez plus à vous reconnecter, et vous pourrez utiliser `--headless`.

## Notes / limites

- Le DOM d'Amazon **change régulièrement** selon le pays et la version du site.
  Si certaines factures ne sont pas captées, lancez **sans** `--headless` pour
  voir ce qui se passe, et ajustez les sélecteurs dans le script
  (sections `invoice_triggers` et `pdf_links`).
- Certaines « factures » Amazon sont des pages HTML : le script les convertit
  alors en PDF via le moteur d'impression du navigateur.
- Ne committez **jamais** le dossier `.amazon_profile/` (il contient vos
  cookies de session) — voir le `.gitignore`.
