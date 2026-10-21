# Catalogue des modules — bases métier

Une fiche par module : ce qu'il possède, ses agrégats, ses règles structurantes, ce qu'il publie et ce dont il dépend.
Le détail classe par classe (invariants, transitions, codes d'erreur, choix faits là où la spec était ouverte) est dans le
`README.md` de chaque module ; cette page sert à voir l'ensemble et les frontières.

Légende de l'état : **Complet** = Domain + Application + Infrastructure + Api + migrations + tests ;
**Domain** = Domain et ses tests faits, couches Application/Infrastructure/Api créées et câblées dans le Host, sans cas
d'usage encore.

| # | Module | Schéma | Préfixe d'erreur | État | Tests Domain |
|---|---|---|---|---|---|
| 1 | Identity & Access | `identity` | `identity.` | Complet | 238 (+ App/Infra/Api) |
| 2 | Tenants | `tenants` | `tenants.` | Complet | 101 (+ App/Infra/Api) |
| 3 | Locations | `locations` | `locations.` | Complet | 125 (+ App/Infra/Api) |
| 4 | Staff & RBAC | `staff` | `staff.` | Complet | 95 (+ App/Infra/Api) |
| 5 | Catalog / Menu | `catalog` | `catalog.` | Complet | 150 (+ App/Infra/Api) |
| 6 | Floor (salles & tables) | `floor` | `floor.` | Complet | 60 (+ App/Infra/Api) |
| 7 | Reservations | `reservations` | `reservations.` | Complet | 141 (+ App/Infra/Api) |
| 8 | Orders | `orders` | `orders.` | Complet | 200 (+ App/Infra/Api) |
| 9 | Kitchen / KDS | `kitchen` | `kitchen.` | Complet | 101 (+ App/Infra/Api) |
| 10 | Payments | `payments` | `payments.` | Complet (migration à régénérer) | 156 (+ App/Infra/Api) |
| 11 | Inventory | `inventory` | `inventory.` | Complet (migration à générer) | 163 (+ App/Infra/Api) |
| 12 | Customers / CRM | `customers` | `customers.` | Complet (migration à générer) | 92 (+ App/Infra/Api) |
| 13 | Notifications | `notifications` | `notifications.` | Complet | 131+ (+ App/Infra/Api) |
| 14 | Promotions | `promotions` | `promotions.` | Complet | 139 (+ App/Infra/Api) |
| 15 | Loyalty | `loyalty` | `loyalty.` | Complet (migration `LoyaltyPrograms` à générer) | 122 (+ App/Infra/Api) |
| 16 | Billing SaaS | `billing` | `billing.` | Complet (migrations `InitialCreate` et `OnlinePaymentAndInvoices` à générer) | 113 (+ App/Infra/Api) |
| 17 | Reporting / Analytics | `reporting` | `reporting.` | Complet (migration `InitialCreate` à générer) | 94 (+ App/Infra/Api) |
| 18 | Files / Media | `files` | `files.` | Complet (migration `InitialCreate` à générer) | 58 (+ App/Infra/Api) |
| 19 | Integrations / Webhooks | `integrations` | `integrations.` | Complet (migration `InitialCreate` à générer) | 128 (+ App/Infra/Api) |
| 20 | Reviews / Feedback | `reviews` | `reviews.` | Complet (migration `InitialCreate` à générer) | 77 (+ App/Infra/Api) |
| 21 | Audit | `audit` | `audit.` | Complet (migration `InitialCreate` à générer) | 75 (+ App/Infra/Api) |
| 22 | Fiscal (e-MECeF) | `fiscal` | `fiscal.` | Complet (migration `InitialCreate` à générer) | 12 (+ App/Infra/Api) |

L'ordre est celui du prompt maître (§21) : un module ne passe à « Complet » qu'après ceux dont il dépend.

---

## 1. Identity & Access — `src/Modules/Identity`

- **Possède** : comptes humains, credentials, sessions (refresh tokens), vérification d'email, invitations, verrouillage.
- **Ne possède pas** : les rôles et permissions métier du restaurant (→ Staff). Identity ne connaît aucun restaurant.
- **Agrégats** : `User` (racine, plateforme) ⊃ `RefreshSession`.
- **Règles structurantes** : email unique (port `IUserEmailUniquenessChecker`) ; un compte `Disabled` n'ouvre pas de
  session ; un refresh token révoqué ne sert plus, son rejeu révoque la famille (`RefreshTokenReplayDetected`) ; on ne
  stocke que le hash du token.
- **Publie** : `UserRegistered`, `UserInvited`, `UserInvitationAccepted`, `EmailVerified`, `PasswordChanged`,
  `UserLocked`, `UserDisabled`, `UserErased`, `SessionStarted`, `SessionRevoked`, `RefreshTokenRotated`…
- **Restaurant sélectionné** : `POST /me/tenant` écrit le tenant dans le jeton d'accès (claim `tid`) après contrôle
  d'appartenance (port `ITenantMemberships`, revérifié à chaque refresh). C'est la seule source du tenant d'une
  requête (`ITenantContext`).
- **Consommé par** : Staff (invitation acceptée → membre activé), Audit (connexions, verrouillages).

## 2. Tenants / Organizations — `src/Modules/Tenants`

- **Possède** : le client SaaS (organisation), son cycle de vie et ses réglages globaux : devise, fuseau, mode de prix
  TTC/HT, branding.
- **Agrégats** : `Tenant` (racine de sécurité logique ; les autres modules ne sont pas ses enfants, ils portent son id).
- **Règles structurantes** : `Closed` est terminal ; un tenant suspendu reste lisible mais les écritures métier sont
  restreintes ; changer de devise ou de mode de prix est refusé dès qu'un prix existe au catalogue (port
  `ITenantPricedCatalogChecker`, servi par le Host depuis Catalog : au moins un produit ou groupe d'options).
- **Publie** : `TenantCreated`, `TenantActivated`, `TenantSuspended`, `TenantReactivated`, `TenantClosed`, changements
  de réglages.
- **Consommé par** : Billing (essai → abonnement), tous les modules (suspension = écriture refusée).

## 3. Locations — `src/Modules/Locations`

- **Possède** : les établissements d'un tenant : coordonnées, horaires, jours exceptionnels, capacité, fuseau local.
- **Agrégats** : `RestaurantLocation` ⊃ `ExceptionalSchedule` ; VO `Address`, `BusinessHours`.
- **Règles structurantes** : créneaux horaires cohérents et sans chevauchement ; un établissement archivé est terminal ;
  « opérationnel » (actif, non fermé temporairement) est la condition demandée par Orders et Reservations.
- **Publie** : `RestaurantLocationCreated`, `LocationActivated`, `LocationTemporarilyClosed`, `LocationReopened`,
  `LocationArchived`, `BusinessHoursChanged`, `ExceptionalScheduleAdded/Removed`…
- **Dépend de** : Billing (quota du nombre d'établissements, vérifié par l'Application).
- **Consommé par** : Reservations (horaires/capacité), Orders (`LocationReference`), Floor, Staff, Kitchen.

## 4. Staff & RBAC — `src/Modules/Staff`

- **Possède** : les employés d'un tenant, leurs affectations par établissement, les rôles et permissions granulaires.
- **Agrégats** : `StaffMember`, `Role` ; VO `PermissionCode` (catalogue fermé `PermissionCatalog`).
- **Règles structurantes** : un membre référence un `UserId` d'Identity, jamais l'inverse ; le dernier propriétaire
  actif du tenant ne peut être ni retiré ni suspendu ; nom de rôle unique par tenant ; permissions uniquement issues du catalogue.
- **Publie** : `StaffInvited`, `StaffActivated`, `StaffAssignedToLocation`, `StaffSuspended`, `StaffLeft`,
  `RolePermissionGranted/Revoked`…
- **Consommé par** : l'autorisation de toutes les routes métier (permissions effectives, invalidation de cache), Audit.
- **Lot P1** : caisses partagées (PIN par membre, pointage arrivée et départ, export des heures) ; renvoi d'une
  invitation (`StaffInvitationResent`, lien renvoyé par Identity) ; suppression d'un rôle personnalisé que plus aucun
  membre ne détient (`RoleDeleted`).
- **Planning de l'équipe** : `ScheduleWeek` (brouillon puis publication, créneaux avec poste et pause), `ScheduleSettings`,
  `TimeOffRequest` (absences à valider), `ShiftSwapRequest` (remplacement accepté par le collègue puis validé par le
  gérant) ; écart prévu / pointé. Permission `staff.schedule`. Publie `ScheduleWeekPublished`, `TimeOffRequested/Decided`,
  `ShiftSwapRequested/Accepted/Decided` (l'hôte prévient l'équipe via Notifications).

## 5. Catalog / Menu — `src/Modules/Catalog`

- **Possède** : produits, variantes, groupes d'options, menus publiés, catégories de taxe, prix par établissement,
  allergènes, disponibilité.
- **Agrégats** : `Product` ⊃ `ProductVariant` ; `ModifierGroup` ⊃ `Modifier` ; `Menu` ⊃ `MenuSection` ; `TaxCategory`.
- **Règles structurantes** : `Money` dans la devise du tenant ; un produit archivé n'est plus vendable ; un menu publié
  ne référence que des produits actifs ; un taux de taxe modifié ne change pas les commandes passées (Orders
  garde un snapshot) ; un produit est vendu dans tous les établissements ou dans une liste choisie, avec rupture
  possible par établissement (caisse, borne, site et cartes n'affichent que les produits vendus sur place).
- **Publie** : `ProductCreated`, `ProductPriceChanged`, `ProductUnavailable`, `ProductArchived`, `MenuPublished`,
  `TaxCategoryRateChanged`…
- **Consommé par** : Orders (construction des `ProductSnapshot`), Inventory (recettes), Tenants (prix existants),
  Kitchen (routage par catégorie).
- **État** : complet de bout en bout. Routes `/catalog/...` (tenant-member ; écritures : `catalog.manage`). Prix saisis
  en nombres, devise imposée (celle du tenant à la création). Pas encore exposés : allergènes (pas de référentiel),
  prix par établissement (non modélisé), réactions aux événements, snapshot de ligne pour Orders (viendra avec
  Orders). Voir son README §6-8.

## 6. Floor — salles & tables — `src/Modules/Floor`

- **Possède** : salles, tables (capacité, QR code, hors service) et sessions de table (une occupation réelle).
- **Agrégats** : `DiningArea` ⊃ `RestaurantTable` ; `TableSession`.
- **Règles structurantes** : une table n'a qu'une session ouverte à la fois (port `ITableOccupancyChecker`) ; on ne
  ferme pas une session dont des commandes sont ouvertes (port `ITableSessionOrdersChecker`) ; une table hors service
  n'est pas attribuable.
- **Publie** : `TableSessionOpened`, `TableSessionClosed`, `TableSetOutOfService`…
- **Consommé par** : Orders (`TableSessionReference`), Reservations (installation `GuestSeated`).
- **Lot P1** : déplacement et fusion de sessions de table ; QR de table signé (jeton, sans nom de table) servi par
  Storefront pour la commande à table.
- **État** : complet de bout en bout. Routes `/floor/...` (tenant-member ; plan : `floor.manage`, service :
  `floor.serve`). Le plan de salle renvoie la session en cours de chaque table. Une table ne se libère pas tant
  qu'une commande de sa session est ouverte (réponse d'Orders). Voir son README §6-8.

## 7. Reservations — `src/Modules/Reservations`

- **Possède** : les réservations et la politique de réservation d'un établissement.
- **Agrégats** : `Reservation` ; VO `ReservationPolicy` ; DomainService `ReservationAvailabilityPolicy`.
- **Règles structurantes** : statuts `Pending → Confirmed → Seated → Completed`, sorties `Cancelled` / `NoShow` ;
  disponibilité évaluée sur horaires + fermetures + capacité ; **le surbooking est empêché par l'Infrastructure**
  (transaction + verrou sur le créneau, à tester en concurrence sur le dernier créneau) ; historique des transitions.
- **Publie** : `ReservationCreated`, `ReservationConfirmed`, `ReservationRescheduled`, `GuestSeated`,
  `ReservationCancelled`, `ReservationNoShow`…
- **Dépend de** : Locations (horaires, capacité), Floor (tables/zones), Customers (client optionnel).
- **Consommé par** : Notifications (confirmation, rappel), Reporting, Customers (historique).
- **État** : complet de bout en bout pour le personnel (routes `/reservations/...`, `reservations.manage` et
  `reservations.configure`). Capacité sous verrou consultatif PostgreSQL, walk-ins de Floor décomptés, horaires vérifiés
  auprès de Locations, politique par établissement, fiche Customers facultative (`customerId`), contact effacé à
  l'anonymisation du client. Réservation par le client lui-même (lot P1) : activable par établissement
  (`AcceptsOnlineBookings`), servie par Storefront (heures proposées, réservation confirmée tout de suite, consultation
  et annulation par lien signé). Voir son README §6-8.

## 8. Orders — `src/Modules/Orders` (agrégat central)

- **Possède** : la commande sur place / à emporter / livraison, ses lignes, remises, totaux et transitions.
- **Agrégats** : `Order` ⊃ `OrderItem` ; VO `ProductSnapshot`, `TaxSnapshot`, `OrderItemModifier`, `OrderDiscount`,
  `OrderSettlement`, `OrderNumber`, `Quantity`.
- **Règles structurantes** : `Draft → Placed → Accepted → Preparing → Ready → Completed`, annulation possible avant
  `Completed` ; lignes modifiables en `Draft` uniquement ; totaux déterministes (TTC ou HT, taxe arrondie par ligne,
  remise répartie au plus fort reste) ; snapshot tarifaire figé ; clôture seulement si soldée ; création idempotente
  (`Idempotency-Key`, côté Application).
- **Publie** : `OrderCreated`, `OrderPlaced`, `OrderAccepted`, `OrderPreparationStarted`, `OrderReady`,
  `OrderCompleted`, `OrderCancelled`.
- **Dépend de** : Catalog (snapshots), Locations, Floor, Customers (références locales), Promotions (remises
  calculées), Payments (`OrderSettlement`).
- **Consommé par** : Kitchen, Payments, Inventory, Loyalty, Promotions (usage), Reporting, Integrations.
- **État** : complet de bout en bout pour le personnel (routes `/orders/...`, `orders.take`, `orders.process`,
  `orders.discount`). Lignes figées depuis le Catalog (`GetSellableItemQuery`), numéros du jour par établissement,
  remises manuelles. La clôture lit le montant réglé auprès de Payments. Floor interroge
  désormais Orders avant de libérer une table. Voir son README §7-9.

## 9. Kitchen / KDS — `src/Modules/Kitchen`

- **Possède** : tickets cuisine, postes (stations), routage des lignes vers les postes, priorités, temps de préparation.
- **Agrégats** : `KitchenTicket` ⊃ `KitchenTicketItem` ; `KitchenStation` ; `StationRouting` ;
  policy `TicketReadinessPolicy`.
- **Règles structurantes** : un ticket naît d'une commande acceptée (snapshot `KitchenOrderSnapshot`, aucune lecture
  d'Orders ensuite) ; il est prêt quand toutes ses lignes le sont ; un poste désactivé ne reçoit plus de lignes.
- **Publie** : `KitchenTicketCreated`, `KitchenPreparationStarted`, `KitchenTicketItemReady`, `KitchenTicketReady`,
  `KitchenTicketCompleted`, `KitchenTicketCancelled`…
- **Consomme** : `OrderAccepted`, `OrderCancelled`. **Consommé par** : Orders (`Preparing`, `Ready`), Reporting (temps
  de prép.).
- **État** : complet de bout en bout (routes `/kitchen/...`, `kitchen.configure`, `kitchen.operate`). L'hôte relie
  Orders et Kitchen par l'outbox (`KitchenOrderFlow`) : commande acceptée -> ticket, commande annulée -> ticket annulé,
  ticket démarré / prêt -> commande en préparation / prête. Kitchen lit les lignes par `GetOrderLinesQuery` (sans
  prix). Voir son README §6-8.

## 10. Payments — `src/Modules/Payments`

- **Possède** : le paiement des clients du restaurant (pas la facturation SaaS), ses tentatives, remboursements et
  webhooks prestataires.
- **Agrégats** : `Payment` ⊃ `PaymentAttempt`, `Refund`.
- **Règles structurantes** : états explicites ; le retour navigateur n'est jamais une preuve, le webhook signé l'est ;
  webhooks idempotents (payload brut conservé, signature + horodatage vérifiés) ; remboursé ≤ capturé ;
  `PaymentCapturedAfterCancellation` signale une capture tardive à rembourser ; abstraction prestataire (Stripe, moyens
  locaux) en Infrastructure.
- **Publie** : `PaymentCreated`, `PaymentAuthorized`, `PaymentCaptured`, `PaymentFailed`, `PaymentCancelled`,
  `RefundRequested`, `RefundSucceeded`…
- **Consommé par** : Orders (règlement), Loyalty, Reporting, Audit, Integrations.
- **Deux niveaux de configuration** : la plateforme définit ce qui est supporté (`PaymentProvider` : catalogue,
  nature comptoir/en ligne, devises, proposé ou retiré) ; chaque restaurant ce qu'il accepte réellement
  (`RestaurantPaymentConfiguration` : mode compte du restaurant / compte de la plateforme, moyens retenus, affectations
  aux établissements). Les identifiants marchands vont au coffre (`IPaymentSecretStore`, table chiffrée à part) ; les
  agrégats n'en gardent que la référence.
- **État** : complet de bout en bout (routes `/payments/...`, `/platform/payment-providers`, webhooks
  `/payments/webhooks/{provider}/{configurationId}` ; permissions `payments.collect`, `payments.refund`, `payments.configure`).
  Comptoir et en ligne ; intégration `sandbox` pour le développement, aucun prestataire réel branché. Addition partagée
  sans dépassement, par montant ou par article. Orders lit le montant réglé par `GetOrderSettledAmountQuery`. Encaissements
  de la plateforme pour Billing (`StartPlatformCollectionCommand`, `GetPlatformCollectionStatusQuery`) sur le compte
  marchand de la plateforme, état relu chez le prestataire (`IPaymentGateway.FetchStatusAsync` : FedaPay, CinetPay,
  MTN MoMo, sandbox). Voir son README §7-10 et `docs/PAYMENTS-CONFORMITE.md` (cahier des charges en 24 étapes : étapes 1-2 faites).

## 11. Inventory — `src/Modules/Inventory`

- **Possède** : ingrédients, articles de stock par établissement, recettes, mouvements, inventaires physiques, seuils.
- **Agrégats** : `InventoryItem` ⊃ `StockMovement` (ledger) ; `Ingredient` ; `Recipe`.
- **Règles structurantes** : le stock est un **ledger de mouvements** (entrée, vente, correction, perte, transfert),
  jamais une valeur écrasée ; quantités en unité normalisée ; ajustement d'inventaire audité ; seuil bas → événement.
- **Publie** : `StockReceived`, `StockConsumed`, `StockWentNegative`, `WasteRecorded`, `StockAdjusted`,
  `StockCountConfirmed`, `StockTransferredOut/In`, `LowStockReached`, `RecipeChanged`.
- **Consomme** : `OrderPreparationStarted` (consommation via recette, idempotente par commande). **Consommé par** :
  Notifications (alerte seuil), Reporting.
- **État** : complet de bout en bout (routes `/inventory/...`, `inventory.manage`, `inventory.operate`). L'hôte relie
  Orders et Inventory par l'outbox (`InventoryOrderFlow`) : préparation commencée -> stock consommé d'après les recettes
  (variante d'abord), une fois par commande. Lot P1 : coût unitaire (coût moyen pondéré), coût des recettes et marge,
  sortie manuelle, fournisseurs, bons de commande et suggestions de réapprovisionnement. Voir son README §6-8.

## 12. Customers / CRM — `src/Modules/Customers`

- **Possède** : clients d'un tenant, préférences, tags, notes, consentements (RGPD).
- **Agrégats** : `Customer` ; `Consent` (un par finalité et canal, port `IConsentUniquenessChecker`).
- **Règles structurantes** : données minimales ; anonymisation irréversible (`CustomerAnonymized`) qui coupe les liens
  personnels ailleurs (Reservations, Reviews) ; aucun envoi marketing sans consentement actif.
- **Publie** : `CustomerCreated`, `CustomerContactChanged`, `CustomerAnonymized`, `ConsentGranted`, `ConsentWithdrawn`.
- **Consommé par** : Orders, Reservations, Loyalty, Notifications (consentements), Reviews.
- **État** : complet de bout en bout pour le personnel (routes `/customers/...`, `customers.view`, `customers.manage`,
  `customers.anonymize`, `customers.configure`). Règle de contact par restaurant, recherche exacte par email ou
  téléphone (pas d'unicité), consentements avec historique, autorisation d'envoi marketing. L'anonymisation retire les
  consentements dans la même transaction et efface le contact des réservations du client (outbox). Commandes et
  réservations rattachables à une fiche. Lot P1 : notes, fusion de doublons (`CustomerMerged`, commandes, avis,
  réservations et fidélité reportés par l'hôte), export des données d'un client, import CSV, audience marketing des
  campagnes. Voir son README §6-8.

## 13. Notifications — `src/Modules/Notifications`

- **Possède** : envois email/SMS/WhatsApp/push via fournisseurs abstraits, templates versionnés, tentatives, journal.
- **Agrégats** : `Notification` ⊃ `NotificationAttempt` ; `NotificationTemplate`. `TenantId` nullable : `null` =
  niveau plateforme (vérification de compte…).
- **Règles structurantes** : retry borné puis abandon ; template publié immuable (nouvelle version sinon) ; contenu
  effaçable (`Redact`) après envoi ; aucune donnée sensible dans le journal.
- **Consomme** : événements de Reservations, Orders, Inventory, Identity. **Publie** : `NotificationSent`,
  `NotificationFailed`, `NotificationDeliveryAbandoned`…
- **Lot P1** : campagnes (`Campaign` : audience par consentement marketing, envoi programmé par lots, idempotent par
  client) et règle de fréquence marketing par restaurant (`MarketingRules`).

## 14. Promotions — `src/Modules/Promotions`

- **Possède** : promotions (règles, périodes, conditions, limites d'usage) et codes promo.
- **Agrégats** : `Promotion` ; `Coupon` ; DomainService `DiscountPolicy` (calcul pur).
- **Règles structurantes** : une remise est calculée dans le mode de prix de la commande (TTC/HT) et ne dépasse pas le
  sous-total ; limites d'usage tenues par un ledger (port `IPromotionUsageLedger`) ; un usage est annulé si la commande
  l'est (`PromotionUsageReverted`, `CouponReleased`).
- **Publie** : `PromotionApplied`, `PromotionUsageReverted`, `CouponRedeemed`, `CouponReleased`, `PromotionExpired`…
- **Consomme** : `OrderCancelled`. **Consommé par** : Orders (`OrderDiscount`), Reporting.

- **État** : complet de bout en bout (routes `/promotions/...`, `promotions.view`, `promotions.apply`,
  `promotions.manage`). Application à une commande en brouillon par id ou code coupon, prévisualisation, job
  d'expiration. L'hôte relie Orders et Promotions par l'outbox (`PromotionsFlow`) : promotion appliquée -> remise posée
  sur la commande (`ApplyExternalDiscountCommand`), usage rendu si elle la refuse ; commande annulée -> usages rendus.
  Voir son README §6-9.

## 15. Loyalty — `src/Modules/Loyalty`

- **Possède** : comptes de fidélité, règles de gain/dépense, récompenses.
- **Agrégats** : `LoyaltyAccount` ⊃ `LoyaltyTransaction` (ledger **immuable**, corrections par écritures inverses) ;
  `Reward` ; `StoredValueCard` (carte cadeau ou avoir : empreinte du code, solde, mouvements bornés, idempotents par
  référence).
- **Règles structurantes** : le ledger est la vérité, le solde en est la projection stockée ; chaque opération porte
  des points strictement positifs ; seule une écriture inverse (points déjà dépensés) peut rendre le solde négatif ; gain idempotent par commande (port
  `ILoyaltyLedgerLookup`) ; annulation de commande → `PointsEarnReversed`.
- **Consomme** : `OrderCompleted`, `OrderCancelled`, `CustomerAnonymized`. **Publie** : `PointsEarned`,
  `PointsRedeemed`, `RewardRedeemed`, `PointsExpired`…
- **État** : complet de bout en bout (routes `/loyalty/...`, `loyalty.view`, `loyalty.operate`, `loyalty.manage`,
  `loyalty.configure`). Barème par restaurant (désactivé par défaut, inscription automatique, expiration après
  inactivité, job du module). L'hôte relie Orders, Customers et Loyalty par l'outbox (`LoyaltyFlow`) : commande terminée
  -> points ; annulée -> points repris et récompense rendue ; client anonymisé -> compte clos ; récompense échangée ->
  remise posée sur la commande (`ApplyExternalDiscountCommand`), points rendus si la commande la refuse. Voir son README §6-9.

## 16. Billing SaaS — `src/Modules/Billing`

- **Possède** : facturation du restaurateur par la plateforme : plans, abonnement, essai, quotas, entitlements.
  Séparé de Payments (paiement des clients du restaurant).
- **Agrégats** : `Plan` (plateforme) ; `SaasSubscription` ; `UsageCounter` ; `SubscriptionPayment` ;
  `SubscriptionCheckout` (règlement en ligne) ; VO `Entitlement`.
- **Règles structurantes** : `Trialing → Active → PastDue → Suspended` (réactivation possible), `Cancelled` terminal ; un seul abonnement vivant
  par tenant (port `ILiveSubscriptionChecker`) ; quotas (établissements, utilisateurs, fonctionnalités) contrôlés par un
  service d'entitlement côté Application, pas dans les contrôleurs ; webhooks de facturation idempotents et audités.
- **Publie** : `SubscriptionTrialStarted`, `SubscriptionActivated`, `SubscriptionPastDue`, `SubscriptionSuspended`,
  `SubscriptionCancelled`, `QuotaReached`…
- **Consommé par** : Tenants (suspension), Locations / Staff (quotas), tous les modules (fonctionnalités).
- **Intégration** : routes `/billing` (restaurant, `billing.view` / `billing.manage`) et `/admin/billing` (plateforme :
  catalogue, ouverture d'abonnement, paiements enregistrés, impayé, suspension, résiliation). Job d'échéance (fin
  d'essai, impayé puis suspension après délai de grâce, résiliation programmée). L'hôte répercute les événements
  d'abonnement sur le statut du restaurant (`BillingFlow` : activation, réactivation, suspension ; jamais fermeture) et
  sert les quotas mensuels de Notifications depuis le plan (sans abonnement vivant : 0). Lot P1 : règlement en ligne de
  la période due par le restaurant (`/billing/checkouts`, prestataire de la plateforme par `ISubscriptionCollector`,
  relecture par la console et par le job, expiration à 30 minutes) ; numéro de facture `F{année}-{rang}` sans trou,
  donné dans la transaction qui enregistre le paiement ; facture par `GET /billing/payments/{id}/invoice`. Voir son
  README §6-9.

## 17. Reporting / Analytics — `src/Modules/Reporting`

- **Possède** : définitions de rapports, planifications, exports. Les chiffres viennent de **projections** alimentées
  par événements, jamais des agrégats transactionnels.
- **Agrégats** : `ReportDefinition` (VO `ReportSchedule`) ; `ExportJob`.
- **Règles structurantes** : mesures et filtres whitelistés (`ReportCatalog`) ; export à durée de vie limitée
  (`ExportExpired`) ; fichiers d'export stockés via Files.
- **Consomme** : événements d'Orders, Payments, Kitchen, Reservations, Inventory, Promotions, Loyalty (compteurs du
  jour par restaurant, `ReportingFeed`) ; Tenants et Billing (vue de la plateforme : restaurants, abonnements, MRR).
- **Intégration** : routes `/reporting` (tableau de bord, rapports ponctuels et enregistrés, planification, exports CSV ;
  `reporting.view` / `reporting.manage` / `reporting.export`) et `/admin/reporting` (plateforme). Job : envois
  planifiés, production et expiration des exports. Export terminé annoncé par Notifications (`staff.report_ready`,
  lien vers la route de téléchargement). Fichiers stockés par Files (R2). Mesures horaires `sales.hourly_orders` et
  `sales.hourly_revenue` (dimension : heure locale, fixée à l'enregistrement). Voir son README §6-9.

## 18. Files / Media — `src/Modules/Files`

- **Possède** : métadonnées des fichiers (images produits, documents, exports), upload signé, validation MIME/taille,
  quarantaine, suppression logique. Les octets sont dans un stockage objet S3-compatible (Infrastructure).
- **Agrégats** : `MediaFile`.
- **Publie** : `FileUploaded`, `FileQuarantined`, `FileDeleted`. **Consommé par** : Catalog, Reporting, Tenants
  (logo) — qui ne gardent qu'un `FileId`.
- **Intégration** : stockage Cloudflare R2 (API S3, SigV4 écrite dans le module) ; envoi et téléchargement directs par
  URLs signées courtes ; vérification de la taille réelle, du type reconnu aux octets et antivirus (port) avant mise à
  disposition ; routes `/files` (`files.view` / `files.upload` / `files.manage`) ; job des uploads abandonnés. Stocke
  les exports de Reporting (`ReportingExportFilesFromFiles`). Voir son README §6-9.

## 19. Integrations / Webhooks — `src/Modules/Integrations`

- **Possède** : webhooks sortants (endpoints, livraisons, signature HMAC, retries, dead-letter) et clés d'API
  partenaires (scopes, rotation, révocation).
- **Agrégats** : `WebhookEndpoint` ; `WebhookDelivery` ; `ApiCredential`.
- **Règles structurantes** : secret jamais stocké en clair (référence vers le secret store) ; retry borné puis
  dead-letter ; clé d'API stockée hachée, affichée une seule fois.
- **Consomme** : les événements d'intégration publics des autres modules (abonnements par endpoint).
- **Intégration** : 11 événements publics (Orders, Payments, Reservations, Kitchen, Inventory) publiés par l'hôte
  (`WebhookFeed`, données en liste blanche, sans donnée personnelle) ; signature Standard Webhooks ; secrets chiffrés
  (AES-256-GCM, `Integrations:SecretsKey`) ; envoi anti-SSRF (chaque adresse résolue vérifiée) par le job du module ;
  clés d'API (`X-Api-Key`, empreinte SHA-256) dont les scopes sont des permissions des restaurants hors `staff.*`,
  `integrations.*`, `billing.*` ; routes `/integrations` (`integrations.view` / `integrations.manage`).
- **Lot 2** : débit par clé d'API (429, en mémoire par instance) ; désactivation automatique d'un endpoint après N
  abandons d'affilée, annoncée par Notifications (`staff.webhook_disabled`) ; période de grâce de 24 h à la rotation du
  secret (double signature) ; URL masquée partout hors envoi ; dernière utilisation et clés dormantes ; envoi de test
  (`integrations.ping`) ; événements porteurs de l'auteur (`ChangeActor`) et contrat de correspondance vers Audit.
  Voir son README §6-10.

## 20. Reviews / Feedback — `src/Modules/Reviews`

- **Possède** : avis internes, enquêtes, réponses, modération de base.
- **Agrégats** : `Feedback` ; `Survey` ⊃ `SurveyQuestion`.
- **Règles structurantes** : une enquête publiée n'est plus modifiable ; anonymisation du client → lien détaché
  (`FeedbackCustomerDetached`).
- **Consomme** : `CustomerAnonymized` (sollicitation d'avis sur `OrderCompleted` : pas encore).
- **Intégration** : avis enregistrés par `reviews.submit` (tablette, application par clé d'API), établissement, client
  et commande chargés dans le tenant du contexte, un avis par commande (index unique partiel) ; examen par
  l'utilisateur du token, archivage ; statistiques calculées en base ; questionnaires (brouillon, publication, clôture,
  version protégeant le brouillon entier) ; anonymisation d'un client -> auteur et commentaire retirés
  (`ReviewsAnonymizationFlow`) ; note basse -> notification in-app `staff.new_review` sans commentaire ni auteur
  (`ReviewAlertFlow`, `Reviews:LowRatingAlertThreshold`) ; examen, archivage, détachement, publication et clôture
  audités ; routes `/reviews` (`reviews.view` / `reviews.submit` / `reviews.manage`). Réponses aux questionnaires (lot
  P1) : `SurveyResponse`, une par commande, collectées par Storefront (lien, QR, page de suivi), résultats agrégés.
  Voir son README §6-9.

## 21. Audit — `src/Modules/Audit`

- **Possède** : le journal **append-only** des actions sensibles : acteur, tenant (nullable = plateforme), action,
  cible, horodatage, contexte (sans secret ni donnée sensible).
- **Modèle** : `AuditEntry` (entité append-only, pas d'agrégat : rien ne se modifie ni ne se supprime).
- **Consomme** : événements sensibles d'Identity, Staff, Tenants, Payments, Billing, Integrations (contrat prêt : README d'Integrations §10), Inventory
  (ajustements), Reviews (examen, archivage, détachement, questionnaires).
- **Intégration** : journal en ajout seul alimenté par l'outbox (`AuditFeed` de l'hôte, 99 événements, une règle et une
  liste blanche de champs par événement, une écriture par événement) ; `UPDATE`/`DELETE`/`TRUNCATE` refusés en base
  (triggers), purge de rétention seule autorisée (`Audit:RetentionDays`) ; routes `/audit` (`audit.view`) et
  `/admin/audit` (plateforme). Voir son README §6-9.

## 22. Fiscal — facture normalisée e-MECeF — `src/Modules/Fiscal`

- **Possède** : le profil fiscal du restaurant (IFU, RCCM, régime TVA/TPS, AIB, environnement DGI), le coffre de son
  jeton e-MECeF (chiffré, table à part), et les factures normalisées (vente FV, avoir FA) avec leur file d'envoi.
- **Agrégats** : `FiscalProfile` (un par tenant) ; `FiscalInvoice` (instantané figé : lignes en francs CFA entiers,
  paiements e-MECeF, éléments de sécurité rendus par la DGI).
- **Règles structurantes** : francs CFA seulement ; facturation impossible sans jeton ; changer d'IFU efface le jeton ;
  paiements égaux au total ; un avoir ne dépasse pas sa facture et attend qu'elle soit certifiée ; une facture par
  source (commande, remboursement) ; nouvel essai espacé puis échec, relance manuelle. Aucune donnée du client.
- **Consomme** (par l'hôte, `FiscalFlow`) : `OrderCompleted`, `RefundSucceeded`. **Sert** : les mentions fiscales des
  reçus (`GetOrderFiscalQuery`, console et Printing).
- **Intégration** : client HTTP e-MECeF (`EmecfClient`, jeton en Bearer seulement), file `FiscalInvoiceWorker` ; routes
  `/fiscal` (`fiscal.view`, `fiscal.configure`) ; `Fiscal:TokenKey` obligatoire.

## Avancement P2 (14 octobre 2026)

Détail fonctionnel : `AUDIT-FONCTIONNALITES-CONSOLE.md` §8. Changements par module :

- **Catalog** : prix par établissement et canal (`price_overrides`), créneaux des cartes (`slots`), allergènes servis par l'API.
- **Floor / Kitchen** : plan dessiné (`layout_*`, dont `layout_rotation`), envoi par service (`courses`, plats en attente).
- **Files** : miniatures (`thumbnail_of`) ; les exports de Reporting acceptent aussi le PDF et le XLSX.
- **Identity** : validation en deux étapes TOTP (secret chiffré, codes de secours en empreintes, connexion en deux temps).
- **Notifications** : préférences par type d'alerte de l'équipe (`muted_types`).
- **Loyalty** : cartes cadeaux et avoirs (`stored_value_cards`, routes `/loyalty/stored-value-cards`).
- **Payments** :
  - moyens disponibles selon la commande ; Wave ;
  - moyens `GiftCard` (24) et `DeliveryPlatform` (25), non comptés à la clôture ;
  - port `IPaymentStoredValue` servi par Loyalty (débit avant enregistrement, compensation si l'enregistrement échoue) ;
  - remboursement en avoir (`POST /payments/{id}/refunds/store-credit`).
- **Orders** : canal `DeliveryPlatform` avec plateforme et numéro externe (unique par restaurant).
- **Reporting** : exports XLSX et PDF écrits sans dépendance ; export comptable (`POST /reporting/exports/accounting`) ; mesure `sales.revenue_tax` ; port `IReportingLocationNames`.
- **Storefront** : anti-robot Turnstile par application ; langue du client transmise à la création de sa fiche.

