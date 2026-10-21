# Module Kitchen (KDS)

But : transformer une commande acceptée en travail de préparation suivi par poste.

## 1. Frontières d'agrégat

| Racine | Enfants | Référencé par id uniquement |
|---|---|---|
| `KitchenTicket` | `KitchenTicketItem` | `OrderId`, `OrderItemId`, `LocationId`, `StationId` |
| `KitchenStation` | — | `LocationId` |
| `StationRouting` | — (routes = objets-valeur `StationRoute`) | `LocationId`, `StationId`, `ProductId` |

- `KitchenTicket` possède ses lignes : le statut du ticket dépend de l'état de ses lignes (policy de disponibilité), ils changent dans la même transaction.
- `KitchenStation` est une racine séparée : c'est de la configuration, référencée par `StationId` depuis les lignes.
- `StationRouting` est une racine séparée, **une par établissement** : elle dit quel poste prépare quel produit (`ProductId -> StationId`) et porte un poste par défaut obligatoire. C'est de la configuration, modifiée indépendamment des postes et des tickets ; l'unicité des routes par produit et leur nombre borné sont ses invariants. Aucune référence au module Catalog : seul le `ProductId` du noyau est utilisé.
- Aucune référence au module Orders : le ticket est créé depuis `KitchenOrderSnapshot` (copie locale : commande, tenant, établissement, lignes), construit par l'Application quand la commande est acceptée.
- Le lien ligne -> poste passe par `KitchenStationReference`, qui porte tenant, établissement et activité du poste. Son constructeur est `internal` et ses propriétés en lecture seule : elle ne s'obtient que par `KitchenStation.ToReference()` (pas de référence forgée).
- L'établissement d'un poste ou d'un routage arrive par `LocationReference(TenantId, LocationId, IsOperational)` (construite par l'Application depuis le module Locations) ; seul le `LocationId` est stocké.
- `KitchenOrderSnapshot` / `KitchenOrderLine` décrivent un agrégat d'un autre module (Orders) : ils restent publics, construits par l'Application, ids contrôlés.

## 2. Classes

| Classe | Rôle DDD | Responsabilité | Transitions | Events | Erreurs |
|---|---|---|---|---|---|
| `KitchenTicket` | AggregateRoot, tenant-owned | Travail de préparation d'une commande : statut, priorité, lignes, horodatages (`CreatedAtUtc`, `StartedAtUtc`, `ReadyAtUtc`, `CompletedAtUtc`, `CancelledAtUtc`), `CanBeMarkedReady`, `CanBeCompleted` | `Queued -> InProgress` (`Start` ou 1er `MarkItemStarted`) ; `InProgress -> Ready` (`MarkReady`, selon la policy) ; `Ready -> Completed` (toutes les lignes prêtes) ; `Queued/InProgress/Ready -> Cancelled` ; `Completed`, `Cancelled` terminaux | `KitchenTicketCreated`, `KitchenPreparationStarted`, `KitchenTicketItemStarted`, `KitchenTicketItemReady`, `KitchenTicketReady`, `KitchenTicketCompleted`, `KitchenTicketCancelled`, `KitchenTicketPriorityChanged` | `KitchenTicketErrors.*` (`kitchen.ticket.*`, `kitchen.ticket_item.*`), `common.tenant.mismatch` |
| `KitchenTicketItem` | Entity (enfant du ticket) | Ligne à préparer : `OrderItemId` source, nom, variante (`VariantName?`), options (`Modifiers`, liste ordonnée en lecture seule), note libre (`Note?`) et quantité (snapshot), poste, statut. `Start` / `MarkReady` sont `internal`, pilotés par `KitchenTicket.MarkItemStarted` / `MarkItemReady` | `Queued -> Preparing -> Ready` (monotone, sans saut ni retour) | levés par le ticket | `kitchen.ticket_item.not_queued`, `kitchen.ticket_item.not_preparing` |
| `KitchenStation` | AggregateRoot, tenant-owned | Poste de préparation d'un établissement : nom, activité | `Active -> Inactive` (`Deactivate`) ; `Inactive -> Active` (`Activate`) | `KitchenStationCreated`, `KitchenStationActivated`, `KitchenStationDeactivated` | `KitchenStationErrors.*` (`kitchen.station.*`) |
| `StationRouting` | AggregateRoot, tenant-owned | Routage produit -> poste d'un établissement : poste par défaut, routes (une par produit, <= 2000), `StationFor(productId)` (route ou poste par défaut), `Uses(stationId)` ; `Create`, `ChangeDefaultStation`, `SetRoute` (ajout ou remplacement), `RemoveRoute` | — | `StationRoutingCreated`, `StationRoutingDefaultStationChanged`, `StationRouteSet`, `StationRouteRemoved` | `StationRoutingErrors.*` (`kitchen.station_routing.*`), `common.tenant.mismatch`, `common.id.empty` |
| `StationRoute` | ValueObject (record) | Couple `ProductId` / `StationId` | — | — | — |
| `IStationRoutingUniquenessChecker` | Port | Répond « cet établissement n'a-t-il encore aucun routage ? » | — | — | — |
| `TicketReadinessPolicy` | ValueObject (policy) | Part des lignes qui doit être prête pour déclarer le ticket prêt ; défaut `AllItemsReady` (100 %), `AtLeast(1..100)` | — | — | `kitchen.ticket.readiness_percentage_out_of_range` |
| `KitchenOrderSnapshot` | ValueObject | Copie locale de la commande : au moins une ligne, `OrderItemId` distincts, ids non vides, aucune ligne nulle | — | — | `snapshot_without_line`, `snapshot_duplicate_order_item`, `common.id.empty` |
| `KitchenOrderLine` | ValueObject | Ligne du snapshot : nom non vide (<= 200), variante optionnelle (<= 120), options (noms non vides <= 120, 50 au plus, copie défensive, dans l'égalité), note optionnelle (trimée, <= 200), quantité > 0, poste | — | — | `line_name_empty`, `line_name_too_long`, `line_variant_name_too_long`, `line_modifier_name_empty`, `line_modifier_name_too_long`, `line_too_many_modifiers`, `line_note_too_long`, `line_quantity_not_positive` |
| `KitchenTicketLineRouting` | Record (donnée d'event) | `ItemId`, `OrderItemId`, `StationId` d'une ligne, porté par `KitchenTicketCreated.Lines` | — | — | — |
| `LocationReference` | Référence (record public) vers le module Locations | `TenantId`, `LocationId`, `IsOperational` ; ids non vides | — | — | `common.id.empty` |
| `KitchenPriority` | Enum | `Normal = 1`, `High = 2`, `Rush = 3` | — | — | `kitchen.ticket.priority_unknown` |
| `StationName` | ValueObject | Nom trimé, non vide, <= 60 | — | — | `kitchen.station.name_empty`, `kitchen.station.name_too_long` |
| `KitchenStationReference` | Référence (record, constructeur `internal`) | Id + tenant + établissement + activité du poste ; produite uniquement par `KitchenStation.ToReference()` | — | — | — |
| `IKitchenStationNameUniquenessChecker` | Port | Répond « ce nom est-il libre dans cet établissement ? » | — | — | — |
| `KitchenTicketId`, `KitchenTicketItemId`, `StationId`, `StationRoutingId` | Ids typés locaux | — | — | — | `common.id.empty` |

## 3. Choix faits là où la spec était ouverte

- **Policy « items prêts »** : `TicketReadinessPolicy`, fixée à la création du ticket (`CreateFromOrder(..., readinessPolicy)`), défaut `AllItemsReady`. `AtLeast(n)` permet à un établissement d'annoncer un ticket partiellement prêt ; il faut toujours au moins une ligne prête.
- **`Complete` exige toutes les lignes prêtes, quelle que soit la policy** : la policy ne gouverne que `MarkReady`. Un ticket `Ready` sous policy partielle ne peut être remis au service (`Complete`) que lorsque chaque ligne est `Ready` (`kitchen.ticket.items_not_all_ready` sinon) — aucune ligne ne reste figée « jamais préparée » dans un ticket clos. Les lignes restantes se terminent sur le ticket `Ready` ; `CanBeCompleted` l'indique à l'écran. `Cancel` reste possible.
- **Ce que le cuisinier lit** : chaque ligne porte le nom, la variante, les options choisies (dans l'ordre de la ligne de commande) et la note libre, copiés du snapshot à la création du ticket. Bornes = celles d'une ligne Orders : nom <= 200, variante <= 120, option <= 120 (50 au plus), note <= 200. Les trois paramètres sont obligatoires dans `KitchenOrderLine.Create` (valeur nulle / liste vide acceptées) pour que l'Application ne puisse pas les oublier.
- **Events auto-suffisants** : `KitchenTicketCreated.Lines` porte le routage de chaque ligne (`ItemId`, `OrderItemId`, `StationId`) pour alimenter un écran de poste depuis l'event ; `KitchenTicketReady` porte `ReadyItemCount` / `TotalItemCount` (ticket partiellement prêt visible en aval).
- **Établissement** : `KitchenStation.Create` et `StationRouting.Create` prennent une `LocationReference` et vérifient seulement qu'elle est du même tenant (configuration : `IsOperational` n'est pas exigé).
- **`MarkReady` reste une action explicite** : la dernière ligne prête ne fait pas passer le ticket à `Ready`. C'est le passe (chef / expéditeur) qui « bumpe » le ticket, car `KitchenTicketReady` déclenche des effets visibles (notification client, statut de commande). `CanBeMarkedReady` indique à l'écran quand l'action est possible.
- **Ready prématuré** : `MarkReady` sur un ticket `Queued` -> `kitchen.ticket.not_in_progress` ; sur un ticket `InProgress` dont la policy n'est pas satisfaite -> `kitchen.ticket.items_not_ready`.
- **Démarrage** : `Start` démarre le ticket sans ligne ; démarrer la première ligne d'un ticket `Queued` le passe à `InProgress`. `KitchenPreparationStarted` n'est levé qu'une fois.
- **Lignes** : transitions permises tant que le ticket n'est pas terminal (y compris sur un ticket `Ready` sous policy partielle, pour finir les lignes restantes avant `Complete`). `Queued -> Ready` direct est refusé.
- **Annulation** : permise depuis `Queued`, `InProgress` et `Ready`. `Cancel(clock, reason)` accepte un motif optionnel (vide = aucun ; trimé, <= 500 caractères, `kitchen.ticket.cancellation_reason_too_long`), conservé dans `CancellationReason` et porté par `KitchenTicketCancelled.Reason` : l'annulation d'une commande a toujours un motif en amont, une annulation décidée en cuisine peut ne pas en avoir.
- **Priorité** : enum `Normal/High/Rush` ; `ChangePriority` refusé sur un ticket terminal, sans effet ni événement si la priorité est identique.
- **Établissement sur les events du ticket** : tous les events de `KitchenTicket` (y compris `KitchenTicketItemStarted`, `KitchenTicketItemReady`, `KitchenTicketPriorityChanged`) portent `LocationId`, pour que les écrans par établissement filtrent sans recharger le ticket.
- **Routage produit -> poste** : `StationRouting.StationFor(productId)` renvoie le poste routé, sinon le poste par défaut. Le poste par défaut étant obligatoire, **toute ligne de commande est toujours routable**, y compris une commande composée uniquement de boissons ou de produits sans route : il n'existe pas de « commande sans ligne routable » ; un établissement qui veut un poste Bar route ses boissons vers lui, sinon elles arrivent au poste par défaut. `SetRoute` ajoute ou remplace ; `RemoveRoute` renvoie le produit au poste par défaut et refuse un produit sans route (`route_not_found`). Un produit peut être routé explicitement vers le poste par défaut (la route survit alors à un changement de poste par défaut). Borne : `StationRouting.MaxRoutes = 2000` routes (`route_limit_reached`) ; le remplacement d'une route existante reste possible à la limite.
- **Poste visé par le routage** : même tenant (`common.tenant.mismatch`), même établissement (`station_other_location`) et actif (`station_inactive`) au moment où on le désigne.
- **Poste désactivé après coup** : le routage **garde l'id** (rien n'est re-routé en silence vers un autre poste) ; `StationFor` continue de le renvoyer et `KitchenTicket.CreateFromOrder` refuse la ligne (`kitchen.ticket.station_inactive`) tant que le routage n'est pas corrigé ou le poste réactivé. `StationRouting.Uses(stationId)` permet à l'Application de refuser la désactivation d'un poste encore utilisé (voir § 5).
- **Même valeur = no-op silencieux** : `ChangeDefaultStation` vers le poste déjà par défaut, `SetRoute` vers le poste déjà routé et `KitchenStation.Rename` vers le même nom ne modifient rien et ne lèvent rien (sans même contrôler l'activité du poste / la réponse d'unicité) ; `ChangePriority` l'était déjà.
- **Postes à la création du ticket** : chaque ligne doit viser un poste du même tenant, du même établissement et actif. Un poste désactivé ensuite n'invalide pas les tickets existants.
- **Poste** : créé actif ; `Activate` / `Deactivate` refusent les transitions sans effet ; `Rename` est permis sur un poste inactif.
- **Quantité** : entier > 0. **Un ticket par commande** (décision owner) : l'Application crée UN seul ticket, à l'acceptation de la commande ; l'agrégat ne peut pas le prouver seul, la garantie est applicative (voir § 5).
- **Events ajoutés** : `KitchenTicketItemStarted`, `KitchenTicketItemReady`, `KitchenTicketCompleted`, `KitchenTicketCancelled`, `KitchenTicketPriorityChanged`, `KitchenStationCreated/Activated/Deactivated`, `StationRoutingCreated`, `StationRoutingDefaultStationChanged`, `StationRouteSet`, `StationRouteRemoved`.

## 4. Matrice invariant -> méthode -> test

| Invariant | Méthode | Test |
|---|---|---|
| Au moins une ligne | `KitchenOrderSnapshot.Create` | `KitchenOrderSnapshotTests.Snapshot_create_rejects_an_order_without_line` |
| `OrderItemId` distincts | `KitchenOrderSnapshot.Create` | `KitchenOrderSnapshotTests.Snapshot_create_rejects_the_same_order_item_twice` |
| Ids du snapshot non vides | `KitchenOrderSnapshot.Create`, `KitchenOrderLine.Create` | `KitchenOrderSnapshotTests.Snapshot_create_rejects_empty_ids`, `KitchenOrderSnapshotTests.Line_create_rejects_an_empty_order_item_id` |
| Nom de ligne non vide, <= 200 | `KitchenOrderLine.Create` | `KitchenOrderSnapshotTests.Line_create_rejects_an_empty_name`, `KitchenOrderSnapshotTests.Line_create_rejects_a_too_long_name` |
| Variante, options, note : trimées, bornes d'une ligne Orders | `KitchenOrderLine.Create` | `KitchenOrderSnapshotTests.Line_create_keeps_the_trimmed_variant_modifiers_in_order_and_note`, `KitchenOrderSnapshotTests.Line_create_treats_a_blank_variant_or_note_as_none_and_accepts_no_modifier`, `KitchenOrderSnapshotTests.Line_create_accepts_exactly_the_bounds_of_an_order_line`, `KitchenOrderSnapshotTests.Line_create_rejects_a_variant_a_modifier_or_a_note_beyond_its_bound` |
| Option non vide, non nulle | `KitchenOrderLine.Create` | `KitchenOrderSnapshotTests.Line_create_rejects_an_empty_or_null_modifier` |
| Options : copie défensive, lecture seule, dans l'égalité | `KitchenOrderLine` | `KitchenOrderSnapshotTests.Line_copies_its_modifiers_and_does_not_expose_a_mutable_list`, `KitchenOrderSnapshotTests.Line_equality_includes_variant_modifiers_and_note` |
| Aucune ligne nulle | `KitchenOrderSnapshot.Create` | `KitchenOrderSnapshotTests.Snapshot_create_rejects_a_null_line` |
| La ligne du ticket porte variante, options, note | `KitchenTicket.CreateFromOrder` | `KitchenTicketTests.CreateFromOrder_copies_variant_modifiers_and_note_onto_the_items` |
| `KitchenTicketCreated` porte le routage de chaque ligne | `KitchenTicket.CreateFromOrder` | `KitchenTicketTests.KitchenTicketCreated_carries_the_routing_of_every_line_in_ticket_order` |
| Quantité > 0 | `KitchenOrderLine.Create` | `KitchenOrderSnapshotTests.Line_create_rejects_a_non_positive_quantity` |
| Ticket créé `Queued`, lignes `Queued` | `KitchenTicket.CreateFromOrder` | `KitchenTicketTests.CreateFromOrder_builds_a_queued_ticket_with_queued_items_and_raises_KitchenTicketCreated` |
| Poste du même tenant | `KitchenTicket.CreateFromOrder` | `KitchenTicketTests.CreateFromOrder_rejects_a_station_of_another_tenant` |
| Poste du même établissement | `KitchenTicket.CreateFromOrder` | `KitchenTicketTests.CreateFromOrder_rejects_a_station_of_another_location` |
| Poste actif | `KitchenTicket.CreateFromOrder` | `KitchenTicketTests.CreateFromOrder_rejects_an_inactive_station` |
| Priorité connue | `CreateFromOrder`, `ChangePriority` | `KitchenTicketTests.CreateFromOrder_rejects_an_unknown_priority`, `KitchenTicketTests.ChangePriority_rejects_an_unknown_priority` |
| `Start` seulement depuis `Queued` | `KitchenTicket.Start` | `KitchenTicketTests.Start_rejects_a_ticket_already_in_progress`, `KitchenTicketTests.Start_rejects_a_ready_ticket` |
| 1re ligne démarrée => ticket `InProgress` | `KitchenTicket.MarkItemStarted` | `KitchenTicketTests.MarkItemStarted_on_a_queued_ticket_starts_the_item_and_the_ticket` |
| `KitchenPreparationStarted` une seule fois | `Start`, `MarkItemStarted` | `KitchenTicketTests.KitchenPreparationStarted_is_raised_only_once_per_ticket`, `KitchenTicketTests.MarkItemStarted_after_Start_does_not_raise_KitchenPreparationStarted_again` |
| Ligne : transition monotone (pas de redémarrage) | `KitchenTicket.MarkItemStarted` | `KitchenTicketTests.MarkItemStarted_rejects_an_item_that_is_already_preparing_or_ready` |
| Ligne : pas de saut `Queued -> Ready` | `KitchenTicket.MarkItemReady` | `KitchenTicketTests.MarkItemReady_rejects_skipping_the_preparing_step` |
| Ligne : `Ready` définitif, ligne inconnue refusée | `KitchenTicket.MarkItemReady`, `MarkItemStarted` | `KitchenTicketTests.MarkItemReady_rejects_an_item_that_is_already_ready_and_an_unknown_item`, `KitchenTicketTests.MarkItemStarted_rejects_an_unknown_item_and_leaves_the_ticket_queued` |
| Pas de passage automatique à `Ready` | `KitchenTicket.MarkItemReady` | `KitchenTicketTests.Last_item_becoming_ready_does_not_mark_the_ticket_ready_by_itself` |
| Ready seulement si lignes prêtes (ready prématuré) | `KitchenTicket.MarkReady` | `KitchenTicketTests.MarkReady_rejects_a_ticket_whose_items_are_not_all_ready`, `KitchenTicketTests.MarkReady_rejects_a_started_ticket_without_any_ready_item`, `KitchenTicketTests.MarkReady_rejects_a_queued_ticket` |
| Ready quand la policy est satisfaite | `KitchenTicket.MarkReady` | `KitchenTicketTests.MarkReady_moves_the_ticket_to_ready_once_every_item_is_ready_and_raises_KitchenTicketReady`, `KitchenTicketTests.MarkReady_follows_a_partial_readiness_policy` |
| Pas de double `Ready` | `KitchenTicket.MarkReady` | `KitchenTicketTests.MarkReady_rejects_an_already_ready_ticket` |
| Policy : seuil 1..100, au moins une ligne prête | `TicketReadinessPolicy.AtLeast`, `IsSatisfiedBy` | `TicketReadinessPolicyTests.AtLeast_rejects_a_percentage_outside_1_to_100`, `TicketReadinessPolicyTests.No_policy_is_satisfied_without_a_ready_item_or_without_items`, `TicketReadinessPolicyTests.AllItemsReady_requires_every_item` |
| `Completed` seulement après `Ready` | `KitchenTicket.Complete` | `KitchenTicketTests.Complete_rejects_a_queued_ticket`, `KitchenTicketTests.Complete_rejects_a_ticket_in_progress_even_when_all_items_are_ready` |
| `Completed` seulement quand toutes les lignes sont prêtes (policy partielle comprise) | `KitchenTicket.Complete` | `KitchenTicketTests.Complete_rejects_a_ticket_ready_under_a_partial_policy_until_every_item_is_ready`, `KitchenTicketTests.A_ticket_ready_under_a_partial_policy_can_still_be_cancelled` |
| `KitchenTicketReady` porte lignes prêtes / total | `KitchenTicket.MarkReady` | `KitchenTicketTests.MarkReady_moves_the_ticket_to_ready_once_every_item_is_ready_and_raises_KitchenTicketReady`, `KitchenTicketTests.MarkReady_follows_a_partial_readiness_policy` |
| Horodatages d'état = horodatages des events | `CreateFromOrder`, `MarkReady`, `Complete`, `Cancel` | `KitchenTicketTests.State_timestamps_equal_the_event_timestamps` |
| Annulation possible jusqu'à `Completed` | `KitchenTicket.Cancel` | `KitchenTicketTests.Cancel_is_allowed_until_completion` |
| Motif d'annulation optionnel, trimé, <= 500, porté par l'event | `KitchenTicket.Cancel` | `KitchenTicketTests.Cancel_keeps_the_trimmed_reason_and_carries_it_in_the_event`, `KitchenTicketTests.Cancel_treats_a_blank_reason_as_no_reason`, `KitchenTicketTests.Cancel_accepts_a_reason_at_the_limit_and_rejects_a_longer_one` |
| Tout event du ticket porte `LocationId` | toutes les méthodes de `KitchenTicket` | `KitchenTicketTests.Every_ticket_event_carries_the_location` |
| `Completed` / `Cancelled` terminaux | toutes les mutations | `KitchenTicketTests.Closed_ticket_rejects_every_mutation` |
| Priorité modifiable hors état terminal | `KitchenTicket.ChangePriority` | `KitchenTicketTests.ChangePriority_is_allowed_on_in_progress_and_ready_tickets`, `KitchenTicketTests.ChangePriority_to_the_same_priority_raises_nothing` |
| Nom de poste unique par établissement | `KitchenStation.Create`, `Rename` | `KitchenStationTests.Create_rejects_a_name_already_used_in_the_location`, `KitchenStationTests.Rename_rejects_a_name_already_used_in_the_location` |
| Même nom = no-op | `KitchenStation.Rename` | `KitchenStationTests.Rename_to_the_same_name_is_a_no_op_whatever_the_uniqueness_answer` |
| Un routage par établissement | `StationRouting.Create` | `StationRoutingTests.Create_rejects_a_second_routing_for_the_location` |
| Routage créé sans route, poste par défaut obligatoire + event | `StationRouting.Create` | `StationRoutingTests.Create_builds_a_routing_without_route_and_raises_StationRoutingCreated`, `StationRoutingTests.Create_accepts_the_reference_of_a_real_station` |
| Tenant / établissement du routage non vides | `StationRouting.Create`, `LocationReference` | `StationRoutingTests.Create_rejects_empty_tenant_or_location` |
| Établissement du même tenant (opérationnel ou non) | `StationRouting.Create`, `KitchenStation.Create` | `StationRoutingTests.Create_rejects_a_location_of_another_tenant_and_accepts_one_that_is_not_operational`, `KitchenStationTests.Create_rejects_a_location_of_another_tenant_and_accepts_one_that_is_not_operational` |
| Référence de poste = état réel du poste | `KitchenStation.ToReference` | `KitchenStationTests.ToReference_carries_id_tenant_location_and_activity` |
| Poste visé : même tenant, même établissement, actif | `StationRouting.Create`, `ChangeDefaultStation`, `SetRoute` | `StationRoutingTests.Create_rejects_a_default_station_of_another_tenant_or_location_or_inactive`, `StationRoutingTests.ChangeDefaultStation_rejects_a_station_of_another_tenant_or_location_or_inactive`, `StationRoutingTests.SetRoute_rejects_a_station_of_another_tenant_or_location_or_inactive_and_an_empty_product` |
| Changement de poste par défaut + event, même poste = no-op | `StationRouting.ChangeDefaultStation` | `StationRoutingTests.ChangeDefaultStation_replaces_it_and_raises_StationRoutingDefaultStationChanged`, `StationRoutingTests.ChangeDefaultStation_to_the_same_station_is_a_no_op` |
| Une route par produit (ajout ou remplacement) + event | `StationRouting.SetRoute` | `StationRoutingTests.SetRoute_adds_a_route_and_raises_StationRouteSet`, `StationRoutingTests.SetRoute_replaces_the_station_of_an_already_routed_product`, `StationRoutingTests.SetRoute_may_route_a_product_explicitly_to_the_default_station` |
| Même route = no-op | `StationRouting.SetRoute` | `StationRoutingTests.SetRoute_to_the_station_already_routed_is_a_no_op` |
| Nombre de routes borné | `StationRouting.SetRoute` | `StationRoutingTests.SetRoute_rejects_a_new_route_beyond_the_limit_but_still_replaces_existing_ones` |
| Retrait d'une route existante seulement + event | `StationRouting.RemoveRoute` | `StationRoutingTests.RemoveRoute_sends_the_product_back_to_the_default_station_and_raises_StationRouteRemoved`, `StationRoutingTests.RemoveRoute_rejects_a_product_without_route` |
| Toute ligne est routable (route ou poste par défaut) | `StationRouting.StationFor` | `StationRoutingTests.StationFor_returns_the_route_or_falls_back_to_the_default_station`, `StationRoutingTests.An_order_made_only_of_unrouted_products_is_fully_routable_to_the_default_station` |
| Poste désactivé : route conservée, refus à la création du ticket | `StationRouting.StationFor`, `Uses`, `KitchenTicket.CreateFromOrder` | `StationRoutingTests.A_station_deactivated_after_routing_stays_routed_and_is_refused_at_ticket_creation`, `StationRoutingTests.Uses_tells_whether_a_station_is_the_default_one_or_a_route_target` |
| Tenant / établissement non vides | `KitchenStation.Create`, `LocationReference` | `KitchenStationTests.Create_rejects_empty_tenant_or_location` |
| Pas d'activation sans effet | `KitchenStation.Activate` | `KitchenStationTests.Activate_rejects_an_already_active_station` |
| Pas de désactivation sans effet | `KitchenStation.Deactivate` | `KitchenStationTests.Deactivate_rejects_an_already_inactive_station` |
| Nom de poste non vide, <= 60 | `StationName.Create` | `KitchenStationTests.StationName_create_rejects_an_empty_value`, `KitchenStationTests.StationName_create_trims_and_validates` |
| Id non vide | `XxxId.From` | `KitchenStationTests.Module_ids_reject_an_empty_guid_and_round_trip` |

## 5. Ce qui relève de l'Application / Infrastructure

- **Unicité du nom de poste par établissement** : l'Application appelle `IKitchenStationNameUniquenessChecker` (comparaison insensible à la casse, en excluant le poste renommé) et passe le booléen à `Create` / `Rename`. L'index unique en base `(TenantId, LocationId, Name)` reste le garde-fou final.
- **Unicité du routage par établissement** : l'Application appelle `IStationRoutingUniquenessChecker` et passe le booléen à `StationRouting.Create`. L'index unique en base `(TenantId, LocationId)` reste le garde-fou final. L'Application construit la `LocationReference` (tenant, id, `IsOperational` = location active) depuis la location chargée ; l'agrégat contrôle l'appartenance au tenant. L'existence des `ProductId` routés est vérifiée par l'Application.
- **Contrat de création du ticket** : à l'**acceptation** de la commande (événement d'intégration du module Orders), l'Application crée **UN ticket par commande** :
  1. charger le `StationRouting` de l'établissement de la commande (son absence = établissement non configuré : erreur applicative, à traiter avant d'ouvrir les commandes) ;
  2. pour chaque ligne de commande, `StationRouting.StationFor(productId)` donne le `StationId` — toujours une réponse, il n'y a jamais de ligne non routable ;
  3. charger les `KitchenStation` correspondants sous isolation tenant et passer `ToReference()` à `KitchenOrderLine.Create`, avec ce que la ligne de commande expose : nom du produit (<= 200), nom de variante optionnel (<= 120), noms des options (<= 120 chacun, dans l'ordre) et note optionnelle (<= 200) — copiés tels quels, sans troncature ;
  4. `KitchenOrderSnapshot.Create` puis `KitchenTicket.CreateFromOrder`.
- **Un seul ticket par commande / idempotence** (événement d'acceptation rejoué) : `CreateTicketForOrder` renvoie le ticket existant ; index unique `(TenantId, OrderId)` sur les tickets en garde-fou.
- **Désactivation d'un poste** : avant `KitchenStation.Deactivate`, charger le `StationRouting` de l'établissement et refuser (ou faire re-router d'abord) si `Uses(stationId)` ; sinon les commandes contenant les produits concernés échoueront à la création du ticket (`kitchen.ticket.station_inactive`), qui reste le garde-fou final.
- **Produit archivé / supprimé du catalogue** : retirer sa route (`RemoveRoute`) via un handler ; une route orpheline est sans effet métier mais compte dans la borne.
- **Concurrence** : deux postes qui agissent en même temps sur le même ticket (ex. deux `MarkReady`) -> le second est rejeté de façon déterministe par l'état de l'agrégat ; le verrouillage optimiste (version de ligne) est géré par l'Infrastructure.
- **Écrans de poste** : `KitchenTicketCreated.Lines` donne le poste de chaque ligne sans recharger le ticket ; le détail affiché (nom, variante, options, note, quantité) se lit sur `KitchenTicket.Items`.
- **Ticket partiellement prêt** : `KitchenTicketReady.ReadyItemCount < TotalItemCount` signale une annonce partielle ; la remise au service (`Complete`) n'est acceptée qu'une fois toutes les lignes prêtes.
- **Propagation** : `KitchenPreparationStarted` / `KitchenTicketReady` / `KitchenTicketCancelled` sont traduits en événements d'intégration pour Orders et Notifications ; l'annulation d'une commande déclenche `Cancel(clock, reason)` via un handler, en transmettant le motif de l'annulation amont (tronqué à `KitchenTicket.MaxCancellationReasonLength` si le format amont est plus long).
- Persistance : constructeurs `private` sans paramètre réservés à l'ORM ; aucune dépendance EF Core / ASP.NET Core.

## 6. Application (`Kitchen.Application`)

Le tenant d'un cas d'usage vient **toujours** d'`ITenantContext.RequireTenant()` : claim `tid` du jeton pendant une
requête, `BackgroundTenant` du message traité pour le flux par l'outbox. Sans tenant : `403 common.tenant.required`.

| Cas d'usage | Effet | Erreurs propres à l'Application |
|---|---|---|
| `CreateStation(locationId, name)`, `RenameStation`, `ActivateStation` | Nom unique dans l'établissement (insensible à la casse) | `kitchen.location.not_found`, `kitchen.station.not_found`, `kitchen.tenant.not_writable` |
| `DeactivateStation` | Refusé tant que le routage l'utilise (défaut ou cible d'une route) | `kitchen.station.in_use` |
| `CreateStationRouting(locationId, defaultStationId)` | Un routage par établissement | `kitchen.station.not_found` |
| `ChangeDefaultStation`, `SetStationRoute(locationId, productId, stationId)`, `RemoveStationRoute` | Le produit doit exister au Catalog du tenant (port `IKitchenProducts`) | `kitchen.station_routing.not_found`, `kitchen.product.not_found` |
| `CreateTicketForOrder(orderId)` | **Idempotent** : renvoie le ticket existant. Sinon lit la commande (port `IKitchenOrders`, lignes sans prix), route chaque ligne (`StationFor`) et crée le ticket (priorité `Normal`) | `kitchen.order.not_found`, `kitchen.order.not_accepted`, `kitchen.station_routing.not_found` |
| `CancelTicketForOrder(orderId, reason)` | Suit l'annulation de la commande ; sans effet sans ticket ou ticket clos ; motif tronqué à 500 ; **pas** de contrôle « tenant accepte les écritures » | — |
| `StartTicket`, `StartTicketItem`, `MarkTicketItemReady`, `MarkTicketReady`, `CompleteTicket`, `CancelTicket(reason?)`, `ChangeTicketPriority` | Transitions (règles de l'agrégat) | `kitchen.ticket.not_found` |
| `ListStations(locationId)`, `GetStationRouting(locationId)`, `GetTicketById`, `ListOpenTickets(locationId?, stationId?)` | Lectures ; l'écran cuisine trie par priorité décroissante puis ancienneté | `kitchen.station_routing.not_found`, `kitchen.ticket.not_found` |

- **`KitchenWorkspace`** : tenant courant, tenant qui accepte les écritures (port `IKitchenTenants`), établissement
  (port `IKitchenLocations`), chargement dans ce tenant, sauvegarde.
- **Permissions** : lecture ouverte à tout membre ; `kitchen.configure` (postes, routage), `kitchen.operate` (tickets).

## 7. Infrastructure (`Kitchen.Infrastructure`)

- Schéma `kitchen` : `kitchen_stations`, `station_routings`, `kitchen_tickets`, `kitchen_ticket_items`,
  `outbox_messages`. `KitchenDbContext : TenantDbContext` (filtre EF sur le tenant, en plus du filtre des repositories).
- **Unicité** : colonne générée `normalized_name = upper(name)` + index unique `(tenant_id, location_id,
  normalized_name)` (422 `kitchen.station.name_not_unique`) ; index unique `(tenant_id, location_id)` sur les routages
  (422 `kitchen.station_routing.location_already_has_routing`) et `(tenant_id, order_id)` sur les tickets (422
  `kitchen.ticket.already_exists`).
- **jsonb** : routes du routage (`routes`) et options d'une ligne (`modifiers`). Domaine touché :
  `StationRouting._routes` et `KitchenTicketItem._modifiers` ne sont plus `readonly`.
- **Ordre des lignes** : colonne `position` posée par l'unit of work. Index `(tenant_id, location_id, status)` pour
  l'écran cuisine.
- **Concurrence optimiste** sur `xmin` ; une ligne qui change réécrit aussi son ticket : deux postes qui agissent en
  même temps sur le même ticket -> le second reçoit 409.
- **Migration** : `Persistence/Migrations/…_InitialCreate`. `database update` lit `KITCHEN_DB`.

## 8. API (`Kitchen.Api`) et intégration avec Orders

Groupe `/kitchen`, protégé par l'hôte avec la policy `tenant-member`. Les créations renvoient `201` + `{ id }`.

| Méthode | Route | Corps | Permission |
|---|---|---|---|
| `POST` | `/kitchen/stations` | `{ locationId, name }` | `kitchen.configure` |
| `GET` | `/kitchen/stations?locationId` | — | membre |
| `PUT` | `/kitchen/stations/{id}/name` | `{ name }` | `kitchen.configure` |
| `POST` | `/kitchen/stations/{id}/activate` · `/deactivate` | — | `kitchen.configure` |
| `POST` | `/kitchen/routings/{locationId}` | `{ defaultStationId }` | `kitchen.configure` |
| `GET` | `/kitchen/routings/{locationId}` | — | membre |
| `PUT` | `/kitchen/routings/{locationId}/default-station` | `{ stationId }` | `kitchen.configure` |
| `PUT` · `DELETE` | `/kitchen/routings/{locationId}/routes/{productId}` | `{ stationId }` · — | `kitchen.configure` |
| `POST` | `/kitchen/tickets` | `{ orderId }` (rattrapage : commande acceptée avant la configuration) | `kitchen.operate` |
| `GET` | `/kitchen/tickets?locationId&stationId` · `/kitchen/tickets/{id}` | — | membre |
| `POST` | `/kitchen/tickets/{id}/start` · `/ready` · `/complete` | — | `kitchen.operate` |
| `POST` | `/kitchen/tickets/{id}/items/{itemId}/start` · `/ready` | — | `kitchen.operate` |
| `POST` | `/kitchen/tickets/{id}/cancel` | `{ reason? }` (corps facultatif) | `kitchen.operate` |
| `PUT` | `/kitchen/tickets/{id}/priority` | `{ priority: Normal/High/Rush }` | `kitchen.operate` |

**Flux avec Orders** (dans l'hôte, `KitchenOrderFlow`, consommateur d'outbox) :

- `orders.OrderAccepted` -> `CreateTicketForOrder` ; `orders.OrderCancelled` -> `CancelTicketForOrder(motif)` ;
- `kitchen.KitchenPreparationStarted` -> la commande passe `Preparing` ; `kitchen.KitchenTicketReady` -> la commande
  passe `Ready` (via `Preparing` si besoin). Une commande déjà plus loin n'est pas touchée.
- Chaque message est traité dans son propre scope, pour le tenant du message (`BackgroundTenant`). Un refus métier
  (cuisine non configurée, commande déjà avancée, tenant suspendu) est journalisé puis ignoré ; une erreur technique
  remonte et le message est retenté.
- La remise au service (`CompleteTicket`) et l'annulation décidée en cuisine ne changent pas la commande.

Tests : `Kitchen.Application.Tests` (fakes), `Kitchen.Infrastructure.Tests` et `Kitchen.Api.Tests` (vrai PostgreSQL,
Docker ; **la migration doit exister**).
