# Module Inventory — Domain

But : tracer les ingrédients et les mouvements de stock de manière auditable.

## 1. Frontières d'agrégat

| Racine | Enfants | Référencé par id seulement |
|---|---|---|
| `Ingredient` | — | — |
| `InventoryItem` | `StockMovement` (ledger, écritures ajoutées seulement) | `LocationId` (kernel, reçu dans un `LocationReference`), `IngredientId` |
| `Recipe` | `RecipeComponent` (VO) | `ProductId` (kernel, reçu dans un `ProductReference`), `VariantId?` (kernel), `IngredientId` |

- `InventoryItem` est l'unité de cohérence du stock d'**un** ingrédient dans **un** établissement : la décision « stock suffisant ? » et l'écriture du mouvement sont prises ensemble. Le ledger est la vérité, `CurrentQuantity` en est la somme courante **stockée**.
- **L'agrégat ne charge pas son historique.** Le ledger est non borné et aucune règle ne le relit (`CurrentQuantity` n'est jamais recalculée depuis la collection). `Movements` = **les mouvements ajoutés depuis le chargement de l'agrégat**, rien d'autre : l'Infrastructure les insère et **ne réhydrate pas** l'historique (après un rechargement la collection est vide). Chaque `StockMovement` est autonome pour un stockage séparé : il porte `TenantId` et `InventoryItemId`. Invariant vérifiable sur l'agrégat : **quantité après = quantité avant + somme des deltas ajoutés** ; « somme du ledger = stock » se vérifie sur le ledger stocké.
- `Ingredient` est un référentiel par tenant ; les autres agrégats n'en reçoivent qu'un instantané `IngredientReference` (id, tenant, unité de base, actif) pour vérifier tenant / activité / dimension. Il est **non falsifiable** : constructeur `internal`, produit uniquement par `Ingredient.ToReference()`.
- `LocationReference` (tenant, id, `IsOperational`) et `ProductReference` (tenant, id) décrivent des agrégats d'**autres** modules : publics (l'Application les bâtit), ids contrôlés (`Guard.NotEmpty`) et non remplaçables ensuite.
- `Recipe` est séparée du produit (module Catalog) et du stock : elle ne fait que *calculer* une consommation (`ComputeConsumption`), l'Application l'applique ensuite aux `InventoryItem`.
- Un transfert touche deux `InventoryItem` : deux agrégats, deux mouvements, une même référence de transfert. Il passe obligatoirement par le service de domaine `StockTransfer.Execute` ; `TransferOut` / `TransferIn` sont `internal`, un transfert à un seul côté n'est plus exprimable depuis l'extérieur.

## 2. Classes

| Classe | Rôle DDD | Responsabilité | Transitions | Events | Erreurs (`inventory.`) |
|---|---|---|---|---|---|
| `UnitOfMeasure` / `UnitDimension` | Enums | g, kg (masse) ; ml, l (volume) ; pièce (comptage) | — | — | `unit.unknown` |
| `Quantity` | Value Object | Montant décimal ≥ 0, ≤ 1 000 000 000, 6 décimales au plus + unité ; conversion exacte dans une dimension | — | — | `quantity.negative`, `quantity.too_large`, `quantity.too_many_decimals`, `unit.incompatible`, `unit.unknown` |
| `Ingredient` | Aggregate Root | Nom unique par tenant, unité de base figée | `Active -> Inactive` (terminal pour `Rename`) | — | `ingredient.name_required`, `.name_too_long`, `.name_not_unique`, `.inactive`, `.already_inactive` |
| `IngredientReference` | Value Object (snapshot, constructeur `internal`) | Ce que stock et recette ont le droit de savoir d'un ingrédient ; produit seulement par `Ingredient.ToReference()` | — | — | — |
| `LocationReference` | Snapshot d'un autre module (record public) | Tenant + `LocationId` + `IsOperational`, pour `InventoryItem.Create` (contrôle de tenant seulement) | — | — | `common.id.empty` |
| `ProductReference` | Snapshot d'un autre module (record public) | Tenant + `ProductId`, pour `Recipe.Create` | — | — | `common.id.empty` |
| `InventoryItem` | Aggregate Root | Stock, seuil de réappro, policy de stock négatif, ledger | pas d'état ; chaque opération ajoute un mouvement | `StockReceived`, `StockConsumed`, `StockWentNegative`, `WasteRecorded`, `StockAdjusted`, `StockCountConfirmed`, `StockTransferredOut`, `StockTransferredIn`, `LowStockReached` | `item.movement_quantity_not_positive`, `.insufficient_stock`, `.movement_reason_required`, `.movement_reason_too_long`, `.movement_reference_too_long`, `.transfer_reference_required`, `.order_reference_required`, `.not_unique_for_location`, `.negative_stock_policy_unknown`, `.stock_is_negative`, `unit.incompatible`, `ingredient.inactive`, `common.tenant.mismatch`, `common.id.empty` |
| `StockMovement` | Entity (enfant, immuable, autonome) | Écriture du ledger : tenant, item, type (`Receipt`, `Consumption`, `Adjustment`, `Waste`, `Transfer`, `CountConfirmed`), delta signé (zéro uniquement pour `CountConfirmed`), unité, date, référence, motif | aucune | (levés par la racine) | — |
| `NegativeStockPolicy` | Enum (policy) | `Forbid` (défaut) / `Allow` ; ne s'applique pas à `ConsumeForSale` | — | — | — |
| `StockTransfer` | Domain Service (statique, pur) | `Execute(source, destination, quantité, référence, clock)` : même tenant, même ingrédient, deux items distincts, puis sortie + entrée | — | (levés par les deux racines) | `transfer.same_item`, `transfer.ingredient_mismatch`, `common.tenant.mismatch` + erreurs de mouvement |
| `Recipe` | Aggregate Root | Composition d'un produit — ou d'une de ses variantes (`VariantId?`) —, calcul de consommation | pas d'état | `RecipeChanged` (`IngredientAdded` / `QuantityChanged` / `IngredientRemoved`, avec `VariantId?` et la nouvelle `Quantity?`) | `recipe.not_unique_for_product`, `.ingredient_duplicate`, `.ingredient_not_found`, `.quantity_not_positive`, `.too_many_ingredients`, `.units_sold_not_positive`, `unit.incompatible`, `ingredient.inactive`, `common.tenant.mismatch`, `common.id.empty` |
| `RecipeComponent` | Value Object | `IngredientId` + `Quantity` pour une unité vendue | — | — | — |

## 3. Choix faits là où la spec était ouverte

- **Unité du stock** : un `InventoryItem` a une seule unité, fixée à la création, qui doit être *compatible* avec l'unité de base de l'ingrédient (pas forcément identique : la recette parle en g, la réserve en kg). Toute quantité entrante dans une unité compatible est convertie ; une unité d'une autre dimension est rejetée (`inventory.unit.incompatible`).
- **`BaseUnit` non modifiable** : stocks, ledgers et recettes sont exprimés dans cette dimension ; la changer rendrait l'historique ininterprétable. Pour changer de dimension on crée un autre ingrédient.
- **Conversions exactes** : facteurs en puissances de dix sur `decimal`, aucun arrondi. `ConvertTo` et `Multiply` repassent par `Quantity.Create` : un résultat au-delà de la borne ou de 6 décimales est rejeté (`quantity.too_large`, `quantity.too_many_decimals`), jamais tronqué.
- **Précision** : 6 décimales au plus (1 mg exprimé en kg, 1 µl en l). Une quantité plus fine est rejetée à la création : aucun mouvement ne peut donc être écrit sans que le stock bouge réellement. L'égalité de `Quantity` est structurelle (1 kg ≠ 1000 g tant qu'on n'a pas converti).
- **Delta signé** : `Quantity` est toujours ≥ 0 ; le delta du ledger (`StockMovement.QuantityDelta`) et `CurrentQuantity` sont des `decimal` signés dans l'unité de l'item (le stock peut être négatif sous `Allow`).
- **`Adjust` = inventaire physique** : on passe la quantité *comptée*, l'agrégat écrit la différence signée. Motif obligatoire. C'est la seule opération qui corrige ; elle ramène toujours le stock à une valeur ≥ 0, donc la policy ne la bloque jamais.
- **Un comptage qui confirme le stock laisse une trace** : un inventaire physique doit être auditable même quand il ne trouve aucun écart. `Adjust` avec une quantité comptée égale au stock écrit un mouvement de type dédié `CountConfirmed`, de delta zéro (motif obligatoire, référence optionnelle), et lève `StockCountConfirmed` (quantité comptée, motif) — **pas** `StockAdjusted`, ni `LowStockReached` (rien ne franchit rien). Type dédié plutôt que `Adjustment` à delta zéro : la règle « quantité de mouvement > 0 » reste vraie pour tous les autres types et l'exception est lisible dans le ledger ; `StockMovement` impose l'équivalence *delta = 0 ⇔ type `CountConfirmed`*.
- **Stock négatif** : `NegativeStockPolicy` portée par l'item, `Forbid` par défaut. Sous `Forbid`, `Consume` / `RecordWaste` / `TransferOut` au-delà du stock sont rejetés. On ne peut pas repasser à `Forbid` tant que le stock est négatif.
- **Consommation de vente jamais refusée** (`ConsumeForSale`) : la vente a déjà eu lieu, la refuser ferait échouer le handler indéfiniment. Elle est toujours écrite, quel que soit `NegativeStockPolicy`, avec la référence de commande obligatoire. Elle lève `StockConsumed`, `LowStockReached` au franchissement du seuil et `StockWentNegative` au franchissement de zéro (event dédié plutôt qu'un drapeau sur `StockConsumed`, pour ne pas changer cet event). `Consume` (consommation manuelle) continue d'honorer la policy : un item sous `Forbid` peut donc avoir un stock négatif après une vente, et seules les sorties manuelles y restent bloquées jusqu'au prochain `Adjust`.
- **Moment de la consommation** (décision du propriétaire) : au démarrage de la préparation (`OrderPreparationStarted` d'Orders), pas à la prise de commande. **Aucun retour automatique en stock à l'annulation** : c'est une perte ; ce qui est réellement récupérable est constaté à la main par `Adjust` (comptage) ou `RecordWaste`.
- **`LowStockReached`** : levé uniquement au franchissement (stock > seuil avant, ≤ seuil après), y compris quand c'est le seuil qui monte ; jamais tant que le stock reste bas. Un item neuf (stock 0) est déjà « bas » : pas d'événement.
- **Motif** obligatoire pour `RecordWaste` et `Adjust` ; `Reference` optionnelle ailleurs ; référence de transfert obligatoire et identique des deux côtés (type `Transfer`, delta négatif à la source, positif à la destination). `StockTransfer.Execute` vérifie d'abord tout ce qui pourrait faire échouer la destination (conversion dans son unité), de sorte qu'un transfert rejeté laisse les deux items intacts.
- **Events ajoutés** : `StockAdjusted`, `StockCountConfirmed`, `StockTransferredOut`, `StockTransferredIn`, `StockWentNegative`. `Ingredient` ne lève aucun événement (la spec n'en liste pas).
- **`RecipeChanged` porte la nouvelle quantité** (`Quantity?`) pour `IngredientAdded` et `QuantityChanged`, `null` pour `IngredientRemoved`, ainsi que le `VariantId?` de la recette : un consommateur n'a pas à recharger la recette.
- **Recette par produit ou par (produit, variante)** : `Recipe.Create(tenant, ProductReference, isUniqueForProduct, VariantId? variantId = null)`. Sans variante, c'est la recette du produit ; avec, celle d'une variante précise (une grande pizza ne consomme pas la pâte d'une petite). L'unicité couvre le couple (produit, variante). `ComputeConsumption` est inchangé. Le domaine ne sait pas si la variante appartient au produit : c'est le Catalog qui le garantit (voir §5).
- **Établissement** : `InventoryItem.Create` prend un `LocationReference` et ne contrôle que le tenant ; `IsOperational` n'est pas exigé (agrégat de configuration : on paramètre le stock d'un établissement pas encore ouvert ou fermé pour travaux). L'item stocke le `LocationId`.
- **Ids vides refusés** (`common.id.empty`) : tenant de `Ingredient.Create`, `InventoryItem.Create`, `Recipe.Create` ; ids de `LocationReference` et `ProductReference` ; `VariantId` quand il est fourni.
- **Horloge lue une fois par opération** : chaque opération lit l'horloge une fois (mouvement et events portent le même instant) ; `StockTransfer.Execute` lit l'instant une seule fois pour les deux côtés du transfert.
- **Méthodes ajoutées** : `ChangeReorderThreshold`, `ChangeNegativeStockPolicy`, `Recipe.ComputeConsumption`, `Ingredient.ToReference`.
- **Même valeur = no-op silencieux** : `Ingredient.Rename` (même nom), `Recipe.ChangeQuantity` (même quantité), `ChangeReorderThreshold` (même seuil), `ChangeNegativeStockPolicy` (même policy) ne font rien — ni event, ni erreur. Les arguments restent validés (unité, policy connue) et les gardes d'état aussi (ingrédient inactif). **Exception voulue : `Adjust`** — un comptage égal au stock n'est pas un no-op, c'est un fait d'inventaire (voir ci-dessus).
- **Bornes** : nom 100 car., référence 100, motif 250, 100 composants par recette, quantité ≤ 1 000 000 000 et 6 décimales.
- **Références de corrélation** : 100 caractères au plus (`StockMovement.ReferenceMaxLength`) ; une référence venant d'une commande est l'`OrderId` en chaîne (36 caractères).
- **Ledger hors mémoire** : `Movements` ne contient que les mouvements ajoutés depuis le chargement (voir §1) ; `CurrentQuantity` est stockée et jamais recalculée depuis la collection.
- **Unité `Piece`** : les quantités en pièces ne sont **pas** forcées à l'entier — les fractions sont permises (un demi-citron, un quart de gâteau), avec la même précision de 6 décimales que les autres unités. Choix documenté, aucune règle supplémentaire.

## 4. Matrice invariant -> méthode -> test

| Invariant | Méthode | Test |
|---|---|---|
| Quantité ≥ 0, bornée, unité connue | `Quantity.Create` | `QuantityTests.Create_rejects_a_negative_amount`, `Create_accepts_the_maximum_amount_and_rejects_above`, `Create_rejects_an_unknown_unit` |
| Quantité : 6 décimales au plus | `Quantity.Create` | `QuantityTests.Create_accepts_up_to_six_decimals`, `Create_rejects_more_than_six_decimals`, `Create_ignores_trailing_zeros_beyond_six_decimals` |
| Borne et précision tenues après conversion / multiplication | `Quantity.ConvertTo`, `Quantity.Multiply`, `Recipe.ComputeConsumption` | `QuantityTests.ConvertTo_keeps_the_maximum_bound`, `ConvertTo_keeps_the_six_decimal_precision`, `Multiply_keeps_the_maximum_bound`, `RecipeTests.ComputeConsumption_keeps_the_quantity_bound` |
| Conversion exacte dans une dimension | `Quantity.ConvertTo` | `QuantityTests.ConvertTo_converts_exactly_inside_a_dimension`, `ConvertTo_round_trip_loses_nothing` |
| Pas de conversion entre dimensions | `Quantity.ConvertTo` | `QuantityTests.ConvertTo_rejects_another_dimension` |
| Nom d'ingrédient requis / borné | `Ingredient.Create`, `Rename` | `IngredientTests.Create_rejects_a_blank_name`, `Create_accepts_a_name_at_the_limit_and_rejects_a_longer_one`, `Rename_rejects_a_blank_or_too_long_name` |
| Nom unique par tenant | `Ingredient.Create`, `Rename` | `IngredientTests.Create_rejects_a_name_already_used_in_the_tenant`, `Rename_rejects_a_name_already_used_in_the_tenant` |
| Unité de base figée | — (aucun mutateur) | `IngredientTests.Base_unit_cannot_be_changed_after_creation` |
| Même nom = no-op silencieux | `Rename` | `IngredientTests.Rename_to_the_current_name_is_a_silent_no_op` |
| Ingrédient inactif non modifiable / désactivation unique | `Rename`, `Deactivate` | `IngredientTests.Rename_rejects_an_inactive_ingredient`, `Deactivate_rejects_an_already_inactive_ingredient` |
| Unité de l'item compatible avec l'ingrédient | `InventoryItem.Create` | `InventoryItemTests.Create_rejects_a_unit_incompatible_with_the_ingredient_base_unit`, `Create_accepts_a_unit_compatible_with_the_ingredient_base_unit` |
| Ingrédient du même tenant et actif | `InventoryItem.Create` | `InventoryItemTests.Create_rejects_an_ingredient_of_another_tenant`, `Create_rejects_an_inactive_ingredient` |
| Un item par (établissement, ingrédient) | `InventoryItem.Create` | `InventoryItemTests.Create_rejects_a_second_item_for_the_same_location_and_ingredient` |
| Établissement du même tenant (opérationnel ou non) | `InventoryItem.Create` | `InventoryItemTests.Create_rejects_a_location_of_another_tenant`, `Create_accepts_a_location_that_is_not_operational` |
| Ids non vides (tenant, établissement, produit, variante) | `Ingredient.Create`, `InventoryItem.Create`, `Recipe.Create`, `LocationReference`, `ProductReference` | `IngredientTests.Create_rejects_an_empty_tenant`, `InventoryItemTests.Create_rejects_an_empty_tenant_and_an_empty_location`, `RecipeTests.Create_rejects_empty_ids` |
| `IngredientReference` non falsifiable | constructeur `internal`, `Ingredient.ToReference` | `IngredientTests.Reference_cannot_be_built_outside_the_module`, `ToReference_snapshots_identity_tenant_unit_and_activity` |
| Quantité de mouvement > 0 | `Receive`, `Consume`, `ConsumeForSale`, `RecordWaste`, `StockTransfer.Execute` | `InventoryItemTests.Receive_rejects_a_zero_quantity`, `Consume_rejects_a_zero_quantity`, `ConsumeForSale_rejects_a_zero_quantity_an_incompatible_unit_and_a_too_long_reference`, `RecordWaste_rejects_a_zero_quantity_and_an_incompatible_unit`, `Transfer_rejects_a_zero_quantity_an_incompatible_unit_and_a_too_long_reference` |
| Unité compatible convertie, incompatible rejetée | toutes les opérations | `InventoryItemTests.Receive_converts_a_compatible_unit_to_the_item_unit`, `Receive_rejects_an_incompatible_unit`, `Consume_converts_a_compatible_unit_to_the_item_unit`, `Consume_rejects_an_incompatible_unit`, `Adjust_rejects_an_incompatible_unit` |
| Consommation (test obligatoire) | `Consume` | `InventoryItemTests.Consume_decreases_the_stock_and_appends_a_negative_consumption_movement`, `Consume_raises_StockConsumed_with_the_resulting_stock` |
| Consommation de vente jamais refusée, même sous `Forbid` | `ConsumeForSale` | `InventoryItemTests.ConsumeForSale_decreases_the_stock_and_raises_StockConsumed`, `ConsumeForSale_is_never_refused_even_when_negative_stock_is_forbidden`, `ConsumeForSale_on_an_empty_item_goes_negative` |
| `StockWentNegative` au franchissement de zéro seulement | `ConsumeForSale` | `InventoryItemTests.StockWentNegative_is_raised_on_the_crossing_only`, `ConsumeForSale_down_to_exactly_zero_is_not_negative` |
| Référence de commande obligatoire (≤ 100, un `OrderId` y tient) | `ConsumeForSale` | `InventoryItemTests.ConsumeForSale_requires_the_order_reference`, `An_order_id_fits_the_movement_reference` |
| La consommation manuelle honore toujours la policy | `Consume`, `RecordWaste` | `InventoryItemTests.Manual_Consume_still_honours_the_policy_after_a_forced_sale_consumption` |
| Stock négatif interdit sous `Forbid` | `Consume`, `RecordWaste`, `StockTransfer.Execute` | `InventoryItemTests.Consume_beyond_the_stock_is_rejected_when_negative_stock_is_forbidden`, `RecordWaste_beyond_the_stock_is_rejected_when_negative_stock_is_forbidden`, `Transfer_beyond_the_stock_is_rejected_when_negative_stock_is_forbidden` |
| Stock négatif permis sous `Allow` | idem | `InventoryItemTests.Consume_beyond_the_stock_goes_negative_when_negative_stock_is_allowed`, `RecordWaste_beyond_the_stock_is_accepted_when_negative_stock_is_allowed`, `Transfer_beyond_the_stock_is_accepted_when_negative_stock_is_allowed` |
| Concurrence métier : dernière unité | `Consume` | `InventoryItemTests.Consume_of_the_last_units_twice_rejects_the_second_attempt` |
| Ajustement (test obligatoire) = quantité comptée, delta signé | `Adjust` | `InventoryItemTests.Adjust_sets_the_stock_to_the_counted_quantity_with_a_negative_delta`, `Adjust_sets_the_stock_to_the_counted_quantity_with_a_positive_delta`, `Adjust_brings_a_negative_stock_back_to_the_counted_quantity` |
| Ajustement motivé | `Adjust` | `InventoryItemTests.Adjust_requires_a_reason`, `Adjust_rejects_a_too_long_reason` |
| Comptage égal au stock = trace auditable (`CountConfirmed`, delta 0, `StockCountConfirmed`, pas de `StockAdjusted`), motif exigé | `Adjust` | `InventoryItemTests.Adjust_to_a_count_equal_to_the_current_stock_records_the_confirmation`, `Adjust_confirming_an_empty_or_low_stock_is_recorded_without_any_other_event`, `Adjust_confirming_the_stock_still_requires_a_reason` |
| Delta zéro ⇔ type `CountConfirmed` | `StockMovement.Create` | `InventoryItemTests.Only_a_count_confirmation_carries_a_zero_delta` |
| Perte motivée | `RecordWaste` | `InventoryItemTests.RecordWaste_requires_a_reason` |
| Transfert : même quantité, référence obligatoire et partagée | `StockTransfer.Execute` | `InventoryItemTests.Transfer_requires_a_transfer_reference`, `Transfer_moves_the_same_quantity_under_the_same_reference`, `Transfer_converts_the_quantity_to_each_item_unit`, `Transfer_raises_StockTransferredOut_on_the_source`, `Transfer_raises_StockTransferredIn_on_the_destination` |
| Transfert : même tenant, même ingrédient, deux items distincts | `StockTransfer.Execute` | `InventoryItemTests.Transfer_rejects_items_of_different_tenants`, `Transfer_rejects_items_of_different_ingredients`, `Transfer_rejects_the_same_item_on_both_sides` |
| Transfert tout ou rien (rejet = deux items intacts) | `StockTransfer.Execute` | `InventoryItemTests.Transfer_rejected_by_the_destination_unit_leaves_the_source_untouched`, `Transfer_beyond_the_stock_is_rejected_when_negative_stock_is_forbidden` |
| Pas de transfert à un seul côté | `TransferOut`, `TransferIn` (`internal`) | `InventoryItemTests.A_one_sided_transfer_cannot_be_written_from_outside` |
| `LowStockReached` au franchissement seulement | toutes les sorties, `ChangeReorderThreshold` | `InventoryItemTests.LowStockReached_is_raised_when_the_stock_falls_exactly_to_the_threshold`, `LowStockReached_is_not_raised_again_while_the_stock_is_already_low`, `LowStockReached_is_raised_again_after_a_replenishment_and_a_new_crossing`, `LowStockReached_is_not_raised_while_the_stock_stays_above_the_threshold` |
| Quantité après = quantité avant + somme des deltas ajoutés (longue séquence aléatoire, rechargements simulés) ; stock = somme du ledger stocké | `AppendAt` (chemin d'écriture unique) | `InventoryItemTests.Quantity_moves_by_exactly_the_appended_deltas_over_a_long_random_sequence`, `Current_quantity_always_equals_the_sum_of_the_ledger_deltas`, `A_rejected_operation_leaves_the_ledger_and_the_projection_untouched` |
| Aucune règle ne relit l'historique | toutes | `InventoryItemTests.Operations_do_not_need_the_history_in_memory` |
| Mouvement autonome (tenant + item) | `StockMovement` | `InventoryItemTests.Quantity_moves_by_exactly_the_appended_deltas_over_a_long_random_sequence`, `StockMovementTests.A_movement_carries_its_tenant_and_its_item` |
| Un seul instant pour les deux côtés d'un transfert | `StockTransfer.Execute` | `InventoryItemTests.Transfer_stamps_both_sides_with_the_same_instant` |
| Policy : pas de `Forbid` sur stock négatif | `ChangeNegativeStockPolicy` | `InventoryItemTests.ChangeNegativeStockPolicy_cannot_forbid_while_the_stock_is_negative` |
| Même seuil / même policy = no-op silencieux | `ChangeReorderThreshold`, `ChangeNegativeStockPolicy` | `InventoryItemTests.ChangeReorderThreshold_to_the_same_threshold_is_a_silent_no_op`, `ChangeNegativeStockPolicy_to_the_current_policy_is_a_silent_no_op` |
| Mouvement immuable (test obligatoire) | `StockMovement` | `StockMovementTests.StockMovement_exposes_no_public_or_internal_setter`, `StockMovement_exposes_no_public_or_internal_mutating_method`, `A_movement_keeps_its_values_whatever_happens_to_the_item_afterwards`, `A_mistake_is_corrected_by_a_new_movement_not_by_editing_the_ledger` |
| Date du mouvement = horloge | toutes les opérations | `InventoryItemTests.Movements_are_timestamped_by_the_clock_in_order` |
| Une recette par (produit, variante) | `Recipe.Create` | `RecipeTests.Create_rejects_a_second_recipe_for_the_same_product`, `Create_rejects_a_second_recipe_for_the_same_product_and_variant`, `Create_can_target_one_variant_of_the_product` |
| Produit du même tenant | `Recipe.Create` | `RecipeTests.Create_rejects_a_product_of_another_tenant` |
| Ingrédient unique dans la recette (test « doublon ») | `Recipe.AddIngredient` | `RecipeTests.AddIngredient_rejects_a_duplicate_ingredient` |
| Quantité de recette > 0 | `AddIngredient`, `ChangeQuantity` | `RecipeTests.AddIngredient_rejects_a_zero_quantity`, `ChangeQuantity_rejects_a_zero_quantity` |
| Unité du composant compatible | `AddIngredient`, `ChangeQuantity` | `RecipeTests.AddIngredient_rejects_a_unit_incompatible_with_the_ingredient_base_unit`, `ChangeQuantity_rejects_a_unit_of_another_dimension` |
| Ingrédient du même tenant et actif | `AddIngredient` | `RecipeTests.AddIngredient_rejects_an_ingredient_of_another_tenant`, `AddIngredient_rejects_an_inactive_ingredient` |
| `RecipeChanged` à chaque changement, jamais sur rejet ni no-op ; porte la nouvelle quantité (ou `null` au retrait) | `AddIngredient`, `ChangeQuantity`, `RemoveIngredient` | `RecipeTests.Every_change_raises_its_own_RecipeChanged`, `ChangeQuantity_to_the_same_quantity_is_a_silent_no_op`, `AddIngredient_adds_the_component_and_raises_RecipeChanged`, `ChangeQuantity_replaces_the_quantity_and_raises_RecipeChanged`, `RemoveIngredient_removes_the_component_and_raises_RecipeChanged` |
| Consommation pour N ventes, calcul pur | `Recipe.ComputeConsumption` | `RecipeTests.ComputeConsumption_multiplies_each_component_by_the_units_sold`, `ComputeConsumption_is_pure`, `ComputeConsumption_rejects_non_positive_units_sold` |

## 5. Ce qui relève de l'Application / Infrastructure

- **Unicité** : ports `IIngredientNameUniquenessChecker` (nom, insensible à la casse, par tenant), `IInventoryItemUniquenessChecker` (établissement + ingrédient), `IRecipeUniquenessChecker` (produit + variante, `VariantId?`). Le domaine reçoit la réponse en `bool` ; l'index unique en base est le garde-fou final — pour les recettes, `(TenantId, ProductId, VariantId)` en traitant la variante nulle comme une valeur (deux index filtrés, ou `NULLS NOT DISTINCT`), sinon deux recettes « produit » pourraient coexister.
- **Persistance du ledger** : table propre, en insertion seule, une ligne par `StockMovement` avec `TenantId` et `InventoryItemId`. À l'enregistrement d'un item, l'Infrastructure **insère** `Movements` (les mouvements ajoutés depuis le chargement) et met à jour `CurrentQuantity` dans la même transaction ; au chargement elle **ne réhydrate pas** la collection. `Movements` n'est donc pas une vue de l'historique : pour l'afficher (journal, audit d'inventaire, lignes `CountConfirmed`), lire la table.
- **Concurrence** : deux consommations simultanées du même item doivent être sérialisées (version optimiste sur `InventoryItem` ou verrou de ligne) ; le domaine garantit seulement que la seconde tentative sur l'état à jour est rejetée.
- **Transfert** : l'Application génère la référence de transfert, charge les deux `InventoryItem`, appelle `StockTransfer.Execute` et persiste les deux agrégats **dans la même transaction** ; elle garantit qu'une référence n'est pas rejouée.
- **Vente** : sur `OrderPreparationStarted` (module Orders), pour chaque ligne chercher **d'abord la recette de la variante** (`ProductId` + `VariantId` de la ligne, quand elle en porte un), **puis, à défaut, la recette du produit** (`VariantId` nul) ; pas de recette = pas de consommation. Appeler `ComputeConsumption(n)`, puis `ConsumeForSale` sur chaque `InventoryItem` de l'établissement avec l'`OrderId` en chaîne comme référence. Le handler ne peut pas être refusé pour stock insuffisant ; il doit être idempotent par commande (inbox / événement traité une fois), le domaine ne déduplique pas les consommations d'une même commande (plusieurs lignes peuvent consommer le même ingrédient).
- **Annulation d'une commande** : aucun handler de retour en stock. L'opérateur constate le réel par `Adjust` ou `RecordWaste`.
- **`StockWentNegative`** : à router vers une alerte / une demande de comptage.
- **Cohérence des ids** : l'Application construit `LocationReference` et `ProductReference` à partir des agrégats chargés sous isolation tenant (le domaine compare leur tenant à celui de l'agrégat créé). Elle vérifie aussi qu'un `VariantId` appartient bien au produit (Catalog) : le domaine le stocke tel quel.
- **Contrôle périodique** : `CurrentQuantity` = somme des `QuantityDelta` de la table ledger pour l'item ; un job peut le vérifier (le domaine ne recalcule jamais le stock depuis la collection).
- **`StockCountConfirmed`** : à router vers le journal d'inventaire (preuve qu'un comptage a eu lieu, sans écart).

## 6. Application (`Inventory.Application`)

Le tenant d'un cas d'usage vient **toujours** d'`ITenantContext.RequireTenant()` (claim `tid` en requête,
`BackgroundTenant` pour un message traité en arrière-plan). Une écriture exige un tenant qui accepte les écritures
(`inventory.tenant.not_writable`), sauf la consommation d'une commande : la vente a déjà eu lieu.

| Cas d'usage | Effet | Erreurs propres à l'Application |
|---|---|---|
| `CreateIngredient(name, baseUnit)`, `RenameIngredient`, `DeactivateIngredient` | Référentiel des ingrédients | `inventory.ingredient.not_found` |
| `CreateInventoryItem(locationId, ingredientId, unit?, reorderThreshold?, negativeStockPolicy)` | Article d'un ingrédient actif dans un établissement du restaurant (unité de l'ingrédient par défaut, seuil 0) | `inventory.location.not_found`, `inventory.ingredient.not_found` |
| `ReceiveStock`, `ConsumeStock`, `RecordWaste`, `CountStock` (inventaire physique) | Mouvements ; une quantité `{ amount, unit? }` sans unité est dans l'unité de l'article | `inventory.item.not_found`, `inventory.quantity.required` |
| `TransferStock(source, destination, quantity, reference?)` | Les deux articles, une transaction ; référence `TR-...` générée si absente | `inventory.item.not_found` |
| `ChangeReorderThreshold`, `ChangeNegativeStockPolicy` | Paramètres de l'article | idem |
| `CreateRecipe(productId, variantId?)`, `AddRecipeIngredient`, `ChangeRecipeQuantity`, `RemoveRecipeIngredient` | Recette d'un produit du Catalog ou d'une de SES variantes | `inventory.product.not_found`, `inventory.product.variant_not_found`, `inventory.recipe.not_found` |
| `ConsumeStockForOrder(orderId)` | Recette de la variante, sinon du produit ; une sortie `ConsumeForSale` par ingrédient (lignes additionnées dans l'unité de l'article), référence = id de la commande ; **une fois par commande** (`order_consumptions`) ; produits sans recette et ingrédients sans article seulement signalés | `inventory.order.not_found`, `inventory.order.not_in_preparation`, `inventory.order.already_consumed` (course perdue) |
| `ListIngredients(includeInactive)`, `GetIngredientById`, `ListInventoryItems(locationId?, lowStockOnly)`, `GetInventoryItemById`, `ListStockMovements(itemId, take ≤ 500)`, `ListRecipes(productId?)`, `GetRecipeById` | Lectures, possibles pour un restaurant suspendu | — |

- **Permissions** : lecture ouverte à tout membre ; `inventory.manage` (ingrédients, articles, seuils, recettes) ;
  `inventory.operate` (mouvements, transferts, consommation d'une commande).
- **Ports fournis par l'hôte** : `IInventoryTenants`, `IInventoryLocations`, `IInventoryProducts` (produit + variantes),
  `IInventoryOrders` (lignes produit / variante / quantité, préparation commencée ou non).

## 7. Infrastructure (`Inventory.Infrastructure`)

- Schéma `inventory` : `ingredients` (nom normalisé en majuscules, index unique), `inventory_items` (seuil en deux
  colonnes, index unique tenant + établissement + ingrédient), `stock_movements` (registre en insertion seule),
  `recipes` (deux index uniques partiels : produit sans variante, produit + variante), `recipe_ingredients`,
  `order_consumptions` (clé tenant + commande), `outbox_messages`. Contraintes CHECK sur les unités, la policy, le seuil,
  les types de mouvement (delta nul seulement pour `CountConfirmed`) et les quantités de recette.
- **Concurrence** : version `xmin` sur les ingrédients, articles et recettes ; changer une ligne de recette réécrit aussi
  la recette ; deux mouvements simultanés sur un article ne partent jamais du même stock.
- **Migration** : `dotnet ef migrations add InitialCreate --project src/Modules/Inventory/Inventory.Infrastructure --startup-project src/Modules/Inventory/Inventory.Infrastructure --output-dir Persistence/Migrations` (`database update` lit `INVENTORY_DB`).

## 8. API (`Inventory.Api`) et hôte

| Méthode | Route | Corps | Permission |
|---|---|---|---|
| `GET` / `POST` | `/inventory/ingredients` (`?includeInactive`) | `{ name, baseUnit }` -> `{ id }` | membre / `inventory.manage` |
| `GET` · `PUT` · `POST` | `/inventory/ingredients/{id}` · `/{id}/name` · `/{id}/deactivate` | `{ name }` | membre / `inventory.manage` |
| `GET` / `POST` | `/inventory/items` (`?locationId&lowStockOnly`) | `{ locationId, ingredientId, unit?, reorderThreshold?, negativeStockPolicy? }` | membre / `inventory.manage` |
| `GET` | `/inventory/items/{id}` · `/{id}/movements?take` | — | membre |
| `POST` | `/inventory/items/{id}/receipts` · `/consumptions` | `{ quantity: { amount, unit? }, reference? }` | `inventory.operate` |
| `POST` | `/inventory/items/{id}/waste` · `/counts` | `{ quantity \| countedQuantity, reason, reference? }` | `inventory.operate` |
| `PUT` | `/inventory/items/{id}/reorder-threshold` · `/negative-stock-policy` | `{ threshold }` · `{ policy }` | `inventory.manage` |
| `POST` | `/inventory/transfers` | `{ sourceItemId, destinationItemId, quantity, reference? }` | `inventory.operate` |
| `GET` / `POST` | `/inventory/recipes` (`?productId`) · `/inventory/recipes/{id}` | `{ productId, variantId? }` | membre / `inventory.manage` |
| `POST` · `PUT` · `DELETE` | `/inventory/recipes/{id}/ingredients` · `/{ingredientId}` | `{ ingredientId, quantity }` · `{ quantity }` | `inventory.manage` |
| `POST` | `/inventory/orders/{orderId}/consumption` | — -> `{ alreadyConsumed, movementCount, productsWithoutRecipe, ingredientsWithoutStockItem }` | `inventory.operate` |

- **Hôte** (`InventoryBridges.cs`) : Tenants, Locations, Catalog (`GetProductByIdQuery`, variantes), Orders
  (`GetOrderLinesQuery`) ; `InventoryOrderFlow` : `orders.OrderPreparationStarted` -> `ConsumeStockForOrder` par
  l'outbox (refus métier journalisé et ignoré, erreurs techniques retentées).
- **Hors périmètre pour l'instant** : alertes de seuil bas et de stock négatif (module Notifications), retour en stock
  d'une commande annulée (volontairement aucun : l'inventaire constate le réel), coût des ingrédients, fournisseurs et
  bons de commande.
