# Module Tenants — Domain

Le client SaaS (organisation), son cycle de vie et ses paramètres globaux.

## 1. Frontière d'agrégat

- **`Tenant`** est la seule racine ; son `Id` **est** le `TenantId` du shared kernel. Il n'est pas `ITenantOwned`
  (il *est* le tenant).
- Tout est value object à l'intérieur (`TenantName`, `Currency` et `TimeZoneId` du kernel, `Branding`) ou simple
  booléen (`PricesIncludeTax`) : pas d'entité enfant.
- Racine de sécurité **logique** seulement : restaurants, menus, commandes, abonnement SaaS... ne sont **pas** des
  enfants de `Tenant` ; ils portent un `TenantId`. Charger un tenant ne charge rien d'autre.

## 2. Classes

| Classe | Rôle DDD | Responsabilité | Transitions / events | Erreurs |
|---|---|---|---|---|
| `Tenant` | AggregateRoot | Cycle de vie et paramètres globaux ; expose `CanAcceptBusinessWrites` | `Trial -> Active` (Activate) ; `Trial/Active -> Suspended` (Suspend) ; `Suspended -> Active` (Reactivate) ; `Trial/Active/Suspended -> Closed` (Close, terminal). Events : `TenantCreated`, `TenantActivated`, `TenantSuspended`, `TenantReactivated`, `TenantClosed`, `TenantRenamed`, `TenantDefaultCurrencyChanged`, `TenantTimeZoneChanged`, `TenantPriceTaxModeChanged`, `TenantBrandingUpdated` | `tenants.tenant.closed`, `.suspended`, `.only_trial_can_be_activated`, `.already_suspended`, `.not_suspended`, `.suspension_reason_invalid`, `.currency_locked_by_priced_catalog` |
| `TenantName` | ValueObject | Nom trimé, 2 à 100 caractères, sans caractère de contrôle | — | `tenants.tenant_name.empty`, `.length_invalid`, `.invalid_characters` |
| `TimeZoneId` | ValueObject **du kernel** (`SaasRestaurant.BuildingBlocks.Domain`, non redéclaré ici) | Identifiant IANA validé par la BCL (`TimeZoneInfo`), `ToLocal(instant)` | — | `common.time_zone.empty`, `.unknown` |
| `ITenantPricedCatalogChecker` | Port (interface) | « Le tenant a-t-il déjà des produits tarifés ? » — réponse passée en `bool hasPricedCatalog` à `ChangeDefaultCurrency` | — | — |
| `Branding` | ValueObject | `DisplayName`, `LogoUrl?` (http/https absolue), `PrimaryColor?` (hex) | — | `tenants.branding.display_name_empty`, `.display_name_too_long`, `.display_name_invalid_characters`, `.logo_url_invalid`, `.color_invalid` |
| `TenantStatus`, `TenantSuspensionReason` | Enums | Listes fermées | — | — |

## 3. Choix faits là où la spec était ouverte

- **Statut initial : `Trial`.** `Activate` n'est possible que depuis `Trial`.
- **« Suspendu reste lisible, écritures restreintes »** : rendu explicite par `Tenant.CanAcceptBusinessWrites`
  (vrai en `Trial` et `Active` uniquement) et `EnsureCanAcceptBusinessWrites()` qui lève `tenants.tenant.suspended`
  ou `tenants.tenant.closed`. Le domaine Tenants ne bloque pas lui-même les autres modules : c'est l'Application
  qui consulte cette réponse avant une commande métier. La lecture n'est jamais restreinte par le domaine.
- Un tenant `Suspended` peut encore modifier **ses propres paramètres** (nom, devise, fuseau, mode de prix, branding) : ce ne
  sont pas des écritures métier et le support doit pouvoir corriger un compte suspendu.
- `Suspend` exige une raison (`TenantSuspensionReason`), conservée tant que le tenant est suspendu et portée par
  l'event. `Reactivate` ramène toujours à `Active`, même si la suspension est survenue pendant le trial (une
  période d'essai interrompue ne reprend pas ; la durée du trial relève du module Billing).
- `Closed` est terminal : toute méthode (transitions **et** paramètres) lève `tenants.tenant.closed`.
- Modifier un paramètre avec la **même valeur** est un no-op silencieux, sans event (formulaire ré-enregistré).
- `ChangeDefaultCurrency` ne convertit rien : c'est le défaut des **futurs** documents ; les montants déjà
  enregistrés ailleurs gardent leur devise (snapshots).
- **Verrou de devise** : dès que le catalogue contient des produits tarifés, changer la devise rendrait tous ces
  produits inutilisables pour de nouvelles commandes. `ChangeDefaultCurrency(newCurrency, hasPricedCatalog, clock)`
  lève `tenants.tenant.currency_locked_by_priced_catalog` quand `hasPricedCatalog` est vrai **et** que la devise
  change réellement (même devise = no-op, même avec un catalogue tarifé). Le booléen vient du port
  `ITenantPricedCatalogChecker`, implémenté côté Application/Infrastructure sur le module Menu.
- **Prix TTC ou HT — `PricesIncludeTax`** (décision produit) : les prix du menu sont **TTC par défaut** (`true` à
  `Create`, pratique de la restauration), mais le SaaS est multi-pays : c'est un paramètre du tenant, modifié par
  `ChangePriceTaxMode(pricesIncludeTax, clock)` (event `TenantPriceTaxModeChanged`). Le module Orders reçoit cette
  valeur **en paramètre** à la création d'une commande ; une commande existante garde le mode de sa création.
  `Create` ne prend pas ce paramètre (un tenant HT appelle `ChangePriceTaxMode(false)` à l'onboarding) et
  `TenantCreated` ne le porte pas (inchangé).
- **Fuseau du tenant = fuseau PAR DÉFAUT de ses établissements** : chaque `RestaurantLocation` porte désormais son
  propre `TimeZoneId` ; l'Application lui passe `tenant.TimeZone` à la création si l'utilisateur n'en choisit pas
  d'autre. `ChangeTimeZone` du tenant ne modifie **pas** les établissements existants.
- `Branding` par défaut à la création = `DisplayName` égal au nom du tenant. `Rename` ne modifie pas le branding.
- `PrimaryColor` : `#RGB` ou `#RRGGBB`, normalisée en `#RRGGBB` majuscules. Pas de canal alpha ni de nom CSS.
- `TimeZoneId` est le type **du kernel** (partagé avec Locations) : forme IANA (`Area/Location`, ou `UTC`) **et**
  connu de `TimeZoneInfo` avec `HasIanaId` ; les identifiants Windows (« Romance Standard Time ») sont refusés. La
  casse est conservée telle que fournie.
- **Égalité de `Branding`** : l'URL du logo est comparée par `AbsoluteUri` (l'égalité de `System.Uri` ignore le
  fragment : `logos.svg#light` et `logos.svg#dark` étaient « égales » et `UpdateBranding` devenait un no-op).
- **Ordre des gardes de `Suspend`** : l'état terminal passe en premier ; un tenant `Closed` répond
  `tenants.tenant.closed` même si la raison fournie est invalide.
- **Durcissement (revue)** : `Tenant.Create` ne reçoit aucun identifiant (il crée le `TenantId`) : rien à garder
  contre un id vide. Le seul enum reçu (`TenantSuspensionReason`) est validé par `Enum.IsDefined`. Chaque opération
  lit l'horloge une seule fois : `SuspendedAtUtc` / `ClosedAtUtc` / `CreatedAtUtc` égalent le `OccurredAtUtc` de l'event.
- Non modélisé (hors spec) : slug/sous-domaine unique, propriétaire du tenant, plan d'abonnement (module Billing).

## 4. Matrice invariant -> méthode -> test

| Invariant | Méthode | Test |
|---|---|---|
| Nom non vide, 2..100 | `TenantName.Create` | `TenantValueObjectsTests.TenantName_rejects_an_empty_value`, `TenantName_enforces_its_length_limits`, `TenantName_trims_the_value`, `TenantName_rejects_control_characters` |
| Fuseau IANA valide | `TimeZoneId.Create` (kernel) | `TenantValueObjectsTests.TimeZoneId_accepts_iana_identifiers`, `TimeZoneId_rejects_an_empty_value`, `TimeZoneId_rejects_unknown_or_non_iana_identifiers`, `TimeZoneId_rejects_a_value_longer_than_the_maximum` |
| Devise valide | `Currency.FromCode` (kernel) | `TenantTests.Create_cannot_be_reached_with_an_invalid_currency_or_time_zone` |
| Couleur valide si fournie | `Branding.Create` | `TenantValueObjectsTests.Branding_accepts_and_normalizes_a_valid_hex_color`, `Branding_rejects_an_invalid_color`, `Branding_can_be_created_with_a_display_name_only` |
| Nom d'affichage requis et borné | `Branding.Create` | `TenantValueObjectsTests.Branding_rejects_an_empty_display_name`, `Branding_enforces_the_display_name_maximum_length`, `Branding_rejects_control_characters_in_the_display_name` |
| Logo = URL http(s) absolue | `Branding.Create` | `TenantValueObjectsTests.Branding_accepts_an_absolute_http_logo_url`, `Branding_rejects_a_relative_logo_url`, `Branding_rejects_a_logo_url_that_is_not_http` |
| Création en Trial, prix TTC par défaut + `TenantCreated` | `Tenant.Create` | `TenantTests.Create_starts_a_trial_tenant_with_its_settings`, `Create_raises_TenantCreated`, `Create_defaults_the_branding_to_the_tenant_name`, `Create_keeps_the_given_branding` |
| Activation depuis Trial uniquement | `Tenant.Activate` | `TenantTests.Activate_moves_a_trial_tenant_to_active_and_raises_TenantActivated`, `Activate_rejects_an_active_tenant`, `Activate_rejects_a_suspended_tenant` |
| Suspension depuis Trial/Active, raison valide | `Tenant.Suspend` | `TenantTests.Suspend_suspends_a_trial_or_active_tenant_and_raises_TenantSuspended`, `Suspend_rejects_an_already_suspended_tenant`, `Suspend_rejects_an_undefined_reason` |
| Suspendu : lisible, écritures métier refusées | `Tenant.CanAcceptBusinessWrites`, `EnsureCanAcceptBusinessWrites` | `TenantTests.Suspended_tenant_refuses_business_writes_but_remains_a_live_tenant`, `Trial_and_active_tenants_accept_business_writes`, `Suspended_tenant_can_still_update_its_settings` |
| Closed signalé avant une raison invalide | `Tenant.Suspend` | `TenantTests.Suspend_on_a_closed_tenant_reports_the_closed_state_before_an_invalid_reason` |
| Horodatages d'état = heure de l'event | `Suspend`, `Close` | `TenantTests.State_timestamps_equal_the_time_of_the_event_they_come_with` |
| Brandings distincts si le logo diffère par le fragment | `Branding` (égalité) | `TenantValueObjectsTests.Branding_distinguishes_logo_urls_differing_only_by_their_fragment` |
| Réactivation depuis Suspended uniquement | `Tenant.Reactivate` | `TenantTests.Reactivate_moves_a_suspended_tenant_to_active_and_clears_the_suspension`, `Reactivate_of_a_tenant_suspended_during_its_trial_makes_it_active`, `Reactivate_rejects_a_tenant_that_is_not_suspended` |
| Closed terminal | `Tenant.Close` + toutes | `TenantTests.Close_can_be_reached_from_trial_active_and_suspended`, `Closed_is_terminal_and_rejects_every_transition`, `Closed_is_terminal_and_rejects_every_settings_change`, `Closed_tenant_refuses_business_writes` |
| Devise verrouillée par un catalogue tarifé | `ChangeDefaultCurrency` | `TenantTests.ChangeDefaultCurrency_is_rejected_once_the_catalog_is_priced`, `ChangeDefaultCurrency_to_the_same_currency_is_a_no_op_even_with_a_priced_catalog`, `ChangeDefaultCurrency_changes_the_currency_and_raises_an_event` |
| Mode de prix TTC/HT : modifiable sauf Closed, event si changement | `ChangePriceTaxMode` | `TenantTests.ChangePriceTaxMode_switches_the_mode_and_raises_TenantPriceTaxModeChanged`, `ChangePriceTaxMode_is_allowed_during_the_trial_and_while_suspended`, `Settings_changes_to_the_same_value_raise_nothing`, `Closed_is_terminal_and_rejects_every_settings_change` |
| Paramètres : event seulement si changement réel | `Rename`, `ChangeDefaultCurrency`, `ChangeTimeZone`, `ChangePriceTaxMode`, `UpdateBranding` | `TenantTests.Rename_changes_the_name_and_raises_TenantRenamed`, `ChangeDefaultCurrency_changes_the_currency_and_raises_an_event`, `ChangeTimeZone_changes_the_time_zone_and_raises_an_event`, `UpdateBranding_replaces_the_branding_and_raises_an_event`, `Settings_changes_to_the_same_value_raise_nothing`, `Settings_changes_are_allowed_during_the_trial`, `Rename_does_not_touch_the_branding` |

## 5. Application / Infrastructure

- Appliquer `CanAcceptBusinessWrites` aux commandes des autres modules (middleware/pipeline) ; filtrage des
  requêtes par `TenantId` (isolation).
- Réagir aux events : `TenantSuspended`/`TenantClosed` (couper les accès, notifier), provisioning sur `TenantCreated`.
- Qui déclenche `Suspend`/`Reactivate` : Billing (impayé, fin de trial) ou back-office, via un handler.
- Concurrence optimiste sur `Tenant` (deux transitions simultanées).
- **Port `ITenantPricedCatalogChecker`** : à implémenter sur le catalogue (module Menu : au moins un produit avec un
  prix pour ce tenant) ; le handler appelle le port puis `ChangeDefaultCurrency(currency, hasPricedCatalog, clock)`.
  Course résiduelle (premier produit tarifé créé pendant le changement) : à sérialiser côté Application.
- **`PricesIncludeTax`** : le handler de création de commande lit `tenant.PricesIncludeTax` et le passe à Orders.
- **Fuseau par défaut des établissements** : passer `tenant.TimeZone` à `RestaurantLocation.Create` par défaut.
- `TimeZoneId` (kernel) dépend de la base tz de l'hôte (ICU/tzdata) : garder des images à jour ; conversion
  UTC -> local par `TimeZoneId.ToLocal(instant)`.
- Stockage et validation du fichier logo (module Files) : le domaine ne garde qu'une URL.

## 6. Infrastructure (`Tenants.Infrastructure`)

- **PostgreSQL + EF Core**, schéma dédié `tenants` (table d'historique des migrations comprise). Enregistrement :
  `services.AddTenantsInfrastructure(connectionString)` ; `IClock` et `ITenantPricedCatalogChecker` restent à
  fournir par l'hôte (le second se répond depuis le catalogue).
- **Mapping** : value objects convertis en colonnes simples (`name`, `default_currency`, `time_zone`), `Branding`
  en colonnes `branding_*` de la même table, enums en entier (valeurs explicites du domaine).
- **Concurrence optimiste** sur la colonne système `xmin` ; un conflit devient une `ConcurrencyException`
  (`tenants.tenant.concurrent_update`), rien n'est écrit.
- **Events** : `ITenantsUnitOfWork.SaveChangesAsync` écrit les domain events dans `tenants.outbox_messages` dans la
  même transaction que l'agrégat, puis les efface de l'agrégat (socle partagé
  `SaasRestaurant.BuildingBlocks.Infrastructure`, commun à tous les modules). `id` (identity) suit l'ordre d'émission. Le
  dispatcher du socle (`AddOutboxDispatcher<TenantsDbContext>()`) les publie aux `IOutboxConsumer` ; aucun
  consommateur ne s'intéresse encore aux events de Tenants.
- **Migrations** : `dotnet ef migrations add <Nom> --project src/Modules/Tenants/Tenants.Infrastructure
  --startup-project src/Modules/Tenants/Tenants.Infrastructure --output-dir Persistence/Migrations` ;
  `database update` lit la chaîne de connexion dans `TENANTS_DB`.
- **Tests** (`Tenants.Infrastructure.Tests`) : sur un vrai PostgreSQL via Testcontainers, Docker requis.

## 7. API (`Tenants.Api`)

- Point d'entrée du module pour l'hôte : `services.AddTenantsModule(connectionString)` (handlers + persistance) et
  `app.MapTenantsEndpoints()`. Les conventions HTTP communes viennent de `SaasRestaurant.BuildingBlocks.Api` :
  `services.AddApiConventions()` + `app.UseExceptionHandler()`.
- **Aucune autorisation n'est posée par le module** : l'hôte la chaîne sur le groupe retourné
  (`MapTenantsEndpoints().RequireAuthorization(...)`). Le host actuel exige la policy `PlatformAdmin`
  (voir `src/Host/README.md`). Migrations : `services.MigrateTenantsDatabaseAsync()`.

| Route | Corps | Succès |
|---|---|---|
| `POST /tenants` | `name`, `defaultCurrencyCode`, `timeZoneId`, `pricesIncludeTax` (défaut `true`) | `201` + `Location`, `{ id }` |
| `GET /tenants/{id}` | — | `200` `TenantDto` (enums en texte) |
| `POST /tenants/{id}/activate`, `/reactivate`, `/close` | — | `204` |
| `POST /tenants/{id}/suspend` | `reason` | `204` |
| `PUT /tenants/{id}/name` | `name` | `204` |
| `PUT /tenants/{id}/default-currency` | `currencyCode` | `204` |
| `PUT /tenants/{id}/time-zone` | `timeZoneId` | `204` |
| `PUT /tenants/{id}/price-tax-mode` | `pricesIncludeTax` | `204` |
| `PUT /tenants/{id}/branding` | `displayName`, `logoUrl?`, `primaryColor?` (remplacement complet) | `204` |

- **Restaurant courant** (`app.MapCurrentTenantEndpoints()`, policy `tenant-member` posée par l'hôte) :
  `GET /tenant` -> `200` `TenantDto` du restaurant sélectionné. Le tenant vient UNIQUEMENT du claim `tid` du jeton
  (`ITenantContext`) ; sans restaurant sélectionné : `403` `common.tenant.required`. Utilisé par la console du
  restaurant (`restaurantos-app`) pour la devise, le mode de prix (TTC/HT) et le nom affiché.
- **Erreurs** en `application/problem+json`, code stable dans `code` : `DomainException` -> `422`,
  `NotFoundException` -> `404`, `ConcurrencyException` -> `409`. Un JSON illisible reste un `400` du framework.
- **Tests** (`Tenants.Api.Tests`) : le module servi en mémoire (TestServer) sur un vrai PostgreSQL, Docker requis.

## 8. Demandes de démonstration (`Tenants.Domain.Prospects`)

Le formulaire du site vitrine (`restaurantos-site`, page Contact) enregistre une `DemoRequest` : agrégat de la
PLATEFORME (aucun tenant, déclaré dans `PlatformAggregates` des tests d'architecture), table `tenants.demo_requests`.

- Données : nom du contact, restaurant, pays, email et/ou téléphone (au moins l'un des deux, contrainte en base),
  plan qui l'intéresse, message ; suivi (`New`, `Contacted`, `Converted`, `Declined`) et note interne.
- **Données personnelles** : elles ne sortent JAMAIS dans un événement (`DemoRequestSubmitted` ne porte que l'id),
  un log ou une trace. L'email envoyé à l'équipe (`DemoRequestAlertFlow` dans l'hôte, destinataire
  `Sales:DemoRequestNotificationEmail`, vide = pas d'email) ne contient que le lien vers le back-office.
  Suppression définitive possible depuis le back-office (`DELETE`), par exemple à la demande du prospect.

| Route | Accès | Corps / réponse |
|---|---|---|
| `POST /demo-requests` | anonyme, limitée à 5 par heure et par adresse client (`tenants-demo-requests`) | `contactName`, `restaurantName`, `email?`, `phone?` (E.164), `country`, `planInterest?`, `message?`, `website` (piège à robots : rempli = ignoré, même réponse) -> `202` sans corps |
| `GET /admin/demo-requests?status&page&pageSize` | `PlatformAdmin` | page de `DemoRequestDto`, plus récentes d'abord |
| `GET /admin/demo-requests/{id}` | `PlatformAdmin` | `DemoRequestDto` |
| `PUT /admin/demo-requests/{id}` | `PlatformAdmin` | `status`, `note?` -> `204` |
| `DELETE /admin/demo-requests/{id}` | `PlatformAdmin` | `204` |

Migration à générer : `dotnet ef migrations add DemoRequests` (projet `Tenants.Infrastructure`).
