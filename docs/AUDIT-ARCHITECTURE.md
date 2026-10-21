# Audit d'architecture — SaasRestaurant (backend .NET 10)

Date : 10/10/2026. Périmètre : `src/` (21 modules, BuildingBlocks, Host) et `Tests/` (89 projets, environ 3 565 tests
déclarés). Méthode : lecture du code et analyse statique scriptée (références entre projets, ponts de l'hôte,
consommateurs d'outbox, dépendances synchrones, conventions des routes). AUCUNE exécution : le SDK .NET n'est pas
disponible dans l'environnement d'audit ; les comportements dynamiques sont déduits du code.

## Synthèse

| # | Audit | Priorité | Verdict | Points bloquants |
|---|---|---|---|---|
| 1 | Architecture globale | Rouge | CONFORME | Aucun ; l'hôte concentre toute l'intégration (Program.cs : 542 lignes) |
| 2 | Clean Architecture | Orange | CONFORME | 0 violation sur 84 projets |
| 3 | DDD | Orange | CONFORME avec réserves | Domain events servis tels quels comme contrats d'intégration |
| 4 | Découpage des modules | Rouge | PARTIEL | Tenants est un hub (19 dépendants) ; 3 inversions de dépendance |
| 5 | Dépendances | Orange | PARTIEL | 0 cycle à la compilation, 3 cycles à l'exécution |
| 6 | API | Orange | PARTIEL | Ni versioning, ni OpenAPI ; 2 styles de pagination ; pas d'ETag |
| 7 | Communication interne | Orange | BON | 2 écritures synchrones entre modules ; aucune mise en cache |
| 8 | Transactions | Rouge | À CORRIGER | 2 doubles écritures ; appel HTTP prestataire dans une transaction verrouillée |
| 9 | Idempotence | Rouge | À CORRIGER | Message empoisonné = outbox du module bloquée ; pas d'`Idempotency-Key` |
| 10 | Évolutivité | Jaune | PARTIEL | Contrats implicites (JSON des domain events), ponts en mémoire |

Les trois chantiers à ouvrir en premier, détaillés en fin de document :

1. Durcir le dispatcher d'outbox : tentatives bornées, file morte (dead-letter), une livraison par consommateur.
2. Ajouter `Idempotency-Key` sur les POST qui créent de l'argent ou des commandes.
3. Sortir l'appel HTTP au prestataire de paiement de la transaction verrouillée.

---

## 1. Architecture globale (Rouge) — CONFORME

**Constats**

- **Monolithe modulaire** : 21 modules, chacun découpé en `Domain` / `Application` / `Infrastructure` / `Api`, composés
  par un seul hôte (`SaasRestaurant.Host`). Un processus, une base PostgreSQL, un schéma par module (nom du schéma = nom
  du module), un historique de migrations par schéma.
- **Aucune requête entre schémas** : aucun SQL ni mapping EF d'une infrastructure ne référence le schéma d'un autre
  module (analyse des 21 projets `Infrastructure`, hors migrations).
- **L'hôte est le seul point d'intégration** :
  - 53 ponts (`XxxFromYyy`) servent les ports d'un module avec les queries d'un autre ;
  - 15 consommateurs d'outbox traduisent les événements d'un module en commandes d'un autre.
  - Aucun module ne référence un autre module.
- **Outbox par module**, écrite dans la transaction de l'agrégat (`OutboxUnitOfWork`), distribuée en mémoire par un
  `OutboxDispatcher<TContext>` par module.
- **Isolation des tenants** : `TenantDbContext` applique un filtre fail-closed (sans tenant, rien n'est lu), en plus du
  filtre explicite de chaque dépôt.

**Risques**

- `Program.cs` (542 lignes) mélange enregistrement, configuration, routes et migrations de 21 modules : c'est le
  fichier le plus exposé aux conflits et aux oublis (une permission ou un consommateur non enregistré passe la
  compilation).
- Un seul rôle PostgreSQL pour tous les schémas : un défaut d'un module peut toucher les tables d'un autre.

**Recommandations**

- Découper `Program.cs` en une méthode d'extension par module côté hôte (`builder.AddOrdersComposition()`), qui
  regroupe module, ponts et consommateurs.
- Ajouter un test d'architecture (NetArchTest ou ArchUnitNET) qui fige les règles vérifiées ici à la main :
  dépendances entre couches, absence de références entre modules, schéma unique par DbContext.
- En production : un rôle PostgreSQL par module, avec droits limités à son schéma.

## 2. Clean Architecture (Orange) — CONFORME

**Constats (vérifiés sur les 84 `.csproj` de `src/`)**

| Couche | Référence autorisée | Violations |
|---|---|---|
| Domain | BuildingBlocks.Domain | 0 |
| Application | son Domain, BuildingBlocks.Application | 0 |
| Infrastructure | son Application, BuildingBlocks.Infrastructure | 0 |
| Api | son Application et son Infrastructure, BuildingBlocks.Api | 0 |
| Entre modules | interdit | 0 |

- `Domain` et `Application` n'ont AUCUN package NuGet et aucun `using` d'EF Core, d'ASP.NET Core ou de Npgsql.
- Le domaine est synchrone : aucune méthode `async` dans les 21 projets `Domain`. Les vérifications qui demandent la
  base passent par l'Application, qui transmet le résultat au domaine (`isUnique`, `AvailabilityDecision`).
- Les couches `Api` touchent leur `DbContext` uniquement pour appliquer les migrations (22 fichiers) : c'est de la
  composition, c'est acceptable.

**Réserves**

- Plusieurs interfaces de vérification vivent dans `Domain` alors que seul l'Application les appelle :
  `ITableSessionOrdersChecker`, `ITableOccupancyChecker`, les `I…UniquenessChecker` de Catalog, Customers, Identity,
  Inventory et Billing. Ce n'est pas une violation, mais elles seraient mieux placées dans `Application/Abstractions`.

## 3. DDD (Orange) — CONFORME avec réserves

**Constats**

- 45 agrégats, 17 entités, plusieurs centaines d'objets-valeurs et d'ids typés, 268 domain events.
- 0 setter public dans les 21 projets `Domain`.
- 0 référence objet d'un agrégat vers un autre agrégat : les liens passent par des ids typés (vérifié par analyse).
- Les données d'un autre module entrent comme miroirs locaux (`LocationReference`, `CustomerReference`,
  `OrderReference` propres à chaque module) remplis par l'Application, jamais saisis : c'est une couche
  anti-corruption.
- Politiques de domaine explicites : `ReservationAvailabilityPolicy`, `DiscountPolicy`, `TicketReadinessPolicy`,
  `WebhookRetryPolicy`, `OverdraftPolicy`, etc.
- Concurrence optimiste (`xmin`) sur les agrégats modifiables.

**Réserves**

- **Les domain events SONT les contrats d'intégration** : `OutboxMessage.From` sérialise l'événement du domaine tel
  quel (nom de classe, propriétés), et l'hôte le relit champ par champ (`GetProperty("TenantId")`). Renommer une
  propriété d'un événement casse un consommateur À L'EXÉCUTION, sans erreur de compilation ni version de message.
- Notifications compte 7 agrégats : c'est cohérent (templates, configurations, prestataires, inbox, appareils...),
  mais c'est le module le plus large ; à surveiller s'il grossit encore.

**Recommandation**

- Introduire des événements d'intégration explicites et versionnés : un projet `X.Contracts` par module, avec un
  record par événement public et un champ `Version`. Le domaine publie ses events ; une table de correspondance
  (dans l'Application ou l'Infrastructure) écrit le contrat dans l'outbox. Les consommateurs de l'hôte désérialisent le
  contrat typé au lieu de lire le JSON à la main.

## 4. Découpage des modules (Rouge) — PARTIEL

**Correspondance avec le métier**

| Notion métier | Module(s) |
|---|---|
| Restaurant | Tenants (compte, statut, devise) + Locations (établissements, horaires) |
| Menu | Catalog (produits, variantes, options, menus, taxes) |
| Order | Orders (agrégat central), Kitchen (tickets), Payments |
| Reservation | Reservations + Floor (salles, tables, sessions) |
| Billing | Billing (plans, abonnements, quotas) |
| Transverses | Identity, Staff, Customers, Notifications, Promotions, Loyalty, Inventory, Reporting, Files, Integrations, Reviews, Audit |

Le découpage suit bien les sous-domaines. Les modules en aval (Reporting, Audit, Integrations) ne sont dépendus par
personne : c'est la bonne forme.

**Défauts de frontière**

1. **Tenants est un hub** : 19 modules l'interrogent de façon synchrone. 26 cas d'usage appellent
   `AcceptsBusinessWritesAsync` à chaque écriture. Si Tenants est lent ou indisponible, toutes les écritures du système
   le sont aussi.
2. **Tenants dépend de Catalog** (`PricedCatalogFromCatalog`, verrouillage de la devise dès qu'un prix existe) : le
   module amont dépend d'un module aval.
3. **Files dépend de Catalog et Locations** (`FileOwnersFromModules`, validation du propriétaire d'un fichier) : un
   sous-domaine générique dépend de sous-domaines métier.
4. **Floor demande à Orders** s'il reste des commandes ouvertes sur une session de table, alors qu'Orders interroge
   déjà Floor (voir l'audit 5).

**Recommandations**

- Tenants : publier `TenantSuspended`, `TenantReactivated` et `TenantClosed`, et tenir dans chaque module une
  projection locale « tenant accepte les écritures » (ou un cache court, de 30 à 60 s, derrière le port
  `IXxxTenants` existant, sans toucher aux modules).
- Devise : Catalog publie `FirstPricedProductCreated` ; Tenants verrouille sa devise en réaction.
- Files : le module propriétaire valide son propre fichier (Catalog vérifie qu'un média appartient à son produit),
  ou Files accepte un propriétaire opaque `(module, id)` sans le vérifier.

## 5. Dépendances (Orange) — PARTIEL

**À la compilation : aucun cycle.** Le graphe des `ProjectReference` est acyclique et respecte les couches (audit 2).

**À l'exécution : trois cycles synchrones** (composantes fortement connexes du graphe des ponts de l'hôte)

| Cycle | Arcs |
|---|---|
| Tenants → Catalog → Locations → Tenants | `PricedCatalogFromCatalog`, `CatalogLocationsFromLocations`, `TenantDirectoryForLocations` |
| Orders ↔ Floor, Orders ↔ Payments | `OrderTableSessionsFromFloor` / `TableSessionOrdersFromOrders` ; `OrderPaymentsFromPayments` / `PaymentOrdersFromOrders` |
| Identity ↔ Staff | `TenantMembershipsFromStaff` / `UserAccountsFromIdentity` |

Dépendances entrantes (nombre de modules qui interrogent) : Tenants 19, Locations 10, Orders 7, Catalog 5,
Customers 5. S'y ajoute Staff, appelé à chaque requête protégée par `IPermissionChecker`.

Les cycles n'empêchent pas le monolithe de fonctionner (ce sont des lectures en mémoire), mais :

- ils interdisent d'extraire l'un des modules du cycle sans l'autre ;
- ils créent des chaînes de lecture en cascade dans une même requête.

**Recommandations (par ordre de rapport gain / effort)**

- **Orders ↔ Payments** : Orders tient le montant réglé dans une projection locale alimentée par `PaymentCaptured` et
  `RefundSucceeded`, au lieu de demander `GetOrderSettledAmountQuery`.
- **Orders ↔ Floor** : Floor tient « commandes ouvertes par session » à partir d'`OrderPlaced`, `OrderCompleted` et
  `OrderCancelled`.
- **Tenants → Catalog** : voir l'audit 4.
- **Identity ↔ Staff** : acceptable (le couple identité / appartenance est structurel) ; à garder dans le même
  déploiement si l'on extrait un jour.

## 6. API (Orange) — PARTIEL

**Conforme**

- **Erreurs** : problem details uniformes avec un `code` stable par erreur (`DomainExceptionHandler`) :

  | Exception | Statut |
  |---|---|
  | `UnauthenticatedException` | 401 |
  | `ForbiddenException` | 403 |
  | `NotFoundException` | 404 |
  | `ConcurrencyException` | 409 |
  | `DomainException` | 422 |
  | `TooManyRequestsException` | 429 |

- Une ressource d'un autre restaurant répond 404 : son existence n'est jamais révélée.
- Le tenant n'est jamais un paramètre de route : il vient du jeton (claim `tid`) ou de la clé d'API.
- Énumérations en texte (`JsonStringEnumConverter`), instants en UTC suffixés `Utc`, routes en kebab-case au pluriel,
  actions en sous-ressources (`POST /orders/{id}/place`).
- Permissions déclarées par route (`RequirePermission`) ; limite de débit sur l'authentification et par clé d'API.

**Écarts**

| Sujet | Constat | Recommandation |
|---|---|---|
| Versioning | Aucun (ni `/v1`, ni en-tête, ni `Asp.Versioning`) | Préfixe `/v1` porté par l'hôte sur tous les groupes (un seul endroit) avant la première intégration externe |
| Documentation | Pas d'OpenAPI | `AddOpenApi()` + `MapOpenApi()` (.NET 10), utile aussi aux clés d'API des partenaires |
| Pagination | Deux styles : `page`/`pageSize` (Catalog, Locations, Orders, Staff) et `skip`/`take` (Audit, Billing, Customers, Integrations, Loyalty, Promotions, Reporting, Reviews) ; `take` seul (Inventory, Notifications) | Un seul style (`page`/`pageSize` avec bornes partagées) et une enveloppe commune `{ items, page, pageSize, totalCount }` dans BuildingBlocks |
| Enveloppe | Les quatre modules « page » renvoient un DTO avec `TotalCount`, les autres un tableau nu | Même enveloppe partout |
| Unicité violée | 409 dans Integrations et Audit (`ConcurrencyException`), 422 dans Customers et Reviews (`DomainException`) | Une règle : 409 pour « existe déjà » (conflit d'état), 422 pour une règle métier |
| Concurrence entre requêtes | `xmin` protège la transaction, pas le cycle « lire puis modifier » du client : aucun `ETag` / `If-Match` | Exposer la version en `ETag` sur les GET et l'exiger en `If-Match` sur les PUT des agrégats édités à plusieurs (menus, questionnaires, politiques) |
| Préfixes d'administration | `/admin/audit`, `/admin/billing`, `/admin/reporting`, mais `/platform/payment-providers`, `/notification-providers` et `/tenants` (réservé à la plateforme) | Un seul préfixe `/admin/...` pour toute route de la plateforme |

## 7. Communication interne (Orange) — BON

**Synchrone (lectures, en mémoire, via les ports)** : 53 ponts. Ce sont des lectures, sauf deux écritures :

| Pont | Écriture synchrone dans un autre module |
|---|---|
| `UserAccountsFromIdentity` (Staff → Identity) | `InviteUserCommand` : crée le compte et envoie l'invitation |
| `ReportingExportFilesFromFiles` (Reporting → Files) | `StoreGeneratedFileCommand`, `DeleteOwnedFileCommand` |

**Asynchrone (outbox, au moins une fois)** : 15 consommateurs ; toutes les réactions métier passent par là
(cuisine, stock, fidélité, promotions, notifications, webhooks, reporting, audit, anonymisation, facturation).

Le partage est sain : on lit en synchrone ce dont on a besoin pour décider, on réagit en asynchrone à ce qui s'est
passé.

**Risques**

- **Aucun cache** (ni `IMemoryCache`, ni `HybridCache`) : une écriture courante fait 2 lectures dans d'autres modules
  (permission dans Staff, « le tenant accepte les écritures » dans Tenants), chacune avec sa connexion à la base.
  L'appartenance au personnel n'est vérifiée qu'à la sélection du restaurant et au rafraîchissement du jeton.
- **Couplage de disponibilité** : un module lent ralentit tous ceux qui l'interrogent (Tenants et Staff en premier).

**Recommandations**

- `HybridCache` (.NET 9+) derrière `IPermissionChecker` et les ports `IXxxTenants`, invalidé par les événements de
  Staff et de Tenants (ou par un délai court).
- Les deux écritures synchrones : voir l'audit 8.

## 8. Transactions (Rouge) — À CORRIGER

**Conforme**

- Une transaction = un module : chaque `UnitOfWork` n'écrit que dans son schéma et y ajoute ses messages d'outbox dans
  la même transaction. Il n'y a jamais d'écriture dans deux schémas au sein d'une même transaction.
- Les décisions sous contrainte sont prises SOUS VERROU dans la transaction qui écrit : capacité de réservation et
  reste à payer d'une commande (`pg_advisory_xact_lock`).
- Les gardes ultimes sont en base : index uniques, CHECK, `xmin`.

**Défauts**

1. **Doubles écritures sans compensation**
   - **Invitation d'un membre du personnel** (`InviteStaffHandler`) : le compte Identity est créé et l'email part,
     PUIS le membre Staff est enregistré. Si la seconde écriture échoue, le compte reste invité sans appartenance. Le
     rejeu est sûr (`FindOrInvite` retrouve le compte), mais l'email a déjà été envoyé.
   - **Export de Reporting** : le fichier est stocké dans Files PUIS l'export est marqué terminé. En cas d'échec, le
     fichier est orphelin. À l'expiration, l'ordre est inverse : le fichier est supprimé PUIS l'export est marqué
     expiré. En cas d'échec, l'export pointe vers un fichier qui n'existe plus.
2. **Appel HTTP au prestataire dans une transaction verrouillée** : `StartOnlinePaymentHandler` prend le verrou de la
   commande (ce qui ouvre la transaction) puis appelle le prestataire AVANT d'enregistrer. Une connexion et un verrou
   sont tenus pendant toute la latence du prestataire : les autres caisses de la même commande attendent, et la
   transaction peut expirer.
3. **Le dispatcher tient sa transaction pendant toute la distribution** : `FOR UPDATE SKIP LOCKED` sur 100 messages,
   puis tous les consommateurs (chacun avec ses propres transactions dans d'autres schémas) sont exécutés avant le
   commit. Il en résulte des transactions longues et une connexion bloquée par module.
4. **Refus métier abandonnés en silence** : un consommateur qui reçoit une `DomainException` la journalise puis
   l'ignore. Si le refus est temporaire, l'effet est PERDU sans trace exploitable. Exemple : restaurant suspendu au
   moment de `OrderCompleted`, donc points de fidélité jamais crédités.

**Recommandations**

- Invitation : enregistrer d'abord le membre (statut Invited, sans compte), puis créer le compte et envoyer l'email
  en réaction à `StaffInvited` par l'outbox.
- Export : stocker le fichier sous un id déterministe (id de l'export) pour qu'un rejeu le remplace ; à l'expiration,
  marquer d'abord l'export, puis supprimer le fichier en réaction à `ExportExpired`.
- Paiement en ligne : en trois temps.
  1. Sous verrou, enregistrer le paiement en état « intention en cours » et réserver le montant, puis relâcher.
  2. Appeler le prestataire hors transaction.
  3. Enregistrer la référence ; en cas d'échec, annuler le paiement.
- Dispatcher : lire et réserver le lot (colonne `locked_until`), committer, distribuer hors transaction, puis marquer
  chaque message traité.
- Refus métier : écrire le message refusé dans une file morte consultable et rejouable (voir l'audit 9) au lieu de
  seulement le journaliser.

## 9. Idempotence (Rouge) — À CORRIGER

**Ce qui est idempotent**

| Point | Mécanisme |
|---|---|
| Webhooks des prestataires de paiement | Index unique `(configuration_id, event_id)` |
| Notifications déclenchées par événement | Inbox transactionnelle `(consommateur, id d'événement)` + clé d'idempotence par envoi |
| Audit | Origine unique `outbox.{module}.{id}` |
| Reporting | « source déjà comptée » |
| Webhooks sortants | Une livraison vivante par (endpoint, événement) |
| Cuisine, stock | Un ticket par commande ; une consommation de stock par commande |
| Fidélité, promotions | `AlreadyDone` sur gain, reprise, libération |
| Remise externe sur une commande | `DiscountAlreadyApplied` (refus du domaine, ignoré par le consommateur) |
| Anonymisations | Naturellement rejouables |
| Encaissement | Verrou de commande + reste à payer : on ne peut pas encaisser plus que dû |

**Défauts**

1. **Message empoisonné = outbox du module BLOQUÉE** : une exception non métier (par exemple `KeyNotFoundException`
   sur `GetProperty("TenantId")` si un événement change de forme) remonte. Le message reste en tête et est retenté
   toutes les 2 s SANS LIMITE. Tous les messages suivants du module sont bloqués, pour TOUS les consommateurs. Il n'y
   a ni compteur de tentatives, ni file morte, ni alerte.
2. **Distribution « tout ou rien »** : chaque message est passé à tous les consommateurs (15) à la suite. Si le 10ᵉ
   échoue, les 9 premiers seront rejoués au passage suivant. Tous les consommateurs actuels le supportent, mais c'est
   une règle implicite, non testée et non documentée : un futur consommateur non idempotent dupliquera ses effets.
3. **Ordre perdu avec plusieurs instances** : avec `SKIP LOCKED`, l'instance B prend les messages 101 à 200 pendant
   que l'instance A traite 1 à 100. Un `OrderCancelled` peut alors être traité avant l'`OrderPlaced` de la même
   commande (ticket cuisine créé après l'annulation).
4. **Pas d'`Idempotency-Key` côté API** : un double clic ou un rejeu réseau sur `POST` crée deux ressources.
   - Remboursement partiel (`RequestRefundCommand`) : deux remboursements tant que le remboursable le permet.
   - Encaissement hors ligne partiel : deux paiements dans la limite du reste dû.
   - Commandes, réservations, avis, clients : doublons.

   Seul Notifications accepte une clé d'idempotence.
5. **Une partie de l'idempotence repose sur des refus du domaine** (transition déjà faite, remise déjà appliquée) :
   c'est correct, mais cela se confond dans les journaux avec de vrais refus.

**Recommandations**

- **Dispatcher** :
  - colonnes `attempt_count`, `last_error_code` et `dead_lettered_at_utc` sur `outbox_messages` ;
  - au-delà de N tentatives (par exemple 10, avec temporisation croissante), le message passe en file morte et la
    distribution continue ;
  - route d'administration pour lister et rejouer ;
  - métrique et alerte sur l'âge du plus vieux message en attente.
- **Une livraison par consommateur** : table `outbox_deliveries(message_id, consumer, processed_at_utc)` ou inbox
  générique dans BuildingBlocks (celle de Notifications, généralisée). Un échec ne rejoue plus que le consommateur
  fautif.
- **Ordre** : distribuer par clé d'ordre (agrégat ou tenant) avec un verrou consultatif par clé, ou une seule instance
  active par module (élection de leader via `pg_try_advisory_lock`) tant que le débit le permet.
- **API** : middleware `Idempotency-Key` dans BuildingBlocks.Api pour les POST de création.
  - Table `(tenant, clé, empreinte de la requête, statut, réponse)`, conservée 24 h.
  - Même clé avec le même corps : la première réponse est rejouée. Même clé avec un autre corps : 422.
  - Obligatoire sur `/payments` et les remboursements, facultatif ailleurs.
- **Journaux** : distinguer « déjà fait » (`Debug`) de « refusé » (`Warning`) dans les consommateurs.

## 10. Évolutivité (Jaune) — PARTIEL

**Prêt pour une extraction**

- Schéma propre par module, migrations propres, aucune jointure entre schémas, aucune référence de projet entre
  modules.
- Toutes les dépendances passent par des ports (`IXxxTenants`, `IXxxOrders`...), donc remplaçables par un client
  HTTP ou gRPC sans toucher l'Application.
- Réactions déjà asynchrones par outbox : le passage à un broker (Kafka) ne change que le transport.

**Freins**

| Frein | Coût d'extraction | Levier |
|---|---|---|
| Contrats implicites (JSON des domain events, lus à la main par l'hôte) | Élevé : aucune garantie de compatibilité entre deux déploiements | Contrats `X.Contracts` versionnés (audit 3) |
| 53 ponts synchrones en mémoire | Moyen : chaque pont devient un appel réseau, donc latence et pannes partielles | Projections locales pour Tenants et Staff, cache, suppression des cycles (audit 5) |
| Dispatcher en mémoire, livraison tout ou rien, ordre fragile | Moyen | Livraison par consommateur et inbox générique (audit 9) avant tout broker |
| Une seule chaîne de connexion et un seul rôle | Faible | Une chaîne par module (même base au départ), un rôle par schéma |
| Composition dans un seul `Program.cs` | Faible | Une extension de composition par module (audit 1) |

**Candidats naturels à l'extraction** (aucun module ne les interroge, ils ne font que consommer) : Reporting, Audit,
Integrations (webhooks), Notifications. Les modules du cœur (Tenants, Locations, Orders, Catalog) doivent rester
ensemble tant que les cycles de l'audit 5 existent.

---

## Plan d'action

| Ordre | Action | Audits | Effort |
|---|---|---|---|
| 1 | Dispatcher : tentatives bornées, file morte, alerte, rejeu d'administration | 8, 9 | 1 à 2 j |
| 2 | Livraison par consommateur (inbox générique dans BuildingBlocks) | 9, 10 | 2 j |
| 3 | Middleware `Idempotency-Key` (obligatoire sur paiements et remboursements) | 6, 9 | 1 à 2 j |
| 4 | Paiement en ligne en trois temps (appel au prestataire hors verrou) | 8 | 1 j |
| 5 | Invitation Staff et export Reporting réordonnés via l'outbox | 8 | 1 j |
| 6 | Cache des tenants et des permissions (`HybridCache`), invalidé par événements | 4, 7 | 1 j |
| 7 | Ordre de distribution (instance unique par module ou verrou par clé) | 9 | 0,5 à 1 j |
| 8 | Contrats d'intégration versionnés (`X.Contracts`) pour les ~150 événements consommés | 3, 10 | 3 à 5 j (progressif) |
| 9 | API : `/v1`, OpenAPI, pagination et enveloppe uniques, 409/422 harmonisés, préfixe `/admin` | 6 | 2 j |
| 10 | Suppression des cycles Orders ↔ Payments, Orders ↔ Floor, Tenants → Catalog (projections) | 4, 5 | 2 à 3 j |
| 11 | Tests d'architecture (couches, modules, schémas) et découpage de `Program.cs` | 1, 2 | 1 j |
| 12 | Un rôle PostgreSQL par schéma en production | 1, 10 | 0,5 j |

Les actions 1 à 5 corrigent des risques de PERTE ou de DOUBLON (argent, messages). Les actions 6 à 12 préparent la
montée en charge et l'extraction.

## Corrections apportées

État au 10/10/2026, après le lot de corrections. Rien n'a été compilé ni exécuté dans l'environnement de correction :
`dotnet build` puis `dotnet test` sont à lancer avant toute mise en production.

| Action | Fait | Où |
|---|---|---|
| 1 | 10 tentatives au plus, délai exponentiel (5 s à 15 min), file morte, health check « outbox » (Degraded), routes `/v1/admin/outbox` (état, file morte, rejeu) | `OutboxDispatcher`, `OutboxMonitor`, `OutboxAdministration` |
| 2 | Journal `outbox_deliveries` par (message, consommateur) : un consommateur déjà livré n'est jamais rejoué ; un refus métier part directement en file morte | `OutboxDelivery`, `OutboxDispatcher` |
| 3 | Middleware `Idempotency-Key` (POST, PATCH), stockage PostgreSQL (schéma `idempotency`, purge horaire), obligatoire sur `/payments/offline`, `/online`, `/{id}/retry`, `/{id}/refunds` | `BuildingBlocks.Api/Idempotency.cs`, `PostgresIdempotencyStore` |
| 4 | Paiement en ligne : création et commit (verrou libéré), appel au prestataire, enregistrement de la tentative ; prestataire indisponible = paiement annulé | `StartOnlinePaymentHandler` |
| 5 | Invitation : compte créé sans email, email envoyé par `staff.StaffInvited` ; export : le fichier est supprimé par `reporting.ExportExpired` ; stockage d'un export idempotent | `StaffInvitationFlow`, `ExportFileCleanup` |
| 6 | Cache mémoire des tenants et des permissions (30 s), invalidé par les événements Tenants et Staff | `Host/Caching.cs` |
| 7 | Une seule instance distribue l'outbox d'un module (verrou consultatif PostgreSQL de session) | `OutboxSql.TryLockAsync` |
| 8 | Projet `SaasRestaurant.Contracts` : un contrat typé et versionné par événement consommé, lecture stricte (`[EventVersion]`, version plus récente refusée), tests de compatibilité avec les domain events | `src/Contracts`, `Tests/Contracts` |
| 9 | Routes sous `/v1` ; OpenAPI (`/openapi/v1.json`, en développement ou avec `Api:ExposeOpenApi`) ; `page`/`pageSize` (100 au plus) et enveloppe `{ items, page, pageSize, totalCount, hasMore }` partout ; conflit avec l'état existant (unicité) = 409 via `DomainErrorKind.Conflict` ; administration sous `/admin` | `PagedResult`, `PageRequest`, `DomainExceptionHandler` |
| 10 | Projections locales alimentées par l'outbox : `orders.order_payment_settlements` (`payments.PaymentSettlementChanged`), `floor.session_orders` (`orders.OrderCreated`, `OrderCompleted`, `OrderCancelled`), `tenants.priced_catalogs` (`catalog.CatalogPriced`) ; plus aucun appel synchrone dans ces trois sens | `OrderSettlementFlow`, `SessionOrderFlow`, `PricedCatalogFlow` |
| 11 | Tests d'architecture étendus (schéma propre et unique par module, outbox par module, contrats isolés) ; `Program.cs` découpé en une extension de composition par module | `Tests/Architecture`, `Host/Composition` |
| 12 | Script des rôles `saas_<schéma>` (propriétaires de leur seul schéma) ; chaîne de connexion dédiée facultative par module (`ConnectionStrings:SaasRestaurant.<Module>`) | `docs/sql/roles-par-schema.sql` |

Correction du constat de l'audit 2 : des tests d'architecture existaient déjà (`LayerDependencyTests`,
`DomainModelTests`) ; l'action 11 les a complétés.

Non traité : ETag / `If-Match` sur les ressources modifiables.

### À faire avant la mise en production

1. Générer une migration dans chaque module qui a une outbox (colonnes de `outbox_messages`, table `outbox_deliveries`,
   et les tables des projections dans Orders, Floor et Tenants) : `scripts/migrations-corrections.sh`.
2. Exécuter `docs/sql/backfill-projections.sql` une fois, après les migrations, pour remplir les projections à partir
   des données existantes.
3. Exécuter `docs/sql/roles-par-schema.sql` avec un compte administrateur, donner un mot de passe à chaque rôle, puis
   renseigner les chaînes `ConnectionStrings:SaasRestaurant.<Module>`.
4. Prévenir les clients de l'API : préfixe `/v1`, enveloppe de pagination, 409 au lieu de 422 pour les doublons,
   `Idempotency-Key` obligatoire sur les paiements.
