# Module Orders — Domain

Cœur transactionnel des ventes : lignes, calcul des montants, transitions d'état, vérité historique (snapshots).

## 1. Frontière d'agrégat

- **`Order` (racine)** possède ses `OrderItem` (entités enfants) et ses `OrderDiscount` (VO). Tout ce qui influence un total
  est dans la même frontière : les totaux sont une fonction pure du contenu de l'agrégat, recalculée à chaque mutation.
- **`OrderItem`** n'existe et ne change que via `Order` (factory et mutateurs `internal`).
- **Par id uniquement** : `LocationId`, `CustomerId`, `TableSessionId`, `ProductId`, `VariantId`, `ModifierId`. Aucune
  navigation vers Catalog, Floor, Customers, Promotions. À la création, la location, le client et la session de table
  arrivent sous forme de **références locales** (`LocationReference`, `CustomerReference`, `TableSessionReference`) qui
  portent le tenant et l'état de l'agrégat d'origine ; la commande les vérifie puis ne garde que les ids. `Payment` référence `OrderId` mais vit dans son propre module : ce qu'il a
  encaissé arrive à `Complete` sous la forme d'un VO local `OrderSettlement`.
- **Plusieurs commandes par session de table** : rien dans `Order` n'impose l'unicité de `TableSessionId` (voir §4).
- **Snapshots** : `ProductSnapshot` (nom, variante vendue, prix unitaire, `TaxSnapshot`, options) est copié sur la ligne à
  l'ajout. Rien n'est relu plus tard : un changement de carte ou de taux ne modifie jamais une commande existante.
- **Atomicité** : tout mutateur de lignes/remises passe par `Order.Change`, qui restaure lignes, remises et totaux à
  l'identique si la mutation ou le recalcul lève. Une commande n'est jamais laissée à moitié modifiée.
- **Sortie vers les autres modules** : `Order.DescribeLines()` (voir §6) — jamais d'entité dans un event.

## 2. Classes

| Classe | Rôle DDD | Responsabilité | Events | Erreurs (`orders.*`) |
|---|---|---|---|---|
| `Order` | AggregateRoot, tenant-owned | Lignes, remises, totaux (dans son `PriceTaxMode`), cycle de vie, clôture seulement si soldée | `OrderCreated`, `OrderPlaced`, `OrderAccepted`, `OrderPreparationStarted`, `OrderReady`, `OrderCompleted`, `OrderCancelled` | `order.currency_mismatch`, `order.type_unknown`, `order.price_tax_mode_unknown`, `order.settlement_order_mismatch`, `order.not_settled`, `common.tenant.mismatch`, `common.id.empty`, `order.location_not_operational`, `order.customer_anonymized`, `order.table_session_requires_dine_in`, `order.table_session_location_mismatch`, `order.table_session_not_open`, `order.too_many_discounts`, `order.finalized`, `order.not_editable`, `order.invalid_transition`, `order.empty_cannot_be_placed`, `order.item_not_found`, `order.too_many_items`, `order.discount_already_applied`, `order.discount_not_found`, `order.cancellation_reason_invalid` |
| `OrderItem` | Entity enfant | Snapshot d'un article (produit, variante, options, note cuisine), total de ligne, part de remise, taxe et net de ligne | (aucun : seule la racine lève) | `order_item.modifier_duplicate`, `order_item.modifier_not_found`, `order_item.unit_price_negative`, `order_item.unit_price_too_large`, `order_item.too_many_modifiers`, `order_item.note_too_long` |
| `OrderItemModifier` | ValueObject, sans tenant | Snapshot d'option (`ModifierId`, nom <= 120, delta de prix par unité, éventuellement négatif, borné en valeur absolue) | — | `order_item_modifier.name_invalid`, `order_item_modifier.price_delta_too_large`, `common.id.empty` |
| `ProductSnapshot` | ValueObject | Ce que la commande doit savoir du produit à l'instant de l'ajout (+ `TenantId` du produit, `VariantId?` + `VariantName`) | — | `product_snapshot.name_invalid`, `product_snapshot.unit_price_negative`, `product_snapshot.unit_price_too_large`, `product_snapshot.variant_name_required`, `product_snapshot.variant_name_without_variant`, `product_snapshot.variant_name_too_long`, `common.id.empty` |
| `LocationReference` | record (référence locale, publique) | `(TenantId, LocationId, IsOperational)` de la location, ids non vides | — | `common.id.empty` |
| `CustomerReference` | record (référence locale, publique) | `(TenantId, CustomerId, IsAnonymized)` du client | — | `common.id.empty` |
| `TableSessionReference` | record (référence locale, publique) | `(TenantId, TableSessionId, LocationId, IsOpen)` de la session de table | — | `common.id.empty` |
| `OrderLineView`, `OrderLineModifierView` | records de lecture, constructeurs `internal` | Description d'une ligne pour la cuisine et le stock, obtenue uniquement par `Order.DescribeLines()` | — | — |
| `TaxSnapshot` | ValueObject | Catégorie de taxe figée (code, nom, taux en fraction 0..1, 6 décimales max) ; `TaxOn` (taxe ajoutée à un HT) et `TaxIncludedIn` (taxe contenue dans un TTC) | — | `tax_snapshot.category_code_invalid`, `tax_snapshot.category_name_invalid`, `tax_snapshot.rate_out_of_range`, `tax_snapshot.rate_too_precise` |
| `OrderDiscount` | ValueObject, sans tenant | Remise déjà décidée : montant borné + origine (`DiscountSource` connu + référence + libellé) | — | `order_discount.amount_not_positive`, `order_discount.amount_too_large`, `order_discount.source_unknown`, `order_discount.reference_invalid`, `order_discount.label_too_long` |
| `OrderSettlement` | ValueObject (référence locale) | Ce que Payments a soldé pour la commande : tenant, `OrderId`, `PaidAmount` >= 0 | — | `order_settlement.paid_amount_negative`, `common.id.empty` |
| `PriceTaxMode` | Enum | `TaxInclusive` (1) / `TaxExclusive` (2), fixé à la création | — | — |
| `DiscountOrigin` | record d'event | Paire (`DiscountSource`, référence) portée par `OrderCancelled` | — | — |
| `OrderNumber` | ValueObject | Numéro lisible, normalisé en majuscules | — | `order_number.empty`, `order_number.too_long`, `order_number.invalid_format` |
| `Quantity` | ValueObject | Entier de 1 à 9 999 | — | `quantity.not_positive`, `quantity.too_large` |
| `ProportionalAllocator` | helper interne | Répartition proportionnelle sans perte d'unité mineure | — | — |

Transitions (`Order.Status`) :

```
Draft -> Placed -> Accepted -> Preparing -> Ready -> Completed   (Ready -> Completed : seulement si soldée)
Draft | Placed | Accepted | Preparing | Ready -> Cancelled
Completed, Cancelled : terminaux (toute mutation => orders.order.finalized)
```

Lignes, notes et remises ne sont modifiables qu'en `Draft` (`orders.order.not_editable` ensuite).

## 3. Calcul des montants (déterministe)

Le **mode de prix** (`PriceTaxMode`) est un paramètre de `Order.Create`, lu par l'Application dans le réglage du tenant
(défaut produit : **TTC**, `TaxInclusive`) et **jamais modifié ensuite**. Il dit comment lire tous les montants entrants de
la commande : prix unitaires, deltas d'options et remises sont tous TTC (`TaxInclusive`) ou tous HT (`TaxExclusive`).
Tous les montants sont dans la devise de la commande. `arrondi` = à la précision de la devise, demi en s'éloignant de zéro
(`Money.Rounded`).

Étapes communes aux deux modes (1 à 4) :

1. `LineTotal(i) = (UnitPrice + Σ PriceDelta des options) × Quantity` — exact, sans arrondi.
2. `SubTotal = Σ LineTotal(i)`.
3. `DiscountTotal = min(Σ montants des remises appliquées, SubTotal)` — plafonné, réévalué à chaque changement de lignes
   (la remise décidée est conservée telle quelle dans `Discounts`).
4. Répartition de `DiscountTotal` sur les lignes au prorata de `LineTotal`, en unités mineures entières (méthode du plus fort
   reste) : `part(i) = floor(DiscountTotal × LineTotal(i) / SubTotal)`, puis les unités restantes sont données une à une aux
   plus grands restes ; à égalité de reste : la ligne au plus grand `LineTotal`, puis le plus petit `ProductId`, et
   seulement ensuite la ligne la plus ancienne (lignes du même produit et du même total, donc interchangeables). Le
   résultat **ne dépend pas de l'ordre d'insertion des lignes**. Garanties : `Σ part(i) = DiscountTotal` et
   `0 <= part(i) <= LineTotal(i)`. Calcul en `BigInteger`, donc aucune erreur de division.
   `TaxableAmount(i) = LineTotal(i) − part(i)` : le montant de ligne remisé, dans le mode de la commande.

**Mode `TaxExclusive` (prix HT, la taxe s'ajoute)** — comportement historique, inchangé au centime :

5. `NetAmount(i) = TaxableAmount(i)` ; `TaxAmount(i) = arrondi(TaxableAmount(i) × Rate(i))` (`TaxSnapshot.TaxOn`).
6. `TaxTotal = Σ TaxAmount(i)` ; `Total = SubTotal − DiscountTotal + TaxTotal`.

**Mode `TaxInclusive` (prix TTC, la taxe est contenue)** :

5. `brut(i) = TaxableAmount(i)` (TTC remisé) ;
   `TaxAmount(i) = arrondi(brut(i) − brut(i) / (1 + Rate(i)))` (`TaxSnapshot.TaxIncludedIn`) ;
   `NetAmount(i) = brut(i) − TaxAmount(i)` — le net est **déduit** de la taxe arrondie, jamais arrondi séparément, donc
   `NetAmount(i) + TaxAmount(i) = brut(i)` à l'unité près, toujours.
6. `TaxTotal = Σ TaxAmount(i)` (taxe contenue) ; `Total = SubTotal − DiscountTotal`. Le client paie exactement les prix
   affichés moins la remise ; `SubTotal`, `DiscountTotal` et `Total` sont TTC.

Dans les deux modes :

- **la taxe est arrondie par ligne** et `TaxTotal` est la somme des taxes de ligne ;
- `TotalWithTax(i) = NetAmount(i) + TaxAmount(i)` et `Σ TotalWithTax(i) = Total` : aucun centime perdu ;
- `NetTotal = Total − TaxTotal = Σ NetAmount(i)` est le total hors taxe à imprimer sur le ticket :
  **`NetTotal + TaxTotal = Total`**. En `TaxExclusive`, `NetTotal = SubTotal − DiscountTotal` ; en `TaxInclusive`,
  `Total = SubTotal − DiscountTotal`.
- `Total >= 0` ; un taux 0 donne une taxe nulle (`NetAmount = TaxableAmount`).

Exemple (EUR, 12,00 à 20 %) : `TaxInclusive` => Total 12,00, taxe 2,00, net 10,00 ; `TaxExclusive` => Total 14,40, taxe 2,40,
net 12,00. Vérifié à la main en EUR (2 décimales), XOF (0) et KWD (3), taux 0, taux atypiques (5,5 %, 9,975 %, 2,1 %),
remises, plus 500 commandes pseudo-aléatoires **dans chaque mode**.

## 4. Choix faits là où la spec était ouverte

- **Atomicité des mutateurs** (défaut corrigé : mutation puis recalcul qui lève = ligne/quantité/remise fantôme avec des
  totaux périmés). Choix : **restauration sur échec**, un seul mécanisme pour tous les mutateurs (`Order.Change` capture
  lignes — quantité, options, part de remise, net, taxe —, remises et les quatre totaux, exécute mutation + recalcul, et
  remet tout en place sur n'importe quelle exception). `ChangeItemNote` valide puis affecte un seul champ : atomique par
  construction. Les tests atteignent le recalcul en échec en simulant une commande persistée avant les bornes.
- **Bornes d'entrée** (constantes publiques, en unités majeures de la devise) : prix unitaire
  `ProductSnapshot.MaxUnitPriceAmount` = 100 000 000 ; delta d'option `OrderItemModifier.MaxPriceDeltaAmount` = idem en
  valeur absolue ; **prix unitaire options comprises** plafonné à la même valeur ; remise `OrderDiscount.MaxAmount` =
  1 000 000 000 ; `OrderItem.MaxModifiers` = 50 options par ligne ; `Order.MaxDiscounts` = 20 remises ; (déjà) 200 lignes,
  quantité 9 999. Pire cas : 200 × 9 999 × 100 000 000 ≈ 2,0 × 10^14 de sous-total, 4,0 × 10^14 avec 100 % de taxe, sous
  `Money.MaxAbsoluteAmount` = 10^15 : aucun panier réel n'atteint `common.money.out_of_range`. 10^9 par unité aurait
  dépassé ce plafond (2 × 10^15) : 10^8 est la plus grande puissance de dix qui tient.
- **Variante vendue** : `ProductSnapshot.VariantId?` + `VariantName` (1..120, obligatoire avec la variante, interdit sans),
  recopiés sur `OrderItem`. Le prix unitaire du snapshot **inclut déjà** le delta de la variante (calculé par Catalog).
- **Note cuisine** : `OrderItem.Note` (texte libre, 200 max, blanc = absent). Donnée à `AddItem(product, quantity, note)` —
  un paramètre et non un champ du snapshot, car c'est une saisie du client, pas un fait du catalogue — puis
  `ChangeItemNote(itemId, note)` en `Draft` uniquement ; même valeur = no-op ; aucun effet sur les totaux.
- **Références à la création** : `Order.Create(tenantId, LocationReference, …, CustomerReference?, TableSessionReference?)`.
  Location : même tenant et **opérationnelle** (`order.location_not_operational`). Client : même tenant, non anonymisé
  (`order.customer_anonymized`). Session de table : `DineIn` seulement, même tenant, **même location que la commande**
  (`order.table_session_location_mismatch`), **ouverte** (`order.table_session_not_open`). Ces références sont
  publiques (leurs agrégats vivent dans d'autres modules, l'Application les construit) et refusent les ids vides.
- **`OrderItemModifier` et `OrderDiscount` restent sans tenant** : ce sont des snapshots copiés par l'Application (option
  d'un produit dont le tenant est vérifié via `ProductSnapshot.TenantId`, décision de remise), pas des références à un
  agrégat ; il n'y a rien à comparer.
- **Règlement dans une autre devise** : `orders.order.currency_mismatch` (et non plus `not_settled`), plus clair pour
  l'appelant ; `not_settled` ne signifie plus que « montant insuffisant ».

- **Prix TTC ou HT** (décision produit : TTC par défaut, réglage du tenant) : `PriceTaxMode` obligatoire à `Create`, figé,
  publié dans `OrderCreated`, `OrderPlaced` et `OrderCompleted` (enum, comme `OrderType`). Pas de valeur par défaut dans le
  Domain : c'est l'Application qui lit le réglage. Une remise est un montant dans le mode de la commande (TTC sur une
  commande TTC) : Promotions doit la calculer sur la même base.
- **Clôture seulement si soldée** (décision produit : rien ne ferme une commande impayée) : `Complete(OrderSettlement, clock)`.
  Le règlement doit être du même tenant (`common.tenant.mismatch`), viser cette commande
  (`orders.order.settlement_order_mismatch`), être dans la devise de la commande (`orders.order.currency_mismatch`) et
  couvrir `Total` — sinon `orders.order.not_settled`. **Le trop-perçu est accepté** : pourboires et arrondis de caisse
  ne sont pas modélisés. Une commande à total nul se clôture avec un règlement à zéro (aucun `Payment` n'existe pour elle).
  Le statut est vérifié avant le règlement (`finalized` / `invalid_transition` d'abord).
- **Remises ré-évaluables** (décision produit) : une remise calculée par Promotions sur un panier antérieur est périmée dès
  qu'une ligne change. `ClearDiscounts()` (brouillon uniquement, sans effet s'il n'y en a aucune) retire toutes les remises ;
  **l'Application doit appeler `ClearDiscounts` puis ré-appliquer les remises ré-évaluées après tout changement de ligne
  (`AddItem`, `RemoveItem`, `ChangeItemQuantity`, `AddItemModifier`, `RemoveItemModifier`) et avant `Place`**.
  `AddItem`/`RemoveItem` ne vident pas les remises d'eux-mêmes : seul le plafonnement est recalculé.
- **Deuxième tournée à table** (décision produit) : c'est une **nouvelle commande sur la même session de table**. Les lignes
  d'une commande restent figées après `Draft` ; plusieurs commandes peuvent partager un `TableSessionId` (aucune unicité
  dans le Domain ni à prévoir en base).
- **`OrderCancelled`** porte `CustomerId` et les origines (`DiscountOrigin` = source + référence) de toutes les remises
  présentes sur la commande, même plafonnées, pour que Promotions/Loyalty annulent leurs effets sans recharger la commande.
- **`OrderPreparationStarted`** (avec `LocationId`) est le fait sur lequel le stock est consommé (décision produit).
- **Même valeur = no-op** : `ChangeItemQuantity` avec la quantité déjà portée par la ligne ne fait rien.
- **Devise** : paramètre de `Create`, fixe. Produit, option ou remise dans une autre devise => `orders.order.currency_mismatch`.
- **Édition en `Draft` uniquement** : dès `Placed`, les totaux sont ceux publiés dans `OrderPlaced` (base du paiement et du
  ticket cuisine). Modifier une commande passée = annuler/recréer ou un futur cas d'usage dédié.
- **Annulation depuis `Ready` autorisée** (commande jamais retirée, client parti). Le remboursement éventuel relève de Payments ;
  `OrderCancelled.PreviousStatus` indique jusqu'où la commande était allée. Raison obligatoire (1..500 caractères).
- **Session de table réservée à `DineIn`**, et facultative pour `DineIn` (sur place sans table attribuée).
- **Remises** : cumulables, identifiées par (`Source`, `Reference` insensible à la casse), une seule fois chacune.
  `RemoveDiscount` ajouté (absent de la spec) pour pouvoir corriger un brouillon, `ClearDiscounts` pour tout ré-évaluer. L'éligibilité et le cumul autorisé sont
  décidés par Promotions ; Orders ne fait qu'appliquer et plafonner.
- **Options à delta négatif autorisées** ("sans fromage"), mais le prix unitaire options comprises ne peut jamais être négatif.
- **Un produit ajouté deux fois = deux lignes** (pas de fusion implicite). 200 lignes max, quantité max 9 999.
- **Une heure par opération** : chaque transition lit l'horloge une fois ; l'horodatage d'état est celui de l'event.
- **`OrderCancelled.Discounts`** et `DescribeLines()` renvoient des listes en lecture seule (pas un tableau modifiable).
- **`AddItemModifier` / `RemoveItemModifier`** exposés par la racine (ce sont `AddModifier`/`RemoveModifier` de `OrderItem`).
- **Pas d'event pour les mutations de brouillon** : un brouillon n'est pas encore un fait métier ; `OrderPlaced` porte les totaux figés.
- **`OrderNumber`** : 1..20 caractères, `A-Z0-9` en groupes séparés par un seul tiret (`A-0042`, `20260115-017`).
- **Une taxe par ligne** (un `TaxSnapshot`). Les taxes composées (ex. deux taxes cumulées) ne sont pas modélisées.
- **Produit d'un autre tenant** : `AddItem` vérifie `ProductSnapshot.TenantId` (`common.tenant.mismatch`).
- **Ids vides** (`default`) : refusés avec `common.id.empty` par `Order.Create` (tenant), les trois références,
  `ProductSnapshot` (tenant, produit, variante), `OrderItemModifier` (option) et `OrderSettlement` (tenant, commande).

## 5. Matrice invariant -> méthode -> test

| Invariant | Méthode | Test |
|---|---|---|
| Au moins une ligne pour passer commande | `Order.Place` | `OrderLifecycleTests.Place_rejects_an_empty_order`, `.Place_rejects_an_order_whose_last_line_was_removed` |
| Mutateur en échec = commande inchangée (lignes, remises, totaux), y compris quand le recalcul déborde | `Order.Change` (tous les mutateurs de lignes/remises), `ChangeItemNote` | `OrderAtomicityTests.AddItem_that_fails_leaves_the_order_untouched`, `.AddItem_whose_recalculation_overflows_adds_no_phantom_line`, `.ChangeItemQuantity_that_fails_leaves_the_order_untouched`, `.ChangeItemNote_that_fails_leaves_the_order_untouched`, `.AddItemModifier_that_fails_leaves_the_order_untouched`, `.RemoveItemModifier_that_fails_leaves_the_order_untouched`, `.RemoveItem_that_fails_leaves_the_order_untouched`, `.ApplyDiscount_that_fails_leaves_the_order_untouched`, `.RemoveDiscount_and_ClearDiscounts_that_fail_leave_the_order_untouched` |
| Bornes des montants d'entrée | `ProductSnapshot.Create`, `OrderItemModifier.Create`, `OrderDiscount.Create`, `OrderItem.AddModifier`/`RemoveModifier` | `OrderAtomicityTests.Unit_price_is_accepted_at_its_cap_and_rejected_above`, `.Modifier_delta_is_accepted_at_its_cap_in_both_directions_and_rejected_beyond`, `.Discount_amount_is_accepted_at_its_cap_and_rejected_above`, `.Unit_price_with_modifiers_is_accepted_at_the_cap_and_rejected_above` |
| Au plus 50 options par ligne, 20 remises par commande | `AddItem`, `AddItemModifier`, `ApplyDiscount` | `OrderAtomicityTests.Line_accepts_the_maximum_number_of_modifiers_and_rejects_one_more`, `.Order_accepts_the_maximum_number_of_discounts_and_rejects_one_more` |
| La plus grosse commande permise reste sous `Money.MaxAbsoluteAmount` | recalcul | `OrderAtomicityTests.Largest_order_the_caps_allow_stays_within_the_money_range` (2 modes) |
| Variante : id non vide, nom obligatoire avec, interdit sans, borné ; recopiée sur la ligne | `ProductSnapshot.Create`, `Order.AddItem` | `ProductSnapshotTests.Create_keeps_the_variant_with_its_trimmed_name`, `.Create_requires_the_name_of_the_variant`, `.Create_rejects_a_variant_name_without_a_variant`, `.Create_accepts_a_variant_name_of_the_maximum_length_and_rejects_one_more`, `.Snapshots_of_two_variants_of_the_same_product_differ`, `OrderLinesContractTests.AddItem_keeps_the_variant_that_was_sold`, `.AddItem_of_a_product_without_variant_leaves_the_variant_empty` |
| Nom d'option jusqu'à 120 (contrat Catalog) | `OrderItemModifier.Create` | `OrderItemModifierTests.Create_accepts_a_name_as_long_as_the_catalogue_allows`, `.Create_rejects_a_too_long_name` |
| Note cuisine : bornée, `Draft` seulement, même valeur = no-op, sans effet sur les totaux | `Order.AddItem`, `Order.ChangeItemNote` | `OrderLinesContractTests.AddItem_keeps_the_trimmed_kitchen_note`, `.AddItem_accepts_a_note_of_the_maximum_length_and_rejects_one_more`, `.ChangeItemNote_sets_replaces_and_removes_the_note_without_touching_the_totals`, `.ChangeItemNote_to_the_same_note_changes_nothing`, `.ChangeItemNote_rejects_a_too_long_note_or_an_unknown_line`, `.ChangeItemNote_is_only_allowed_on_a_draft` |
| Contrat de sortie des lignes : complet, lecture pure, immuable, dans les bornes cuisine, non forgeable | `Order.DescribeLines` | `OrderLinesContractTests.DescribeLines_gives_what_the_kitchen_and_the_stock_need_for_every_line`, `.DescribeLines_is_a_pure_read_available_in_every_status`, `.DescribeLines_returns_a_detached_immutable_description`, `.DescribeLines_never_exceeds_the_bounds_promised_to_the_kitchen`, `.Line_views_can_only_be_obtained_from_an_order` |
| Ids vides refusés | `Order.Create`, références, `ProductSnapshot.Create`, `OrderItemModifier.Create`, `OrderSettlement.Create` | `OrderCreationTests.Create_rejects_an_empty_tenant_or_an_empty_foreign_id`, `ProductSnapshotTests.Create_rejects_an_empty_tenant_product_or_variant_id`, `OrderItemModifierTests.Create_rejects_an_empty_modifier_id`, `OrderSettlementTests.Create_rejects_an_empty_tenant_or_order_id` |
| Location du même tenant et opérationnelle | `Order.Create` | `OrderCreationTests.Create_rejects_a_location_of_another_tenant`, `.Create_rejects_a_location_that_is_not_operational` |
| Client du même tenant, non anonymisé | `Order.Create` | `OrderCreationTests.Create_rejects_a_customer_of_another_tenant`, `.Create_rejects_an_anonymized_customer` |
| Session de table du même tenant, de la même location, ouverte | `Order.Create` | `OrderCreationTests.Create_rejects_a_table_session_of_another_tenant`, `.Create_rejects_a_table_session_of_another_location`, `.Create_rejects_a_table_session_that_is_closed`, `.Create_keeps_only_the_ids_of_its_references` |
| Source de remise connue | `OrderDiscount.Create`, `Order.RemoveDiscount` | `OrderDiscountTests.Create_rejects_an_unknown_source`, `OrderAtomicityTests.RemoveDiscount_and_ClearDiscounts_that_fail_leave_the_order_untouched` |
| Élément `null` dans les options = erreur de programmation | `ProductSnapshot.Create` | `ProductSnapshotTests.Create_rejects_a_null_modifier_as_a_programming_error` |
| Horodatage d'état = heure de l'event | `Create`, `Place`, `Complete`, `Cancel` | `OrderLifecycleTests.Timestamps_of_a_transition_equal_the_time_of_its_event` |
| Listes publiées non modifiables | `Order.Cancel`, `Order.DescribeLines` | `OrderLifecycleTests.Cancel_publishes_discounts_that_cannot_be_altered_by_a_consumer`, `OrderLinesContractTests.DescribeLines_returns_a_detached_immutable_description` |
| Quantité > 0 (et bornée) | `Quantity.Create` | `QuantityTests.Create_rejects_zero_or_negative`, `.Create_rejects_more_than_the_maximum`, `OrderItemsTests.A_quantity_of_zero_cannot_even_be_expressed` |
| Devise unique (produit) | `Order.AddItem` | `OrderItemsTests.AddItem_rejects_a_product_priced_in_another_currency` |
| Devise unique (option) | `Order.AddItem`, `Order.AddItemModifier` | `OrderItemsTests.AddItem_rejects_a_modifier_priced_in_another_currency`, `.AddItemModifier_rejects_another_currency` |
| Devise unique (remise) | `Order.ApplyDiscount` | `OrderTotalsTests.ApplyDiscount_rejects_another_currency` |
| Snapshot produit obligatoire et complet | `Order.AddItem`, `ProductSnapshot.Create` | `OrderItemsTests.AddItem_copies_the_product_snapshot_onto_the_line`, `.AddItem_requires_a_snapshot_and_a_quantity`, `ProductSnapshotTests.*` |
| Même tenant que le produit | `Order.AddItem` | `OrderItemsTests.AddItem_rejects_a_product_of_another_tenant` |
| Total de ligne = (prix + options) × quantité | `OrderItem.LineTotal` | `OrderItemsTests.Line_total_is_unit_price_plus_modifier_deltas_times_quantity`, `.Line_total_of_a_line_without_modifier_is_unit_price_times_quantity` |
| Prix unitaire options comprises >= 0 | `AddItem`, `AddItemModifier`, `RemoveItemModifier` | `OrderItemsTests.AddItem_rejects_modifiers_that_make_the_unit_price_negative`, `.AddItemModifier_rejects_a_delta_that_makes_the_unit_price_negative`, `.RemoveItemModifier_rejects_removing_a_surcharge_that_covers_a_negative_delta` |
| Option unique par ligne | `AddItem`, `AddItemModifier` | `OrderItemsTests.AddItem_rejects_the_same_modifier_twice_in_the_snapshot`, `.AddItemModifier_rejects_a_duplicate_modifier` |
| Totaux recalculés à chaque mutation | toutes les mutations de lignes/remises | `OrderItemsTests.RemoveItem_removes_the_line_and_recalculates`, `.ChangeItemQuantity_updates_the_line_and_the_totals`, `.AddItemModifier_adds_the_option_and_recalculates`, `OrderTotalsTests.Totals_are_identical_whatever_the_sequence_of_mutations_that_led_to_the_same_content` |
| Formule complète HT (options, taux mixtes, remise) | `Order` (recalcul) | `OrderTotalsTests.Totals_with_modifiers_mixed_rates_and_a_discount_match_the_documented_formula`, `.Totals_add_tax_on_top_of_tax_exclusive_prices`, `.Tax_exclusive_totals_expose_the_net_total_before_tax` |
| Formule complète TTC (options, taux mixtes, remise) | `Order` (recalcul), `OrderItem.ApplyPricing` | `OrderTotalsTests.Inclusive_totals_extract_the_tax_contained_in_tax_inclusive_prices`, `.Inclusive_totals_with_modifiers_mixed_rates_and_a_discount_match_the_documented_formula`, `.Same_basket_costs_the_menu_price_in_inclusive_mode_and_more_in_exclusive_mode` |
| TTC : taxe contenue = `brut − brut / (1 + taux)`, arrondie par ligne, demi loin de zéro | `TaxSnapshot.TaxIncludedIn` | `TaxSnapshotTests.TaxIncludedIn_extracts_the_tax_contained_in_a_gross_amount`, `.TaxIncludedIn_rounds_half_away_from_zero_to_the_currency_precision`, `OrderTotalsTests.Inclusive_line_tax_is_rounded_half_away_from_zero`, `.Inclusive_tax_is_rounded_per_line_and_the_total_is_the_sum_of_line_taxes` |
| TTC : net + taxe = brut par ligne, taux 0 et taux atypiques | `OrderItem.ApplyPricing` | `OrderTotalsTests.Inclusive_tax_with_an_awkward_rate_keeps_net_plus_tax_equal_to_the_price` (4 cas), `.Inclusive_prices_at_rate_zero_contain_no_tax` |
| TTC : remise TTC, taxe extraite du prix remisé, plafonnement | recalcul | `OrderTotalsTests.Inclusive_discount_is_a_tax_inclusive_amount_and_tax_is_extracted_from_the_discounted_price`, `.Inclusive_discount_larger_than_the_subtotal_is_capped_and_leaves_no_tax` |
| TTC : devise sans décimale / à 3 décimales | recalcul | `OrderTotalsTests.Inclusive_totals_in_a_zero_decimal_currency_stay_whole_and_lose_no_unit`, `.Inclusive_totals_in_a_three_decimal_currency_keep_three_decimals` |
| `NetTotal + TaxTotal = Total` et `Σ lignes = totaux`, dans les deux modes | recalcul | `OrderTotalsTests.Parts_always_sum_exactly_to_the_totals_over_many_awkward_orders` (2 modes × 500 commandes) |
| Mode de prix fixé à la création, valeur connue | `Order.Create` | `OrderCreationTests.Create_fixes_the_price_tax_mode_of_the_order`, `.Create_rejects_an_unknown_price_tax_mode` |
| Mode publié avec les totaux | `Place`, `Complete` | `OrderLifecycleTests.Place_publishes_tax_inclusive_totals_with_their_mode`, `.Place_moves_a_draft_with_lines_to_placed_and_publishes_the_frozen_totals`, `.Complete_in_tax_inclusive_mode_requires_the_tax_inclusive_total` |
| Clôture seulement si soldée (`PaidAmount >= Total` ; autre devise => `currency_mismatch`) | `Order.Complete` | `OrderLifecycleTests.Complete_rejects_an_order_that_is_not_fully_paid`, `.Complete_rejects_a_settlement_paid_in_another_currency`, `.Complete_accepts_a_settlement_that_covers_exactly_the_total`, `.Complete_accepts_an_overpayment`, `.Complete_accepts_a_zero_settlement_when_the_total_is_zero` |
| Règlement du même tenant et de cette commande | `Order.Complete` | `OrderLifecycleTests.Complete_rejects_a_settlement_of_another_tenant`, `.Complete_rejects_a_settlement_of_another_order`, `.Complete_checks_the_status_before_the_settlement` |
| Règlement : montant payé >= 0 | `OrderSettlement.Create` | `OrderSettlementTests.Create_rejects_a_negative_paid_amount`, `.Create_accepts_a_zero_paid_amount` |
| Remises ré-évaluables, sans vidage automatique | `Order.ClearDiscounts` | `OrderTotalsTests.ClearDiscounts_removes_every_discount_and_restores_the_totals`, `.ClearDiscounts_does_nothing_when_there_is_no_discount`, `.ClearDiscounts_lets_the_same_origin_be_applied_again_with_a_fresh_amount`, `.Changing_lines_does_not_clear_discounts_by_itself` |
| Plusieurs commandes par session de table | `Order.Create` | `OrderCreationTests.Several_orders_can_share_the_same_table_session` |
| `OrderCancelled` porte client et origines des remises | `Order.Cancel` | `OrderLifecycleTests.Cancel_publishes_the_customer_and_the_origin_of_every_discount_to_undo`, `.Cancel_does_not_publish_discounts_that_were_cleared` |
| `OrderPreparationStarted` porte `LocationId` | `Order.StartPreparation` | `OrderLifecycleTests.Each_step_event_carries_tenant_order_and_time` |
| Même quantité = no-op | `Order.ChangeItemQuantity` | `OrderItemsTests.ChangeItemQuantity_to_the_same_quantity_changes_nothing` |
| Remise plafonnée au sous-total, total >= 0 | `Order.ApplyDiscount` | `OrderTotalsTests.Discount_larger_than_the_subtotal_is_capped_so_the_total_never_goes_negative`, `.Discount_equal_to_the_subtotal_brings_the_total_to_zero`, `.Capped_discount_is_re_evaluated_when_lines_change`, `.Discount_on_an_empty_order_deducts_nothing_until_lines_are_added` |
| Répartition de la remise sans centime perdu | `ProportionalAllocator.Allocate` | `OrderTotalsTests.Discount_that_does_not_divide_evenly_loses_no_cent`, `.Free_line_receives_no_discount_share`, `.Parts_always_sum_exactly_to_the_totals_over_many_awkward_orders` |
| Départage des restes égaux indépendant de l'ordre d'insertion (plus grand total, puis `ProductId`, puis position) | `ProportionalAllocator.Allocate` | `OrderTotalsTests.Leftover_unit_does_not_depend_on_the_order_in_which_tied_lines_were_added`, `.Leftover_unit_goes_to_the_largest_line_when_remainders_tie`, `.Leftover_unit_goes_to_the_first_line_when_identical_lines_tie`, `.Totals_and_line_amounts_are_the_same_whatever_the_order_in_which_the_lines_were_added` (2 modes × 300 paniers) |
| Taxe sur la base remisée, arrondie par ligne | `OrderItem.ApplyPricing`, `TaxSnapshot.TaxOn` | `OrderTotalsTests.Tax_is_computed_on_the_discounted_base`, `.Tax_is_rounded_per_line_and_the_total_is_the_sum_of_line_taxes`, `.Line_tax_is_rounded_half_away_from_zero`, `TaxSnapshotTests.TaxOn_rounds_half_away_from_zero_to_the_currency_precision` |
| Devise sans décimale / à 3 décimales | recalcul | `OrderTotalsTests.Totals_in_a_zero_decimal_currency_stay_whole_and_lose_no_unit`, `.Totals_in_a_three_decimal_currency_keep_three_decimals` |
| Remise unique par origine | `Order.ApplyDiscount`, `RemoveDiscount` | `OrderTotalsTests.ApplyDiscount_rejects_the_same_origin_twice`, `.ApplyDiscount_accepts_the_same_reference_from_another_source`, `.RemoveDiscount_rejects_a_discount_that_is_not_applied`, `.RemoveDiscount_restores_the_totals` |
| Remise : montant > 0, référence obligatoire | `OrderDiscount.Create` | `OrderDiscountTests.Create_rejects_a_zero_or_negative_amount`, `.Create_rejects_a_missing_reference` |
| Lignes/remises figées après `Draft` | mutateurs de lignes/remises | `OrderItemsTests.Lines_and_discounts_are_frozen_once_the_order_is_placed` |
| Transitions valides | `Place`…`Complete` | `OrderLifecycleTests.Happy_path_goes_from_draft_to_completed_raising_one_event_per_step`, `.Place_moves_a_draft_with_lines_to_placed_and_publishes_the_frozen_totals` |
| Transitions invalides rejetées | `Place`…`Complete` | `OrderLifecycleTests.Transition_from_the_wrong_status_is_rejected` (20 cas) |
| Annulation depuis tout état non terminal, raison obligatoire | `Order.Cancel` | `OrderLifecycleTests.Cancel_is_allowed_from_every_non_terminal_status`, `.Cancel_requires_a_reason`, `.Cancel_rejects_a_reason_longer_than_the_maximum` |
| `Completed`/`Cancelled` immuables | les 15 mutateurs publics (`ChangeItemNote` compris ; `DescribeLines` est la seule lecture publique, déclarée comme telle dans le test) | `OrderImmutabilityTests.Finalized_order_rejects_every_mutation_and_stays_untouched` (30 cas), `.Every_public_mutator_of_Order_is_covered_by_the_immutability_matrix` |
| Aucun setter public | — | `OrderImmutabilityTests.Order_and_its_lines_expose_no_public_setter` |
| Session de table seulement en `DineIn` | `Order.Create` | `OrderCreationTests.Create_rejects_a_table_session_on_a_non_dine_in_order`, `.Create_accepts_a_dine_in_order_with_or_without_a_table_session` |
| Numéro : non vide, borné, format | `OrderNumber.Create` | `OrderNumberTests.Create_rejects_an_empty_number`, `.Create_accepts_the_maximum_length_and_rejects_one_more`, `.Create_rejects_an_invalid_format` |
| Égalité par valeur des options | `OrderItemModifier` | `OrderItemModifierTests.Modifiers_are_equal_when_id_name_and_delta_match`, `.Modifiers_differ_when_any_component_differs` |
| Events seulement après mutation valide | toutes | `OrderLifecycleTests.Place_rejects_an_empty_order`, `.Transition_from_the_wrong_status_is_rejected`, `OrderItemsTests.Item_mutations_raise_no_event` |

## 6. Ce qui relève de l'Application / Infrastructure

### LE contrat d'intégration des lignes : `Order.DescribeLines()`

Les events d'Orders ne portent **aucune entité** et aucune ligne. Quand un module aval a besoin du contenu de la commande
(ticket cuisine sur `OrderAccepted`, consommation du stock sur `OrderPreparationStarted`), le handler applicatif charge
l'`Order` et appelle `DescribeLines()` : c'est **la seule** source autorisée, ne pas lire `Items` pour cela. Lecture pure,
disponible dans tous les statuts, liste immuable et détachée de `OrderLineView`, dans l'ordre d'ajout :

| Champ | Type | Borne garantie par Orders |
|---|---|---|
| `OrderItemId` | id kernel | non vide |
| `ProductId` | id kernel | non vide |
| `VariantId` | `VariantId?` | non vide si présent |
| `ProductName` | `string` | 1..200 |
| `VariantName` | `string?` | 1..120, présent si et seulement si `VariantId` l'est |
| `Quantity` | `int` | 1..9 999 |
| `Modifiers` | liste de `OrderLineModifierView(ModifierId, Name)` | **au plus 50** par ligne, nom 1..120, dans l'ordre d'ajout |
| `Note` | `string?` | <= 200, `null` si absente |
| `FormulaName` | `string?` | nom de la formule pour un composant, `null` sinon |

**Formules** : une ligne de formule porte ses composants figés (`OrderItemComponent` : étape, produit, variante, supplément,
options ; jsonb `components`, au plus 20). `DescribeLines()` en fait une ligne par composant, avec l'id du composant,
la quantité et la note de la formule ; les options propres à la formule vont au premier composant (le total reste <= 50).
Ainsi la cuisine (postes, ticket) et le stock (recettes) traitent chaque produit choisi comme une ligne ordinaire ; Kitchen
préfixe la note du nom de la formule. Prix unitaire = prix de la formule + options + suppléments et options des composants.

Au plus 200 lignes de commande (une formule en donne une par composant). Ces bornes sont celles que Kitchen accepte (noms <= 200/120/120, note <= 200, 50 options par ligne) :
Orders ne produit jamais plus (`OrderLinesContractTests.DescribeLines_never_exceeds_the_bounds_promised_to_the_kitchen`).
Aucun prix n'est exposé : le ticket et le stock n'en ont pas besoin. Les lignes étant figées dès `Placed`, deux appels
après placement renvoient le même contenu. Modifier une de ces bornes est une décision inter-modules.

### Autres responsabilités

- **Unicité de `OrderNumber` par location** et sa génération (séquence) : Application + index unique `(TenantId, LocationId, Number)`.
- **`PriceTaxMode`** : lu dans le réglage du tenant (TTC par défaut) et passé à `Order.Create`. Les prix du `ProductSnapshot`
  et les montants des `OrderDiscount` doivent être fournis dans ce même mode.
- **Construction de `ProductSnapshot`** depuis Catalog (produit actif, disponible, prix, taxe, options autorisées) et des
  `OrderDiscount` depuis Promotions/Loyalty : handlers applicatifs. Le Domain ne vérifie ni la disponibilité ni l'éligibilité.
- **Ré-évaluation des remises** : après tout changement de ligne et avant `Place`, `ClearDiscounts()` puis ré-application
  des remises recalculées par Promotions/Loyalty sur le panier courant.
- **Construction de `OrderSettlement`** avant `Complete` : `PaidAmount = Σ Payment.SettledAmount` sur les paiements de la
  commande (capturé − remboursé − **remboursements en cours** ; pas `NetPaidAmount`, qui compte encore l'argent en train
  d'être rendu), zéro dans la devise de la commande s'il n'y en a aucun ;
  `TenantId`/`OrderId` viennent des paiements chargés (ou de la commande s'il n'y en a aucun). Lecture et `Complete` dans la
  même transaction : un remboursement concurrent est une course que seul le verrouillage applicatif peut fermer.
- **Deuxième tournée** : créer une nouvelle `Order` avec le même `TableSessionId` ; la clôture de la session (module Floor)
  est refusée tant qu'une de ses commandes n'est ni `Completed` ni `Cancelled`.
- **Références de `Order.Create`** : l'Application charge la location, le client et la session de table et construit
  `LocationReference` (`IsOperational` = location active), `CustomerReference` (`IsAnonymized`),
  `TableSessionReference` (`LocationId` de la table, `IsOpen`) à partir des agrégats eux-mêmes, jamais de la requête. Le
  Domain décide ; l'existence relève du chargement.
- **`ProductSnapshot`** : `UnitPrice` = prix du produit **delta de variante inclus** ; `VariantId`/`VariantName` de la
  variante choisie ; options (`OrderItemModifier`) et remises (`OrderDiscount`) copiées sans tenant.
- **Concurrence** : verrou optimiste (rowversion) sur `Order` ; l'agrégat est l'unité de cohérence.
- **Persistance** : `LineTotal`, `TaxableAmount`, `TotalWithTax` sont calculés ; `DiscountAmount`/`TaxAmount` par ligne et les
  quatre totaux sont stockés (vérité historique), ainsi que `PriceTaxMode`, `NetAmount`, `VariantId`, `VariantName` et `Note` par ligne ; `NetTotal` est calculé. `ProportionalAllocator` est `internal`.
- **Réactions aux events** (ticket cuisine sur `OrderAccepted` via `DescribeLines()`, paiement, remboursement après annulation, fidélité, consommation du stock sur
  `OrderPreparationStarted` via `DescribeLines()`, restitution des coupons/récompenses sur `OrderCancelled`) : handlers hors Domain.

## 7. Application (`Orders.Application`)

Le tenant d'un cas d'usage vient **toujours** d'`ITenantContext.RequireTenant()` (claim `tid` du jeton), jamais de la
requête. Sans restaurant sélectionné : `403 common.tenant.required`.

| Cas d'usage | Effet | Erreurs propres à l'Application |
|---|---|---|
| `CreateOrder(locationId, type, tableSessionId?)` | Brouillon dans la **devise** et le **mode de prix** (TTC/HT) du tenant ; numéro du jour tiré par `IOrderNumberGenerator` | `orders.location.not_found`, `orders.table_session.not_found`, `orders.tenant.not_writable` |
| `AddOrderItem(productId, quantity, variantId?, modifierIds?, note?)` | Le **Catalog décrit la vente** (port `IOrderCatalog` : produit actif, variante, options valides pour chaque groupe, prix, taxe) ; la commande fige l'instantané | `orders.product.not_found`, erreurs du Catalog (`catalog.product.not_sellable`, `catalog.modifier_group.selection_*`, `catalog.sale.modifier_not_offered`…) |
| `ChangeOrderItemQuantity`, `ChangeOrderItemNote`, `RemoveOrderItem` | Brouillon seulement (règles de l'agrégat) | `orders.order.not_found` |
| `ApplyManualDiscount(amount, reference, label?)`, `RemoveDiscount(source, reference)` | Remise `Manual` (geste commercial) dans la devise et le mode de la commande ; plafonnée au sous-total | idem |
| `PlaceOrder`, `AcceptOrder`, `StartOrderPreparation`, `MarkOrderReady` | Transitions | idem |
| `CompleteOrder` | Lit le montant réglé (port `IOrderPayments`, servi par Payments : Σ `SettledAmount`) et clôt si le total est couvert | idem + `orders.order.not_settled` (domaine) |
| `CancelOrder(reason)` | Depuis tout statut non terminal | idem |
| `GetOrderById`, `ListOrders(page, pageSize, locationId?, status?, tableSessionId?)` | Détail (lignes, options, remises, totaux) ; liste des plus récentes | `orders.order.not_found`, `orders.paging.invalid` |
| `HasOpenOrdersForTableSession(sessionId)` | Pour Floor (via l'hôte) : une commande de la session est-elle ni terminée ni annulée ? | — |
| `GetOrderLines(orderId)` | Pour Kitchen (via l'hôte) : statut, établissement et lignes à préparer (`DescribeLines()`, sans prix, dans l'ordre) | `orders.order.not_found` |

- **`OrdersWorkspace`** : tenant courant, tenant **qui accepte les écritures** (port `IOrderTenants` : suspendu, fermé
  ou inconnu = 422 `orders.tenant.not_writable`), chargement **dans ce tenant**, sauvegarde.
- **Modes de prix** : le tenant dit si ses prix sont TTC (`PricesIncludeTax`) ; le mode est figé sur la commande et les
  prix du Catalog y sont copiés tels que saisis.
- **Remises** : seules les remises manuelles existent pour l'instant. Elles ne sont pas « réévaluées » à chaque
  changement de ligne (un geste commercial est un montant fixe) : la commande les plafonne d'elle-même. La
  réévaluation (`ClearDiscounts` puis réapplication) viendra avec Promotions et Loyalty.
- **Permissions** : lecture ouverte à tout membre ; `orders.take` (créer, composer, passer), `orders.process`
  (accepter, préparer, prête, clôturer, annuler), `orders.discount` (remises manuelles).
- **Hors périmètre pour l'instant** : changer les options d'une ligne existante (la retirer et la rajouter), fiche
  client (`CustomerReference`, module Customers), réactions aux événements (consommation de stock, fidélité),
  commande en ligne par le client. Le ticket cuisine est relié par l'hôte (`KitchenOrderFlow`, voir le README de
  Kitchen §8) : la commande passe en préparation puis prête quand la cuisine avance.

## 8. Infrastructure (`Orders.Infrastructure`)

- Schéma `orders` : `orders`, `order_items`, `order_number_counters`, `outbox_messages`.
- **`OrdersDbContext : TenantDbContext`** : filtre EF sur le tenant de la requête, **en plus** du filtre explicite du
  repository.
- **Montants** : chaque `Money` en deux colonnes (`numeric(19,3)` + `<colonne>_currency`) ; totaux de la commande et
  part de remise / net / taxe de chaque ligne **stockés** (vérité historique), le reste recalculé. Taxe figée en trois
  colonnes (`tax_category_code`, `tax_category_name`, `tax_rate`).
- **jsonb** : options d'une ligne (`order_items.modifiers`) et remises de la commande (`orders.discounts`), relues via
  les fabriques du domaine. Domaine touché : `OrderItem._modifiers` et `Order._discounts` ne sont plus `readonly`.
- **Ordre des lignes** (celui de `DescribeLines`) : colonne `position` posée par l'unit of work.
- **Numéros** : `AAAAMMJJ-NNN` par établissement et par jour UTC, compteur incrémenté en une instruction
  (`INSERT … ON CONFLICT DO UPDATE … RETURNING`) ; index unique `(tenant_id, location_id, number)` en garde-fou
  (422 `orders.order.number_already_used`). Un numéro tiré pour une commande non enregistrée est perdu (trou).
- **Concurrence optimiste** sur `xmin` ; changer seulement une ligne (sa note) réécrit aussi la commande (409).
- **Migration** : `Persistence/Migrations/…_InitialCreate`. `database update` lit `ORDERS_DB`.

## 9. API (`Orders.Api`)

Groupe `/orders`, protégé par l'hôte avec la policy `tenant-member`. Les créations renvoient `201` + `{ id }`.

| Méthode | Route | Corps | Permission |
|---|---|---|---|
| `POST` | `/orders` | `{ locationId, type: DineIn/Takeaway/Delivery, tableSessionId? }` | `orders.take` |
| `GET` | `/orders?page&pageSize&locationId&status&tableSessionId` · `/orders/{id}` | — | membre |
| `POST` | `/orders/{id}/items` | `{ productId, quantity, variantId?, modifierIds?, note? }` | `orders.take` |
| `PUT` | `/orders/{id}/items/{itemId}/quantity` · `/note` | `{ quantity }` · `{ note }` | `orders.take` |
| `DELETE` | `/orders/{id}/items/{itemId}` | — | `orders.take` |
| `POST` | `/orders/{id}/discounts` | `{ amount, reference, label? }` | `orders.discount` |
| `DELETE` | `/orders/{id}/discounts/{source}/{reference}` | — | `orders.discount` |
| `POST` | `/orders/{id}/place` | — | `orders.take` |
| `POST` | `/orders/{id}/accept` · `/start-preparation` · `/ready` · `/complete` | — | `orders.process` |
| `POST` | `/orders/{id}/cancel` | `{ reason }` | `orders.process` |

Erreurs : `401` non connecté, `403` sans restaurant sélectionné ou sans la permission, `404` ressource inconnue ou d'un
autre tenant, `409` conflit de concurrence, `422` règle métier (code dans `code`).

Tests : `Orders.Application.Tests` (fakes), `Orders.Infrastructure.Tests` et `Orders.Api.Tests` (vrai PostgreSQL,
Docker ; **la migration doit exister**).
