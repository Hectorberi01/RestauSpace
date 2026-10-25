# Modèles de sites (base interne)

Point de départ de l'équipe pour les **sites sur mesure** vendus aux restaurants (sur devis, page
`restauspace.com/site-sur-mesure`). Rien ici n'est déployé par RestauSpace : chaque site livré est hébergé par le
restaurant (Cloudflare Pages, Netlify, Vercel ou tout hébergement de fichiers).

- `maquis/` : site statique Next.js branché sur l'API de vente en ligne (`/v1/storefront`) avec la clé publique d'une
  application de vente « Web ». Carte, commande, paiement, suivi, réservation et compte client. Configuration dans
  `site.config.json` ; voir `maquis/README.md`.

Pour un nouveau client : copier le dossier dans un dépôt propre au client, adapter le design et les textes, puis
déclarer dans sa console RestauSpace l'adresse du site (origines autorisées) et les pages de retour du paiement.

Jenkins vérifie le typage de `maquis/` à chaque build.
