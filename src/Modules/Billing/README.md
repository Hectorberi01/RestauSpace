# Billing — Domain (SaaS Billing & Entitlements)

But : gérer ce que le restaurateur paie au SaaS et les fonctionnalités auxquelles il a droit.
Sans rapport avec les paiements des commandes (module Payments).

## 1. Frontières d'agrégat

- **SaasSubscription** (racine, tenant-owned) : statut, plan courant, période courante, annulation programmée.
  Elle référence le plan par `PlanId` et reçoit un `PlanReference` (id, actif) quand elle doit le vérifier.
  `PlanReference` a un **constructeur `internal`** : seul `Plan.ToReference()` la produit, on ne peut pas déclarer
  actif un plan archivé.
- **Plan** (racine, **global plateforme** : pas de `TenantId`, pas `ITenantOwned`) : l'offre commerciale et ses
  `Entitlement` (VO, un par `FeatureCode`). Modifier un plan ne doit charger aucun abonnement.
- **UsageCounter** (racine, tenant-owned) : consommation d'une fonctionnalité sur la période courante. Séparé de
  l'abonnement car il est écrit à très haute fréquence ; il reçoit l'`Entitlement` applicable en paramètre.
- Rien n'est partagé avec d'autres modules hormis `TenantId`, `Money`, `Period`.

## 2. Classes

| Classe | Rôle DDD | Responsabilité | Transitions | Events | Erreurs (`billing.…`) |
|---|---|---|---|---|---|
| `SaasSubscription` | AggregateRoot, tenant-owned | Cycle de vie de l'abonnement | `(StartTrial) -> Trialing` ; `Trialing/PastDue/Suspended -> Active` (Activate) ; `Active -> PastDue` (MarkPastDue) ; `Trialing/Active/PastDue -> Suspended` (Suspend, avec `SuspensionReason`) ; `Active -> Active` (RenewPeriod, période courante terminée) ; `tout état vivant -> Cancelled` (RenewPeriod avec annulation programmée, période courante terminée) ; `tout sauf Cancelled -> Cancelled` (Cancel) ; `Cancelled` terminal | `SubscriptionTrialStarted`, `SubscriptionActivated`, `SubscriptionPlanChanged`, `SubscriptionPlanChangeScheduled`, `SubscriptionPlanChangeUnscheduled`, `SubscriptionPastDue`, `SubscriptionSuspended`, `SubscriptionCancellationScheduled`, `SubscriptionCancellationUnscheduled`, `SubscriptionPeriodRenewed`, `SubscriptionCancelled` | `subscription.already_subscribed`, `plan_archived`, `plan_change_not_allowed`, `cancelled`, `cannot_activate`, `not_active`, `cannot_suspend`, `suspension_reason_invalid`, `trial_not_expired`, `period_ended`, `period_not_consistent`, `period_not_ended`, `period_not_contiguous`, `cancellation_scheduled`, `cancellation_already_scheduled`, `cancellation_not_scheduled`, `plan_change_scheduled`, `plan_change_not_scheduled` |
| `SuspensionReason` | Enum | `TrialExpired`, `PaymentOverdue`, `PolicyViolation`, `Other` ; stockée sur l'abonnement et portée par `SubscriptionSuspended` | — | — | — |
| `Plan` | AggregateRoot global | Prix catalogue, intervalle, droits | `Active -> Archived` (Archive) ; `Archived` terminal | `PlanCreated`, `PlanDefinitionChanged`, `PlanArchived` | `plan.name_required`, `name_too_long`, `price_negative`, `price_currency_mismatch`, `billing_interval_invalid`, `duplicate_entitlement`, `archived` |
| `Entitlement` | ValueObject | Droit/quota d'une fonctionnalité : `Enabled`, `Limit?` (null = illimité) | — | — | `entitlement.limit_negative`, `entitlement.disabled_with_limit` |
| `FeatureCode` | ValueObject | Clé normalisée (minuscules, 2–64, `a-z 0-9 . _ -`, commence par une lettre) | — | — | `feature_code.required`, `feature_code.invalid` |
| `PlanReference` | Snapshot (record, ctor `internal`, propriétés en lecture seule) | Id + actif, pour l'abonnement ; produite uniquement par `Plan.ToReference()` | — | — | — |
| `UsageCounter` | AggregateRoot, tenant-owned | Compter et borner l'usage | `Increment` ; `ResetForNewPeriod` (Used -> 0) | `QuotaReached`, `UsageCounterReset` | `usage_counter.already_exists`, `amount_not_positive`, `feature_mismatch`, `outside_period`, `overflow`, `feature_disabled`, `quota_exceeded`, `period_not_after_current`, `period_not_ended` |
| `ILiveSubscriptionChecker`, `IUsageCounterUniquenessChecker` | Ports | Unicités inter-agrégats | — | — | — |

**Les deux « PlanChanged » de la spec** : `SubscriptionPlanChanged` = un abonnement passe d'un plan à un autre ;
`PlanDefinitionChanged` = l'offre elle-même change (prix ou droit, cf. `PlanDefinitionChange`).

## 3. Choix faits là où la spec était ouverte

- **Cancelled terminal « selon policy »** : la policy retenue est explicite et sans exception — un abonnement
  annulé ne se réactive jamais ; revenir = `StartTrial` d'un **nouvel** abonnement. Toute méthode sur un
  abonnement annulé lève `subscription.cancelled`.
- **Un seul abonnement vivant (non annulé) par tenant** : booléen `tenantHasNoLiveSubscription` fourni par le port.
- **Activate** prend la période payée. Cohérence (`period_not_consistent`, sinon `period_ended` si elle est déjà
  finie) : elle ne commence pas avant la période courante, et **soit elle contient l'instant courant, soit elle
  commence exactement à la fin de la période courante quand celle-ci court encore** (paiement anticipé de la
  période qui suit l'essai). Un abonnement `Active` a donc toujours une période courante qui contient maintenant
  ou qui prolonge sans trou celle qui court. Conséquences : convertir un essai « au milieu » se fait avec une
  période commençant maintenant ; après une suspension (période courante terminée) la période payée doit
  contenir maintenant — elle ne peut pas commencer dans le futur.
- **Annulation programmée respectée** : tant que `CancelAtPeriodEnd` est posé, `Activate` avec une période qui
  dépasse la fin de la période courante est rejeté (`cancellation_scheduled`) ; l'appelant doit d'abord
  `UnscheduleCancellation` (ou `Cancel`). Une réactivation à l'intérieur de la période courante (impayé régularisé)
  reste permise et conserve le drapeau.
- **RenewPeriod** n'est appelable **qu'une fois la période courante terminée** (`period_not_ended` sinon : pas de
  renouvellements enchaînés à l'avance). Branche normale : exige `Active` et une période commençant exactement à
  la fin de la courante. Si `CancelAtPeriodEnd` est posé, le renouvellement **annule** l'abonnement
  (`SubscriptionCancelled`, raison `AtPeriodEnd`) au lieu de renouveler, **quel que soit le statut vivant**
  (`Active`, `PastDue`, `Suspended`, et aussi `Trialing`) : une annulation programmée ne peut pas être perdue
  parce que l'abonnement est passé en impayé entre-temps ; la période courante est conservée et la période
  proposée est ignorée. `Cancel` direct porte la raison `Requested`.
- **ScheduleCancellation** est réversible par `UnscheduleCancellation` tant que l'abonnement n'est pas annulé ;
  reprogrammer deux fois ou déprogrammer sans programmation est rejeté.
- **ChangePlan** : permis en `Trialing`, `Active` et après un essai expiré (un impayé se règle d'abord) ; plan
  archivé rejeté ; **même plan = no-op silencieux**. Essai et essai expiré : immédiat. Période payée (`Active` hors
  offre) : **programmé à l'échéance** (`ScheduledPlanId`, date d'effet = fin de période, pas de prorata) ; le plan
  courant et ses droits restent jusque-là. Rechoisir le plan courant ou `UnschedulePlanChange` retire le changement ;
  un autre plan le remplace. Le plan programmé devient le plan courant au début de la période suivante
  (`RenewPeriod`, `Activate`, ou `ApplyScheduledPlan` par le job d'échéance), avant le calcul du montant : la
  période suivante est facturée au nouveau plan, options comprises. Changement programmé et résiliation programmée
  s'excluent (`plan_change_scheduled` / `cancellation_scheduled`). Une offre de la plateforme retire le changement
  programmé. La plateforme n'a pas de route de changement de plan : elle offre un plan (immédiat).
- **Suspend(reason)** : depuis `Trialing`, `Active` ou `PastDue`, avec une `SuspensionReason` explicite stockée
  (`SaasSubscription.SuspensionReason`, remise à `null` par `Activate`) et portée par `SubscriptionSuspended`
  (avec le statut précédent). **Fin d'essai sans perdre l'abonnement** : un essai non converti est suspendu avec
  `TrialExpired` — raison réservée à un abonnement `Trialing` dont la période d'essai est terminée
  (`trial_not_expired` sinon) — puis `Activate(périodePayée)` le ramène en `Active` sans nouvel essai ni nouvel
  abonnement. `Cancel` reste la sortie définitive.
- **Même valeur = no-op silencieux** : `Plan.ChangePriceForFuture` (même prix), `Plan.SetEntitlement` (droit
  identique) et `SaasSubscription.ChangePlan` (même plan) retournent sans mutation ni événement ; les erreurs
  `plan.price_unchanged`, `plan.entitlement_unchanged` et `subscription.same_plan` ont disparu. Les transitions
  répétées (activer un actif, suspendre un suspendu, programmer deux fois, archiver deux fois...) restent rejetées.
- **Plan** : prix >= 0 (plan gratuit permis) ; la devise du plan est fixée à la création.
  **`ChangePriceForFuture` ne modifie que le prix catalogue** : les périodes déjà commencées des abonnements ne
  sont jamais touchées (un abonnement ne stocke pas de prix ; la facture de la période, hors de ce module, garde
  le prix auquel elle a été émise). Le nouveau prix vaut pour les périodes facturées ensuite.
- **Entitlements cohérents** : un par `FeatureCode` ; `SetEntitlement` ajoute ou remplace ; valeur identique =
  no-op ; désactivé => pas de limite ; limite >= 0 ; null = illimité. Pas de suppression : on pose `Disabled`.
- **Droit absent = désactivé** : `Plan.EntitlementFor(feature)` rend le droit stocké ou `Entitlement.Disabled(feature)`.
  `FindEntitlement` (nullable) a été retiré : il n'y a plus qu'une seule réponse possible, et jamais de `null` à
  interpréter côté appelant.
- **Ids vides** : `SaasSubscription.StartTrial` et `UsageCounter.Open` rejettent un `TenantId` `default`
  (`common.id.empty`). Le `PlanId` d'une `PlanReference` vient toujours d'un vrai plan.
- **Élément `null`** dans les droits passés à `Plan.Create` : `ArgumentNullException` (erreur de programmation).
- **Une seule lecture de l'horloge par opération** : l'instant qui juge la règle (période terminée, usage dans la
  période) est celui que portent l'état (`StartedAtUtc`, `CancelledAtUtc`) et l'événement.
- **Plan archivé** : plus aucune modification ; les abonnements existants le gardent, aucun nouveau ne peut le prendre.
- **UsageCounter — identité** : id typé `UsageCounterId` ; clé métier `(TenantId, FeatureCode)` avec **une période
  courante** qui avance par `ResetForNewPeriod`. La période ne fait donc pas partie de l'identité (sinon le reset
  changerait l'identité de l'agrégat) ; l'historique par période est porté par `UsageCounterReset`
  (période et consommation précédentes).
- **Increment(amount, entitlement, blocking, clock)** : `blocking: true` applique le droit (fonctionnalité
  désactivée ou limite dépassée => rejet, rien ne change) ; `blocking: false` ne fait que mesurer (quota souple)
  et peut dépasser. Refusé hors de la période du compteur (il faut d'abord `ResetForNewPeriod`).
- **QuotaReached** : levé exactement quand `Used` franchit la limite (`avant < limite <= après`), donc une seule
  fois par période et par limite ; jamais pour un droit illimité ni pour une limite 0 (déjà « atteinte » à 0).
- **ResetForNewPeriod** : exige que la période courante soit terminée (`period_not_ended` : on n'efface jamais la
  consommation d'une période en cours) ; la nouvelle période commence à la fin de la courante ou après (trou
  permis, chevauchement refusé).
- **Débordement** : un incrément qui ferait dépasser `long.MaxValue` lève `usage_counter.overflow`
  (`DomainException`), bloquant ou non, et ne change rien.

## 4. Matrice invariant -> méthode -> test

| Invariant | Méthode | Test |
|---|---|---|
| FeatureCode normalisé et contrôlé | `FeatureCode.Create` | `EntitlementTests.FeatureCode_is_trimmed_and_lower_cased`, `FeatureCode_is_required`, `FeatureCode_rejects_invalid_formats`, `FeatureCode_length_is_bounded` |
| Limite >= 0 | `Entitlement.Create` | `EntitlementTests.Limit_cannot_be_negative`, `Limit_of_zero_is_valid_and_allows_nothing` |
| Null = illimité | `Entitlement.Unlimited` | `EntitlementTests.Null_limit_means_unlimited` |
| Désactivé => pas de limite, rien de permis | `Entitlement.Create` | `EntitlementTests.Disabled_entitlement_cannot_carry_a_limit`, `Disabled_entitlement_allows_nothing` |
| Limite inclusive | `Entitlement.Allows` | `EntitlementTests.Allows_is_inclusive_of_the_limit` |
| Plan global, sans tenant | `Plan` | `PlanTests.Plan_is_platform_global_and_not_tenant_owned` |
| Prix >= 0 | `Plan.Create`, `ChangePriceForFuture` | `PlanTests.Create_rejects_a_negative_price`, `Create_accepts_a_free_plan_without_entitlement`, `ChangePriceForFuture_accepts_zero_and_rejects_a_negative_price` |
| Nom requis / borné, intervalle connu | `Plan.Create` | `PlanTests.Create_rejects_a_missing_name`, `Create_rejects_a_too_long_name`, `Create_rejects_an_unknown_billing_interval` |
| Un droit par FeatureCode | `Plan.Create`, `SetEntitlement` | `PlanTests.Create_rejects_two_entitlements_for_the_same_feature`, `SetEntitlement_keeps_one_entitlement_per_feature_whatever_the_sequence`, `SetEntitlement_replaces_the_quota_of_an_existing_feature`, `SetEntitlement_adds_a_new_feature` |
| Droit identique = no-op | `Plan.SetEntitlement` | `PlanTests.SetEntitlement_with_an_identical_entitlement_changes_nothing` |
| Droit absent = désactivé | `Plan.EntitlementFor` | `PlanTests.EntitlementFor_returns_the_stored_entitlement_or_a_disabled_one_when_the_feature_is_missing`, `A_feature_missing_from_the_plan_blocks_usage`, `Create_accepts_a_free_plan_without_entitlement` |
| Droits non modifiables de l'extérieur | `Plan.Entitlements` | `PlanTests.Entitlements_cannot_be_modified_from_outside` |
| Changement de prix : futur seulement | `Plan.ChangePriceForFuture` | `PlanTests.ChangePriceForFuture_updates_the_catalogue_price`, `ChangePriceForFuture_changes_nothing_but_the_catalogue_price` (le plan ne partage aucun état avec un abonnement : le test ne porte que sur l'état et l'événement du plan) |
| Une devise par plan ; même prix = no-op | `Plan.ChangePriceForFuture` | `PlanTests.ChangePriceForFuture_rejects_another_currency`, `ChangePriceForFuture_to_the_same_price_changes_nothing` |
| Plan archivé : plus de changement | `Plan.*` | `PlanTests.Archived_plan_rejects_every_change`, `Archive_withdraws_the_plan` |
| `PlanReference` non forgeable | `Plan.ToReference` | `SaasSubscriptionTests.A_plan_reference_can_only_be_built_by_its_plan`, `PlanTests.Archive_withdraws_the_plan` |
| Tenant non vide | `SaasSubscription.StartTrial`, `UsageCounter.Open` | `SaasSubscriptionTests.StartTrial_rejects_an_empty_tenant`, `UsageCounterTests.Open_rejects_an_empty_tenant` |
| Droit `null` = erreur de programmation | `Plan.Create` | `PlanTests.Create_rejects_a_null_entitlement_as_a_programming_error` |
| Horloge lue une fois (état = événement) | `StartTrial`, `RenewPeriod`, `Increment`, `ResetForNewPeriod` | `SaasSubscriptionTests.StartTrial_stamps_the_state_and_the_event_with_the_same_instant`, `RenewPeriod_with_a_scheduled_cancellation_stamps_the_state_and_the_event_with_the_same_instant`, `UsageCounterTests.Increment_reads_the_clock_once_so_a_boundary_increment_is_judged_and_stamped_at_one_instant`, `ResetForNewPeriod_reads_the_clock_once` |
| Un abonnement vivant par tenant | `SaasSubscription.StartTrial` | `SaasSubscriptionTests.StartTrial_rejects_a_tenant_that_already_has_a_live_subscription` |
| Plan actif requis à la souscription | `SaasSubscription.StartTrial` | `SaasSubscriptionTests.StartTrial_rejects_an_archived_plan` |
| Période d'essai non terminée | `SaasSubscription.StartTrial` | `SaasSubscriptionTests.StartTrial_rejects_a_trial_period_already_over` |
| Activate depuis Trialing/PastDue/Suspended | `SaasSubscription.Activate` | `SaasSubscriptionTests.Activate_converts_a_trial_into_a_paid_period`, `Activate_recovers_a_past_due_subscription_on_the_same_period`, `Activate_recovers_a_suspended_subscription_with_a_new_period`, `Activate_rejects_an_already_active_subscription` |
| Périodes cohérentes à l'activation : contient maintenant, ou suit la période qui court | `SaasSubscription.Activate` | `SaasSubscriptionTests.Activate_rejects_a_period_starting_before_the_current_one`, `Activate_rejects_a_period_already_over`, `Activate_can_convert_a_trial_early`, `Activate_can_be_paid_in_advance_for_the_period_following_the_running_trial`, `Activate_rejects_a_period_that_neither_contains_now_nor_follows_the_running_period`, `Activate_rejects_a_future_period_once_the_current_period_has_ended` |
| Fin d'essai : suspension `TrialExpired` puis réactivation du même abonnement | `Suspend`, `Activate` | `SaasSubscriptionTests.Suspend_ends_an_expired_trial_without_cancelling_the_subscription`, `Suspend_for_trial_expiry_requires_a_trial_that_is_over`, `Activate_brings_back_a_subscription_suspended_at_trial_end_without_a_new_trial`, `Activate_after_a_trial_expiry_suspension_rejects_a_period_starting_after_now` |
| Annulation programmée : pas d'activation au-delà de la période courante | `SaasSubscription.Activate` | `SaasSubscriptionTests.Activate_rejects_a_period_going_beyond_a_scheduled_cancellation`, `Activate_within_the_current_period_keeps_a_scheduled_cancellation` |
| Changement de plan : plan actif ; même plan = no-op | `SaasSubscription.ChangePlan` | `SaasSubscriptionTests.ChangePlan_during_a_paid_period_is_scheduled_for_its_end`, `The_scheduled_plan_becomes_current_when_the_next_period_starts`, `ChangePlan_to_the_current_plan_changes_nothing`, `ChangePlan_rejects_an_archived_plan`, `ChangePlan_is_allowed_during_the_trial` |
| Changement de plan en Trialing/Active seulement | `SaasSubscription.ChangePlan` | `SaasSubscriptionTests.ChangePlan_rejects_a_past_due_or_suspended_subscription` |
| Deux events « PlanChanged » distincts | events | `SaasSubscriptionTests.Events_of_subscription_and_plan_have_distinct_names` |
| PastDue depuis Active seulement | `SaasSubscription.MarkPastDue` | `SaasSubscriptionTests.MarkPastDue_flags_an_active_subscription`, `MarkPastDue_rejects_any_status_but_active` |
| Suspend depuis Trialing/Active/PastDue, raison explicite stockée et publiée | `SaasSubscription.Suspend` | `SaasSubscriptionTests.Suspend_is_allowed_from_trialing_past_due_and_active_and_records_the_reason`, `Suspend_carries_each_reason`, `Suspend_rejects_an_unknown_reason`, `Suspend_rejects_an_already_suspended_subscription_and_keeps_its_reason` |
| Annulation programmée réversible, non rejouable | `ScheduleCancellation`, `UnscheduleCancellation` | `SaasSubscriptionTests.ScheduleCancellation_sets_the_flag_without_changing_the_status`, `ScheduleCancellation_rejects_a_second_scheduling`, `UnscheduleCancellation_clears_the_flag`, `UnscheduleCancellation_rejects_when_nothing_is_scheduled` |
| Cancel depuis tout état vivant | `SaasSubscription.Cancel` | `SaasSubscriptionTests.Cancel_is_allowed_from_every_live_status` |
| Cancelled terminal | toutes | `SaasSubscriptionTests.Cancelled_is_terminal` |
| Renouvellement : Active + période contiguë | `SaasSubscription.RenewPeriod` | `SaasSubscriptionTests.RenewPeriod_moves_to_the_contiguous_next_period`, `RenewPeriod_rejects_a_gap_or_an_overlap`, `RenewPeriod_requires_an_active_subscription` |
| Renouvellement seulement une fois la période courante terminée | `SaasSubscription.RenewPeriod` | `SaasSubscriptionTests.RenewPeriod_rejects_a_renewal_before_the_current_period_has_ended`, `RenewPeriod_cannot_be_chained_in_advance` |
| Renouvellement avec annulation programmée => annulation, à l'échéance, quel que soit le statut vivant | `SaasSubscription.RenewPeriod` | `SaasSubscriptionTests.RenewPeriod_cancels_instead_of_renewing_when_cancellation_is_scheduled`, `RenewPeriod_honours_a_scheduled_cancellation_whatever_the_live_status`, `RenewPeriod_does_not_cancel_before_the_scheduled_period_end` |
| Enchaînement complet | — | `SaasSubscriptionTests.Full_lifecycle_trial_paid_past_due_recovered_renewed_cancelled` |
| Un compteur par (tenant, fonctionnalité) | `UsageCounter.Open` | `UsageCounterTests.Open_rejects_a_second_counter_for_the_same_feature`, `Open_starts_at_zero` |
| Used >= 0 (incrément > 0) | `UsageCounter.Increment` | `UsageCounterTests.Increment_rejects_a_non_positive_amount`, `Increment_adds_the_amount` |
| Droit de la bonne fonctionnalité | `UsageCounter.Increment` | `UsageCounterTests.Increment_rejects_an_entitlement_of_another_feature` |
| Usage dans la période du compteur | `UsageCounter.Increment` | `UsageCounterTests.Increment_rejects_usage_outside_the_period` |
| Action bloquante : jamais au-delà de la limite, rien ne change | `UsageCounter.Increment` | `UsageCounterTests.Blocking_increment_beyond_the_limit_is_rejected_and_changes_nothing`, `Blocking_increments_never_exceed_the_limit`, `Blocking_increment_with_a_zero_limit_is_always_rejected` |
| Concurrence quota (dernière unité) | `UsageCounter.Increment` | `UsageCounterTests.Two_increments_for_the_last_unit_only_the_first_one_wins` |
| Fonctionnalité désactivée bloquée | `UsageCounter.Increment` | `UsageCounterTests.Blocking_increment_is_rejected_when_the_feature_is_disabled` |
| QuotaReached exactement à l'atteinte de la limite | `UsageCounter.Increment` | `UsageCounterTests.Blocking_increment_up_to_the_limit_is_accepted_and_raises_QuotaReached`, `QuotaReached_is_not_raised_below_the_limit`, `Non_blocking_increment_can_exceed_the_limit_and_raises_QuotaReached_once`, `QuotaReached_is_raised_again_after_the_limit_is_raised_and_reached`, `Unlimited_entitlement_never_blocks_nor_raises_QuotaReached` |
| Non bloquant : mesure seulement | `UsageCounter.Increment` | `UsageCounterTests.Non_blocking_increment_meters_a_disabled_feature_without_event` |
| Reset : période à/après la fin de la courante, Used = 0 | `UsageCounter.ResetForNewPeriod` | `UsageCounterTests.ResetForNewPeriod_starts_the_next_period_at_zero`, `ResetForNewPeriod_accepts_a_period_starting_after_a_gap`, `ResetForNewPeriod_rejects_a_period_starting_before_the_end_of_the_current_one` |
| Reset seulement quand la période courante est terminée | `UsageCounter.ResetForNewPeriod` | `UsageCounterTests.ResetForNewPeriod_rejects_a_reset_while_the_current_period_is_still_running` |
| Débordement = erreur métier stable | `UsageCounter.Increment` | `UsageCounterTests.Increment_that_would_overflow_is_rejected_with_a_domain_error` |
| Quota du plan appliqué par le compteur | `Plan.EntitlementFor` + `Increment` | `UsageCounterTests.Counter_enforces_the_quota_granted_by_a_plan` |

## 5. Ce qui relève de l'Application / Infrastructure

- **Unicités** : `ILiveSubscriptionChecker` (un abonnement non annulé par tenant ; index unique filtré sur
  `TenantId` où statut <> Cancelled) et `IUsageCounterUniquenessChecker` (index unique `(TenantId, FeatureCode)`).
  Les booléens passés aux fabriques ne sont qu'un premier filtre ; l'index est la garde finale. L'unicité du nom
  de plan n'est pas demandée par la spec ; si elle est voulue, c'est un index unique.
- **Concurrence quota** : le compteur est l'unité de cohérence ; deux incréments concurrents pour la dernière
  unité sont départagés par le verrouillage optimiste (version) ou un `UPDATE ... WHERE Used + n <= limit`
  atomique. Le second est rejoué sur l'état à jour et rejeté par le domaine.
- **Résolution du droit** : l'Application charge le plan de l'abonnement, prend `Plan.EntitlementFor(feature)`
  (le domaine rend déjà `Disabled` pour une fonctionnalité absente) et le passe à `Increment`, en choisissant `blocking`.
- **Catalogue des `FeatureCode`** : les codes sont **déclarés par l'Application sous forme de constantes**, chacun
  étiqueté d'un des deux types de quota :
  - **quota de consommation** (ex. commandes par mois, SMS envoyés) : mesuré par un `UsageCounter`, remis à zéro
    à chaque période (`ResetForNewPeriod`), appliqué par `Increment(..., blocking)` ;
  - **quota de stock** (ex. nombre de locations, de sièges staff) : **pas de compteur** ; l'Application compte
    l'existant en direct (requête sur le module concerné) et vérifie `plan.EntitlementFor(feature).Allows(count + 1)`
    avant de créer. Rien à remettre à zéro.
- **Horloge de facturation** : webhooks du prestataire de paiement et jobs appellent `Activate`, `MarkPastDue`,
  `Suspend(reason)`, `RenewPeriod`, `ResetForNewPeriod` ; l'idempotence des webhooks se gère à ce niveau.
  `RenewPeriod` et `ResetForNewPeriod` ne s'appellent qu'à l'échéance ou après (le domaine le vérifie). Le job de
  fin d'essai appelle `Suspend(TrialExpired)` sur les abonnements `Trialing` dont la période est terminée ; le job
  d'échéance appelle `RenewPeriod` sur **tous** les abonnements vivants à échéance ayant `CancelAtPeriodEnd`
  (pas seulement les `Active`), sinon l'annulation programmée ne serait jamais appliquée.
- **Correspondance événement Billing -> méthode du module Tenants** (faite par l'Application, les modules ne se
  référencent pas) :

  | Événement Billing | Action sur le Tenant |
  |---|---|
  | `SubscriptionTrialStarted` | aucune transition : le tenant reste en `Trial` |
  | `SubscriptionActivated(PreviousStatus = Trialing)` | `Activate` (première activation) |
  | `SubscriptionActivated(PreviousStatus = Suspended)` | `Reactivate` si le tenant est suspendu ; `Activate` s'il n'avait jamais été activé (essai expiré puis payé) — l'Application choisit d'après le statut du tenant |
  | `SubscriptionActivated(PreviousStatus = PastDue)` | aucune transition (le tenant n'avait pas été suspendu) |
  | `SubscriptionSuspended(Reason)` | `Suspend(reason)` en traduisant `SuspensionReason` vers la raison du module Tenants |
  | `SubscriptionCancelled` | **décision de l'Application / du propriétaire** : `Close` (fermeture du compte) ou `Suspend` (compte gardé en attente d'un nouvel abonnement). Le domaine Billing ne tranche pas. |
  | `SubscriptionPastDue`, `SubscriptionPeriodRenewed`, `SubscriptionPlanChanged`, `SubscriptionCancellation(Un)Scheduled` | aucune transition du tenant |
- **Prix facturé** : la facture/le snapshot de prix d'une période vit côté facturation ; `Plan.Price` n'est que
  le prix catalogue courant. Calcul des périodes (mensuel/annuel) à partir de `BillingInterval` et prorata :
  Application.
- `PlanReference` ne s'obtient que par `Plan.ToReference()` après chargement du plan (constructeur `internal` : le
  domaine le garantit, l'Application ne peut pas la fabriquer).

## 6. Persistance (étape 1)

Schéma `billing`, historique des migrations dans le même schéma. Le **catalogue des plans est global** (pas de
`tenant_id`, pas de filtre) ; abonnements et compteurs portent un `tenant_id` obligatoire et sont filtrés sur le tenant
courant (aucune ligne sans tenant : fail-closed).

| Table | Contenu | Gardes en base |
|---|---|---|
| `plans` | Agrégat `Plan` (prix catalogue `price` + `price_currency`) | CHECK statut, intervalle, prix >= 0, devise ; version `xmin` |
| `plan_entitlements` | Droits du plan (un par fonctionnalité), toujours chargés avec lui | Index unique `(plan_id, feature_code)` ; CHECK limite >= 0, droit désactivé sans limite |
| `subscriptions` | Agrégat `SaasSubscription`, période courante en colonnes `current_period_start_utc` / `_end_utc` | Index unique `(tenant_id)` filtré `status <> Cancelled` (un abonnement vivant) ; CHECK statut, motif, période, `cancelled_at_utc` présent si et seulement si `Cancelled`, motif seulement si suspendu ou résilié ; FK vers le plan (Restrict) ; version `xmin` |
| `usage_counters` | Agrégat `UsageCounter` (consommation du mois civil UTC) | Index unique `(tenant_id, feature_code)` ; CHECK `used >= 0`, période ; version `xmin` |
| `outbox_messages` | Événements à publier | — |

Choix :

- **Un droit modifié réécrit le plan** (unit of work) : la version du plan protège deux modifications simultanées du
  catalogue, même quand seule la ligne du droit change.
- **Erreurs de la base traduites** : second abonnement vivant -> `billing.subscription.already_subscribed` ; second
  compteur (course) -> `billing.concurrent_update` (le cas d'usage est rejouable) ; version périmée ->
  `billing.concurrent_update`.
- **Lectures de la plateforme déclarées** : `PlatformSubscriptions` (back-office et job) et
  `HasNoLiveSubscriptionAsync` (un administrateur n'a pas de tenant dans son jeton) sont les seules requêtes en
  `IgnoreQueryFilters`, et prennent le tenant explicitement quand il y en a un.

Migration à générer :

```bash
dotnet ef migrations add InitialCreate --project src/Modules/Billing/Billing.Infrastructure --output-dir Persistence/Migrations
```

## 7. Application, intégration et API (étapes 2 à 6)

### Cas d'usage

| Cas d'usage | Déclencheur | Règles appliquées ici |
|---|---|---|
| `CreatePlan`, `ChangePlanPrice`, `SetPlanEntitlement`, `ArchivePlan` | Plateforme | Un plan n'est jamais supprimé (archivé) ; prix dans la devise du plan |
| `StartSubscription` | Restaurant (`billing.manage`) | Essai de `Billing:TrialLength` (14 j) ; restaurant accessible en écriture ; un seul abonnement vivant |
| `ChangeSubscriptionPlan`, `UnscheduleSubscriptionCancellation` | Restaurant (`billing.manage`) | Restaurant accessible en écriture |
| `ScheduleSubscriptionCancellation` | Restaurant (`billing.manage`) | Possible même suspendu : un restaurant peut toujours décider de partir |
| `AdminStartSubscription` | Plateforme | Restaurant existant (Tenants) ; essai de 1 à 90 jours |
| `RecordSubscriptionPayment` | Plateforme | Essai en cours : la période payée suit l'essai ; essai fini, impayé, suspendu : à la suite si cela contient maintenant, sinon à partir de maintenant ; période payée échue : renouvellement contigu ; période en cours : `nothing_to_pay` (double envoi sans effet) |
| `AdminMarkSubscriptionPastDue`, `AdminSuspendSubscription`, `AdminCancelSubscription` | Plateforme | — |
| `ProcessDueSubscription` | Job `SubscriptionDueWorker` | Résiliation programmée -> résilié ; essai fini -> suspendu (`TrialExpired`) ; période payée échue -> impayé ; impayé depuis `Billing:PastDueGracePeriod` (7 j) -> suspendu (`PaymentOverdue`). Rejouable |
| `ConsumeFeature` | Hôte, au nom d'un autre module | Compteur ouvert à la première consommation, remis à zéro au mois suivant ; bloquant ou simple mesure |
| `GetFeatureLimit` | Hôte, au nom d'un autre module | Droit du plan de l'abonnement vivant ; sans abonnement vivant ou fonctionnalité absente du plan : DÉSACTIVÉ |

Pas de prestataire de paiement SaaS à ce stade : la plateforme enregistre les paiements. Un futur webhook appellera
`RecordSubscriptionPayment` (même idempotence).

### Saga Billing -> Tenants (par l'outbox, `BillingFlow` dans l'hôte)

| Événement Billing | Statut du restaurant | Action |
|---|---|---|
| `SubscriptionTrialStarted` | Suspendu (ancien abonnement résilié) | `Reactivate` |
| `SubscriptionActivated` | Essai | `Activate` |
| `SubscriptionActivated` | Suspendu | `Reactivate` |
| `SubscriptionSuspended(reason)` | Pas suspendu | `Suspend` (TrialExpired, PaymentOverdue, PolicyViolation, sinon Other) |
| `SubscriptionCancelled(reason)` | Pas suspendu | `Suspend` (programmée -> `RequestedByTenant`, immédiate -> `Other`) ; jamais `Close`, qui reste une décision de la plateforme |

Le statut du restaurant est lu d'abord : un rejeu ne change rien ; un restaurant fermé n'est jamais modifié. Écart
avec le §5 : `SubscriptionTrialStarted` réactive un restaurant suspendu (seule la plateforme peut ouvrir un abonnement
à un restaurant suspendu, et c'est alors pour le rouvrir).

### Quotas des notifications

L'hôte remplace le quota par défaut de Notifications (`INotificationEntitlementChecker`, enregistré après le module) :
la limite mensuelle d'un canal est le droit `notifications.{email|sms|whatsapp|push|inapp}.monthly` du plan.
Désactivé -> 0, activé sans limite -> illimité. **Conséquence** : un restaurant sans abonnement vivant, ou dont le plan
ne cite pas un canal, n'envoie plus de notification soumise à quota sur ce canal. Les plans doivent donc lister les
canaux vendus.

### Routes

| Route | Accès |
|---|---|
| `GET /billing/plans`, `GET /billing/subscription` | Restaurant, `billing.view` |
| `POST /billing/subscription`, `PUT /billing/subscription/plan`, `DELETE /billing/subscription/scheduled-plan`, `POST` et `DELETE /billing/subscription/cancellation` | Restaurant, `billing.manage` |
| `GET` et `POST /admin/billing/plans`, `GET /admin/billing/plans/{id}`, `PUT /{id}/price`, `PUT /{id}/entitlements`, `POST /{id}/archive` | Plateforme (`Policies.PlatformAdmin`) |
| `POST /admin/billing/tenants/{tenantId}/subscription`, `GET /admin/billing/subscriptions[?tenantId,status,skip,take]`, `GET /{id}`, `POST /{id}/payments`, `/past-due`, `/suspend`, `/cancel` | Plateforme |

Les routes du restaurant ne prennent ni tenant ni id d'abonnement : tout vient du jeton. La seule route où un tenant
vient de la requête est celle de la plateforme qui ouvre l'abonnement d'un restaurant. Migration appliquée au
démarrage (`MigrateBillingDatabaseAsync`) ; job coupable par `Billing:RunWorkers=false`.

## 8. Tests (étape 7)

| Projet | Contenu |
|---|---|
| `Billing.Domain.Tests` | Plans, droits, abonnement, compteurs |
| `Billing.Application.Tests` | `BillingUseCaseTests` : catalogue, essai unique, plan archivé, changement de plan, restaurant suspendu, résiliation programmée et retirée, paiements (essai, renouvellement, après suspension), job d'échéance (essai, impayé, délai de grâce, résiliation, tenant du job), plateforme, consommation et remise à zéro mensuelle, droits désactivés, multi-tenant |
| `Billing.Infrastructure.Tests` | `BillingModelTests` (sans base) ; `BillingPersistenceTests` : plan et droits, remplacement d'un droit et version, isolation, abonnement vivant unique, lectures de la plateforme, liste des échéances, concurrence, compteur unique, outbox |
| `Billing.Api.Tests` | Authentification et policy d'administration, permissions, parcours du restaurant, catalogue, erreurs, parcours de la plateforme, job d'échéance, isolation, restaurant suspendu |

## 9. Audit (étape 8)

| Point | Application | Vérification |
|---|---|---|
| TenantId toujours issu du contexte | Routes du restaurant sans tenant ni id ; `RequireTenant()` | `BillingEndpointsTests`, `BillingUseCaseTests` |
| Restaurant qui désigne un autre tenant | Impossible : seule une route de la plateforme reçoit un tenant | `BillingEndpointsTests` |
| Lecture croisée impossible | Filtres EF sur abonnements et compteurs ; lectures sans filtre limitées à la plateforme, déclarées | `BillingPersistenceTests` |
| Un abonnement vivant | Port + index unique filtré | `BillingPersistenceTests`, `BillingUseCaseTests` |
| Concurrence | Version `xmin` sur plan, abonnement, compteur | `BillingPersistenceTests` |
| Quota non contournable | Consommation bloquante dans le domaine ; sans abonnement = désactivé | `BillingUseCaseTests` |
| Secrets, données de carte | Aucun : pas de prestataire de paiement, aucune donnée de carte stockée | — |
| Journal | Ids, statuts et codes seulement (`BillingFlow`, `SubscriptionDueWorker`) | Revue |

Limites connues : pas de prestataire de paiement ni de factures (montant facturé non historisé : `Plan.Price` est le
prix courant) ; pas de prorata au changement de plan (programmé à l'échéance pendant une période payée) ; quotas de stock (établissements, personnel) déclarés mais pas
encore appliqués par Locations et Staff ; `orders.monthly` non encore consommé par Orders.

## 10. Historique des paiements d'abonnement

- Agrégat `SubscriptionPayment` (table `subscription_payments`, par tenant) : créé par `RecordSubscriptionPaymentHandler`
  dans la même transaction que l'activation ou le renouvellement de l'abonnement. Il recopie le nom et le prix du plan
  au moment du paiement (un changement de prix ne réécrit pas l'historique) et la période payée. Immuable ; événement
  `SubscriptionPaymentRecorded`. Ce n'est pas une facture (aucune numérotation légale).
- Un paiement refusé (`billing.subscription.nothing_to_pay`) n'écrit rien.
- Routes : `GET /admin/billing/payments[?tenantId,subscriptionId,from,to,page,pageSize]` (plateforme, tous tenants,
  `from` inclus et `to` exclu sur la date d'enregistrement) ; `GET /billing/payments[?page,pageSize]` (restaurant du
  contexte, permission `billing.view`).
- Migration à générer après ce changement :
  `dotnet ef migrations add SubscriptionPayments --project src/Modules/Billing/Billing.Infrastructure --output-dir Persistence/Migrations`

## 11. Plafonds appliqués par les autres modules et devise des plans

| Droit | Module | Où | Compté |
|---|---|---|---|
| `orders.monthly` | Orders (`IOrderPlanLimits`) | passage d'une commande (`PlaceOrderHandler`), pas le brouillon | commandes passées dans le mois civil UTC, annulées comprises |
| `locations.count` | Locations (`ILocationPlanLimits`) | création d'un établissement | établissements non archivés |
| `staff.count` | Staff (`IStaffPlanLimits`) | invitation par le restaurant | membres invités, actifs ou suspendus (pas ceux partis) |

- Servis par l'hôte (`PlanLimitsFromBilling`) depuis `GetFeatureLimitQuery` : droit désactivé = 0, activé sans limite =
  aucun plafond.
- Restaurant SANS abonnement vivant (`FeatureLimitDto.HasLiveSubscription` faux) : aucun plafond. La création du
  restaurant et de son propriétaire par la plateforme précède l'abonnement ; un abonnement résilié suspend le
  restaurant (saga Billing -> Tenants), ce qui bloque ses écritures.
- Action de la plateforme (tenant du contexte différent) : aucun plafond ; l'intégration du propriétaire n'est jamais
  bloquée.
- Contrôle puis écriture, sans verrou : deux opérations simultanées peuvent dépasser le plafond d'une unité.
- Les quotas de notifications restent appliqués par Notifications (fail-closed : sans abonnement, rien n'est envoyé).
- Erreurs : `orders.plan.monthly_limit_reached`, `locations.plan.limit_reached`, `staff.plan.limit_reached`.

Devise : un plan n'est vendu qu'aux restaurants de sa devise (devise par défaut du tenant, `IBillingTenants`).
`GET /billing/plans` ne renvoie que les plans actifs de la devise du restaurant ; démarrer un abonnement ou changer
de plan dans une autre devise est refusé (`billing.plan.currency_mismatch`, plateforme comprise).
`GET /admin/billing/plans?currency=XOF` filtre le catalogue de la plateforme.

## 12. Paiement en ligne par devise, plans offerts et indicateurs

**Prestataire par devise.** Les abonnements sont encaissés sur le compte de la plateforme, chez le prestataire choisi
pour la devise du plan (table `online_payment_routes`, une ligne par devise) ; sans choix, `Billing:OnlinePayment:Provider`
s'applique. Le choix n'est accepté que si le prestataire est prêt (inscrit, actif, en ligne, devise acceptée,
intégration présente, identifiants de la plateforme rangés : `CheckPlatformCollectionQuery` de Payments, sans appel au
prestataire). Les identifiants restent au coffre de Payments ; Billing ne garde que le code du prestataire.

**Plan offert.** `SaasSubscription.GrantComplimentary(plan, until?)` : la plateforme rend l'abonnement actif sans
paiement jusqu'à une date, ou sans fin (`UnlimitedEndUtc`, 31/12/9999). Depuis tout statut vivant (impayé et suspendu
compris : le restaurant est réactivé par la saga habituelle) ; une résiliation programmée est retirée ; une nouvelle offre
remplace la précédente. Un restaurant sans abonnement en reçoit un.

| Règle | Où |
|---|---|
| Rien à payer pendant l'offre (paiement manuel ou en ligne : `nothing_to_pay`) | `SubscriptionDues.NextPaidPeriod` (période en cours) |
| Le restaurant ne change pas de plan offert | `ChangePlan` -> `billing.subscription.complimentary` |
| Offre sans fin : ni résiliation ni option par le restaurant | `ScheduleCancellation`, `SubscribeAddOnHandler` |
| Fin d'une offre limitée : impayé, délai de grâce, suspension | job d'échéance (`MarkPastDue` efface l'offre) |
| Fin anticipée par la plateforme | `EndComplimentary` : la période s'arrête maintenant |
| MRR : une offre compte 0 | `GetBillingOverviewQuery`, projection Reporting |

**Préavis.** Le job prévient une fois par échéance, dans les 7 jours qui précèdent la fin d'un essai ou d'une offre
limitée (`NoticeEnding`, colonne `ending_notice_sent_for_utc`, événement `SubscriptionEndingSoon`). L'hôte
(`SubscriptionNoticesFlow`) envoie `staff.subscription_ending` dans l'application et par email aux membres actifs qui ont
`billing.manage` : plan, date, lien vers la page Abonnement ; aucun montant.

**Indicateurs.** `GET /admin/billing/overview` : MRR et revenu moyen par devise (actifs et impayés hors offres, plan et
options ramenés au mois), statuts, offres, résiliations du mois, conversion des essais sur 90 jours, encaissements des
six derniers mois, à traiter (échéances des 7 jours, impayés avec leur date de suspension), règlements en ligne refusés ou
expirés sur 30 jours.

| Route | Rôle |
|---|---|
| `POST /admin/billing/tenants/{tenantId}/complimentary` `{ planId, untilUtc? }` | offrir un plan (null = sans fin) |
| `POST /admin/billing/subscriptions/{id}/complimentary/end` | mettre fin à l'offre |
| `GET /admin/billing/online-payment` · `PUT /admin/billing/online-payment/{currency}` `{ provider }` | prestataire par devise |
| `GET /admin/billing/overview` | tableau de bord |

Migration : `scripts/migrations-abonnements.sh` (`ComplimentaryAndPaymentRoutes`).

## 13. Inscription en libre-service et plans gratuits

- **Inscription** (hôte, `SignUp.cs`) : `GET /v1/public/sign-up` renvoie les pays ouverts (France en EUR, Bénin et zone
  UEMOA en XOF, avec leur fuseau), les plans de base actifs de ces devises et la durée d'essai. `POST /v1/public/sign-up`
  (anonyme, limité en débit comme `/auth`, champ piège `website`) contrôle conditions acceptées, pays, nom du restaurant
  et plan AVANT toute écriture, puis : compte du propriétaire en attente (email de confirmation), restaurant dans la
  devise et le fuseau du pays, propriétaire actif, essai d'un mois sur le plan choisi (plans payants seulement). Un échec après la création du restaurant le
  ferme. La connexion n'est possible qu'une fois l'email confirmé.
- **Console** : `/inscription?plan=<nom>&pays=fr|bj` (lien des tarifs du site), puis « Vérifiez votre boîte mail ».
  Pendant l'essai, la carte du plan propose « Payer maintenant » (page Abonnement, règlement en ligne).
- **Plan gratuit** (Découverte, retiré du catalogue ; le code reste pour les abonnements en cours) : un mois (`Billing:FreePlanMonths`, défaut 1), une seule fois par restaurant : au premier
  abonnement, ou en y passant pendant le premier essai sans dépasser le premier mois. Sinon
  `billing.subscription.free_plan_unavailable`. Le préavis part 7 jours avant la fin ; à la fin, suspendu (essai expiré).
  Le restaurant choisit alors un plan payant (changement de plan permis après un essai expiré) puis paie.
  Seul un plan gratuit déjà payé (offre de la plateforme) se renouvelle seul (`RenewedFree`).

## 14. Fonctionnalités du plan et règles des options

- `GET /billing/features` (`billing.view`) : `{ planName, status, trialEndsAtUtc, features: [{ code, enabled, limit }], usage }`,
  tous les codes de `BillingFeatures.All`, plan et options combinés ; sans abonnement vivant, tout est désactivé. `usage` : null.
- Porte des fonctionnalités : l'hôte remplace `AllowAllPlanFeatures` par `PlanFeatureGateFromBilling` (fail-closed : autre
  tenant que le contexte, ou pas d'abonnement vivant).
- Souscription d'une option (restaurant et plateforme) : une option qui donne `locations.count` exige `locations.extra`
  dans le plan de base (`billing.add_on.not_available_on_plan`) ; une option dont tous les droits sont déjà illimités dans
  le plan est refusée (`billing.add_on.already_included`). Une limite d'option s'ajoute à celle du plan : elle n'est donc
  jamais « déjà incluse » tant que le plan est limité.
- Archiver un plan (`scripts/seed-plans.py --archive-missing`) le retire de la vente seulement : abonnements, options,
  renouvellements, paiements et droits en cours continuent.

## 15. Plusieurs prestataires par devise

- Pour chaque devise, la plateforme propose un ou plusieurs prestataires prêts (ex. EUR : Stripe et PayPal), dans
  l'ordre (`online_payment_routes.provider` puis `other_providers`). `PUT /admin/billing/online-payment/{devise}` prend
  `{providers: [...]}` (l'ancienne forme `{provider}` reste acceptée) ; liste vide : retour au réglage de l'hôte.
- `GET /billing/online-payment` renvoie `options` (prestataires prêts, nom, moyens de paiement) ; le restaurant choisit
  et `POST /billing/checkouts` prend `provider` (absent : le premier). Un prestataire non proposé est refusé
  (`billing.online_payment.provider_not_offered`).
- Migration : `scripts/migrations-paiement-multiple.sh` (Billing `SubscriptionPaymentProviders`, colonne ajoutée avec un
  tableau vide par défaut : les choix existants sont gardés).
