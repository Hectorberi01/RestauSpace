# Audit de cohérence — backend et trois applications

Date : 23 octobre 2026. Périmètre : backend .NET (`src/`), console (`restaurantos-app`), site public (`restaurantos-site`),
back-office (`restaurantos-super-admin`), configuration et déploiement. Audit en lecture seule, sans nouvelle
fonctionnalité : seules les incohérences entre ce qui existe déjà sont relevées. Chaque constat a été vérifié dans le
code (fichier:ligne). Chemins relatifs à la racine du dépôt.

## Synthèse

| | Haute | Moyenne | Basse |
|---|---|---|---|
| Contrats API ↔ fronts | 1 | 4 | 5 |
| Permissions et sécurité | 2 | 4 | 11 |
| Textes et UX | 4 | 8 | 10 |
| Configuration et déploiement | 3 | 7 | 10 |

Ce qui est sain :

- **Contrats :** 716 routes et ~757 appels front comparés, aucun appel vers une route inexistante. 292 interfaces TS alignées
  sur leurs DTO, pagination `items/page/pageSize/totalCount/hasMore` identique, enums en chaînes.
- **Sécurité :** toutes les routes `/admin/*` sous la policy `PlatformAdmin`. Hors admin, seuls trois endpoints acceptent
  un `TenantId` (refresh, sélection de tenant, acceptation d'invitation), tous revérifiés. Aucun secret dans les DTO, aucune
  donnée de carte stockée, audit nettoyé.
- **Liens :** les liens des e-mails (`verifier-email`, `reinitialiser`, `activer`) mènent à des pages existantes. Toutes
  les `NEXT_PUBLIC_*` sont passées au build, et le CORS couvre les quatre origines de production.

Le problème le plus répandu : **les messages d'erreur du backend s'affichent en anglais**. Le back-office ne traduit
que 33 codes sur ~1 500. La console en traduit environ 640, mais il en manque sur des écrans fréquents (caisse, salle,
2FA, restaurant suspendu).

---

## 1. Haute

### H1 — Les rôles par établissement sont ignorés par les contrôles d'accès
- `src/Modules/Staff/Staff.Application/StaffPermissionChecker.cs:12-17`, `src/BuildingBlocks/SaasRestaurant.BuildingBlocks.Api/Permissions.cs:116`
- Le domaine dit que le rôle affecté à un établissement l'emporte (`StaffMember.cs:254-263`). Pourtant `RequirePermission`
  ne lit que le rôle au niveau du tenant :
  - un « Owner » limité à « Serveur » sur l'établissement B garde tous ses droits sur B ;
  - un membre qui n'a qu'une affectation d'établissement reçoit 403 partout.
- `HasLocationPermissionAsync` n'est utilisé qu'une seule fois (`ReviewsBridges.cs:185`). `/staff/me/permissions` ne lit
  que le rôle tenant.
- Correction : contrôler par établissement sur les routes qui portent un `locationId`, ou retirer les rôles par établissement.

### H2 — Une clé d'API peut recevoir des droits que son créateur n'a pas
- `src/Modules/Integrations/Integrations.Application/Commands/CredentialCommands.cs:46-49`
- `integrations.manage` suffit pour émettre une clé avec n'importe quel scope (`payments.refund`, `customers.anonymize`…).
- Correction : refuser tout scope que l'auteur ne détient pas.

### H3 — La recherche globale de la console ne trouve jamais de produit
- Front `restaurantos-app/components/search.tsx:74` (`pageSize: 200`), back `Catalog.Application/Queries/CatalogQueries.cs:66` (maximum 100).
- L'API répond 422. Le `.catch` met une liste vide en silence.
- Correction : `pageSize: 100`, ou le helper `all('/catalog/products')`.

### H4 — Back-office : messages d'erreur en anglais, page de connexion comprise
- `restaurantos-super-admin/src/services/api/client.ts:198-233` (33 codes traduits), `src/components/layout/auth-gate.tsx:59`.
- Exemples : « The email must be verified before a session can be started. », « The account is locked. », les codes
  `tenants.*`, `billing.*`, `identity.password.*`, `identity.auth.too_many_attempts`, `common.*`. Les erreurs 409 et 429
  ne sont pas gérées non plus.
- Correction : compléter la table pour l'identité, les restaurants, les plans et abonnements, les prestataires ; reprendre
  les cas 409/429 de la console ; afficher un message français générique quand le code est inconnu.

### H5 — Console : un conflit métier est présenté comme une modification simultanée
- `restaurantos-app/lib/api.ts:920`
- Tout 409 sans traduction affiche « Quelqu'un a modifié cet élément en même temps ». C'est faux pour une vingtaine de codes :
  `floor.table_session.table_already_occupied`, `has_open_session`, `table_label_already_used`, `items.already_paid`,
  `catalog.tax_category.code_not_unique`, `campaign.not_draft`, `kitchen.ticket.already_exists`, créneau complet…
- Correction : traduire ces codes et réserver le message générique aux codes `*.concurrent_update`.

### H6 — Console : erreurs fréquentes en anglais (2FA, restaurant suspendu, caisse, formules)
- Table `restaurantos-app/lib/api.ts:271`, `describe` l. 916.
- **2FA :** aucun `identity.two_factor.*` n'est traduit. Pire, un mauvais code à la désactivation (401
  `identity.two_factor.code_invalid`) affiche « Session expirée : reconnectez-vous. » (`app/(console)/profil/two-factor.tsx:117`).
- **Restaurant suspendu :** `*.tenant.not_writable` n'est traduit ni pour `orders`, `catalog`, `floor`, `reservations`,
  `staff`, `delivery`, `fiscal`, `notifications`, `reporting`, ni pour `files`.
- **Caisse et salle :** `orders.order.not_editable`, `invalid_transition`, `location_not_operational`, `schedule_in_past`,
  `too_many_items`, `floor.table_session.has_open_orders`, `reservations.*`, `kitchen.*`.
- **Formules :** les 16 codes `catalog.formula.*`.
- Correction : traduire ces familles en priorité. Les traductions 2FA existent déjà dans `client.ts:199-204` du back-office.

### H7 — Les sauvegardes de la base ne peuvent pas se connecter
- `deploy/database/docker-compose.yml:57` (`PGHOST: postgres`, en TCP depuis le réseau Docker 172.x) face à
  `deploy/database/pg_hba.conf`, qui n'accepte que le socket local, 127.0.0.1 et 10.10.0.0/24.
- Chaque `pg_dump` échoue : aucune sauvegarde, alors que le README s'appuie dessus. Ne concerne que le cas où la base tourne
  avec `deploy/database`.
- Correction : autoriser le sous-réseau du projet Docker dans `pg_hba.conf`, ou sauvegarder par le socket du conteneur postgres.

### H8 — Envoi de photos bloqué par la CSP de la console
- `FILES_ORIGINS` est vide dans `build.env` (Jenkins). Il alimente `NEXT_PUBLIC_FILES_ORIGINS`, lu par
  `restaurantos-app/proxy.ts:11,26` (connect-src).
- Le navigateur envoie les fichiers directement à R2 (`lib/files.ts:147`). Sans cette origine, l'envoi est refusé.
  La règle CORS du bucket R2 (origine `https://app.restauspace.com`, PUT/GET, `Content-Type`) n'est documentée nulle part.
- Correction : `FILES_ORIGINS=https://<AccountId>.r2.cloudflarestorage.com` dans `build.env`, relancer le pipeline, et
  documenter le CORS du bucket.

### H9 — Fichiers d'environnement locaux du Mac incohérents avec la production
- Les fichiers `deploy/production/api.env` et `.env` du Mac (ignorés par git) ne sont pas ceux du VPS, qui ont été corrigés
  pendant le déploiement :
  - SMTP Hostinger avec une clé Resend et `FromAddress` vide, ce qui ferait planter l'API au démarrage ;
  - `POSTGRES_PASSWORD` contient une commande collée par erreur ;
  - `IMAGE_REGISTRY` est absent ;
  - les variables d'observabilité sont vides.
- **La même valeur est utilisée pour huit secrets** (TwoFactor, Payments, Notifications ×2, Integrations, Fiscal,
  Storefront, PublicMedia) : une fuite compromet tous les chiffrements, et les changer plus tard rend les données illisibles.
- Correction : régénérer une valeur par clé avec `generate-secrets.sh` **avant l'ouverture**, vérifier que le VPS n'a pas le
  même défaut, et aligner (ou supprimer) les copies locales.

---

## 2. Moyenne

### Contrats API ↔ fronts
- **M1 — Un mauvais mot de passe actuel est compté deux fois.** `restaurantos-app/lib/api.ts:145-148`,
  `restaurantos-super-admin/src/services/api/client.ts:138-141`, face à `PasswordVerifier.cs:29-31`. Le backend renvoie 401 sur
  `PUT /me/password` et `PUT /me/email` (plus `/me/two-factor/setup|disable` côté back-office). Le front croit à un jeton
  expiré, renouvelle la session et rejoue la requête : deux échecs sont enregistrés sur la limite de 10. Correction : ne
  rejouer que si `code = identity.auth.invalid_access_token`.
- **M2 — Le back-office crée un restaurant sans propriétaire.** `POST /admin/tenants/{id}/owners`
  (`StaffEndpoints.cs:195-202`) existe mais n'est jamais appelé (`live-page.tsx:112`, `admin-api.ts:188-198`).
- **M3 — Fidélité : la valeur `Closure` manque au front.** `restaurantos-app/lib/marketing.ts:129,171` face à
  `LoyaltyEnums.cs:23`. La ligne de clôture d'un compte s'affiche sans libellé.
- **M4 — Lien de l'e-mail « export terminé » cassé.** `ReportingBridges.cs:431-437` construit
  `https://app.restauspace.com/reporting/exports/{id}/file`. Cette page n'existe pas dans la console, et la vraie route est
  `/v1/reporting/exports/{id}/file` sur l'API, avec un jeton Bearer.

### Permissions et sécurité
- **M5 — Coordonnées des clients lisibles sans permission.** `Reservations.Api/ReservationsEndpoints.cs:53-60, 81-84, 153-156`,
  `Floor.Api/FloorEndpoints.cs:158-160` renvoient nom, téléphone et e-mail sans permission, alors que `/by-customer` exige
  `customers.view`. Une clé d'API qui n'a que `reservations.configure` y accède aussi.
- **M6 — Jetons des clients invités dans l'URL et non masqués dans les traces.** `Storefront.Api/StorefrontEndpoints.cs:318…434`.
  Le masquage de `url.path` (`HostComposition.Observability.cs:46-52`) ne couvre que les imprimantes. Le jeton de suivi part
  aussi chez le prestataire de paiement dans `returnUrl` (`ordering.tsx:752`, `tracking.tsx:100`).
- **M7 — Promotions : établissements et produits non vérifiés.** `Promotions.Application/PromotionsWorkspace.cs:108-111`.
  Les `LocationIds` et `ProductIds` sont enregistrés sans vérifier qu'ils appartiennent au tenant, contrairement aux autres modules.
- **M8 — Site public : CSP plus faible et jeton client en `localStorage`.** `restaurantos-site/next.config.ts:13` autorise
  `'unsafe-inline'` sans nonce, alors que la console et le back-office utilisent un nonce. Le jeton client est en
  `localStorage` (`lib/storefront.ts:613-624`), celui de la console en `sessionStorage`.

### Textes et UX
- **M9 — Heures affichées dans le fuseau du navigateur.** Console : `lib/format.ts:22-28`, reçu, ticket imprimé. Site :
  `lib/storefront.ts:467` (`clock()`). Les réservations utilisent bien le fuseau de l'établissement : il y a donc deux
  comportements pour une même notion.
- **M10 — Back-office : journal d'audit et activité affichés en codes techniques.** `mappers.ts:120-125`,
  `dashboard/page.tsx:177`, `live-drawer.tsx:43` affichent par exemple « tenants.tenant.suspended ». La console les traduit.
- **M11 — Back-office : décimales incohérentes dans les montants.** `src/utils/format.ts:5` donne « 29 € » mais « 29,50 € »,
  et des décimales en XOF. La console fixe le nombre de décimales par devise.
- **M12 — Numéros béninois d'exemple à 8 chiffres.** Ils apparaissent dans `payments.tsx:254,286`, `clients/page.tsx:54`,
  `reservations/dialogs.tsx:150,232`, `customer-picker.tsx:72`, `etablissements/[id]/page.tsx:183` et
  `site/app/contact/contact-form.tsx:30`. D'autres écrans utilisent le format à 10 chiffres « +229 01 … ».
- **M13 — Mentions légales « [à compléter] ».** `restaurantos-site/app/mentions-legales/page.tsx:7`, `content/site.ts:30-37`.
- **M14 — Erreurs 5xx ou 400 sans code affichées en anglais.** `restaurantos-app/lib/api.ts:71` et `client.ts:68`
  affichent par exemple « An error occurred while processing your request. ».
- **M15 — Console : erreur brute dans un toast.** `app/(console)/commandes/page.tsx:274` affiche `error.message` au lieu
  de `describe(error)`.
- **M16 — Back-office : « Voir le site » mène au tableau de bord.** `admin-layout.tsx:3` pointe vers `/`.

### Configuration et déploiement
- **M17 — Menu public des QR codes appelé par l'URL publique.** `restaurantos-site/app/menu/[locationId]/page.tsx:42` lit
  `API_INTERNAL_URL`, défini en dev mais absent du service `site` en prod. Correction : `API_INTERNAL_URL: http://api:8080`.
- **M18 — Le README oublie l'origine et la page de retour de l'application « Site web ».** Sans `https://restauspace.com`
  et `https://restauspace.com/commande`, `GuestCheckout.cs:583` refuse le paiement en ligne.
- **M19 — `.env` et `build.env` font doublon.** Contacts, `FILES_ORIGINS` et `ADMIN_IDLE_MINUTES` ne servent qu'au build,
  donc `build.env` côté Jenkins. Le README dit de les remplir dans `.env`, et les valeurs divergent déjà (WhatsApp, téléphone Bénin).
- **M20 — Noms de base différents entre les deux exemples.** `deploy/database/.env.example` utilise `saasrestaurant`,
  `deploy/production/.env.example` utilise `es_restaurant` / `es_restaurant_user`.

---

## 3. Basse

### Contrats
- `restaurantos-super-admin/src/services/api/admin-api.ts:344-346` : `GiftCard`, `DeliveryPlatform` et `CashOnDelivery`
  manquent au type des moyens de paiement (`PaymentMethodType.cs:31-40`).
- `admin-api.ts:404` : sept valeurs de `NotificationFeature` manquent (`DeliveryStatus`, `Image`, `Topic`, `DeviceTargeting`…).
- `restaurantos-site/lib/storefront.ts:290,379-382` : `reservations.waitlist.offer_expired` et `not_offered` ne sont pas traduits.
- `restaurantos-site/lib/storefront.ts:260` : `ReadyForPickup` manque à `DeliveryStatus` (sans effet aujourd'hui).
- Pagination hétérogène : `skip/take` sur `/notifications`, `take` sur `/fiscal/invoices` et `/printing/jobs`, `limit` sur
  `/payments/cash-sessions`, `/payments/journal` et `/audit/me`, alors que le reste utilise `page/pageSize`.
- Routes du back-office jamais branchées alors que l'écran existe : branding, devise, fuseau et mode TTC d'un restaurant
  (`TenantsEndpoints.cs:74-104`), modification d'un prestataire de notification (`NotificationsEndpoints.cs:378`).

### Permissions et sécurité
- `restaurantos-app/app/(console)/caisse/page.tsx:23` : la permission `staff.view` n'existe pas côté backend, donc les noms
  des caissiers ne se chargent jamais.
- `restaurantos-app/app/(console)/salle/page.tsx:339` : la permission `orders.view` n'existe pas côté backend.
- `parametres/notifications.tsx:311`, `online.tsx:380`, `printers.tsx:228` : le front ouvre ces écrans avec « voir OU
  gérer », alors que le backend exige « voir ». Un membre qui a seulement « gérer » obtient 403.
- `Staff.Api/StaffEndpoints.cs:136-139` : `GET /staff/roles` exige `staff.manage`, alors que les modifications exigent `roles.manage`.
- Lectures sensibles sans permission : paiements (`PaymentsEndpoints.cs:59-135`), facture fiscale (`FiscalEndpoints.cs:69`),
  fournisseurs et bons de commande (`InventoryEndpoints.cs:187,210-216`), modèles de notification (`NotificationsEndpoints.cs:138-145`).
- `Identity.Domain/Users/UserEvents.cs:3-4` : l'e-mail figure dans l'outbox de `UserRegistered` et `UserInvited` sans être utilisé.
- `restaurantos-app/next.config.ts:8` : `geolocation=()` bloque le bouton « Me localiser » (`etablissements/[id]/page.tsx:88-93`).
- `restaurantos-site/components/footer.tsx:74` : le formulaire newsletter vers une URL externe est bloqué par `form-action 'self'`.
- `restaurantos-app/components/console.tsx:54-68` : l'e-mail, les permissions et le tenant restent dans `localStorage` après
  déconnexion. La base hors ligne `restaurantos-offline` n'est ni rangée par tenant ni vidée (à vérifier).
- `restaurantos-super-admin/src/admin/settings/page.tsx:298,307` : le lien « Documentation » vers `/docs` est mort en prod.
- `restaurantos-site/content/site.ts:6` : `adminUrl` n'est jamais utilisé, mais l'adresse du back-office est embarquée dans le site public.
- `Loyalty.Api/LoyaltyEndpoints.cs:160-162` : la recherche de carte cadeau n'a pas de limite de débit.
- `ReverseProxy:KnownNetworks` fait confiance par défaut à tout 10.0.0.0/8 (`HostComposition.Platform.cs:88`), qui inclut le
  réseau privé entre serveurs.

### Textes et UX
- Back-office nommé de cinq façons (« Super Admin », « Espace administration », « Administration »…) ; metadata
  « SaaS restaurants » (`app/layout.tsx:1`).
- Jargon visible : hôte de l'API dans la barre latérale, « chargé(s) depuis l'API », « API injoignable (https://…) —
  vérifiez qu'elle est démarrée ».
- Site : « restaurantos.app/developpeurs » (`developpeurs/page.tsx:162`) et « console.restauspace.app »
  (`fonctionnalites/page.tsx:70`, `components/visual.tsx:80`) au lieu de `app.restauspace.com`.
- Capture marketing `public/captures/hero-dashboard.webp` : un restaurant de Cotonou facturé en euros.
- Console : formats de date différents (`journal`, `caisse/vente`, `personnel/tills`, `caisse` affichent les secondes) ;
  fuseau du profil affiché en code brut (`profil/page.tsx:248,285`).
- Back-office : « Close » et « Loading » en anglais pour les lecteurs d'écran (`dialog.tsx:76`, `sheet.tsx:80`, `spinner.tsx:9`).
- La même limite de plan s'appelle « membres de l'équipe » dans la console et « membres du personnel » dans le back-office.

### Configuration et documentation
- `api.env` local : espace avant le nom du bucket (`Files__R2__Bucket= restauspace`).
- `Billing:OnlinePayment:Provider` et une dizaine de réglages lus par le code (`Identity:RateLimiting:*`, `Caching:*`,
  `Api:ExposeOpenApi`, `Audit:RetentionDays`, `ReverseProxy:KnownNetworks`…) ne figurent pas dans `api.env.example`.
- Nom de service OTLP `restauspace-api` en prod et `saasrestaurant-api` en dev ; port OTLP 4327 (appsettings dev) contre 4317
  (compose dev) ; le compose dev renvoie vers `deploy/observability`, qui n'existe plus sur le Mac.
- `src/Host/README.md` périmé : nombre de modules, routes sans `/v1`, `UseForwardedHeaders` dit « pas fait ».
- `.env.example` des fronts incomplets : `NEXT_PUBLIC_CONSOLE_URL`, `SIGNUP_URL`, `NEWSLETTER_URL` et `SOCIAL_*` pour le site,
  `NEXT_PUBLIC_IDLE_MINUTES` pour le back-office.

---

## Points écartés après vérification

Les constats suivants venaient d'une copie partielle du dépôt et sont faux : `Directory.Build.props`, `.gitignore`,
`.dockerignore`, `Jenkinsfile` et `docs/AUDIT-CONTENEURS-PRODUCTION.md` existent bien à la racine. `.gitignore` exclut
`deploy/production/{.env,api.env,build.env}`, et `.dockerignore` exclut `deploy/`, `restaurantos-*/`, `Tests/` et les `bin/obj`.

## Ordre de traitement suggéré

1. **Avant l'ouverture :** H9 (secrets distincts), H1, H2, M5, M6, M7 (sécurité), H7 (sauvegardes), H8 et M18 (photos et
   paiement du site).
2. **Confort d'usage :** H3, H4, H5, H6, M1, M14 (erreurs lisibles en français), M9 (fuseaux).
3. **Le reste,** au fil des évolutions.
