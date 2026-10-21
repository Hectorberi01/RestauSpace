# RestauSpace — Tableau de bord Super Admin

Application React + TypeScript basée sur le design violet / sidebar sombre créé dans cette conversation. Elle couvre les 11 écrans du Super Admin d’un SaaS restaurant.

## Prérequis et lancement

- Node.js 22.13 ou supérieur.
- pnpm (version indiquée dans package.json).

Depuis ce dossier :

```bash
corepack enable
pnpm install --frozen-lockfile
pnpm dev
```

Ouvrez http://localhost:3000/admin/dashboard.
Si Corepack n’est pas disponible : `npm install -g pnpm@11.25.0` puis reprenez les commandes pnpm.

## Vérification et production

```bash
pnpm typecheck
pnpm build
pnpm start
```

pnpm est le seul gestionnaire utilisé : `pnpm-lock.yaml` fixe les dépendances et `pnpm-workspace.yaml` les versions corrigées de dépendances indirectes (avis de sécurité).

## Écrans

Tous reliés à l'API (détail des routes : `docs/ADMIN.md` à la racine du dépôt) : tableau de bord, utilisateurs,
restaurants, abonnements, facturation (paiements d'abonnement), plans, statistiques, notifications (alertes calculées
et envois en échec), paramètres (prestataires de paiement et de notification), journaux d'audit.

## Structure

- app/ : routes Next.js et layout global.
- src/admin/ : pages organisées par domaine.
- src/components/ : layout, tableaux, graphiques, formulaires et feedback.
- components/ui/ : composants shadcn/ui.
- src/hooks/use-admin.tsx : état partagé chargé depuis l'API.
- proxy.ts : Content-Security-Policy avec nonce par requête ; next.config.ts : en-têtes de sécurité.
- src/services/ : données initiales, mutations, journal, exports.
- src/types/, src/constants/, src/utils/ : contrats, navigation et formats.

## Stack

React, TypeScript, Tailwind CSS, shadcn/ui, Lucide, Recharts, TanStack Table, Sonner, jsPDF. Le routage est fourni par Next.js ; React Router n’est pas nécessaire.

## Connexion au backend

Copiez `.env.example` en `.env.local` (`NEXT_PUBLIC_API_URL`, par défaut http://localhost:5080). L'écran de connexion
demande un compte administrateur de la plateforme. Détails, création du premier compte et routes utilisées :
`docs/ADMIN.md` à la racine du dépôt.

- src/services/api/client.ts : appels HTTP, session, renouvellement du jeton, erreurs.
- src/services/api/admin-api.ts : contrats et routes `/v1/admin/*`.
- src/services/api/mappers.ts : conversion vers les lignes des tableaux (statuts en français).
- src/hooks/use-admin.tsx : chargement, rechargement et actions.

## Sécurité

- `proxy.ts` pose une Content-Security-Policy stricte : scripts autorisés par nonce (aucun script externe ou injecté),
  connexions limitées à l'origine de `NEXT_PUBLIC_API_URL`, aucune intégration dans une iframe. Cette variable doit donc
  être définie au build ET au démarrage du serveur.
- `next.config.ts` ajoute `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`,
  `Cross-Origin-Opener-Policy` et, en production, HSTS.
- Toutes les pages sont rendues à la demande (nécessaire au nonce).
