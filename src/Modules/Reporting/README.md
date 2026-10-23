# Module Reporting

But : fournir des indicateurs sans transformer les agrégats transactionnels en modèles de reporting.

## 1. Frontière d'agrégat

- `ReportDefinition` : définition sauvegardée (nom, mesures, filtres, planification). Pas d'enfant ; `MetricSet`, `ReportFilters`
  et `ReportSchedule` sont des value objects remplacés en bloc.
- `ExportJob` : demande d'export asynchrone, cycle de vie indépendant (une définition peut exister sans export et inversement),
  donc racine séparée. Le fichier produit est référencé par le `FileId` du kernel uniquement ; il entre dans l'agrégat via
  `ExportedFileReference` (tenant + id), ce qui permet à `Complete` de refuser le fichier d'un autre tenant. Le demandeur est
  un `UserId` du kernel ; la définition exportée, quand il y en a une, est **stockée** par son `ReportDefinitionId` (id seul,
  les deux racines restent indépendantes) mais **reçue** par `Queue` sous forme de `ReportDefinitionReference` (tenant + id,
  constructeur `internal`, produite uniquement par `ReportDefinition.ToReference()`), ce qui permet de refuser la
  définition d'un autre tenant.
- Les données analytiques elles-mêmes sont des projections/read models : elles ne sont **pas** dans le domaine.
  `ReportCatalog` décrit ce que ces read models savent calculer et filtrer.

## 2. Classes

| Classe | Rôle DDD | Responsabilité | Transitions | Events | Erreurs |
|---|---|---|---|---|---|
| `ReportDefinition` | AggregateRoot (tenant-owned) | Nom, mesures, filtres whitelistés, planification, compatibilité avec le catalogue courant (`EnsureCompatibleWith`) | `non planifié -> planifié` (`ScheduleDelivery`), `planifié -> planifié` (autre planning), `planifié -> non planifié` (`Unschedule`) | `ReportScheduled`, `ReportUnscheduled` | `reporting.report_definition.*` |
| `ReportCatalog` | Value Object | Liste blanche explicite : codes de mesures + champs filtrables | — | — | `reporting.catalog.metrics_empty`, `reporting.code.invalid` |
| `MetricSet` | Value Object | Ensemble non vide de mesures prises dans le catalogue (≤ 30) | — | — | `.metrics_empty`, `.metric_unknown`, `.too_many_metrics` |
| `ReportFilter` (+ `FilterOperator`) | Value Object | Un critère (champ, opérateur, valeur ≤ 200) | — | — | `reporting.code.invalid`, `.filter_operator_invalid`, `.filter_value_invalid` |
| `ReportFilters` | Value Object | Ensemble de critères dont chaque champ est dans le catalogue (≤ 20, peut être vide) | — | — | `.filter_field_not_allowed`, `.filter_duplicated`, `.too_many_filters` |
| `ReportSchedule` (+ `ReportFrequency`) | Value Object | Fréquence, ancre (`DayOfWeek` pour Weekly, jour du mois 1..28 pour Monthly), heure **locale** + `TimeZoneId` du kernel, destinataires (`Email` du kernel, 1 à 20) | — | — | `.schedule_frequency_invalid`, `.schedule_day_of_week_required`, `.schedule_day_of_week_invalid`, `.schedule_day_of_month_required`, `.schedule_day_of_month_invalid`, `.schedule_anchor_not_allowed`, `.schedule_recipients_empty`, `.schedule_too_many_recipients` |
| `ReportDefinitionReference` | Value Object (référence, ctor `internal`) | Porter le `TenantId` d'une définition ; produite uniquement par `ReportDefinition.ToReference()` | — | — | — |
| `ExportJob` | AggregateRoot (tenant-owned) | Cycle de vie d'un export ; qui l'a demandé (`RequestedBy`) et quelle définition (`ReportDefinitionId`, optionnel) | `Queued -> Running -> Completed -> Expired` ; `Running -> Failed` ; `Failed` et `Expired` terminaux | `ExportRequested`, `ExportCompleted`, `ExportFailed`, `ExportExpired` | `reporting.export_job.*` |
| `ExportedFileReference` | Value Object (référence locale, publique) | Fichier stocké par le module Files : `TenantId` (non vide) + `FileId` | — | — | `common.id.empty` (tenant vide) ; `FileId` contrôlé par `Complete` : `reporting.export_job.file_required`, puis `common.tenant.mismatch` |
| `ExportType` | Value Object | Code contrôlé du type d'export (ex. `orders.csv`) | — | — | `reporting.export_job.type_invalid` |
| `ReportDefinitionId`, `ExportJobId` | Ids typés | — | — | — | `common.id.empty` |

## 3. Choix faits là où la spec était ouverte

- **Catalogue explicite** : `ReportCatalog` est passé à `Create`, `ChangeFilters`, `EnsureCompatibleWith` et `ScheduleDelivery`
  (jamais une constante cachée). C'est
  l'Application qui le construit à partir des read models qu'elle expose. Les champs/mesures sont des codes au format contrôlé
  (`minuscules.pointées`), ce qui exclut déjà toute injection de nom de colonne ; la whitelist exclut le reste.
- `ChangeFilters` remplace tout le jeu de filtres et le revalide contre le catalogue fourni. Il n'existe pas de méthode pour
  changer les mesures (absente de la spec) : on crée une autre définition.
- La **valeur** d'un filtre est du texte opaque borné ; son typage (date, id…) appartient au read model, qui doit la passer en
  paramètre de requête.
- La méthode `Schedule` de la spec s'appelle `ScheduleDelivery` (C# interdit un membre `Schedule` à côté de la propriété
  `Schedule`). Replanifier avec un planning différent est permis et relève `ReportScheduled`.
- **Même valeur = no-op silencieux** : `Rename` avec le même nom (après trim), `ChangeFilters` avec le même jeu de filtres
  (l'ordre ne compte pas) et `ScheduleDelivery` avec un planning identique ne mutent rien, ne touchent pas `UpdatedAtUtc` et ne
  lèvent aucun event. L'erreur `schedule_unchanged` a été supprimée. La validation passe **avant** la comparaison : une valeur
  invalide, ou un catalogue qui ne couvre plus la demande, est rejetée même si elle est identique à l'état courant. Les
  transitions répétées restent rejetées (`Unschedule` d'un rapport non planifié, transitions d'`ExportJob`).
- **Catalogue qui évolue** : une définition sauvegardée peut référencer une mesure ou un champ retiré depuis du catalogue.
  `EnsureCompatibleWith(catalog)` le détecte (`catalog_incompatible`, un seul code pour mesure et champ : l'action attendue
  est la même, corriger ou recréer la définition) ; `ScheduleDelivery` l'appelle, on ne planifie donc jamais un rapport
  inexécutable. La définition n'est pas modifiée par ce contrôle.
- **Fichier d'export** : `Complete` reçoit un `ExportedFileReference` et vérifie `EnsureSameTenant` ; l'id vide reste rejeté
  par `file_required` (contrôlé avant le tenant ; code conservé car déjà épinglé par un test, c'est pourquoi le VO ne
  garde que son `TenantId` avec `Guard.NotEmpty` -> `common.id.empty`).
- **Traçabilité de la demande** : `Queue` exige `RequestedBy` (`UserId` non vide) et accepte une
  `ReportDefinitionReference` optionnelle (`null` = export ad hoc). Une définition d'un autre tenant est rejetée par
  `common.tenant.mismatch` ; un id vide n'est plus représentable (la référence vient d'une vraie définition). Le job ne
  stocke que le `ReportDefinitionId`. `ExportRequested` porte les deux, `ExportCompleted`
  porte `RequestedBy` pour notifier le demandeur. `ExportFailed`/`ExportExpired` ne le portent pas (non demandé) : le
  consommateur relit le job.
- `ReportSchedule.Create(frequency, dayOfWeek, dayOfMonth, localTimeOfDay, timeZone, recipients)` :
  - **heure locale** (`LocalTimeOfDay`, tronquée à la minute) dans un **fuseau explicite** (`TimeZone`, `TimeZoneId` du
    kernel, fourni par l'Application : celui du tenant). Avant : heure UTC, qui glissait d'une heure à chaque changement
    d'heure. « 08:00 Europe/Paris » reste 08:00 toute l'année ;
  - **ancre obligatoire** : `DayOfWeek` pour `Weekly` (`schedule_day_of_week_required`, valeur hors enum ->
    `schedule_day_of_week_invalid`), jour du mois **1..28** pour `Monthly` (`schedule_day_of_month_required`,
    `schedule_day_of_month_invalid` ; 28 = dernier jour présent dans tous les mois, pas de règle « dernier jour ») ;
  - **ancre interdite ailleurs** (`schedule_anchor_not_allowed`) : aucun des deux pour `Daily`, pas de jour du mois pour
    `Weekly`, pas de jour de semaine pour `Monthly` ;
  - destinataires dédoublonnés ; ancre, heure et fuseau font partie de l'égalité (changer l'un d'eux est une vraie
    replanification) et sont portés par `ReportScheduled`.
- **Ids vides** : `Create` / `Queue` (tenant, demandeur) et `ExportedFileReference` (tenant) -> `common.id.empty`.
- **Une seule lecture de l'horloge par opération** : `UpdatedAtUtc`, `QueuedAtUtc`, `CompletedAtUtc`... égalent
  l'`OccurredAtUtc` de l'événement.
- `ReportUnscheduled` ajouté (le planificateur doit pouvoir retirer le job). `Rename`/`ChangeFilters` ne lèvent pas d'event.
- `ExportJob.Type` est un code ouvert (`ExportType`) et non un enum : la liste appartient aux workers.
- `Fail` n'est permis que depuis `Running` (un job en file qui ne peut pas démarrer est d'abord `Start`é par le worker).
- `FileId` est conservé après `Expire` (traçabilité) ; `ExportExpired` porte le `FileId` pour que Files supprime le fichier.
- `ExportFailed` et `ExportExpired` ajoutés : faits métier utiles (notification de l'utilisateur, nettoyage).
- Unicité du nom de rapport : non demandée par la spec, non imposée.

## 4. Matrice invariant -> méthode -> test

| Invariant | Méthode | Test |
|---|---|---|
| Filtres whitelistés (création) | `ReportDefinition.Create` | `ReportDefinitionTests.Create_rejects_a_filter_on_a_non_whitelisted_field` |
| Filtres whitelistés (modification), état précédent conservé | `ChangeFilters` | `ReportDefinitionTests.ChangeFilters_rejects_a_non_whitelisted_field_and_keeps_the_previous_filters` |
| La whitelist est celle fournie | `ChangeFilters` | `ReportDefinitionTests.ChangeFilters_uses_the_catalog_it_is_given` |
| Champ hors whitelist rejeté par le VO | `ReportFilters.Create` | `ReportValueObjectsTests.Filters_reject_a_field_that_is_not_whitelisted` |
| Format de champ contrôlé (pas d'injection) | `ReportFilter.Create` | `ReportValueObjectsTests.Filter_rejects_a_malformed_field` |
| Opérateur connu, valeur requise et bornée | `ReportFilter.Create` | `ReportValueObjectsTests.Filter_rejects_an_unknown_operator`, `Filter_requires_a_value`, `Filter_value_length_is_bounded` |
| Pas de critère en double, nombre borné | `ReportFilters.Create` | `ReportValueObjectsTests.Filters_reject_a_duplicate_criterion`, `Filters_count_is_bounded` |
| Remplacement / vidage des filtres | `ChangeFilters` | `ReportDefinitionTests.ChangeFilters_replaces_the_filter_set`, `ChangeFilters_can_clear_every_filter` |
| Au moins une mesure | `MetricSet.Create` | `ReportDefinitionTests.Create_rejects_an_empty_metric_set`, `ReportValueObjectsTests.MetricSet_rejects_an_empty_set` |
| Mesures prises dans le catalogue | `MetricSet.Create` | `ReportDefinitionTests.Create_rejects_a_metric_outside_the_catalog`, `ReportValueObjectsTests.MetricSet_rejects_a_metric_outside_the_catalog` |
| Nombre de mesures borné | `MetricSet.Create` | `ReportValueObjectsTests.MetricSet_size_is_bounded` |
| Catalogue : au moins une mesure, codes bien formés | `ReportCatalog.Create` | `ReportValueObjectsTests.Catalog_requires_at_least_one_metric`, `Catalog_rejects_malformed_codes` |
| Nom non vide et borné | `Create`, `Rename` | `ReportDefinitionTests.Create_rejects_an_empty_name`, `Create_rejects_a_name_over_the_limit`, `Rename_rejects_an_invalid_name_and_keeps_the_previous_one` |
| Tenant obligatoire | `Create`, `Queue` | `ReportDefinitionTests.Create_rejects_a_default_tenant`, `ExportJobTests.Queue_rejects_a_default_tenant` |
| Même nom = no-op (ni `UpdatedAtUtc` ni event) | `Rename` | `ReportDefinitionTests.Rename_to_the_same_name_is_a_silent_no_op` |
| Même jeu de filtres = no-op | `ChangeFilters` | `ReportDefinitionTests.ChangeFilters_with_the_same_filter_set_is_a_silent_no_op` |
| Planification -> `ReportScheduled` | `ScheduleDelivery` | `ReportDefinitionTests.ScheduleDelivery_sets_the_schedule_and_raises_ReportScheduled`, `ScheduleDelivery_replaces_a_different_schedule` |
| Planning identique = no-op, sans event | `ScheduleDelivery` | `ReportDefinitionTests.ScheduleDelivery_with_an_identical_schedule_is_a_silent_no_op` |
| Définition compatible avec le catalogue courant (mesures et champs) | `EnsureCompatibleWith` | `ReportDefinitionTests.EnsureCompatibleWith_accepts_a_catalog_that_still_covers_the_definition`, `EnsureCompatibleWith_rejects_a_metric_that_left_the_catalog`, `EnsureCompatibleWith_rejects_a_filter_field_that_left_the_catalog` |
| Pas de planification d'une définition incompatible, état et planning précédents conservés, sans event | `ScheduleDelivery` | `ReportDefinitionTests.ScheduleDelivery_rejects_a_definition_that_no_longer_fits_the_catalog_without_raising`, `ScheduleDelivery_keeps_the_current_schedule_when_the_catalog_no_longer_fits` |
| Déplanification seulement si planifié | `Unschedule` | `ReportDefinitionTests.Unschedule_removes_the_schedule_and_raises_ReportUnscheduled`, `Unschedule_rejects_a_report_that_is_not_scheduled` |
| Planning : au moins un destinataire, borné, fréquence connue | `ReportSchedule.Create` | `ReportValueObjectsTests.Schedule_requires_at_least_one_recipient`, `Schedule_recipient_count_is_bounded`, `Schedule_rejects_an_unknown_frequency` |
| Weekly : jour de semaine requis et connu | `ReportSchedule.Create` | `ReportValueObjectsTests.Weekly_schedule_requires_a_day_of_the_week`, `Weekly_schedule_rejects_an_unknown_day_of_the_week`, `Weekly_schedule_accepts_every_day_of_the_week` |
| Monthly : jour du mois requis, 1..28 | `ReportSchedule.Create` | `ReportValueObjectsTests.Monthly_schedule_requires_a_day_of_the_month`, `Monthly_schedule_rejects_a_day_outside_1_to_28`, `Monthly_schedule_accepts_days_1_to_28` |
| Ancre interdite pour une fréquence qui ne l'utilise pas | `ReportSchedule.Create` | `ReportValueObjectsTests.Schedule_rejects_an_anchor_its_frequency_does_not_use`, `Daily_schedule_has_no_anchor` |
| Heure locale + fuseau explicite (pas de dérive au changement d'heure) | `ReportSchedule.Create` | `ReportValueObjectsTests.Schedule_keeps_frequency_anchor_local_time_zone_and_distinct_recipients`, `Schedule_time_of_day_is_local_so_it_does_not_drift_with_daylight_saving`, `Schedule_requires_a_time_zone`, `Schedule_equality_is_by_value` |
| Changer d'ancre ou de fuseau = replanification | `ScheduleDelivery` | `ReportDefinitionTests.ScheduleDelivery_to_another_anchor_or_time_zone_is_a_real_change` |
| `ReportDefinitionReference` non forgeable | `ReportDefinition.ToReference` | `ReportDefinitionTests.ToReference_carries_the_definition_tenant_and_id_and_cannot_be_forged` |
| Définition exportée du même tenant | `ExportJob.Queue` | `ExportJobTests.Queue_rejects_a_report_definition_of_another_tenant` |
| Référence de fichier : tenant non vide | `ExportedFileReference` | `ExportJobTests.ExportedFileReference_rejects_an_empty_tenant` |
| Horloge lue une fois (état = événement) | `ScheduleDelivery`, `Unschedule`, `Queue`, `Complete`, `Fail`, `Expire` | `ReportDefinitionTests.ScheduleDelivery_and_Unschedule_read_the_clock_once`, `ExportJobTests.Each_operation_reads_the_clock_once_so_state_and_event_carry_the_same_instant` |
| Export demandé -> `Queued` + `ExportRequested` | `ExportJob.Queue` | `ExportJobTests.Queue_creates_a_queued_job_and_raises_ExportRequested` |
| Demandeur obligatoire, définition exportée optionnelle | `ExportJob.Queue` | `ExportJobTests.Queue_rejects_a_default_requester`, `Queue_records_the_exported_report_definition` |
| `Queued -> Running` uniquement | `Start` | `ExportJobTests.Start_moves_a_queued_job_to_running`, `Start_rejects_a_job_that_is_already_running_or_completed` |
| **Completed nécessite un fichier** (y compris `default(FileId)`) | `Complete` | `ExportJobTests.Complete_without_a_file_is_rejected` |
| **Le fichier appartient au tenant du job** | `Complete` | `ExportJobTests.Complete_rejects_a_file_of_another_tenant` |
| `Completed` atteignable seulement via `Complete` depuis `Running` | `Complete` | `ExportJobTests.Complete_is_rejected_before_the_job_is_started`, `A_completed_job_always_has_a_file_whatever_the_path` |
| Le fichier d'un export terminé n'est pas remplaçable | `Complete` | `ExportJobTests.Complete_cannot_replace_the_file_of_a_completed_job` |
| Complétion -> `ExportCompleted` (avec `RequestedBy`) | `Complete` | `ExportJobTests.Complete_attaches_the_file_and_raises_ExportCompleted` |
| Échec : depuis `Running`, raison requise et bornée | `Fail` | `ExportJobTests.Fail_records_the_reason_and_raises_ExportFailed`, `Fail_requires_a_reason`, `Fail_rejects_a_reason_over_the_limit`, `Fail_is_rejected_when_the_job_is_not_running` |
| `Completed -> Expired` uniquement | `Expire` | `ExportJobTests.Expire_makes_a_completed_job_no_longer_downloadable_and_raises_ExportExpired`, `Expire_is_rejected_when_the_job_is_not_completed` |
| `Failed` terminal | toutes | `ExportJobTests.Failed_is_terminal` |
| `Expired` terminal | toutes | `ExportJobTests.Expired_is_terminal` |
| Aucun setter public | `ReportDefinition` | `ReportDefinitionTests.Aggregate_exposes_no_public_setter` |

## 5. Ce qui relève de l'Application / Infrastructure

- Construction du `ReportCatalog` (par type de rapport / plan) et exécution des requêtes sur les projections, valeurs de filtres
  **toujours paramétrées**.
- Fournir le catalogue **courant** à `ScheduleDelivery` ; appeler `EnsureCompatibleWith(catalogueCourant)` avant chaque
  exécution d'une définition sauvegardée (génération planifiée, export) et traiter `catalog_incompatible` (prévenir, déplanifier).
- Planificateur (réagit à `ReportScheduled` / `ReportUnscheduled`), envoi aux destinataires. **Fuseau** : l'Application
  passe le `TimeZoneId` du tenant à `ReportSchedule.Create` ; le planificateur calcule chaque occurrence en heure
  **locale** de ce fuseau (ancre + `LocalTimeOfDay`) puis la convertit en UTC, en traitant lui-même les heures locales
  inexistantes ou doublées les jours de changement d'heure (le domaine ne calcule pas d'occurrence). Un changement de
  fuseau du tenant ne modifie pas les plannings existants : les replanifier si c'est voulu.
- Migration : les plannings déjà persistés (heure UTC, sans ancre ni fuseau) doivent recevoir un fuseau et, pour
  Weekly/Monthly, une ancre.
- `Queue` : `RequestedBy` vient de l'utilisateur authentifié (jamais du corps de la requête) ; si une définition est
  visée, la charger sous isolation tenant et passer `definition.ToReference()` (seule façon d'obtenir la référence ; le
  domaine vérifie alors lui-même le tenant).
- Worker d'export : `Start`, production du fichier via le module Files, puis `Complete(new ExportedFileReference(...))` ou
  `Fail(reason)`. L'`ExportedFileReference` est construit à partir du **fichier stocké** par Files (son `TenantId` et son
  `Id`, relus après enregistrement), jamais à partir d'une saisie utilisateur ni du tenant du job lui-même. L'appartenance au
  tenant est désormais garantie par le domaine ; l'**existence** du fichier reste à la charge de l'Application.
- Notification du demandeur sur `ExportCompleted.RequestedBy`. Politique d'expiration (durée) et job qui appelle `Expire`.
- Concurrence (deux workers sur le même job) : verrouillage optimiste ; le second `Start` est rejeté par `not_queued`.
- Quotas d'export, droits d'accès aux rapports.

## 6. Persistance (étape 1)

Schéma `reporting`, historique des migrations dans le même schéma. Toutes les tables portent un `tenant_id`
obligatoire ; celles d'un restaurant sont filtrées sur le tenant courant (fail-closed). Les tables `platform_*` ne sont
lues que par les routes d'administration.

| Table | Contenu | Gardes en base |
|---|---|---|
| `report_definitions` | Agrégat `ReportDefinition` ; mesures, filtres et planning en `jsonb`, relus par les fabriques du domaine | Version `xmin` |
| `export_jobs` | Agrégat `ExportJob` | CHECK statut, fichier présent si terminé ou expiré, raison si en échec ; version `xmin` (deux workers) |
| `export_requests` | Paramètres figés d'un export (période, mesures, filtres, destinataires) en `jsonb` | FK vers le job (Cascade) |
| `report_deliveries` | Prochaine exécution d'un rapport planifié, et qui l'a planifié | FK vers la définition (Cascade) ; version `xmin` |
| `daily_metrics` | Compteurs additifs du jour : (tenant, établissement, jour local, mesure, dimension, devise) -> valeur `numeric(20,4)` | Clé primaire sur les six colonnes ; CHECK devise |
| `processed_sources` | Messages de l'outbox déjà comptés | Clé primaire `source_key` |
| `export_files` | Fichiers d'export (`bytea`), en attendant le module Files | — |
| `platform_tenants`, `platform_subscriptions` | État courant de chaque restaurant et de son abonnement | CHECK statuts, devise, montant >= 0 |

Choix :

- **Compteurs par upsert SQL** (`INSERT ... ON CONFLICT DO UPDATE SET value = value + EXCLUDED.value`) : incrément
  atomique, sans lecture préalable ni version. La source est inscrite dans la même transaction : un message rejoué
  n'incrémente rien. Toutes les valeurs passent en paramètres ; seuls les noms de tables sont dans le texte SQL.
- **Jour LOCAL du restaurant** : un fait est compté au jour de son fuseau (Tenants), UTC s'il est inconnu.
- **Vue de la plateforme** : upsert de l'état COURANT relu dans Tenants et Billing ; un instantané plus ancien que le
  stocké ne remplace rien.
- **Fichiers en base** : `IExportFileStore` (en base par défaut, dans la transaction du job) sera remplacé par le
  module Files sans toucher aux cas d'usage.

Migration à générer :

```bash
dotnet ef migrations add InitialCreate --project src/Modules/Reporting/Reporting.Infrastructure --output-dir Persistence/Migrations
```

## 7. Application, intégration et API (étapes 2 à 6)

### Catalogue des mesures (`ReportingMetrics`, liste blanche passée au domaine)

| Source | Mesures stockées | Dimension | Mesures dérivées |
|---|---|---|---|
| Orders | `sales.orders_placed`, `placed_amount`, `discount_amount`, `tax_amount`, `orders_completed`, `revenue`, `orders_cancelled`, `cancelled_amount` | type de commande | `sales.average_ticket`, `sales.cancellation_rate` |
| Payments | `payments.captured_count`, `captured_amount`, `failed_count`, `refund_count`, `refunded_amount` | moyen de paiement | `payments.refund_rate` |
| Reservations | `reservations.created`, `covers_booked`, `seated`, `covers_seated`, `cancelled`, `no_shows` | auteur de l'annulation | `reservations.no_show_rate` |
| Kitchen | `kitchen.tickets_created`, `tickets_ready`, `tickets_cancelled`, `preparation_seconds` | — | `kitchen.average_preparation_minutes` |
| Inventory | `inventory.receipts`, `waste_records`, `adjustments`, `low_stock_alerts`, `negative_stock_alerts` (comptages) | — | — |
| Promotions | `promotions.applied`, `discount_amount`, `reverted`, `coupons_redeemed` | — | — |
| Loyalty | `loyalty.accounts_opened`, `points_earned`, `points_redeemed`, `points_expired`, `rewards_redeemed`, `reward_discount_amount` | — | — |

Montants toujours ventilés par devise. Une mesure dérivée est calculée APRÈS agrégation de la période (un panier
moyen mensuel n'est pas la moyenne des paniers du jour), par période, établissement et devise ; elle est omise si son
dénominateur est nul. Filtres : `location.id`, `currency`, `dimension` ; opérateurs égal, différent, dans une liste.
Granularité : jour, semaine ISO, mois, total (périodes bornées à la demande, 366 jours au plus).

### Cas d'usage

| Cas d'usage | Déclencheur | Règles appliquées ici |
|---|---|---|
| `RecordReportingFacts` | Hôte (`ReportingFeed`, outbox) | Tenant du contexte ; mesures stockées seulement ; idempotent par message |
| `RecordTenantSnapshot`, `RecordSubscriptionSnapshot` | Hôte (`ReportingPlatformFeed`) | Prix annuel ramené au mois |
| `RunReport`, `RunSavedReport`, `GetDashboard` | Personnel (`reporting.view`) | Possible même suspendu |
| `CreateReportDefinition`, `Rename`, `ChangeReportFilters` | Personnel (`reporting.manage`) | Restaurant accessible en écriture |
| `ScheduleReport`, `UnscheduleReport` | Personnel (`reporting.manage`) | Heure locale `HH:mm` dans le fuseau du restaurant ; l'export est fait au nom de la personne qui planifie |
| `RequestExport` | Personnel (`reporting.export`) | D'un rapport enregistré ou ponctuel ; possible même suspendu (le restaurant garde l'accès à ses chiffres) |
| `RunScheduledReport`, `GenerateExport`, `ExpireExport` | Job `ReportingWorker` | Envoi : veille, 7 jours ou mois précédent ; restaurant suspendu = envoi sauté ; rapport hors catalogue = déplanifié. Export : passage « en cours » enregistré d'abord (un seul worker) ; erreur métier = échec avec son code. Expiration après `Reporting:ExportRetention` (7 j) et suppression du fichier |

### Intégration (hôte)

- `ReportingFeed` : événements d'Orders, Payments, Reservations, Kitchen, Inventory, Promotions et Loyalty -> faits.
  Établissement et moyen d'un paiement relus dans Payments ; durée de préparation = prêt - création du ticket (relu
  dans Kitchen). Aucune donnée personnelle n'est copiée.
- `ReportingPlatformFeed` : tout événement `Tenant*` ou `Subscription*` -> état courant relu et recopié.
- `ReportingDeliveryFlow` : `reporting.ExportCompleted` -> Notifications, nouveau type `staff.report_ready` : un e-mail
  par destinataire d'un envoi planifié, sinon une notification dans l'application au demandeur. Le lien pointe vers la
  page « Exports récents » de la console (`{Reporting:PublicBaseUrl}/rapports/planifies`, adresse de la console) :
  le téléchargement s'y fait connecté, le fichier ne voyage jamais dans un message.
  Templates de la plateforme (fr, en ; e-mail et in-app) posés au démarrage de Notifications.

### Routes

| Route | Accès |
|---|---|
| `GET /reporting/catalog`, `GET /reporting/dashboard?from&to[&locationId]`, `POST /reporting/run`, `GET /reporting/definitions`, `GET /reporting/definitions/{id}`, `POST /reporting/definitions/{id}/run`, `GET /reporting/exports`, `GET /reporting/exports/{id}`, `GET /reporting/exports/{id}/file` | Restaurant, `reporting.view` |
| `POST /reporting/definitions`, `PUT /{id}/name`, `PUT /{id}/filters`, `PUT` et `DELETE /{id}/schedule` | Restaurant, `reporting.manage` |
| `POST /reporting/exports` (202) | Restaurant, `reporting.export` |
| `GET /admin/reporting/dashboard?from&to`, `GET /admin/reporting/activity?from&to&granularity&metrics=a,b`, `GET /admin/reporting/tenants?status&skip&take` | Plateforme (`Policies.PlatformAdmin`) |

Migration appliquée au démarrage (`MigrateReportingDatabaseAsync`) ; job coupable par `Reporting:RunWorkers=false`.

## 8. Tests (étape 7)

| Projet | Contenu |
|---|---|
| `Reporting.Domain.Tests` | Définitions, catalogue, filtres, planning, exports |
| `Reporting.Application.Tests` | `ReportingUseCaseTests` : idempotence et jour local, mesures refusées, dérivées après agrégation, semaines et mois, filtres, opérateurs refusés, ventilation par établissement, tableau de bord, rapports enregistrés, restaurant suspendu, planning (heure locale, utilisateur), envoi dû, envoi sauté, export (CSV, une seule production, téléchargement, expiration), export sans paramètres, injection de formule, isolation, plateforme (MRR annuel / 12), changement d'heure, mensuel |
| `Reporting.Infrastructure.Tests` | `ReportingModelTests` (sans base) ; `ReportingPersistenceTests` : upsert et idempotence, isolation et lecture de la plateforme, filtres en base, JSON relu par le domaine, export + paramètres + fichier + outbox, concurrence de deux workers, envois dus, vue de la plateforme |
| `Reporting.Api.Tests` | Authentification et policy d'administration, permissions par action, tableau de bord et rapport filtré, erreurs, rapport planifié, export CSV, isolation, restaurant suspendu, plateforme |

## 9. Audit (étape 8)

| Point | Application | Vérification |
|---|---|---|
| TenantId toujours issu du contexte | Routes du restaurant sans tenant ; faits comptés pour le tenant du contexte, jamais d'un champ reçu | `ReportingEndpointsTests`, `ReportingUseCaseTests` |
| Lecture croisée impossible | Filtres EF sur toutes les tables d'un restaurant ; 404 sur l'id d'un autre ; lectures de tous les tenants limitées au job et à la plateforme, déclarées | `ReportingPersistenceTests`, `ReportingEndpointsTests` |
| Injection | Mesures et champs en liste blanche ; valeurs de filtre parsées (uuid, ISO) et passées en paramètres ; SQL brut paramétré | `ReportingUseCaseTests`, `ReportingPersistenceTests` |
| Injection de formule (CSV) | Cellule commençant par `=`, `+`, `-`, `@` préfixée d'une apostrophe | `ReportingUseCaseTests` |
| Données personnelles | Aucune dans les compteurs (ids d'établissement, codes, nombres) ; e-mails des destinataires uniquement dans le planning | Revue (`ReportingFeed`) |
| Fichier d'export | Jamais dans une notification : lien vers la route authentifiée ; expiration et suppression | `ReportingUseCaseTests` |
| Idempotence | Une source comptée une fois (clé primaire) ; notifications avec clé d'idempotence | `ReportingPersistenceTests` |
| Concurrence | Version `xmin` sur définition, export, envoi ; deux workers sur un export : le second échoue | `ReportingPersistenceTests` |
| Journal | Ids, compteurs et codes seulement (`ReportingWorker`, flux de l'hôte) | Revue |

Limites connues : les faits sans établissement (promotions, fidélité) ne passent pas un filtre `location.id` ; une
réservation est comptée au jour où elle est prise, pas au jour du repas ; activité de la plateforme additionnée sur
les jours locaux de chaque restaurant ; pas de reconstruction des compteurs depuis l'historique (les événements
antérieurs au déploiement ne sont pas comptés) ; fichiers en base jusqu'au module Files ; quotas Notifications du
plan appliqués aux annonces de rapport.
