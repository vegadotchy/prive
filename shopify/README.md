# IATECHFUTUR — Refonte « style Amazon »

Archive versionnée de la personnalisation appliquée à la boutique Shopify
**IATECHFUTUR** (iatechfutur.be) pour reproduire l'ergonomie et l'agencement
d'Amazon, avec la marque IATECHFUTUR (pas de logo ni de marque Amazon — choix
volontaire pour éviter toute contrefaçon / risque de confusion).

## Ce qui a été fait

### 1. Navigation « rayons » (appliquée en LIVE)
Le menu principal (`main-menu`) a été restructuré en départements façon Amazon,
avec sous-rayons, à partir des collections existantes (collections vides exclues) :

- **Nouveautés**, **Meilleures ventes**, **Promotions** (en tête)
- **High-Tech & Informatique** → High-Tech & Gadgets, Informatique, Multimédia, Logiciels, Bureautique, Abonnements
- **Mode & Accessoires** → Mode Femme/Homme/Enfant, Chaussures (F/H/E), Lingerie, Accessoires F/H, Maroquinerie & Luxe
- **Maison, Cuisine & Jardin** → Maison & Déco, Cuisine, Chambre, Salle de bain, Literie, Jardin
- **Beauté & Santé** → Beauté & Hygiène, Santé & Bien-être, Équipements médicaux
- **Bébé, Jouets & Jeux** → Bébé & Puériculture, Jouets & Jeux, Recharges de jeux
- **Sport & Loisirs** → Sport & Fitness, Football, Livres
- **Auto, Bricolage & Animaux** → Auto & Moto, Bricolage & Réparation, Sécurité, Animaux
- **Marques & Luxe**

### 2. Habillage visuel (sur un thème dupliqué, NON publié)
Thème : **« IATECHFUTUR - Style Amazon »** (dupliqué depuis « Copie de Refresh »).

- En-tête sombre Amazon (`#131921`) : logo, barre de recherche large à fond blanc
  avec bouton de recherche orange (`#FEBD69`), icônes compte/panier à droite.
- 2ᵉ barre pleine largeur « Tous les rayons » (`#232f3e`) avec les départements.
- Boutons jaune/orange Amazon (`#FFD814` / `#FFA41C`), en forme de pilule.
- Fiches produits compactes : carte blanche, titre bleu lien (`#007185`),
  prix rouge (`#B12704`), notes en orange, ombre au survol, grille plus dense.
- Pied de page sombre avec bandeau « ↑ Retour en haut ».
- Bandeau d'annonce ramené à une taille raisonnable (22px → 16px).

Fichiers sauvegardés ici :
- `amazon-overrides.css` — feuille de style d'overrides (chargée dans `layout/theme.liquid`).
- `sections-header-group.json` — réglages d'en-tête (menu déroulant, correctif couleur recherche).

## Étape restante (1 clic, côté propriétaire)

L'intégration Shopify **bloque, pour raisons de sécurité, l'édition et la
publication directes du thème live**. L'habillage a donc été construit sur une
copie. Pour le mettre en ligne :

1. Admin Shopify → **Boutique en ligne → Thèmes**
2. Thème **« IATECHFUTUR - Style Amazon »** → **Aperçu** pour vérifier
3. **Publier** quand le rendu convient

La navigation en rayons, elle, est déjà active sur le thème en ligne actuel.

## Référence technique
- Boutique : IATECHFUTUR — iatechfutur.be (Shopify Basic, EUR, Belgique)
- Thème live : « Copie de Refresh » (`OnlineStoreTheme/191595675988`)
- Thème refonte : « IATECHFUTUR - Style Amazon » (`OnlineStoreTheme/197803114836`)
- Menu : `Menu/260179296596` (`main-menu`)
