# Module Reservations — Domain

But : garantir la réservation de capacité sans surbooking.

## 1. Frontières d'agrégat

| Élément | Nature | Pourquoi |
|---|---|---|
| `Reservation` (racine, sans enfant) | Agrégat | Un engagement de capacité pour un client et un créneau ; son cycle de vie (statuts) est sa seule cohérence interne. |
| `ReservationAvailabilityPolicy` | Service de domaine pur | « Ne pas dépasser la capacité » porte sur *plusieurs* réservations : aucun agrégat ne peut le prouver seul. Le service calcule une `AvailabilityDecision` à partir de la capacité et des réservations concurrentes fournies par l'Application. |
| `ReservationPolicy` | Value object | Règles de l'établissement (délais, horizon, durée, cutoff), passées explicitement aux méthodes ; pas stockées dans la réservation. |

`Reservation.Create` reçoit des **références** construites par l'Application — `LocationReference(TenantId, LocationId, IsOperational)` et `CustomerReference(TenantId, CustomerId, IsAnonymized)?` — pour vérifier le tenant, l'état de la location et celui du client ; l'agrégat ne **stocke** que des identifiants : `LocationId`, `CustomerId?`, `TableSessionId?` (session de table du module
Floor, connue à l'installation). Le contact (`GuestContact`) est un **snapshot** : il ne suit pas les modifications
ultérieures de la fiche client, mais il est **effaçable** (`AnonymizeGuest`).

**Temps** : `StartAtUtc` est un **instant** (`DateTimeOffset` UTC). Le créneau est `[StartAtUtc, StartAtUtc + Duration)`
(`Period` du kernel, semi-ouvert : une réservation qui finit à 20:00 ne chevauche pas celle qui commence à 20:00).
Ce module ne connaît **aucun fuseau horaire** : la conversion heure locale <-> instant se fait aux bords
(API/Application), et c'est l'Application qui valide les horaires d'ouverture avec `IsOpenDuring(slot)` du module
Locations **avant** d'évaluer la disponibilité.

**Verrou d'intégrité** : `Reservation.Create`, `Reschedule` et `ChangePartySize` exigent une `AvailabilityDecision`. Son
constructeur est `internal` (seul `ReservationAvailabilityPolicy.Evaluate` la produit) et l'agrégat refuse une décision
qui ne porte pas exactement sur la même location, le même créneau, la même taille de groupe (et, pour une modification,
qui aurait exclu une *autre* réservation que lui-même).

**Décision à usage unique** : une `AvailabilityDecision` est un **jeton consommable** (classe scellée avec état
`IsConsumed`, plus un value object). L'opération qu'elle autorise la consomme ; toute réutilisation lève
`reservations.reservation.availability_decision_already_used`. Une capacité évaluée une fois ne peut donc plus créer deux
réservations (rejeu), ni servir à un `Create` puis à un `Reschedule`. La consommation est le **dernier** contrôle : un
usage refusé (autre demande, capacité insuffisante, policy, tenant...) ne consomme pas le jeton, et un no-op ne l'examine
même pas. **Limite assumée** : l'usage unique n'est pas la fraîcheur — une décision ne sait rien des réservations
écrites après son évaluation ; évaluer et consommer dans la même transaction, sous verrou de capacité, reste le rôle de
l'Application (section 5).

## 2. Classes

| Classe | Rôle DDD | Responsabilité | Transitions | Events | Erreurs |
|---|---|---|---|---|---|
| `Reservation` | AggregateRoot, tenant-owned | Cycle de vie de la réservation, respect policy + capacité | `Pending -> Confirmed` (Confirm) ; `Confirmed -> Seated` (Seat) ; `Seated -> Completed` (Complete) ; `Pending/Confirmed -> Cancelled` (Cancel) ; `Confirmed -> NoShow` (MarkNoShow) ; `Reschedule`/`ChangePartySize` en `Pending/Confirmed` sans changement de statut ; `AnonymizeGuest` dans **tous** les statuts, sans changement de statut. Terminaux : `Completed`, `Cancelled`, `NoShow`. | `ReservationCreated`, `ReservationConfirmed`, `ReservationRescheduled`, `ReservationPartySizeChanged`, `GuestSeated` (porte `TableSessionId?`), `ReservationCompleted`, `ReservationCancelled`, `ReservationNoShow`, `ReservationGuestAnonymized` | `reservations.reservation.party_size_not_positive`, `.party_size_too_large`, `.notes_too_long`, `.lead_time_not_respected`, `.beyond_booking_horizon`, `.duration_too_short`, `.duration_too_long`, `.location_not_operational`, `.customer_anonymized`, `.capacity_insufficient`, `.availability_decision_mismatch`, `.availability_decision_already_used`, `common.tenant.mismatch`, `common.id.empty`, `.already_terminal`, `.not_pending`, `.not_confirmed`, `.not_seated`, `.not_modifiable_after_seating`, `.not_cancellable_after_seating`, `.cancellation_cutoff_passed`, `.modification_cutoff_passed`, `.no_show_before_start`, `.actor_invalid`, `.does_not_hold_capacity` |
| `ReservationStatus` (+ `HoldsCapacity()`, `IsTerminal()`, `CapacityHoldingStatuses`) | Enum | Statuts ; liste explicite de ceux qui occupent de la capacité | — | — | `reservations.reservation.status_unknown` (valeur hors enum) |
| `LocationReference` | ValueObject (référence vers Locations, publique) | `(TenantId, LocationId, IsOperational)`, ids non vides | — | — | `common.id.empty` |
| `CustomerReference` | ValueObject (référence vers Customers, publique) | `(TenantId, CustomerId, IsAnonymized)`, ids non vides | — | — | `common.id.empty` |
| `ReservationActor` (+ `IsBoundByCutoff()`) | Enum | `Customer` / `Staff` / `System` : qui demande l'annulation ou la modification ; seul `Customer` est soumis au cutoff | — | — | `reservations.reservation.actor_invalid` (valeur hors enum) |
| `GuestContact` | ValueObject (snapshot) | Nom (150 max) + téléphone et/ou email ; `GuestContact.Anonymized` = valeur neutre d'effacement (`IsAnonymized`) | — | — | `reservations.guest_contact.name_required`, `.name_too_long`, `.channel_required` |
| `ReservationPolicy` | ValueObject | `MinLeadTime`, `MaxAdvance`, `DefaultDuration`, `CancellationCutoff`, `MinDuration`, `MaxDuration`, `MaxPartySize` ; `CanBookAt`, `CanCancelAt`, `IsTooSoon`, `IsBeyondHorizon`, `IsTooShort`, `IsTooLong`, `SlotFor` | — | — | `reservations.policy.min_lead_time_negative`, `.max_advance_not_positive`, `.max_advance_not_after_lead_time`, `.max_advance_too_long`, `.default_duration_not_positive`, `.default_duration_out_of_bounds`, `.duration_bounds_invalid`, `.cancellation_cutoff_negative`, `.cancellation_cutoff_too_long`, `.max_party_size_invalid` |
| `ReservationAvailabilityPolicy` | DomainService (statique, pur) | `Evaluate(location, créneau, taille, capacité, réservations existantes, exclue?)` | — | — | `reservations.reservation.party_size_not_positive`, `reservations.availability.capacity_negative`, `.capacity_too_large`, `common.id.empty` |
| `AvailabilityDecision` | Jeton à usage unique (classe scellée, **pas** un ValueObject ; ctor et `Consume` `internal`) | Résultat de l'évaluation : `IsAvailable`, `PeakBookedSeats`, `RemainingSeats` + la demande évaluée ; `IsConsumed` | non consommée -> consommée (par `Create` / `Reschedule` / `ChangePartySize` réussis) | — | (vérifiée par `Reservation`) `.availability_decision_mismatch`, `.availability_decision_already_used`, `.capacity_insufficient` |
| `ExistingBooking` | ValueObject | Occupation existante : réservation (`Create`) **ou walk-in sans réservation** (`ForWalkIn`, `ReservationId` null), créneau, couverts | — | — | `reservations.reservation.party_size_not_positive`, `.party_size_too_large`, `common.id.empty` |

## 3. Choix faits là où la spec était ouverte

- **Statuts qui occupent la capacité** : `Pending`, `Confirmed`, `Seated` (`ReservationStatusExtensions.CapacityHoldingStatuses`). `Completed`, `Cancelled`, `NoShow` libèrent les places. Une réservation `Pending` bloque déjà ses places (sinon deux demandes en attente pourraient être confirmées au-delà de la capacité).
- **Capacité = places des tables en service** (décision produit) : le nombre de **couverts** (entier >= 0) passé à `Evaluate` est la somme des places des tables **en service** de la location, lue dans le module Floor par l'Application. Ce n'est ni la `Capacity` déclarative de Locations ni un quota configuré.
- **Les walk-ins occupent la capacité** (décision produit) : des clients installés sans réservation consomment des places. L'Application ajoute donc aux occupations existantes un `ExistingBooking.ForWalkIn(créneau, couverts)` par groupe installé sans réservation (créneau = de l'ouverture de la session de table à sa fin prévue). Un walk-in n'a pas de `ReservationId` : il n'est **jamais** exclu par `excludedReservationId`, et deux walk-ins identiques s'additionnent.
- **Modèle de calcul** : le service calcule le **pic d'occupation simultanée** sur le créneau demandé (balayage) : deux réservations qui se suivent dans le créneau ne s'additionnent pas, deux réservations présentes au même instant oui. Accepté si `pic + taille <= capacité`. L'attribution de tables précises n'est pas modélisée.
- **Fenêtre de réservation** (`Create`, `Reschedule`) : `now + MinLeadTime <= début <= now + MaxAdvance`, bornes incluses, pour le client comme pour le staff. Erreurs distinctes `lead_time_not_respected` / `beyond_booking_horizon`.
- **Cutoff d'annulation — politique explicite** : `Cancel(cancelledBy, reason, policy, clock)`. Un **client** peut annuler tant que `now <= début - CancellationCutoff` (borne incluse) ; au-delà -> `cancellation_cutoff_passed`. Le **staff** (`ReservationActor.Staff`) et le **système** (`ReservationActor.System` : annulations automatiques quand un établissement ferme) outrepassent le cutoff, y compris après l'heure de début tant que le client n'est pas installé. L'acteur est tracé (`CancelledBy`, événement). Une valeur hors enum est rejetée (`actor_invalid`) par `Cancel`, `Reschedule` et `ChangePartySize`.
- **Même cutoff pour les modifications client** : `Reschedule` et `ChangePartySize` demandés par le client après le cutoff -> `modification_cutoff_passed` ; le staff et le système outrepassent. (Sinon le client contournerait le cutoff en déplaçant sa réservation.)
- **Après installation (`Seated`)** : ni replanification, ni changement de taille, ni annulation ; seule `Complete` est possible.
- **`Seat(tableSessionId?, clock)`** : uniquement depuis `Confirmed` (une réservation `Pending` doit d'abord être confirmée). Reçoit le `TableSessionId` (kernel) de la session ouverte pour le groupe, **nullable** (établissement sans plan de salle) ; il est stocké (`Reservation.TableSessionId`) et porté par `GuestSeated`. Le domaine ne vérifie pas que la session existe (autre module). **`MarkNoShow`** : uniquement depuis `Confirmed` et pas avant `StartAtUtc` (une réservation `Pending` jamais confirmée s'annule). Aucun délai de grâce imposé : c'est une décision du staff.
- **Durée** : `Create` reçoit un `Period` ; `policy.SlotFor(start)` applique `DefaultDuration`, mais une durée explicite différente est permise.
- **Valeurs par défaut** (`ReservationPolicy.Default`) : délai minimum 30 min, horizon 90 jours, durée 2 h (15 min à 6 h admis), cutoff 2 h, 50 couverts max. `MinLeadTime` et `CancellationCutoff` peuvent valoir zéro ; `MaxAdvance` et `DefaultDuration` strictement positifs ; `MaxAdvance > MinLeadTime`.
- **Même valeur = no-op silencieux** : `ChangePartySize` à taille identique et `Reschedule` vers le créneau identique (même début, même durée) ne modifient rien et ne lèvent aucun événement. Les gardes d'état restent appliquées (terminal, installé, acteur, cutoff client) ; pour les deux, le no-op intervient **avant** que la décision de disponibilité soit examinée (elle n'est ni vérifiée ni consommée) ; pour `Reschedule` il intervient aussi avant le contrôle de fenêtre et de capacité (rien ne bouge : un formulaire ré-enregistré 20 min avant le début n'est pas refusé pour délai minimum). Pas d'idempotence sur les transitions de statut.
- **Effacement des données du client — `AnonymizeGuest(clock)`** : quand le module Customers anonymise un client, le snapshot de contact doit disparaître. C'est un effacement, pas un changement métier : autorisé dans **tous** les statuts, terminaux compris. `GuestContact` est remplacé par `GuestContact.Anonymized` (nom neutre `"Anonymized guest"`, ni téléphone ni email, construit par le constructeur privé : `Create` exige toujours un canal, donc un contact créé n'est jamais « anonymisé » et l'invariant public est intact) ; `Notes` est vidé ; `CustomerId`, créneau, taille et statut sont conservés. Idempotent : un second appel ne fait rien et ne lève rien. Event `ReservationGuestAnonymized` avec des **ids uniquement**. `IsGuestAnonymized` expose l'état. `CancellationReason` n'est **pas** effacé (voir section 5).
- **Taille de groupe bornée — `ReservationPolicy.MaxPartySize`** (explicite, défaut 50, de 1 à `MaxPartySizeCeiling` = 10 000) : `Create` et `ChangePartySize` lèvent `party_size_too_large` au-delà. Sans borne, `partySize = int.MaxValue` faisait déborder `pic + taille` et la demande était jugée *disponible*.
- **Arithmétique des couverts sans débordement** : `IsAvailable` compare `taille <= capacité - pic` en `long` ; le balayage cumule en `long` et `PeakBookedSeats` sature à `int.MaxValue` (exact, la capacité étant bornée). Bornes dures dans les factories : `ExistingBooking.MaxPartySize` = 10 000 couverts par occupation (réservation ou walk-in, `party_size_too_large`), `ReservationAvailabilityPolicy.MaxCapacity` = 100 000 couverts (`capacity_too_large`). `Evaluate` ne borne pas la taille demandée : une demande démesurée reçoit simplement « indisponible ».
- **Durée du créneau bornée — `MinDuration` / `MaxDuration`** (défauts 15 min / 6 h ; `0 < MinDuration <= MaxDuration <= 24 h` ; `DefaultDuration` doit être dans l'intervalle) : `Create` et `Reschedule` lèvent `duration_too_short` / `duration_too_long`. Un créneau d'un tick ne retenait aucune capacité ; un créneau d'un an la bloquait un an. Les durées sont contrôlées avant la fenêtre de réservation.
- **Durées de la policy bornées** : `MaxAdvance` et `CancellationCutoff` <= 730 jours (`MaxAdvanceCeiling`). Les comparaisons de temps soustraient deux instants au lieu d'ajouter une durée à un instant : aucune ne peut lever `ArgumentOutOfRangeException`, même aux extrémités du calendrier ; `SlotFor` répond `beyond_booking_horizon` si le créneau ne peut pas se terminer avant `DateTimeOffset.MaxValue`.
- **Location et client référencés à la création** : `Create(tenantId, LocationReference, CustomerReference?, ...)`. La location doit appartenir au tenant (`common.tenant.mismatch`) **et** être opérationnelle (`location_not_operational`) ; le client, s'il est fourni, doit appartenir au tenant et ne pas être anonymisé (`customer_anonymized`). `Reschedule` / `ChangePartySize` ne re-vérifient pas l'état de la location (voir section 5).
- **Ids vides** (`default`) refusés avec `common.id.empty` : `tenantId` de `Create`, ids des références, `TableSessionId` fourni à `Seat`, `LocationId` et réservation exclue de `Evaluate`, `ReservationId` de `ExistingBooking.Create`.
- **Enums** : toute valeur hors enum est refusée, y compris par les helpers `HoldsCapacity()` / `IsTerminal()` (`status_unknown`) et `IsBoundByCutoff()` (`actor_invalid`).
- **Horloge lue une fois par opération** : `CreatedAtUtc` et les `OccurredAtUtc` d'une même opération sont identiques, et les contrôles de fenêtre/cutoff utilisent le même instant.
- **`ReservationRescheduled`** porte aussi la durée précédente (`PreviousDuration`).
- `Notes` : optionnelles, trimées, 500 caractères max. `GuestContact.Name` : 150 max (aligné sur le nom client du module Customers).
- Événements ajoutés : `ReservationPartySizeChanged`, `ReservationCompleted`, `ReservationGuestAnonymized`.

## 4. Matrice invariant -> méthode -> test

| Invariant | Méthode | Test |
|---|---|---|
| Party size > 0 | `Create`, `ChangePartySize`, `Evaluate`, `ExistingBooking.Create` | `ReservationTests.Create_rejects_a_party_size_that_is_not_positive`, `ChangePartySize_rejects_a_size_that_is_not_positive`, `ReservationAvailabilityPolicyTests.Evaluate_rejects_a_party_size_that_is_not_positive`, `ExistingBooking_rejects_a_party_size_that_is_not_positive` |
| Tenant et ids non vides | `Create`, `LocationReference`, `CustomerReference`, `Seat`, `Evaluate`, `ExistingBooking.Create` | `ReservationTests.Create_rejects_an_empty_tenant_and_empty_foreign_ids`, `Seat_rejects_an_empty_table_session_id`, `ReservationAvailabilityPolicyTests.Evaluate_and_ExistingBooking_reject_empty_ids` |
| Location du même tenant et opérationnelle | `Create` | `ReservationTests.Create_rejects_a_location_or_a_customer_of_another_tenant`, `Create_rejects_a_location_that_is_not_operational`, `Create_rejects_null_references` |
| Client du même tenant, non anonymisé | `Create` | `ReservationTests.Create_rejects_a_location_or_a_customer_of_another_tenant`, `Create_rejects_an_anonymized_customer_and_stores_the_id_of_a_live_one` |
| Décision à usage unique | `Create`, `Reschedule`, `ChangePartySize` (`AvailabilityDecision.Consume`) | `ReservationTests.Create_consumes_the_decision_which_cannot_create_a_second_reservation`, `A_decision_used_by_Create_cannot_be_replayed_by_Reschedule_or_ChangePartySize`, `Reschedule_and_ChangePartySize_consume_their_decision`, `ReservationAvailabilityPolicyTests.A_decision_cannot_be_replayed_to_book_the_last_seats_twice`, `Decisions_for_the_same_request_and_state_are_distinct_single_use_tokens` |
| Un usage refusé ou un no-op ne consomme pas la décision | `Create`, `Reschedule`, `ChangePartySize` | `ReservationTests.A_rejected_use_does_not_consume_the_decision`, `ChangePartySize_to_the_same_size_neither_examines_nor_consumes_the_decision`, `Reschedule_to_the_same_slot_does_not_consume_the_decision` |
| Party size <= `MaxPartySize` de la policy | `Create`, `ChangePartySize` | `ReservationTests.Create_and_ChangePartySize_enforce_the_maximum_party_size_of_the_policy`, `A_party_of_int_MaxValue_is_never_booked_even_with_a_policy_at_its_ceiling` |
| Pas de débordement d'entier sur les couverts | `AvailabilityDecision.IsAvailable`, `Evaluate` | `ReservationAvailabilityPolicyTests.Evaluate_does_not_let_an_int_MaxValue_party_wrap_around_the_capacity`, `Evaluate_does_not_let_many_large_bookings_wrap_around_and_vanish` |
| Occupations et capacité bornées | `ExistingBooking.Create`, `ForWalkIn`, `Evaluate` | `ReservationAvailabilityPolicyTests.Bookings_and_capacity_are_bounded_to_a_sane_maximum` |
| Occupation `null` = erreur de programmation | `Evaluate` | `ReservationAvailabilityPolicyTests.Evaluate_treats_a_null_booking_as_a_programming_error` |
| Durée du créneau dans `[MinDuration, MaxDuration]` | `Create`, `Reschedule` | `ReservationTests.Create_and_Reschedule_enforce_the_duration_bounds_of_the_policy` |
| Enums hors valeurs refusés par les helpers | `HoldsCapacity`, `IsTerminal`, `IsBoundByCutoff` | `ReservationTests.Status_and_actor_helpers_reject_undefined_values` |
| Liste des statuts occupant la capacité non modifiable | `CapacityHoldingStatuses` | `ReservationTests.Capacity_holding_statuses_cannot_be_altered_through_a_downcast` |
| Une lecture d'horloge par opération | `Create`, `Reschedule`, `ChangePartySize`, `Cancel`, `MarkNoShow`, `CanBookAt` | `ReservationTests.Every_operation_reads_the_clock_once`, `ReservationPolicyTests.CanBookAt_reads_the_clock_once` |
| Créneau respecte le délai minimum | `Create`, `Reschedule` | `ReservationTests.Create_rejects_a_slot_starting_before_the_minimum_lead_time`, `Create_rejects_a_slot_in_the_past`, `Create_accepts_a_slot_starting_exactly_at_the_minimum_lead_time`, `Reschedule_rejects_a_new_slot_outside_the_booking_window` |
| Créneau dans l'horizon | `Create`, `Reschedule` | `ReservationTests.Create_accepts_a_slot_exactly_at_the_booking_horizon_and_rejects_one_second_later`, `Reschedule_rejects_a_new_slot_outside_the_booking_window` |
| Capacité suffisante | `Create`, `Reschedule`, `ChangePartySize` | `ReservationTests.Create_rejects_a_slot_without_enough_remaining_capacity`, `Reschedule_rejects_a_new_slot_without_enough_capacity`, `ChangePartySize_rejects_a_growth_the_slot_cannot_absorb` |
| Décision de disponibilité pour *cette* demande | `Create`, `Reschedule`, `ChangePartySize` | `ReservationTests.Create_rejects_a_decision_made_for_another_location`, `Create_rejects_a_decision_made_for_another_slot`, `Create_rejects_a_decision_made_for_another_party_size`, `Create_rejects_a_decision_that_ignored_some_existing_reservation`, `Reschedule_rejects_a_decision_made_for_another_request`, `ChangePartySize_rejects_a_decision_made_for_another_request`, `ReservationAvailabilityPolicyTests.A_decision_cannot_be_reused_for_a_bigger_party` |
| StartAt est un instant UTC | `Create` | `ReservationTests.Create_stores_the_start_as_a_utc_instant_whatever_the_offset_given` |
| `Pending -> Confirmed` seulement | `Confirm` | `ReservationTests.Confirm_moves_a_pending_reservation_to_confirmed_and_raises_ReservationConfirmed`, `Confirm_rejects_a_reservation_that_is_not_pending` |
| `Confirmed -> Seated` seulement | `Seat` | `ReservationTests.Seat_moves_a_confirmed_reservation_to_seated_and_raises_GuestSeated`, `Seat_rejects_a_reservation_that_is_not_confirmed` |
| Lien avec la session de table (optionnel) | `Seat` | `ReservationTests.Seat_links_the_reservation_to_the_table_session_opened_for_the_party`, `Seat_moves_a_confirmed_reservation_to_seated_and_raises_GuestSeated` (sans session), `Seat_that_is_rejected_does_not_store_the_table_session` |
| `Seated -> Completed` seulement | `Complete` | `ReservationTests.Complete_moves_a_seated_reservation_to_completed_and_raises_ReservationCompleted`, `Complete_rejects_a_reservation_that_is_not_seated` |
| NoShow : depuis Confirmed, pas avant le début | `MarkNoShow` | `ReservationTests.MarkNoShow_moves_a_confirmed_reservation_to_no_show_once_the_start_time_is_reached`, `MarkNoShow_rejects_a_reservation_whose_start_time_is_not_reached`, `MarkNoShow_rejects_a_reservation_that_is_not_confirmed` |
| Annulation client avant cutoff | `Cancel` | `ReservationTests.Cancel_by_the_customer_before_the_cutoff_cancels_and_raises_ReservationCancelled`, `Cancel_by_the_customer_is_accepted_exactly_at_the_cutoff`, `Cancel_by_the_customer_is_refused_after_the_cutoff` |
| Le staff et le système outrepassent le cutoff | `Cancel`, `Reschedule`, `ChangePartySize` | `ReservationTests.Cancel_by_staff_overrides_the_cutoff`, `Cancel_by_staff_is_still_possible_after_the_start_time_while_not_seated`, `Cancel_by_the_system_overrides_the_cutoff_like_staff`, `Reschedule_and_ChangePartySize_by_the_system_override_the_cutoff_like_staff` |
| Acteur = valeur définie de l'enum | `Cancel`, `Reschedule`, `ChangePartySize` | `ReservationTests.Cancel_rejects_an_undefined_actor`, `Reschedule_and_ChangePartySize_reject_an_undefined_actor` |
| Pas d'annulation après installation | `Cancel` | `ReservationTests.Cancel_rejects_a_seated_reservation` |
| Cancelled / Completed / NoShow terminaux | toutes | `ReservationTests.Terminal_reservation_rejects_every_transition_and_change` |
| Reschedule interdit après Seating | `Reschedule` | `ReservationTests.Reschedule_is_forbidden_once_the_guests_are_seated` |
| Reschedule valide | `Reschedule` | `ReservationTests.Reschedule_moves_the_slot_keeps_the_status_and_raises_ReservationRescheduled`, `Reschedule_does_not_count_the_reservation_own_seats_against_itself` |
| Reschedule vers le même créneau = no-op (gardes d'état conservées) | `Reschedule` | `ReservationTests.Reschedule_to_the_same_slot_raises_nothing_even_inside_the_lead_time`, `Reschedule_to_the_same_slot_is_still_rejected_for_a_customer_after_the_cutoff_or_once_terminal` |
| Cutoff appliqué aux modifications client | `Reschedule`, `ChangePartySize` | `ReservationTests.Reschedule_by_the_customer_is_refused_after_the_cutoff_but_staff_can_override`, `ChangePartySize_by_the_customer_is_refused_after_the_cutoff_but_staff_can_override` |
| Changement de taille | `ChangePartySize` | `ReservationTests.ChangePartySize_updates_the_size_and_raises_ReservationPartySizeChanged`, `ChangePartySize_accepts_a_reduction_on_a_full_slot`, `ChangePartySize_is_forbidden_once_the_guests_are_seated`, `ChangePartySize_to_the_same_size_raises_nothing`, `ChangePartySize_to_the_same_size_neither_examines_nor_consumes_the_decision` |
| Statuts occupant la capacité explicites | `HoldsCapacity`, `ToExistingBooking` | `ReservationTests.Only_pending_confirmed_and_seated_reservations_hold_capacity`, `ToExistingBooking_rejects_a_reservation_that_no_longer_holds_capacity` |
| Notes bornées | `Create` | `ReservationTests.Create_enforces_the_notes_length_limit` |
| Effacement du contact dans tout statut, idempotent, ids seulement | `AnonymizeGuest`, `IsGuestAnonymized` | `ReservationTests.AnonymizeGuest_erases_the_contact_in_every_status_and_raises_ReservationGuestAnonymized`, `AnonymizeGuest_clears_the_notes_and_keeps_the_customer_id_and_the_booking_facts`, `AnonymizeGuest_twice_is_a_no_op_without_event`, `Anonymized_reservation_still_follows_its_lifecycle` |
| Contact anonymisé = seule valeur sans canal, hors de portée de `Create` | `GuestContact.Anonymized` | `GuestContactTests.Anonymized_is_a_neutral_contact_without_phone_nor_email`, `A_created_contact_is_never_anonymized_even_with_the_placeholder_name` |
| Ne pas dépasser la capacité | `ReservationAvailabilityPolicy.Evaluate` | `ReservationAvailabilityPolicyTests.Evaluate_accepts_a_party_that_fills_the_capacity_exactly`, `Evaluate_refuses_a_party_that_would_exceed_the_capacity_by_one_seat`, `Evaluate_refuses_everything_when_the_capacity_is_zero`, `Evaluate_rejects_a_negative_capacity` |
| Seules les réservations chevauchant le créneau comptent | `Evaluate` | `ReservationAvailabilityPolicyTests.Evaluate_ignores_bookings_that_do_not_overlap_the_slot`, `Evaluate_counts_a_booking_that_overlaps_the_slot_only_partially`, `Evaluate_does_not_add_up_back_to_back_bookings_inside_the_slot`, `Evaluate_adds_up_bookings_present_at_the_same_instant` |
| Exclusion de la réservation modifiée | `Evaluate` | `ReservationAvailabilityPolicyTests.Evaluate_leaves_out_the_excluded_reservation_only` |
| Les walk-ins occupent la capacité | `ExistingBooking.ForWalkIn`, `Evaluate` | `ReservationAvailabilityPolicyTests.ForWalkIn_builds_an_occupancy_without_reservation_id`, `Evaluate_counts_walk_ins_against_the_capacity`, `Evaluate_adds_up_several_walk_ins_even_when_they_are_identical`, `Evaluate_ignores_a_walk_in_that_has_left_before_the_slot`, `Evaluate_never_leaves_a_walk_in_out_when_a_reservation_is_excluded` |
| Dernier créneau simultané | `Evaluate` + `Create` | `ReservationAvailabilityPolicyTests.Last_seats_requested_twice_on_the_same_state_only_the_first_one_gets_them`, `Seats_of_a_cancelled_reservation_become_available_again` |
| Policy : durées positives | `ReservationPolicy.Create` | `ReservationPolicyTests.Create_rejects_a_negative_lead_time`, `Create_rejects_a_horizon_that_is_not_positive`, `Create_rejects_a_horizon_that_does_not_exceed_the_lead_time`, `Create_rejects_a_default_duration_that_is_not_positive`, `Create_rejects_a_negative_cancellation_cutoff` |
| Policy : bornes de durée, durée par défaut dans les bornes, taille max | `ReservationPolicy.Create` | `ReservationPolicyTests.Create_defaults_the_duration_bounds_and_the_maximum_party_size`, `Create_keeps_explicit_duration_bounds_and_maximum_party_size`, `Create_rejects_invalid_duration_bounds`, `Create_requires_the_default_duration_within_the_duration_bounds`, `Create_rejects_an_invalid_maximum_party_size` |
| Policy : horizon et cutoff bornés, aucune arithmétique de temps ne déborde | `ReservationPolicy.Create`, `CanBookAt`, `CanCancelAt`, `SlotFor` | `ReservationPolicyTests.Create_bounds_the_horizon_and_the_cutoff`, `Time_arithmetic_never_throws_at_the_ends_of_the_calendar` |
| Policy : lead time / horizon | `CanBookAt` | `ReservationPolicyTests.CanBookAt_requires_the_minimum_lead_time`, `CanBookAt_follows_the_clock`, `CanBookAt_stops_at_the_booking_horizon`, `CanBookAt_compares_instants_not_wall_clock_times` |
| Policy : cutoff | `CanCancelAt` | `ReservationPolicyTests.CanCancelAt_is_true_until_the_cutoff_included_and_false_after` |
| Contact : nom + téléphone et/ou email | `GuestContact.Create` | `GuestContactTests.Create_rejects_a_contact_without_phone_nor_email`, `Create_rejects_a_missing_name`, `Create_enforces_the_name_length_limit`, `Name_limit_matches_the_customer_name_limit_of_150_characters`, `Create_accepts_a_name_with_a_phone_only`, `Create_accepts_a_name_with_an_email_only` |

## 5. Application / Infrastructure

- **Concurrence (dernier créneau)** : le domaine rend la décision déterministe pour un état donné ; il ne peut pas empêcher deux transactions de lire le *même* état. L'Application doit, dans **une seule transaction** : (1) prendre un verrou sur la ressource de capacité (p. ex. verrou consultatif / ligne `(TenantId, LocationId, jour)` ou isolation sérialisable), (2) lire les réservations dont le statut est dans `CapacityHoldingStatuses` et qui chevauchent le créneau, **plus les walk-ins** (sessions de table ouvertes sans réservation, module Floor) convertis en `ExistingBooking.ForWalkIn`, (3) appeler `Evaluate`, (4) créer/modifier la réservation, (5) valider. Une `AvailabilityDecision` ne doit jamais être mise en cache ni réutilisée hors de cette transaction : le domaine garantit seulement qu'elle ne sert qu'**une fois** (`availability_decision_already_used`), pas qu'elle est encore vraie. `IsConsumed` vit en mémoire (la décision n'est pas persistée) : une décision par commande, jamais partagée entre deux requêtes. En cas de nouvelle tentative (conflit de version, retry), **ré-évaluer**.
- **Capacité** : somme des places des tables **en service** de la location, lue dans le module Floor par l'Application au moment de l'évaluation.
- **Walk-ins** : l'Application fournit la durée d'occupation d'un walk-in (p. ex. ouverture de session + `ReservationPolicy.DefaultDuration`) ; une session de table liée à une réservation `Seated` (`Reservation.TableSessionId`) ne doit **pas** être comptée une seconde fois comme walk-in.
- **Horaires d'ouverture** : avant `Evaluate`, l'Application appelle `RestaurantLocation.IsOpenDuring(slot)` (module Locations, créneau UTC, fuseau de l'établissement géré là-bas) et refuse la demande si c'est faux ; Les horaires ne sont pas dans ce domaine. L'état de la location, lui, est vérifié par `Create` : l'Application construit `new LocationReference(location.TenantId, location.Id, location.IsOperational)` depuis la location chargée (hypothèse : `IsOperational` = statut `Active` côté Locations). `Reschedule` et `ChangePartySize` ne reçoivent pas de référence : si une location fermée ne doit plus accepter de modification, l'Application le vérifie (ou annule les réservations sur `LocationTemporarilyClosed` / `LocationArchived`).
- **Client** : si la réservation est rattachée à un client, construire `new CustomerReference(customer.TenantId, customer.Id, customer.IsAnonymized)` depuis le client chargé (module Customers) ; `null` pour un invité sans fiche.
- **Installation** : le handler ouvre la session de table (Floor) puis appelle `Seat(tableSessionId, clock)` ; `null` si aucune session n'est suivie.
- **Effacement** : sur l'anonymisation d'un client (module Customers), charger toutes ses réservations (`CustomerId`) et appeler `AnonymizeGuest` ; les réservations sans `CustomerId` (invité non rattaché) doivent être retrouvées par l'Application (recherche par téléphone/email) si l'effacement les vise. `CancellationReason` (texte libre saisi par le staff ou le client) n'est pas effacé par le domaine : à traiter si la politique de confidentialité l'exige.
- **Pour une modification** : passer `excludedReservationId = reservation.Id` à `Evaluate`, sinon la réservation se compte contre elle-même (résultat seulement plus strict, jamais dangereux).
- **`ReservationPolicy`** : persistée avec la configuration de la location (désormais 7 valeurs : + `MinDuration`, `MaxDuration`, `MaxPartySize`) et chargée par l'Application ; `ReservationPolicy.Default` sinon. Une policy stockée hors des nouvelles bornes (horizon > 730 jours, durée par défaut hors `[MinDuration, MaxDuration]`) ne se recharge plus via `Create` : à migrer.
- **`ReservationActor`** : déterminé par l'Application à partir de l'authentification/permissions (jamais d'un champ libre de la requête) ; `System` est réservé aux handlers automatiques (p. ex. annulation des réservations à venir sur `LocationTemporarilyClosed` / `LocationArchived`).
- **Fuseau horaire** : conversion heure locale <-> `StartAtUtc` à la frontière API/Application, avec le fuseau de l'établissement lu dans Locations (`RestaurantLocation.TimeZone`) ; ce module ne manipule que des instants UTC.
- **No-show automatique**, rappels, notifications : jobs/handlers applicatifs réagissant aux événements.
- Verrouillage optimiste (version) sur `Reservation` pour les transitions concurrentes (p. ex. `Seat` et `Cancel` simultanés).

## 6. Application (`Reservations.Application`)

Le tenant d'un cas d'usage vient **toujours** d'`ITenantContext.RequireTenant()` (claim `tid` du jeton), jamais de la
requête. Sans restaurant sélectionné : `403 common.tenant.required`. **Acteur** : ces cas d'usage sont ceux du personnel
du restaurant, l'acteur passé au domaine est toujours `ReservationActor.Staff` (pas de délai limite d'annulation).

| Cas d'usage | Effet | Erreurs propres à l'Application |
|---|---|---|
| `CreateReservation(locationId, startAt, partySize, guestName, guestPhone?, guestEmail?, notes?, durationMinutes?)` | Réservation `Pending` ; durée absente = `DefaultDuration` de la politique | `reservations.location.not_found`, `reservations.reservation.location_closed_during_slot`, `reservations.reservation.duration_invalid`, `reservations.tenant.not_writable` |
| `ConfirmReservation`, `CompleteReservation`, `MarkReservationNoShow` | Transitions (règles de l'agrégat) | `reservations.reservation.not_found`, `reservations.tenant.not_writable` |
| `RescheduleReservation(startAt, durationMinutes?)`, `ChangePartySize(partySize)` | Réévaluées sous verrou **en excluant la réservation elle-même** ; même créneau / même taille = sans effet (gardes d'état appliquées) | idem + `location_closed_during_slot` |
| `SeatReservation(tableSessionId?)` | Installation ; la session (Floor) doit exister, être ouverte et du même établissement | `reservations.table_session.not_found`, `reservations.table_session.not_usable` |
| `CancelReservation(reason?)` | Annulation par le personnel, à tout moment avant l'installation | idem |
| `SetReservationPolicy(locationId, …)` | Politique de l'établissement (minutes, horizon en jours) ; bornes de `ReservationPolicy.Create` | `reservations.location.not_found` |
| `GetReservationById`, `ListReservations(from, to, locationId?, status?)` | Agenda : réservations qui commencent dans `[from, to[` (31 jours max), par heure de début | `reservations.window.invalid` |
| `GetAvailability(locationId, startAt, partySize, durationMinutes?)` | Capacité, pic occupé, places restantes, ouvert ou non ; **sans verrou** (indicatif) | — |
| `GetReservationPolicy(locationId)` | Politique réglée, sinon `ReservationPolicy.Default` | — |

- **Décision de capacité** (`ReservationsWorkspace.EvaluateAsync`), dans la transaction qui écrit ensuite :
  1. verrou de capacité de l'établissement (`IReservationCapacityLock`) ;
  2. réservations qui retiennent de la capacité et commencent au plus 24 h avant le créneau (le chevauchement exact est
     calculé par le domaine) ;
  3. salle (port `IReservationFloor`, réponse de Floor) : **capacité = places des tables en service** ; **walk-ins** =
     sessions ouvertes **sans** réservation et non rattachées à une réservation installée, comptées de leur ouverture
     jusqu'à `ouverture + DefaultDuration` (ou jusqu'à maintenant si elles durent plus) ;
  4. `ReservationAvailabilityPolicy.Evaluate`, puis l'agrégat consomme la décision.
- **Horaires d'ouverture** (port `IReservationLocations`, réponse de Locations : `IsLocationOpenDuringQuery`, heure
  locale et exceptions de l'établissement) : vérifiés avant le verrou, pour la création et le déplacement.
- **Heures** : `startAt` est un instant ISO 8601 avec décalage ; tout est stocké et renvoyé en UTC (`startAtUtc`).
- **Ressource d'un autre tenant** (réservation, établissement, session) : `404 …not_found`, comme un id inconnu.
- **Permissions** : lecture ouverte à tout membre ; `reservations.manage` pour les réservations, `reservations.configure`
  pour la politique. Owner a les deux ; Manager aussi pour les tenants créés après l'ajout du module.
- **Hors périmètre pour l'instant** (documenté, pas codé) :
  - **réservation par le client lui-même** (widget public, `ReservationActor.Customer`, délai limite d'annulation) :
    demande des routes publiques par restaurant, pas encore conçues ;
  - **fiche client** (`CustomerReference`) : le module Customers n'est pas persisté, une réservation ne garde que le
    contact donné ; l'effacement (`AnonymizeGuest`) viendra avec `CustomerAnonymized` ;
  - **réactions aux événements** : annulation automatique (`System`) sur fermeture d'établissement, no-show
    automatique, rappels et notifications ;
  - **walk-ins ouverts en concurrence** : les sessions de Floor ne sont pas sous le verrou de Reservations (autre
    module, autre transaction) ; seules les réservations entre elles sont strictement sérialisées.

## 7. Infrastructure (`Reservations.Infrastructure`)

- Schéma `reservations` : `reservations`, `location_policies`, `outbox_messages`.
- **`ReservationsDbContext : TenantDbContext`** : filtre EF sur le tenant de la requête (rien sans tenant), **en plus** du
  filtre explicite des repositories.
- **Verrou de capacité** : `pg_advisory_xact_lock(hashtextextended('reservations:capacity:<tenant>:<location>', 0))`
  dans une transaction ouverte par le verrou ; l'unit of work valide cette transaction après l'écriture (ce qui relâche
  le verrou), sinon elle est abandonnée avec le contexte. Deux demandes pour le dernier créneau passent l'une après
  l'autre et la seconde voit la première.
- `GuestContact` en type complexe (`guest_name`, `guest_phone`, `guest_email`), durée en `interval`, index
  `(tenant_id, location_id, start_at_utc)` et `(tenant_id, customer_id)`.
- **Politique** : une ligne par `(tenant_id, location_id)` (`ReservationPolicyRow`, forme stockée du value object),
  relue via `ReservationPolicy.Create`.
- **Concurrence optimiste** sur `xmin` (ex. `Seat` et `Cancel` simultanés : 409).
- **Migration** : `Persistence/Migrations/…_InitialCreate`. `database update` lit `RESERVATIONS_DB`.

## 8. API (`Reservations.Api`)

Groupe `/reservations`, protégé par l'hôte avec la policy `tenant-member`. Les créations renvoient `201` + `{ id }`.

| Méthode | Route | Corps | Permission |
|---|---|---|---|
| `POST` | `/reservations` | `{ locationId, startAt, partySize, guestName, guestPhone?, guestEmail?, notes?, durationMinutes? }` | `reservations.manage` |
| `GET` | `/reservations?from&to&locationId&status` | — | membre |
| `GET` | `/reservations/availability?locationId&startAt&partySize&durationMinutes` | — | membre |
| `GET` | `/reservations/{id}` | — | membre |
| `POST` | `/reservations/{id}/confirm` · `/complete` · `/no-show` | — | `reservations.manage` |
| `PUT` | `/reservations/{id}/slot` · `/party-size` | `{ startAt, durationMinutes? }` · `{ partySize }` | `reservations.manage` |
| `POST` | `/reservations/{id}/seat` | facultatif : `{ tableSessionId }` | `reservations.manage` |
| `POST` | `/reservations/{id}/cancel` | facultatif : `{ reason }` | `reservations.manage` |
| `GET` · `PUT` | `/reservations/policies/{locationId}` | `{ minLeadTimeMinutes, maxAdvanceDays, defaultDurationMinutes, cancellationCutoffMinutes, minDurationMinutes, maxDurationMinutes, maxPartySize }` | membre · `reservations.configure` |

Erreurs : `401` non connecté, `403` sans restaurant sélectionné ou sans la permission, `404` ressource inconnue ou d'un
autre tenant, `409` conflit de concurrence, `422` règle métier (code dans `code`).

Tests : `Reservations.Application.Tests` (fakes), `Reservations.Infrastructure.Tests` (vrai PostgreSQL : verrou de
capacité compris) et `Reservations.Api.Tests` (Docker ; **la migration doit exister**).

## 9. Acompte et liste d'attente

### Acompte (`Deposits/DepositRule.cs`, `Reservation`)

- **Règle par établissement** (`DepositRule`, stockée avec la politique) : `Off` / `FromPartySize` (à partir de N
  couverts) / `Always` ; montant par couvert ou par réservation ; délai de paiement de 5 min à 48 h.
- **À la création** (`CreateReservationCommand`, `BookOnline`, `AcceptWaitlistOffer`) : si la règle s'applique, la
  réservation reste `Pending` avec un acompte `AwaitingPayment` dû avant `min(maintenant + délai, heure du repas)`.
  L'équipe peut créer sans acompte (`withoutDeposit`) ou le dispenser ensuite (`WaiveDeposit`).
- **Paiement** : l'argent vit dans Payments. Le pont de l'hôte relaie `DepositSettlementChanged` vers
  `RecordDepositSettlementCommand` : payé en entier, l'acompte passe `Paid` et la réservation est confirmée.
- **Non payé dans le délai** : `ReservationsDueWorker` annule la réservation (« Acompte non payé dans le délai ») et
  libère la capacité.
- **Cycle de vie** : déduit de l'addition (`Applied`) ; annulation à temps → `RefundRequested` puis `Refunded` ;
  absence ou annulation par l'équipe avec « garder l'acompte » → `Forfeited` (remboursable plus tard via
  `RefundDeposit`).

| Statut | Sens |
|---|---|
| `AwaitingPayment` | demandé, pas encore réglé |
| `Paid` | réglé, à déduire |
| `Applied` | déduit de la commande du repas |
| `RefundRequested` · `Refunded` | remboursement en cours · fait |
| `Forfeited` | gardé par le restaurant |
| `Waived` | dispensé par l'équipe |

### Liste d'attente des créneaux (`Waitlist/WaitlistEntry.cs`)

- Inscription en ligne (si `WaitlistSettings.AcceptsOnline`) ou par l'équipe ; refusée en ligne si le créneau est
  libre (`reservations.waitlist.slot_available`). Téléphone obligatoire, souplesse de 0 à 180 min.
- Quand de la capacité se libère (annulation, absence, report, groupe réduit, offre relâchée), `OfferFreedCapacity`
  propose la table au premier inscrit compatible (pas de 15 min dans sa souplesse). Il a `OfferWindow` (5 à 120 min,
  15 par défaut) pour accepter via son lien.
- **Une offre vivante retient la capacité** : `EvaluateAsync` la compte comme une réservation, sauf pour l'inscrit
  lui-même (`excludedOffer`).
- Offre expirée → `ReleaseOffer` (offres manquées +1 ; au-delà, l'inscription expire) et la place passe au suivant.
- L'anonymisation d'un client efface aussi ses inscriptions.

### API ajoutée

| Méthode | Route | Corps | Permission |
|---|---|---|---|
| `POST` | `/reservations/{id}/deposit/waive` · `/deposit/refund` | — | `reservations.manage` |
| `GET` · `PUT` | `/reservations/deposit-rules/{locationId}` | `{ mode, basis, amount, minPartySize, paymentWindowMinutes }` | membre · `reservations.configure` |
| `GET` · `PUT` | `/reservations/waitlist-settings/{locationId}` | `{ acceptsOnline, offerWindowMinutes }` | membre · `reservations.configure` |
| `GET` · `POST` | `/reservations/waitlist` | `?from&to&locationId` · `{ locationId, desiredStart, partySize, flexibilityMinutes, guestName, guestPhone, notes? }` | membre · `reservations.manage` |
| `POST` | `/reservations/waitlist/{id}/cancel` · `/book` | — · `{ startAt }` | `reservations.manage` |
| `POST` | `/reservations/waitlist/offers?locationId` | — | `reservations.manage` |

`POST /reservations` accepte `withoutDeposit`, `POST /reservations/{id}/cancel` accepte `keepDeposit`.
Migration : `DepositsAndWaitlist` (script `scripts/migrations-acompte-attente.sh`).
