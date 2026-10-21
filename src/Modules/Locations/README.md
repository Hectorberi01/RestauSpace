# Module Locations

Établissements physiques d'un tenant : adresse, fuseau horaire, horaires hebdomadaires, exceptions datées, capacité,
cycle de vie.

## 1. Frontière d'agrégat

- **`RestaurantLocation`** est la seule racine, tenant-owned (`TenantId` fixé à la création, `ITenantOwned`).
- **`ExceptionalSchedule`** est une entité enfant : l'unicité « une exception par date et par location » et la
  cohérence de `IsOpenAt` exigent de voir toutes les exceptions de la location. Factories et état `internal`.
- `Address`, `BusinessHours`, `TimeRange`, `Capacity`, `LocationName`, `CountryCode` sont des value objects ; le
  fuseau est le `TimeZoneId` **du kernel** (non redéclaré).
- Référencé par id uniquement : le tenant (`TenantId`). Réservations, commandes, tables, staff référencent
  `LocationId` (kernel) et ne sont **pas** contenus dans l'agrégat.
- **Ce que les autres modules lisent** : `TenantId`, `Id`, `Status` et `IsOperational` (vrai en `Active` uniquement)
  sont publics. Chaque module consommateur construit **sa propre** `LocationReference` locale à partir de ces
  valeurs (remplie par l'Application) ; Locations n'expose aucun type partagé pour cela.

## 2. Classes

| Classe | Rôle DDD | Responsabilité | Transitions / events | Erreurs |
|---|---|---|---|---|
| `RestaurantLocation` | AggregateRoot | Cycle de vie, paramètres, fuseau, horaires ; `IsOpenAt(date, heure)` (local), `IsOpenAtInstant(instantUtc)`, `IsOpenDuring(créneauUtc)`, `IsOperational` | `Draft -> Active` (Activate) ; `Active -> TemporarilyClosed` (TemporarilyClose) ; `TemporarilyClosed -> Active` (Reopen) ; `Draft/Active/TemporarilyClosed -> Archived` (Archive, terminal). Events : `RestaurantLocationCreated`, `LocationActivated`, `LocationTemporarilyClosed`, `LocationReopened`, `LocationArchived`, `LocationRenamed`, `LocationAddressChanged`, `BusinessHoursChanged`, `LocationCapacityChanged`, `LocationTimeZoneChanged`, `ExceptionalScheduleAdded`, `ExceptionalScheduleRemoved` | `locations.location.archived`, `.only_draft_can_be_activated`, `.only_active_can_be_temporarily_closed`, `.only_temporarily_closed_can_be_reopened`, `.reason_too_long` |
| `ExceptionalSchedule` | Entity (enfant) | Exception datée : jour fermé ou plages spécifiques qui **remplacent** l'hebdo | créée/supprimée par la racine | `locations.exceptional_schedule.date_already_defined`, `.opening_without_range`, `.not_found` |
| `BusinessHours` (+ `WeeklySlot`) | ValueObject | Plages par jour de semaine, sans chevauchement, `IsOpenAt(jour, heure)` | — | `locations.business_hours.ranges_overlap`, `.slot_invalid`, `.day_unknown` |
| `TimeRange` | ValueObject | Plage semi-ouverte `[Start, End)` ; `SameDay` ou `Overnight` | — | `locations.business_hours.range_start_not_before_end`, `.overnight_range_too_long` |
| `Address` | ValueObject | Rue, ville, code postal?, pays ISO, lat/lng? | — | `locations.address.*` |
| `CountryCode` | ValueObject | ISO 3166-1 alpha-2 parmi les 249 codes attribués | — | `locations.address.country_code_unknown` |
| `Capacity` | ValueObject | Nombre de couverts >= 0 | — | `locations.capacity.negative` |
| `LocationName` | ValueObject | Nom trimé, 2 à 100 caractères | — | `locations.location_name.*` |
| `TimeZoneId` | ValueObject **du kernel** | Fuseau IANA de l'établissement, `ToLocal(instant)` | — | `common.time_zone.empty`, `.unknown` |
| `ExceptionalScheduleId` | Id typé local | — | — | `common.id.empty` |
| `LocationStatus` | Enum | Liste fermée | — | — |

## 3. Choix faits là où la spec était ouverte

- **Fuseau par établissement** : `RestaurantLocation.TimeZone` (`TimeZoneId` du kernel) est **obligatoire à
  `Create`** ; l'Application passe par défaut le fuseau du tenant, qui n'est donc qu'un défaut. `ChangeTimeZone`
  (refusé si `Archived`, même valeur = no-op, event `LocationTimeZoneChanged`) ne touche pas aux horaires : ils
  gardent leurs valeurs murales, désormais lues dans le nouveau fuseau. `RestaurantLocationCreated` porte l'id du
  fuseau en `string` (`TimeZoneId`).
- **Heure locale** : `BusinessHours`, `ExceptionalSchedule` et `IsOpenAt(DateOnly, TimeOnly)` travaillent en heure
  murale locale de l'établissement. `IsOpenAtInstant(instantUtc)` convertit avec `TimeZone.ToLocal` puis délègue à
  `IsOpenAt` : les autres modules n'ont plus à connaître le fuseau.
- **`IsOpenDuring(Period créneauUtc)`** : vrai seulement si l'établissement est ouvert **à chaque instant** du
  créneau semi-ouvert `[début, fin)` (faux si non `Active`). Implémentation **par parcours des plages en vigueur**,
  sans échantillonnage : depuis le début du créneau, on cherche la plage en vigueur qui couvre l'heure locale
  (plage de la date, ou queue d'une plage overnight de la veille), on avance jusqu'à sa fin, où une plage qui la
  touche doit prendre le relais ; dès qu'aucune plage ne couvre le curseur, c'est faux. Conséquences : un créneau
  finissant exactement à la fermeture est accepté, une minute de plus est refusée ; un créneau à cheval sur minuit
  dans une plage overnight est accepté ; des plages qui se touchent (14:00 puis 14:00, ou « jusqu'à minuit » puis
  00:00 le lendemain) s'enchaînent ; un trou entre deux plages est refusé même si les deux extrémités sont ouvertes.
- **Changement d'heure (DST)** : si le décalage UTC change avant la fin de la plage suivie, le parcours reprend à
  l'instant exact du changement (trouvé par dichotomie sur `ToLocal`, sans `TimeZoneInfo` dans le module) avec la
  nouvelle heure murale. Le résultat est donc toujours égal à « `IsOpenAtInstant` vrai sur tout le créneau » : une
  plage 22:00-04:00 dure 7 h réelles la nuit du passage à l'heure d'hiver et 5 h la nuit du passage à l'heure
  d'été ; une heure de fermeture lue deux fois (02:30 la nuit d'hiver) ferme à sa **première** occurrence, et
  l'établissement est de nouveau « ouvert » pendant la répétition 02:00-02:30. Hypothèse : au plus un changement
  de décalage pendant une même plage (<= 24 h).
- **Politique overnight (explicite, jamais implicite)** :
  - `TimeRange.SameDay(start, end)` exige `start < end` (sinon `range_start_not_before_end`) ; une plage ne
    franchit jamais minuit par accident (`22:00-02:00` en `SameDay` est rejetée).
  - `TimeRange.Overnight(start, end)` exprime un service qui se termine **le lendemain** à `end` ; elle exige
    `end <= start`, donc 24 h maximum. `18:00 -> 00:00` = « jusqu'à minuit » ; `00:00 -> 00:00` = 24 h/24.
  - Une plage overnight **appartient au jour où elle commence** (le vendredi 22:00-02:00 est le service du vendredi).
  - Plages semi-ouvertes : ouvert à `Start`, fermé à `End`. Deux plages qui se touchent ne se chevauchent pas.
  - Le non-chevauchement est vérifié sur la **semaine circulaire** : l'overnight du vendredi contre le samedi, et
    celui du samedi contre le dimanche.
- **`IsOpenAt` de l'agrégat** : faux si la location n'est pas `Active`. Le planning en vigueur pour une date est
  son exception si elle existe, sinon l'hebdo du jour de semaine. Ouvert si une plage de la date couvre l'heure,
  **ou** si une plage overnight de la veille (planning en vigueur de la veille) court encore. Donc : fermer
  exceptionnellement le vendredi annule aussi son service d'après minuit ; fermer exceptionnellement le samedi
  n'annule **pas** la fin du service du vendredi soir (samedi 00:00-02:00).
- **Exceptions** : deux formes de la méthode `AddExceptionalSchedule` de la spec, pour éviter un booléen ambigu :
  `AddExceptionalClosure(date)` (jour fermé, `IsClosed = true`, aucune plage) et
  `AddExceptionalSchedule(date, plages)` (au moins une plage, sans chevauchement entre elles). `IsClosed` est
  dérivé de l'absence de plage. `RemoveExceptionalSchedule(date)` ajouté pour pouvoir corriger une exception.
  Les dates passées ne sont pas refusées (le domaine ne connaît pas le « aujourd'hui » local).
  Le chevauchement entre une exception overnight et le planning du lendemain n'est pas contrôlé (l'union reste
  correcte pour `IsOpenAt`).
- **Immutabilité réelle des horaires (revue)** : `BusinessHours` stocke un `FrozenDictionary` de listes en lecture
  seule (copies privées). `WeeklySchedule` et `RangesOn` ne peuvent plus être re-castés en collection mutable : toute
  tentative d'écriture lève `NotSupportedException`. C'est ce qui protège l'invariant de non-chevauchement et
  l'instance partagée `BusinessHours.AlwaysClosed` (commune à tous les tenants du processus). Même traitement pour
  `CountryCode.KnownCodes` (`FrozenSet`).
- **Jour de semaine hors enum** : `BusinessHours.RangesOn` et `BusinessHours.IsOpenAt` lèvent
  `locations.business_hours.day_unknown` (auparavant : réponse silencieuse). `BusinessHours.Create` garde
  `slot_invalid` pour un slot au jour inconnu ou sans plage.
- **Élément `null` dans les plages d'une ouverture exceptionnelle** : erreur de programmation ->
  `ArgumentNullException` (et non plus une erreur métier). Une `WeeklySlot` sans plage reste `slot_invalid`.
- **Id vide** : `Create` refuse un `TenantId` vide via `Guard.NotEmpty` (`common.id.empty`).
- `Archived` est terminal, **sans règle de réactivation** : toute mutation lève `locations.location.archived`.
- Paramètres (nom, adresse, fuseau, horaires, capacité, exceptions) modifiables en `Draft`, `Active`, `TemporarilyClosed`.
  Même valeur = no-op sans event.
- `Rename` ajouté (la spec liste `LocationName` sans méthode pour le corriger). Pas d'unicité du nom exigée par la spec.
- `Activate` n'exige pas d'horaires non vides (établissement ouvert uniquement sur exceptions/événements).
- `TemporarilyClose` accepte une raison optionnelle (200 caractères max), effacée à `Reopen`/`Archive`.
- `Address.PostalCode` est **optionnel** (plusieurs pays, dont le Bénin, n'ont pas de code postal). Latitude et
  longitude en `decimal`, fournies ensemble ou pas du tout, bornes incluses (`[-90, 90]`, `[-180, 180]`).
- `Capacity` 0 est valide (vente à emporter, capacité non encore définie). Réduire la capacité ne vérifie pas les
  réservations existantes (autre module).

## 4. Matrice invariant -> méthode -> test

| Invariant | Méthode | Test |
|---|---|---|
| Capacité >= 0 | `Capacity.Create`, `SetCapacity` | `LocationValueObjectsTests.Capacity_rejects_a_negative_value`, `Capacity_accepts_zero_and_positive_values`, `RestaurantLocationTests.SetCapacity_cannot_be_reached_with_a_negative_capacity`, `Create_cannot_be_reached_with_a_negative_capacity_or_inconsistent_hours` |
| Nom non vide, 2..100 | `LocationName.Create` | `LocationValueObjectsTests.LocationName_rejects_an_empty_value`, `LocationName_trims_and_enforces_its_length_limits` |
| Country code ISO valide | `CountryCode.Create` | `AddressTests.Create_rejects_an_unknown_country_code`, `CountryCode_accepts_assigned_iso_codes`, `CountryCode_knows_the_249_assigned_iso_3166_1_codes` |
| Lat/lng cohérents (bornes, paire) | `Address.Create` | `AddressTests.Create_accepts_coordinates_up_to_their_limits`, `Create_rejects_a_latitude_beyond_its_limits`, `Create_rejects_a_longitude_beyond_its_limits`, `Create_rejects_a_latitude_without_longitude_and_conversely` |
| Rue/ville requises, longueurs bornées | `Address.Create` | `AddressTests.Create_rejects_an_empty_street`, `Create_rejects_an_empty_city`, `Create_enforces_field_maximum_lengths`, `Create_accepts_a_missing_postal_code` |
| Début < fin (même jour) | `TimeRange.SameDay` | `BusinessHoursTests.SameDay_rejects_a_range_whose_start_is_not_before_its_end` |
| Overnight explicite, <= 24 h | `TimeRange.Overnight` | `BusinessHoursTests.Overnight_creates_a_range_ending_on_the_next_day`, `Overnight_rejects_a_range_longer_than_24_hours`, `Overnight_range_covers_the_evening_of_its_day_and_the_early_next_day` |
| Pas de chevauchement interne | `BusinessHours.Create` | `BusinessHoursTests.Create_rejects_overlapping_ranges_on_the_same_day`, `Create_rejects_a_range_contained_in_another`, `Create_rejects_a_duplicated_range`, `Create_accepts_touching_ranges` |
| Pas de chevauchement overnight / lendemain / bouclage de semaine | `BusinessHours.Create` | `BusinessHoursTests.Create_rejects_an_overnight_range_overlapping_the_next_day`, `Create_accepts_an_overnight_range_touching_the_next_day_opening`, `Create_rejects_a_saturday_overnight_range_overlapping_sunday_across_the_week_boundary` |
| Jour de semaine et plage valides | `BusinessHours.Create`, `RangesOn`, `IsOpenAt` | `BusinessHoursTests.Create_rejects_an_undefined_day_or_a_missing_range`, `Queries_reject_an_undefined_day_of_week` |
| Horaires non mutables par downcast (y compris `AlwaysClosed`) | `BusinessHours.WeeklySchedule`, `RangesOn` | `BusinessHoursTests.WeeklySchedule_cannot_be_mutated_through_a_downcast`, `Ranges_of_a_day_cannot_be_mutated_through_a_downcast`, `AlwaysClosed_shared_instance_cannot_be_opened_through_a_downcast` |
| Codes pays connus non extensibles par downcast | `CountryCode.KnownCodes` | `AddressTests.CountryCode_known_codes_cannot_be_extended_through_a_downcast` |
| Plage `null` = erreur de programmation | `AddExceptionalSchedule` | `RestaurantLocationTests.AddExceptionalSchedule_treats_a_null_range_as_a_programming_error` |
| `IsOpenAt` cohérent avec la politique overnight | `BusinessHours.IsOpenAt` | `BusinessHoursTests.IsOpenAt_follows_the_ranges_of_the_day`, `IsOpenAt_overnight_range_is_open_after_midnight_on_the_next_day_only`, `IsOpenAt_saturday_overnight_range_spills_into_sunday`, `IsOpenAt_range_until_midnight_closes_exactly_at_midnight`, `Create_accepts_round_the_clock_hours_for_every_day` |
| Création en Draft, tenant fixé, fuseau requis, event (avec id du fuseau) | `RestaurantLocation.Create` | `RestaurantLocationTests.Create_starts_a_draft_location_owned_by_the_tenant`, `Create_raises_RestaurantLocationCreated`, `Create_rejects_an_empty_tenant_id`, `Create_rejects_null_arguments`, `Location_guards_cross_tenant_references` |
| `IsOperational` = Active uniquement (référence pour les autres modules) | `IsOperational`, `Status`, `TenantId`, `Id` | `RestaurantLocationTests.IsOperational_is_true_for_an_active_location_only` |
| Activation depuis Draft uniquement | `Activate` | `RestaurantLocationTests.Activate_moves_a_draft_location_to_active_and_raises_LocationActivated`, `Activate_rejects_an_active_location`, `Activate_rejects_a_temporarily_closed_location` |
| Fermeture temporaire depuis Active uniquement | `TemporarilyClose` | `RestaurantLocationTests.TemporarilyClose_closes_an_active_location_and_raises_LocationTemporarilyClosed`, `TemporarilyClose_rejects_a_draft_location`, `TemporarilyClose_rejects_an_already_closed_location`, `TemporarilyClose_rejects_a_reason_longer_than_the_maximum`, `Temporarily_closed_location_is_never_open_whatever_its_hours` |
| Réouverture depuis TemporarilyClosed uniquement | `Reopen` | `RestaurantLocationTests.Reopen_brings_a_temporarily_closed_location_back_to_active`, `Reopen_rejects_a_location_that_is_not_temporarily_closed` |
| Archived terminal, non réactivable | `Archive` + toutes | `RestaurantLocationTests.Archive_can_be_reached_from_draft_active_and_temporarily_closed`, `Archived_is_terminal_and_cannot_be_reactivated`, `Archived_is_terminal_and_rejects_every_change`, `Archived_location_is_never_open` |
| Horaires cohérents à la modification + event | `SetBusinessHours` | `RestaurantLocationTests.SetBusinessHours_replaces_the_hours_and_raises_BusinessHoursChanged`, `SetBusinessHours_cannot_be_reached_with_overlapping_ranges` |
| Fuseau modifiable sauf Archived, event si changement | `ChangeTimeZone` | `RestaurantLocationTests.ChangeTimeZone_changes_the_zone_and_raises_LocationTimeZoneChanged`, `ChangeTimeZone_is_allowed_while_draft_or_temporarily_closed`, `ChangeTimeZone_moves_the_instants_at_which_the_same_wall_clock_hours_apply`, `Settings_changes_to_the_same_value_raise_nothing`, `Archived_is_terminal_and_rejects_every_change` |
| Paramètres : event seulement si changement réel | `Rename`, `ChangeAddress`, `SetCapacity`, `ChangeTimeZone` | `RestaurantLocationTests.Rename_changes_the_name_and_raises_LocationRenamed`, `ChangeAddress_replaces_the_address_and_raises_LocationAddressChanged`, `SetCapacity_changes_the_capacity_and_raises_LocationCapacityChanged`, `Settings_changes_to_the_same_value_raise_nothing`, `Settings_can_be_changed_while_draft_or_temporarily_closed` |
| Date d'exception unique par location | `AddExceptionalClosure`, `AddExceptionalSchedule` | `RestaurantLocationTests.AddExceptionalSchedule_rejects_a_second_exception_on_the_same_date`, `AddExceptionalSchedule_accepts_the_same_weekday_on_another_date`, `RemoveExceptionalSchedule_then_add_allows_redefining_the_date` |
| Jour fermé exceptionnel | `AddExceptionalClosure` | `RestaurantLocationTests.AddExceptionalClosure_closes_that_date_only`, `AddExceptionalClosure_raises_ExceptionalScheduleAdded` |
| Ouverture exceptionnelle : >= 1 plage, sans chevauchement, remplace l'hebdo | `AddExceptionalSchedule` | `RestaurantLocationTests.AddExceptionalSchedule_replaces_the_weekly_ranges_of_that_date`, `AddExceptionalSchedule_can_open_a_day_that_is_normally_closed`, `AddExceptionalSchedule_rejects_an_opening_without_range`, `AddExceptionalSchedule_rejects_overlapping_ranges`, `AddExceptionalSchedule_rejects_a_reason_longer_than_the_maximum` |
| `IsOpenAt` = statut + exceptions + overnight | `RestaurantLocation.IsOpenAt`, `RangesInForceOn` | `RestaurantLocationTests.IsOpenAt_follows_the_weekly_hours_of_an_active_location`, `IsOpenAt_weekly_overnight_range_stays_open_after_midnight`, `IsOpenAt_is_false_for_a_draft_location`, `Exceptional_closure_cancels_the_overnight_service_starting_that_date`, `Exceptional_closure_does_not_cancel_the_tail_of_the_previous_overnight_service`, `Exceptional_overnight_opening_stays_open_after_midnight_on_the_next_date`, `RangesInForceOn_returns_the_exception_ranges_or_the_weekly_ones` |
| Ouverture à un instant UTC = heure murale du fuseau de la location | `IsOpenAtInstant` | `RestaurantLocationTests.IsOpenAtInstant_reads_the_instant_on_the_location_wall_clock`, `IsOpenAtInstant_accepts_an_instant_given_with_any_offset`, `IsOpenAtInstant_follows_an_overnight_range_across_the_local_midnight`, `IsOpenAtInstant_is_false_unless_the_location_is_active` |
| Créneau ouvert en continu : bornes début/fin | `IsOpenDuring` | `RestaurantLocationTests.IsOpenDuring_accepts_a_slot_matching_the_opening_range_exactly`, `IsOpenDuring_rejects_a_slot_starting_before_the_opening_time`, `IsOpenDuring_rejects_a_slot_overflowing_the_closing_time`, `IsOpenDuring_rejects_a_slot_outside_the_opening_hours`, `IsOpenDuring_rejects_a_null_slot` |
| Créneau : minuit, overnight, plages qui se touchent, trous | `IsOpenDuring` | `RestaurantLocationTests.IsOpenDuring_accepts_a_slot_inside_an_overnight_range_crossing_midnight`, `IsOpenDuring_chains_touching_ranges_including_across_midnight`, `IsOpenDuring_follows_round_the_clock_hours_over_several_days`, `IsOpenDuring_rejects_a_slot_spanning_a_closed_gap_between_two_ranges` |
| Créneau : exceptions et statut | `IsOpenDuring` | `RestaurantLocationTests.IsOpenDuring_rejects_a_slot_on_an_exceptionally_closed_date`, `IsOpenDuring_exceptional_closure_also_cancels_the_part_of_its_overnight_service_after_midnight`, `IsOpenDuring_follows_an_exceptional_opening`, `IsOpenDuring_is_false_unless_the_location_is_active` |
| Créneau : changement d'heure (DST) | `IsOpenDuring` | `RestaurantLocationTests.IsOpenDuring_keeps_following_an_overnight_range_when_clocks_go_back`, `IsOpenDuring_closes_at_the_first_reading_of_a_closing_time_repeated_when_clocks_go_back`, `IsOpenDuring_keeps_following_an_overnight_range_when_clocks_go_forward`, `IsOpenDuring_bridges_two_ranges_separated_only_by_the_hour_skipped_when_clocks_go_forward` |
| Suppression d'exception | `RemoveExceptionalSchedule` | `RestaurantLocationTests.RemoveExceptionalSchedule_restores_the_weekly_hours_and_raises_an_event`, `RemoveExceptionalSchedule_rejects_a_date_without_exception` |

## 5. Application / Infrastructure

- **Fuseau** : à `Create`, passer le fuseau choisi ou, par défaut, celui du tenant (`tenant.TimeZone`). La
  conversion UTC -> heure locale est désormais faite par le domaine (`IsOpenAtInstant`, `IsOpenDuring`) via
  `TimeZoneId.ToLocal` du kernel ; la base tz de l'hôte (ICU/tzdata) doit rester à jour.
- **Reservations / Orders** : appeler `location.IsOpenDuring(slot)` (réservation) ou `IsOpenAtInstant(now)`
  (commande) avant d'agir, et construire les `LocationReference(TenantId, LocationId, IsOperational)` locales des
  modules consommateurs à partir de `location.TenantId`, `location.Id` et `location.IsOperational` (vrai en `Active`
  uniquement) — Reservations exige `IsOperational`, Floor ne vérifie que le tenant.
- Réagir à `LocationTimeZoneChanged` (les réservations existantes sont des instants UTC : elles ne bougent pas,
  mais leur heure locale affichée et leur compatibilité avec les horaires changent).
- Index unique `(LocationId, Date)` sur les exceptions comme garde-fou final ; concurrence optimiste sur la
  location (deux ajouts simultanés pour la même date).
- Vérifier que le tenant accepte les écritures (module Tenants) et les quotas de locations (Billing) avant `Create`.
- Réagir à `LocationTemporarilyClosed` / `LocationArchived` / `BusinessHoursChanged` / `LocationCapacityChanged`
  dans Reservations, Orders, Floor (annulations, notifications) via les events d'intégration.
- Géocodage de l'adresse, purge des exceptions passées, filtrage par `TenantId`.

## 6. Application (`Locations.Application`)

Le tenant d'un cas d'usage vient **toujours** d'`ITenantContext.RequireTenant()` (claim `tid` du jeton), jamais de la
requête. Sans restaurant sélectionné : `403 common.tenant.required`.

| Cas d'usage | Effet | Erreurs propres à l'Application |
|---|---|---|
| `CreateLocation(name, address, seats, timeZoneId?, weeklyHours?)` | Location `Draft` du tenant courant ; fuseau absent = fuseau du tenant | `locations.tenant.not_writable`, `locations.time.invalid` |
| `RenameLocation`, `ChangeLocationAddress`, `SetLocationCapacity`, `ChangeLocationTimeZone`, `SetBusinessHours` | Remplacent un réglage entier | `locations.location.not_found`, `locations.tenant.not_writable`, `locations.time.invalid` |
| `ActivateLocation`, `TemporarilyCloseLocation(reason?)`, `ReopenLocation`, `ArchiveLocation` | Cycle de vie (règles du domaine) | idem |
| `AddExceptionalClosure(date, reason?)`, `AddExceptionalOpening(date, ranges, reason?)`, `RemoveExceptionalSchedule(date)` | Exceptions datées | idem |
| `GetLocationById`, `ListLocations(page, pageSize, includeArchived)` | Lecture, même tenant suspendu ; liste triée par nom, archivées masquées par défaut | `locations.location.not_found`, `locations.paging.invalid` |
| `IsLocationOpenDuring(locationId, startUtc, endUtc)` | Ouvert pendant toute la plage (heure locale, exceptions comprises) ; faux s'il n'est pas opérationnel. Pour Reservations, via l'hôte | `locations.location.not_found` |

- **`LocationEditor`** porte les étapes communes à toute écriture : tenant courant, tenant **qui accepte les
  écritures** (port `ITenantDirectory`, réponse du module Tenants : suspendu, fermé ou inconnu = refus 422
  `locations.tenant.not_writable`), chargement **dans ce tenant**, sauvegarde. Aucune règle métier hors du domaine.
- **Location d'un autre tenant** : `404 locations.location.not_found`, exactement comme un id inconnu (pas d'IDOR,
  pas de fuite d'existence).
- **Heures** : chaînes `"HH:mm"` (heure murale locale de l'établissement), `endsNextDay` pour une plage overnight.
  Jours : noms anglais (`"Monday"`). Dates : `"yyyy-MM-dd"`.
- **Pagination** : `page` >= 1 (max 100 000), `pageSize` 1 à 100 (défaut 20).
- **Permissions** : lecture ouverte à tout membre du restaurant ; toute écriture exige `locations.manage`
  (`LocationsPermissions.Manage`, accordée par les rôles Staff : Owner et Manager l'ont).
- **Hors périmètre pour l'instant** : quota d'établissements (Billing n'a pas encore de persistance).

## 7. Infrastructure (`Locations.Infrastructure`)

- Schéma `locations` : `locations`, `exceptional_schedules`, `outbox_messages`.
- **`LocationsDbContext : TenantDbContext`** : filtre EF sur le tenant de la requête (rien sans tenant), **en plus**
  du filtre explicite du repository (`tenant_id = @tenant AND id = @id`). Deux verrous sur la même porte.
- Value objects sans colonnes naturelles en **jsonb**, relus par les factories du domaine : `address`,
  `weekly_hours`, `exceptional_schedules.ranges`. Index `ix_locations_tenant_id`, index unique
  `(location_id, date)` sur les exceptions.
- **Concurrence optimiste** sur `xmin` de `locations` ; ajouter/retirer une exception réécrit aussi la ligne de la
  location (comme Identity pour ses sessions) : deux ajouts concurrents pour la même date ne passent pas tous les
  deux (409, ou 422 `date_already_defined` si l'index unique tranche).
- Domaine touché : `ExceptionalSchedule._ranges` n'est plus `readonly` (l'ORM le pose au chargement).
- **Migration initiale à générer** (une fois, sur ta machine) :
  `dotnet ef migrations add InitialCreate --project src/Modules/Locations/Locations.Infrastructure --startup-project src/Modules/Locations/Locations.Infrastructure --output-dir Persistence/Migrations`.
  `database update` lit `LOCATIONS_DB`.

## 8. API (`Locations.Api`)

Groupe `/locations`, protégé par l'hôte avec la policy `tenant-member` (utilisateur connecté **et** restaurant
sélectionné dans le jeton). Chaque route d'écriture exige en plus la permission `locations.manage`
(`403` sinon).

| Méthode | Route | Corps |
|---|---|---|
| `POST` | `/locations` | `{ name, address: { street, city, postalCode?, countryCode, latitude?, longitude? }, seats, timeZoneId?, weeklyHours?: [{ day, start, end, endsNextDay }] }` → `201` + `Location` |
| `GET` | `/locations?page&pageSize&includeArchived` | — |
| `GET` | `/locations/{id}` | — |
| `PUT` | `/locations/{id}/name` · `/address` · `/capacity` · `/time-zone` · `/business-hours` | `{ name }` · adresse · `{ seats }` · `{ timeZoneId }` · `{ weeklyHours }` |
| `POST` | `/locations/{id}/activate` · `/temporarily-close` `{ reason? }` · `/reopen` · `/archive` | — |
| `POST` | `/locations/{id}/exceptional-closures` | `{ date, reason? }` |
| `POST` | `/locations/{id}/exceptional-openings` | `{ date, ranges: [{ start, end, endsNextDay }], reason? }` |
| `DELETE` | `/locations/{id}/exceptions/{date}` | — |

Erreurs : `401` non connecté, `403` sans restaurant sélectionné, `404` location inconnue ou d'un autre tenant,
`409` conflit de concurrence, `422` règle métier (code dans `code`).

Tests : `Locations.Application.Tests` (fakes), `Locations.Infrastructure.Tests` et `Locations.Api.Tests` (vrai
PostgreSQL, Docker ; **la migration doit exister**). L'API est testée avec la vraie résolution du tenant et la vraie
policy ; seul le jeton est fabriqué par un schéma de test qui pose les mêmes claims qu'Identity.

