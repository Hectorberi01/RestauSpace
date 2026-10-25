# Modèles de sites et démos publiques

- `maquis/` : modèle « Maquis » (Next.js en site statique). Le restaurateur l'héberge lui-même ; voir `maquis/README.md`.
- `_demo/` : accueil de `demo.restauspace.com` et page de paiement simulé (`/paiement-demo/<référence>`).
- `Dockerfile.demo` : image `restauspace-demo` (nginx) qui sert chaque modèle sous `/<slug>/`, branché sur le
  restaurant de démonstration.

## Mise en place de la démo (une fois)

1. **DNS** : enregistrement `demo.restauspace.com` vers le serveur applicatif (le certificat est pris par Traefik).
2. **API** (`deploy/production/api.env`) : paiement simulé permis, page de paiement de la démo.
   ```
   Payments__EnableSandbox=true
   Payments__AllowSandboxOutsideDevelopment=true
   Payments__SandboxCheckoutBaseUrl=https://demo.restauspace.com/paiement-demo/
   ```
   Le prestataire `sandbox` n'est utilisable que par les restaurants listés dans `Demo__TenantIds`.
3. **Restaurant de démonstration** : `python3 scripts/seed-demo.py --api https://api.restauspace.com --admin-email <admin>`
   (variables `DEMO_OWNER_EMAIL` et `DEMO_OWNER_PASSWORD`). Le script crée « Chez Awa », la carte, la vente en ligne,
   l'application « Démo modèles » (il affiche sa clé publique UNE fois) et offre le plan Pro sans limite.
4. Ajouter l'id du restaurant affiché par le script dans `Demo__TenantIds` (api.env), redéployer, puis relancer le script :
   il configure alors le paiement simulé.
5. **Build** : mettre la clé publique dans `DEMO_STOREFRONT_KEY` (fichier `restauspace-build-env` de Jenkins), puis
   lancer une release.

## Comportement du restaurant de démonstration

- Paiement en ligne simulé : la page `/paiement-demo/` appelle `POST /v1/storefront/demo/payments/{ref}/capture`.
- Les commandes acceptées passent seules en préparation (30 s) puis prêtes (90 s).
- Aucun SMS, WhatsApp ni e-mail aux visiteurs, sauf le code de connexion par e-mail.
- Chaque nuit (`Demo__PurgeAtUtc`, 02:00 UTC par défaut) : commandes, paiements, clients, réservations, avis et
  notifications sont effacés. La carte, les réglages et l'équipe restent.

## Développement local

```
cd templates/maquis && npm install
NEXT_PUBLIC_API_URL=http://localhost:5080 NEXT_PUBLIC_STOREFRONT_KEY=pk_live_... NEXT_PUBLIC_DEMO=1 npm run dev
```
