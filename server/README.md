# Serveur d'activation en ligne — IATECH-SHIELD PRO

Implémentation **de référence** du serveur d'activation utilisé par la fonction
« Activer en ligne » de l'application. Elle est fournie prête à déployer ; vous
restez libre de la réécrire dans le langage de votre choix tant que le protocole
ci-dessous est respecté.

## Principe (sécurité)

L'application vérifie **hors-ligne** chaque licence avec sa clé publique embarquée
(ECDSA P-256). Le serveur ne fait qu'**autoriser** l'émission d'une licence
signée ; il ne peut rien forger sans la **clé privée**, et un client ne peut pas
contourner la vérification de signature. Le serveur peut donc tourner sur
n'importe quel hébergeur sans affaiblir la sécurité.

## Protocole

`POST /activate` — corps JSON :

```json
{ "key": "ORDER-XXXX", "machineId": "ABCD…", "product": "iatech-shield", "version": "0.2.1" }
```

Réponse JSON :

```json
{ "ok": true, "licenseKey": "ABCDE-FGHIJ-…", "message": "Activation réussie. Merci !" }
```

- `key` : clé d'achat remise au client après paiement (≠ licence).
- `licenseKey` : licence **signée** renvoyée par le serveur, stockée et vérifiée
  hors-ligne par l'application.

## Déploiement

1. **Générer la paire de clés** (une seule fois) avec l'outil fourni :

   ```bash
   dotnet run --project src/IatechShield.KeyGen -- init --out keys
   # → keys/private.pem (SECRÈTE)  +  keys/public.pem
   ```

   Placez `public.pem` dans `src/IatechShield.Core/Licensing/public_key.pem`
   **avant** de compiler l'application (elle l'embarque). Gardez `private.pem`
   uniquement sur le serveur.

2. **Déclarer les commandes** dans `orders.json` (clé d'achat → palier) :

   ```json
   {
     "ORDER-2A7F-9C31": { "tier": "Yearly" },
     "ORDER-5B88-1D40": { "tier": "Lifetime" }
   }
   ```

   Paliers : `Monthly`, `Yearly`, `Lifetime`.

3. **Lancer le serveur** :

   ```bash
   export IATECH_PRIVATE_KEY_PEM=keys/private.pem
   export IATECH_ORDERS_FILE=orders.json
   dotnet run --project server/ActivationServer --urls http://0.0.0.0:8080
   ```

4. **Pointer l'application vers le serveur** via la variable d'environnement
   (sur le poste client) :

   ```
   IATECH_ACTIVATION_URL=https://votre-domaine/activate
   ```

## Notes

- L'anti-partage est minimal : la première activation lie la clé d'achat à un
  `machineId`. Adaptez (base de données, quotas, révocation) selon vos besoins.
- En production, servez en **HTTPS** et placez le serveur derrière un proxy.
- Ce projet n'est **pas** compilé par la CI de l'application (qui ne publie que
  l'app et la CLI) : il se déploie séparément.
