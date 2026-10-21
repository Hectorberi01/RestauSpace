# RestauSpace · site vitrine

Site public de présentation : accueil, fonctionnalités, tarifs (francs CFA et euros HT), questions fréquentes, contact
(demande de démo), mentions légales et confidentialité. Application Next.js séparée de la console d'administration
(`restaurantos-super-admin`) : pages statiques, aucun appel à l'API, aucun cookie.

## Lancer

```bash
corepack enable && pnpm install
cp .env.example .env.local     # renseigner l'email, le WhatsApp et les téléphones
pnpm dev                       # http://localhost:3002
pnpm build && pnpm start       # production
```

## Contenu

| Fichier | Rôle |
|---|---|
| `content/site.ts` | nom, URL, coordonnées (variables `NEXT_PUBLIC_*`), informations légales À COMPLÉTER |
| `content/features.ts` | fonctionnalités, regroupées par thème (décrivent ce que le backend sait faire) |
| `content/plans.ts` + `content/catalogue.json` | tarifs : `catalogue.json` est une copie de `scripts/plans/catalogue.json` |
| `content/faq.ts` | questions fréquentes |

Après une modification du catalogue des plans : `pnpm sync:plans`, puis rebuild. Les prix affichés et ceux créés en
base par `scripts/seed-plans.py` viennent ainsi du même fichier.

## Formulaire de contact

Il n'envoie rien au serveur : il prépare un message que le visiteur envoie par email (`mailto:`) ou WhatsApp
(`wa.me`). Sans `NEXT_PUBLIC_CONTACT_EMAIL` ni `NEXT_PUBLIC_CONTACT_WHATSAPP`, il affiche que les coordonnées ne sont
pas configurées.

## Sécurité

En-têtes posés par `next.config.ts` : CSP (en production), `X-Frame-Options: DENY`, `X-Content-Type-Options`,
`Referrer-Policy`, `Permissions-Policy`, HSTS en production. Police Inter servie par le site (aucune requête vers
Google).
