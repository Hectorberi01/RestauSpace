# Module Tables & Floor — Domain

But : représenter les salles, les tables et les sessions de service (occupation d'une table).

## 1. Frontières d'agrégat

| Agrégat | Contenu | Pourquoi |
|---|---|---|
| `DiningArea` (racine) | `RestaurantTable` (entités enfants) | L'unicité du libellé de table se prouve à l'intérieur d'une zone : la zone est l'unité de cohérence du plan de salle. Les tables n'ont pas de vie hors de leur zone. |
| `TableSession` (racine) | — | Une session vit au rythme du service (ouverte/fermée plusieurs fois par jour) alors que le plan de salle change rarement ; les séparer évite de verrouiller toute la zone à chaque installation de clients. |

Références par identifiant uniquement : `LocationId` (Locations), `TableId` depuis `TableSession`, `TableSessionId`
référencé par les commandes (module Orders — plusieurs commandes par session), réservation d'origine par
`ReservationReference` opaque (Reservations). `DiningArea.Create` reçoit une `LocationReference` (tenant, id,
opérationnelle ?) construite par l'Application. `TableSession.Open` reçoit une `TableReference` (tenant, location, id, en
service ?) **obtenue uniquement** via `DiningArea.GetTableReference(tableId)` (constructeur `internal`, propriétés sans
`init` : infalsifiable) ; la session recopie le `LocationId` et tous ses events le portent.

## 2. Classes

| Classe | Rôle DDD | Responsabilité | Transitions | Events | Erreurs |
|---|---|---|---|---|---|
| `DiningArea` | AggregateRoot, tenant-owned | Nom de la zone, ajout/retrait de tables, comportements des tables | — | `DiningAreaCreated`, `DiningAreaRenamed`, `TableAdded`, `TableRemoved`, `TableCapacityChanged`, `TableSetOutOfService`, `TableRestored` | `floor.dining_area.name_required`, `.name_too_long`, `.table_label_already_used`, `.table_not_found`, `.table_has_open_session`, `common.tenant.mismatch`, `common.id.empty` |
| `RestaurantTable` | Entity enfant (mutateurs `internal`) | Libellé, capacité, disponibilité | `Available -> OutOfService` (SetOutOfService) ; `OutOfService -> Available` (Restore) | (levés par la racine) | `floor.table.capacity_not_positive`, `.already_out_of_service`, `.not_out_of_service` |
| `TableLabel` | ValueObject | Libellé en forme canonique (NFKC, sans caractère de contrôle/format, espaces réduits, trimé), 1..30 caractères, comparé sans casse | — | — | `floor.table.label_required`, `.label_too_long` |
| `TableStatus` | Enum | Available / OutOfService | — | — | — |
| `TableSession` | AggregateRoot, tenant-owned | Occupation d'une table (d'une location) pendant un service, avec ou sans réservation | `Open -> Closed` (Close, terminal, refusé tant qu'une commande est ouverte) | `TableSessionOpened`, `TableSessionGuestCountChanged`, `TableSessionClosed` (tous avec `LocationId`) | `floor.table_session.guest_count_not_positive`, `.table_out_of_service`, `.table_already_occupied`, `.closed_is_immutable`, `.has_open_orders`, `.already_closed`, `common.tenant.mismatch` |
| `TableReference` | ValueObject (référence, constructeur `internal`) | Tenant, location, id et état de service de la table visée ; produite seulement par `DiningArea.GetTableReference` | — | — | — |
| `LocationReference` | ValueObject (référence vers Locations, publique) | `(TenantId, LocationId, IsOperational)`, ids non vides | — | — | `common.id.empty` |
| `ReservationReference` | ValueObject (id opaque local) | Identifiant de la réservation pour laquelle la session est ouverte | — | — | `common.id.empty` |
| `ITableOccupancyChecker` | Port | « Cette table a-t-elle une session ouverte ? » | — | — | — |
| `ITableSessionOrdersChecker` | Port | « Cette session a-t-elle encore une commande ouverte ? » | — | — | — |

## 3. Choix faits là où la spec était ouverte

- **Normalisation des libellés et des noms de zone (revue)** : avant tout contrôle, le texte est mis en forme
  canonique : Unicode **NFKC**, suppression des caractères de **contrôle** et de **format** (`UnicodeCategory.Control` /
  `Format` : zero-width space, BOM, soft hyphen...), toute suite de blancs (tabulation, saut de ligne, espace insécable)
  réduite à **un** espace, puis trim. Vide et longueur sont testés **après**. Ainsi `"Terrasse 3"`, `"Terrasse  3"`,
  `"Terras\u200Bse 3"`, les variantes NFC/NFD et pleine chasse sont un seul et même libellé, et un libellé fait
  uniquement de caractères invisibles est refusé (`label_required` / `name_required`). C'est la forme canonique qui est
  stockée et publiée dans les events. Un surrogate isolé devient `U+FFFD`.
- **Libellé unique** dans la zone, comparé sur la forme canonique en majuscules invariantes (`"T1"` = `" t1 "`). Le même libellé est permis dans une autre zone. Un libellé libéré par `RemoveTable` est réutilisable.
- **Comportements de table exposés par la racine** : `ChangeTableCapacity`, `SetTableOutOfService`, `RestoreTable` ; les méthodes de `RestaurantTable` sont `internal`.
- **Pas d'idempotence sur les transitions** : mettre hors service une table déjà hors service, restaurer une table disponible sont des erreurs. En revanche redonner la même valeur (même nom de zone, même capacité, même nombre de couverts) est un no-op sans événement.
- **`RemoveTable(tableId, hasOpenSession, clock)`** : refuse de retirer une table occupée (règle inter-agrégats, booléen issu de `ITableOccupancyChecker`). La table est réellement retirée de la zone ; l'historique reste porté par les `TableSession` (qui ne gardent que le `TableId`) et par l'événement `TableRemoved`.
- **Mise hors service d'une table occupée** : autorisée (la session en cours se termine normalement) ; seule l'ouverture d'une nouvelle session est bloquée.
- **Capacité** : entier > 0, pas de borne haute. Le nombre de couverts d'une session n'est pas borné par la capacité de la table (on ajoute une chaise) : seul `GuestCount > 0` est imposé.
- **Nom de zone** : 1..80 caractères, même forme canonique que les libellés. Son unicité par location n'est pas demandée par la spec et n'est pas imposée.
- **`TableSession.Open(tenantId, table, guestCount, tableHasOpenSession, clock)`** : le `tenantId` vient du contexte tenant, la `TableReference` de l'agrégat chargé ; un écart lève `common.tenant.mismatch`. Un `tenantId` vide ou une `ReservationReference` vide (`default`) lèvent `common.id.empty`.
- **`DiningArea.Create(tenantId, location, name, clock)`** : `LocationReference` au lieu d'un `LocationId` nu ; le tenant de la location doit être celui de la zone (`common.tenant.mismatch`), ids vides refusés (`common.id.empty`). `IsOperational` n'est **pas** exigé : le plan de salle se configure avant l'ouverture de l'établissement. La zone ne stocke que le `LocationId`.
- **`LocationId` sur la session** : recopié de la `TableReference` à l'ouverture, jamais modifié. Porté par
  `TableSessionOpened`, `TableSessionGuestCountChanged`, `TableSessionClosed`, et ajouté aux events de table qui ne
  l'avaient pas (`TableCapacityChanged`, `TableSetOutOfService`, `TableRestored`).
- **Clôture avec commandes ouvertes** (décision produit : plusieurs commandes par session, pas de clôture tant qu'une est
  ouverte) : `Close(hasOpenOrders, clock)` ; `true` lève `floor.table_session.has_open_orders`. Le booléen vient du port
  `ITableSessionOrdersChecker` ; même polarité que `hasOpenSession` / `tableHasOpenSession` (vrai = ce qui bloque).
  « Ouverte » = ni `Completed` ni `Cancelled` côté Orders (un brouillon bloque donc la clôture). Une session déjà fermée
  répond `already_closed` avant de regarder les commandes.
- **Session ouverte pour une réservation** : `Open(..., clock, reservation)` accepte une `ReservationReference?` (id opaque,
  l'identifiant de Reservations étant local à ce module) ; `null` = client sans réservation (`IsWalkIn`). Elle est stockée
  et portée par `TableSessionOpened` et `TableSessionClosed`, pour distinguer l'occupation réservée de l'occupation
  walk-in (qui compte dans la capacité de réservation). Floor ne vérifie ni l'existence ni l'état de la réservation.
- **Session fermée** : `ChangeGuestCount` -> `closed_is_immutable` ; second `Close` -> `already_closed` (pas d'idempotence, l'heure de fermeture initiale est conservée).
- `DiningArea.SeatsInService` : somme des places des tables en service (utile au calcul de capacité côté Application).
- Événements ajoutés : `DiningAreaCreated`, `DiningAreaRenamed`, `TableRemoved`, `TableCapacityChanged`, `TableSetOutOfService`, `TableRestored`, `TableSessionGuestCountChanged`.

## 4. Matrice invariant -> méthode -> test

| Invariant | Méthode | Test |
|---|---|---|
| Nom de zone requis et borné | `DiningArea.Create`, `Rename` | `DiningAreaTests.Create_rejects_a_missing_name`, `Create_enforces_the_name_length_limit`, `Rename_rejects_an_empty_or_too_long_name` |
| Forme canonique des noms de zone ; invisible seul refusé | `DiningArea.Create`, `Rename` | `DiningAreaTests.Area_name_is_stored_in_its_canonical_form`, `Area_name_made_of_invisible_characters_only_is_rejected` |
| Zone : tenant et ids non vides, location du même tenant (opérationnelle ou non) | `DiningArea.Create`, `LocationReference` | `DiningAreaTests.Create_rejects_an_empty_tenant_and_an_empty_location_id`, `Create_rejects_a_location_of_another_tenant`, `Create_accepts_a_location_that_is_not_operational_yet` |
| Renommage | `Rename` | `DiningAreaTests.Rename_changes_the_name_and_raises_DiningAreaRenamed`, `Rename_to_the_same_name_changes_nothing` |
| Libellé de table unique dans la zone (casse/espaces ignorés) | `AddTable` | `DiningAreaTests.AddTable_rejects_a_duplicate_label_ignoring_case_and_spaces`, `AddTable_accepts_the_same_label_in_another_area`, `AddTable_accepts_a_label_freed_by_a_removed_table` |
| Libellé requis et borné | `AddTable` (`TableLabel.Create`) | `DiningAreaTests.AddTable_rejects_a_missing_label`, `AddTable_enforces_the_label_length_limit` |
| Libellés visuellement identiques = doublon (espaces, NFC/NFD, invisibles, pleine chasse) | `AddTable` (`TableLabel.Create`) | `DiningAreaTests.AddTable_rejects_a_label_that_reads_the_same_as_an_existing_one`, `AddTable_rejects_the_decomposed_form_of_an_existing_label`, `TableLabel_is_stored_in_its_canonical_form_and_measured_after_normalisation` |
| Libellé invisible refusé | `AddTable` (`TableLabel.Create`) | `DiningAreaTests.AddTable_rejects_a_label_made_of_invisible_characters_only` |
| Capacité > 0 | `AddTable`, `ChangeTableCapacity` | `DiningAreaTests.AddTable_rejects_a_capacity_that_is_not_positive`, `AddTable_accepts_a_capacity_of_one`, `ChangeTableCapacity_rejects_a_capacity_that_is_not_positive`, `ChangeTableCapacity_updates_the_seats_and_raises_TableCapacityChanged` |
| `TableAdded` levé après ajout valide | `AddTable` | `DiningAreaTests.AddTable_adds_an_available_table_and_raises_TableAdded` |
| On n'agit que sur une table de la zone | `RemoveTable`, `ChangeTableCapacity`, `SetTableOutOfService`, `RestoreTable`, `GetTableReference` | `DiningAreaTests.Table_behaviours_reject_a_table_that_is_not_in_the_area` |
| Pas de retrait d'une table occupée | `RemoveTable` | `DiningAreaTests.RemoveTable_rejects_a_table_with_an_open_session`, `RemoveTable_removes_the_table_and_raises_TableRemoved` |
| `Available -> OutOfService` une seule fois | `SetTableOutOfService` | `DiningAreaTests.SetTableOutOfService_marks_the_table_and_raises_TableSetOutOfService`, `SetTableOutOfService_rejects_a_table_already_out_of_service` |
| `OutOfService -> Available` seulement | `RestoreTable` | `DiningAreaTests.RestoreTable_makes_the_table_available_again_and_raises_TableRestored`, `RestoreTable_rejects_a_table_that_is_not_out_of_service` |
| Référence de table fidèle (tenant, location, état) | `GetTableReference` | `DiningAreaTests.GetTableReference_reflects_tenant_location_id_and_service_status` |
| Référence de table infalsifiable | `TableReference` (ctor `internal`, get-only) | `TableSessionTests.TableReference_cannot_be_built_nor_altered_outside_the_dining_area` |
| Session : tenant et référence de réservation non vides | `TableSession.Open` | `TableSessionTests.Open_rejects_an_empty_tenant_or_an_empty_reservation_reference` |
| Events de table avec `LocationId` | `ChangeTableCapacity`, `SetTableOutOfService`, `RestoreTable` | `DiningAreaTests.ChangeTableCapacity_updates_the_seats_and_raises_TableCapacityChanged`, `SetTableOutOfService_marks_the_table_and_raises_TableSetOutOfService`, `RestoreTable_makes_the_table_available_again_and_raises_TableRestored` |
| La session connaît sa location, ses events la portent | `TableSession.Open`, `ChangeGuestCount`, `Close` | `TableSessionTests.Open_starts_a_session_on_the_table_and_raises_TableSessionOpened`, `Open_uses_the_reference_given_by_the_dining_area`, `ChangeGuestCount_updates_the_count_and_raises_TableSessionGuestCountChanged`, `Close_ends_the_session_and_raises_TableSessionClosed` |
| Pas de clôture avec une commande ouverte | `Close` | `TableSessionTests.Close_rejects_a_session_that_still_has_open_orders`, `Close_is_accepted_once_the_last_order_is_no_longer_open`, `Close_reports_an_already_closed_session_before_looking_at_its_orders` |
| Réservation d'origine stockée et publiée ; walk-in = aucune | `Open`, `Close` | `TableSessionTests.Open_for_a_reservation_keeps_its_reference_and_publishes_it`, `Close_of_a_reserved_session_publishes_the_reservation_reference`, `Open_starts_a_session_on_the_table_and_raises_TableSessionOpened`, `ReservationReference_rejects_an_empty_id_and_compares_by_value` |
| Guest count > 0 | `TableSession.Open`, `ChangeGuestCount` | `TableSessionTests.Open_rejects_a_guest_count_that_is_not_positive`, `ChangeGuestCount_rejects_a_count_that_is_not_positive` |
| Pas de session sur une table hors service | `Open` | `TableSessionTests.Open_rejects_an_out_of_service_table`, `Open_uses_the_reference_given_by_the_dining_area` |
| Une seule session ouverte par table | `Open` | `TableSessionTests.Open_rejects_a_table_that_already_has_an_open_session`, `A_second_session_on_the_same_table_is_refused_while_the_first_is_open_then_accepted_once_closed` |
| Table du même tenant | `Open` | `TableSessionTests.Open_rejects_a_table_of_another_tenant` |
| Ouverture / fermeture lèvent leurs events | `Open`, `Close` | `TableSessionTests.Open_starts_a_session_on_the_table_and_raises_TableSessionOpened`, `Close_ends_the_session_and_raises_TableSessionClosed` |
| Double close refusé | `Close` | `TableSessionTests.Close_rejects_a_second_close_and_keeps_the_first_closing_time` |
| Session fermée immuable | `ChangeGuestCount` | `TableSessionTests.A_closed_session_cannot_change_its_guest_count` |

## 5. Application / Infrastructure

- **Une session ouverte par table** : l'Application interroge `ITableOccupancyChecker` puis appelle `TableSession.Open` dans la même transaction. Le garde-fou final est un index unique filtré en base sur `(TenantId, TableId) WHERE ClosedAtUtc IS NULL` : deux ouvertures simultanées -> la seconde échoue à l'écriture.
- **Clôture de session** : l'Application interroge `ITableSessionOrdersChecker` (implémenté sur les commandes du module
  Orders : existe-t-il une commande de cette session ni `Completed` ni `Cancelled` ?) puis appelle `Close` dans la même
  transaction. Une commande créée en concurrence de la clôture doit être rejetée côté Application (la création d'une
  commande sur une session vérifie que la session est ouverte).
- **Réservation** : quand des clients réservés sont installés, l'Application passe l'id de la réservation
  (`ReservationReference.From(reservationId.Value)`) à `Open` et marque la réservation « installée » de son côté ; Floor
  ne valide pas cette référence. Les consommateurs de `TableSessionOpened`/`Closed` (capacité de réservation) lisent
  `LocationId` et `Reservation` (`null` = walk-in).
- **Retrait de table** : même port, même transaction ; à défaut de verrou, une ouverture de session concurrente du retrait doit être rejetée par une contrainte (clé étrangère logique ou revalidation).
- **Tenant de la location** : `DiningArea.Create` reçoit une `LocationReference` que l'Application construit depuis la location chargée (`new LocationReference(location.TenantId, location.Id, location.IsOperational)`, module Locations) ; le domaine vérifie le tenant.
- **Migration des libellés** : les libellés et noms déjà stockés avant la normalisation doivent être re-normalisés (et dédoublonnés) par un script ; l'index unique éventuel porte sur la forme canonique en majuscules.
- **Unicité du nom de zone par location** : non imposée ; si le produit la veut, ajouter un port + index unique `(TenantId, LocationId, upper(Name))`.
- Verrouillage optimiste (version) sur `DiningArea` (deux `AddTable` concurrents avec le même libellé) et sur `TableSession`.
- Lien avec Reservations (placer une réservation sur une table) et Orders (commande -> `TableSessionId`) : orchestré par l'Application via ids/événements.

## 6. Application (`Floor.Application`)

Le tenant d'un cas d'usage vient **toujours** d'`ITenantContext.RequireTenant()` (claim `tid` du jeton), jamais de la
requête. Sans restaurant sélectionné : `403 common.tenant.required`.

| Cas d'usage | Effet | Erreurs propres à l'Application |
|---|---|---|
| `CreateDiningArea(locationId, name)` | Zone vide pour un établissement du tenant (port `IFloorLocations`, réponse de Locations ; ouvert ou pas encore) | `floor.location.not_found`, `floor.tenant.not_writable` |
| `RenameDiningArea`, `AddTable(label, capacity)`, `ChangeTableCapacity` | Plan de salle (règles de `DiningArea`) | `floor.dining_area.not_found`, `floor.tenant.not_writable` |
| `SetTableOutOfService`, `RestoreTable` | Service : autorisé même si la table est occupée | idem |
| `RemoveTable` | Interroge `ITableOccupancyChecker` : refusé tant qu'une session est ouverte | idem |
| `OpenTableSession(tableId, guestCount, reservationId?)` | Trouve la zone de la table (`FindByTableAsync`), prend `GetTableReference`, interroge l'occupation, ouvre la session | `floor.table.not_found`, `floor.tenant.not_writable` |
| `ChangeGuestCount`, `CloseTableSession` | `Close` interroge `ITableSessionOrdersChecker` (module Orders), sauf pour une session déjà fermée | `floor.table_session.not_found`, idem |
| `GetDiningAreaById`, `ListDiningAreas(locationId?)` | **Plan de salle** : zones par nom, tables dans l'ordre d'ajout, avec la session en cours de chaque table (`openSessionId`) et `seatsInService` | `floor.dining_area.not_found` |
| `GetTableSessionById`, `ListOpenTableSessions(locationId?)` | Sessions (ouvertes : par heure d'ouverture) | `floor.table_session.not_found` |

- **`FloorWorkspace`** porte les étapes communes : tenant courant, tenant **qui accepte les écritures** (port
  `IFloorTenants`, réponse de Tenants : suspendu, fermé ou inconnu = 422 `floor.tenant.not_writable`), chargement
  **dans ce tenant**, sauvegarde. Aucune règle métier hors du domaine.
- **Ressource d'un autre tenant** (zone, table, session, établissement) : `404 …not_found`, exactement comme un id
  inconnu.
- **Permissions** : lecture ouverte à tout membre. `floor.manage` (plan : zones, ajout/retrait/capacité des tables) et
  `floor.serve` (service : sessions, couverts, mise hors service et remise en service). Owner a les deux ; Manager aussi
  pour les tenants créés après l'ajout du module (les rôles système existants ne sont pas réécrits).
- **Commandes ouvertes** : réponse du module Orders via l'hôte (`HasOpenOrdersForTableSessionQuery`) ; un brouillon
  compte comme ouvert.
- **Retrait de table concurrent d'une ouverture de session** : non verrouillé (verrouiller la zone à chaque
  installation sérialiserait tout le service). Dans ce cas rare, la session ouverte reste fermable normalement.
- **Hors périmètre pour l'instant** : placement d'une réservation sur une table et marquage « installée » côté
  Reservations (viendra avec ce module, via `TableSessionOpened`).

## 7. Infrastructure (`Floor.Infrastructure`)

- Schéma `floor` : `dining_areas`, `restaurant_tables`, `table_sessions`, `outbox_messages`.
- **`FloorDbContext : TenantDbContext`** : filtre EF sur le tenant de la requête (rien sans tenant), **en plus** du
  filtre explicite des repositories.
- **Une session ouverte par table** : index unique filtré `(tenant_id, table_id) WHERE closed_at_utc IS NULL`,
  traduit en 422 `floor.table_session.table_already_occupied` (deux installations simultanées).
- **Libellé unique dans la zone** : colonne générée `normalized_label = upper(label)` + index unique
  `(dining_area_id, normalized_label)`, traduit en 422 `floor.dining_area.table_label_already_used`.
- **Ordre des tables** : colonne `position` posée par l'unit of work depuis l'ordre de la liste de la zone.
- **Concurrence optimiste** sur `xmin` des zones et des sessions ; ajouter, modifier ou retirer une table réécrit aussi
  la ligne de la zone (409 pour la seconde de deux modifications simultanées).
- **Migration initiale à générer** (une fois, sur ta machine) :
  `dotnet ef migrations add InitialCreate --project src/Modules/Floor/Floor.Infrastructure --startup-project src/Modules/Floor/Floor.Infrastructure --output-dir Persistence/Migrations`.
  `database update` lit `FLOOR_DB`.

## 8. API (`Floor.Api`)

Groupe `/floor`, protégé par l'hôte avec la policy `tenant-member`. Les créations renvoient `201` + `{ id }`.

| Méthode | Route | Corps | Permission |
|---|---|---|---|
| `POST` | `/floor/dining-areas` | `{ locationId, name }` | `floor.manage` |
| `GET` | `/floor/dining-areas?locationId` · `/floor/dining-areas/{id}` | — | membre |
| `PUT` | `/floor/dining-areas/{id}/name` | `{ name }` | `floor.manage` |
| `POST` | `/floor/dining-areas/{id}/tables` | `{ label, capacity }` | `floor.manage` |
| `PUT` | `/floor/dining-areas/{id}/tables/{tableId}/capacity` | `{ capacity }` | `floor.manage` |
| `DELETE` | `/floor/dining-areas/{id}/tables/{tableId}` | — | `floor.manage` |
| `POST` | `/floor/dining-areas/{id}/tables/{tableId}/out-of-service` · `/restore` | — | `floor.serve` |
| `POST` | `/floor/table-sessions` | `{ tableId, guestCount, reservationId? }` | `floor.serve` |
| `GET` | `/floor/table-sessions?locationId` (ouvertes) · `/floor/table-sessions/{id}` | — | membre |
| `PUT` | `/floor/table-sessions/{id}/guest-count` | `{ guestCount }` | `floor.serve` |
| `POST` | `/floor/table-sessions/{id}/close` | — | `floor.serve` |

Erreurs : `401` non connecté, `403` sans restaurant sélectionné ou sans la permission, `404` ressource inconnue ou d'un
autre tenant, `409` conflit de concurrence, `422` règle métier (code dans `code`).

Tests : `Floor.Application.Tests` (fakes), `Floor.Infrastructure.Tests` et `Floor.Api.Tests` (vrai PostgreSQL,
Docker ; **la migration doit exister**).

## 9. File à la porte (`WaitingParties/WaitingParty.cs`)

- Groupe arrivé sans réservation : nom, couverts, téléphone facultatif, note, attente annoncée.
- `Notify` envoie « votre table est prête » (WhatsApp puis SMS via `WaitingPartyNotified`), 5 fois au plus ;
  téléphone requis.
- `Seat(tableId)` ouvre la session de table ; `Seat` et `Leave` **effacent le téléphone**. Les groupes sortis sont
  purgés après 12 h.

| Méthode | Route | Corps | Permission |
|---|---|---|---|
| `GET` · `POST` | `/floor/waiting-parties` | `?locationId` · `{ locationId, name, partySize, phone?, note?, quotedMinutes? }` | membre · `floor.serve` |
| `POST` | `/floor/waiting-parties/{id}/notify` · `/remove` | — | `floor.serve` |
| `POST` | `/floor/waiting-parties/{id}/seat` | `{ tableId }` | `floor.serve` |

Migration : `WaitingParties`.
