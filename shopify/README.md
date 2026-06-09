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

## Mise à jour — Barre de rayons 1 ligne + menu latéral
Ajout d'une section `sections/amazon-categories.liquid` (enregistrée dans
`header-group.json`) qui remplace le menu déroulant qui passait à la ligne par :
- une **barre de rayons sur une seule ligne** (#232f3e), défilement horizontal discret ;
- un bouton **« ☰ Tous les rayons »** qui ouvre un **menu latéral coulissant**
  (« Bonjour, … » + « Choisir une catégorie » + liste verticale des départements
  avec chevrons et sous-rayons dépliables), façon panneau Amazon.

Fichier sauvegardé : `shopify/amazon-categories.liquid`.

## Correctif images produits (thème v2)
La règle `mix-blend-mode:multiply` (+ overrides) sur les images de fiches
produits les rendait invisibles. Correctif ajouté dans le `<style>` de
`amazon-categories.liquid` : `object-fit:contain`, `opacity/visibility` forcés,
`mix-blend-mode:normal`, fond blanc — l'image produit s'affiche en entier façon Amazon.

Comme le thème « IATECHFUTUR - Style Amazon » a été publié (devenu LIVE),
l'API bloque toute écriture dessus. Le correctif a donc été appliqué sur une
nouvelle copie **« IATECHFUTUR - Style Amazon v2 »** (`OnlineStoreTheme/197805572436`),
à publier en 1 clic. Toute modif visuelle ultérieure suit le même cycle
(copie → édition → publication manuelle).

## v3 — Images à taille fixe (200px) + suppression des filtres
- Cause réelle des images invisibles : les overrides cassaient le système de
  **ratio** de Dawn → la boîte image se retrouvait sans hauteur. Correctif :
  `.card__media` forcé en `position:relative;height:200px;overflow:hidden`,
  image en `position:absolute;inset:0;object-fit:contain` → image entière,
  taille fixe, façon Amazon (descriptif/titre dessous, 2 lignes max).
- Suppression du **panneau de filtres latéral** des pages collection
  (`#main-collection-filters`, `.facets*`) ; grille produits en pleine largeur.
- Appliqué sur **« IATECHFUTUR - Style Amazon v3 »** (`OnlineStoreTheme/197807440212`).
  ⚠️ À tester en **Aperçu** avant de publier (publier verrouille l'édition via l'API).

## v4 — Espace connexion / inscription (façon Amazon)
Ajout d'un bloc compte dans la barre de rayons (à droite) :
- Déconnecté : « Bonjour · Identifiez-vous » (→ login) + bouton « S'inscrire » (→ register)
- Connecté : « Bonjour, {prénom} · Mon compte » (→ compte)
Et l'en-tête « Bonjour… » du menu latéral devient cliquable (login/compte) +
lien « Nouveau client ? Inscrivez-vous » pour le mobile.
Appliqué sur **« IATECHFUTUR - Style Amazon v4 »** (`OnlineStoreTheme/197808161108`).
