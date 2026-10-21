# Module Catalog — Domain

But : gérer l'offre vendable (produits, variantes, options) et sa composition en menus par établissement.

## 1. Frontières d'agrégat

| Racine | Enfants | Référencé par id uniquement |
|---|---|---|
| `Product` | `ProductVariant` | `ModifierGroupId` (groupes attachés), `TaxCategoryId`, `AllergenId` |
| `ModifierGroup` | `Modifier` | — (référencé par `Product`) |
| `Menu` | `MenuSection` (liste ordonnée de `ProductId`) | `LocationId`, `ProductId` |
| `TaxCategory` | — | — (référencée par `Product` via `TaxCategoryId`) |

- `Product` possède ses variantes car le prix d'une variante n'a de sens que par rapport au prix de base (invariant `base + delta >= 0`).
- `ModifierGroup` est une racine distincte : un groupe est réutilisé par plusieurs produits et a sa propre cohérence (règle min/max, unicité des options, devise).
- `Menu` ne possède pas les produits : il les liste par `ProductId`. Une commande (module Orders) prend un snapshot et ne dépend jamais du `Product` vivant.
- `TaxCategory` est une racine distincte : c'est le référentiel de taxes du tenant (code, nom, taux), partagé par tous les produits et modifié indépendamment d'eux. `Product` ne stocke que le `TaxCategoryId`.
- Les liens inter-agrégats passent par de petites références locales portant le `TenantId` : `ModifierGroupReference` (`ModifierGroup.ToReference()`), `ProductReference` (`Product.ToReference()`), `TaxCategoryReference` (`TaxCategory.ToReference()`). Leur constructeur est `internal` et leurs propriétés sont en lecture seule : on ne peut les obtenir que de l'agrégat réel (pas de référence forgée, pas de `with`).
- **Identité vue des autres modules** : une ligne de commande référence `ProductId` + `VariantId` optionnel, tous deux des ids du noyau (`VariantId` n'est plus un id local du Catalog). `Product.DescribeForSale(variantId)` est le seul point d'entrée pour assembler une ligne.
- L'établissement d'un menu arrive par `LocationReference(TenantId, LocationId, IsOperational)` (construite par l'Application depuis le module Locations) ; le menu ne stocke que le `LocationId`.

## 2. Classes

| Classe | Rôle DDD | Responsabilité | Transitions | Events | Erreurs |
|---|---|---|---|---|---|
| `Product` | AggregateRoot, tenant-owned | Produit vendable : nom, description, prix de base, taxe, variantes, groupes d'options, allergènes, vendabilité (`IsSellable`, `CanBeSold(variantId)`, `PriceOf(variantId)`), contrat de ligne de commande (`DescribeForSale(variantId?)`) | `Draft -> Active` ; `Active -> Unavailable` ; `Unavailable -> Active` ; `Draft/Active/Unavailable -> Archived` (terminal) | `ProductCreated`, `ProductPriceChanged`, `ProductVariantPriceChanged`, `ProductVariantAvailabilityChanged`, `ProductActivated`, `ProductUnavailable`, `ProductArchived` | `ProductErrors.*` (`catalog.product.*`), `common.tenant.mismatch`, `common.id.empty` |
| `ProductVariant` | Entity (enfant de `Product`) | Déclinaison : nom, delta de prix (peut être négatif), SKU optionnel, disponibilité. Mutateurs `internal`, pilotés par `Product` (`AddVariant`, `RemoveVariant`, `ChangeVariantPriceDelta`, `SetVariantAvailability`) | — | — | via `Product` |
| `ModifierGroup` | AggregateRoot, tenant-owned | Groupe d'options réutilisable : règle de sélection, options, devise unique, validation d'une sélection client (`EnsureSelectionIsValid`), satisfiabilité (`IsSatisfiable`, `EnsureSatisfiable`) | — | `ModifierAdded`, `ModifierRemoved`, `ModifierPriceChanged`, `ModifierAvailabilityChanged` | `ModifierGroupErrors.*` (`catalog.modifier_group.*`) |
| `Modifier` | Entity (enfant de `ModifierGroup`) | Option : nom, supplément (>= 0), disponibilité. Pilotée par `ModifierGroup` (`AddModifier`, `RemoveModifier`, `RenameModifier`, `ChangeModifierPrice`, `SetModifierAvailability`) | — | — | via `ModifierGroup` |
| `SelectionRule` | ValueObject | `0 <= min <= max`, `1 <= max <= 50` | — | — | `selection_min_negative`, `selection_max_below_min`, `selection_max_out_of_range` |
| `Menu` | AggregateRoot, tenant-owned | Composition du catalogue d'un établissement : sections ordonnées, produits ordonnés, publication | `Draft -> Published` ; `Published -> Draft` (Unpublish) ; `Draft/Published -> Archived` (terminal) | `MenuPublished`, `MenuUnpublished`, `MenuArchived` | `MenuErrors.*` (`catalog.menu.*`), `common.tenant.mismatch` |
| `MenuSection` | Entity (enfant de `Menu`) | Section nommée, liste ordonnée et sans doublon de `ProductId` | — | — | via `Menu` |
| `TaxCategory` | AggregateRoot, tenant-owned | Taxe applicable aux produits : code stable (immuable), nom, taux ; `Create`, `Rename`, `ChangeRate`, `Archive`, `ToReference()` | `Active -> Archived` (terminal) | `TaxCategoryCreated`, `TaxCategoryRateChanged`, `TaxCategoryArchived` | `TaxCategoryErrors.*` (`catalog.tax_category.*`), `common.id.empty` |
| `TaxCategoryCode` | ValueObject | Code trimé, mis en majuscules, 1..32, `[A-Z0-9_-]` | — | — | `catalog.tax_category.code_invalid` |
| `TaxCategoryName` | ValueObject | Nom trimé, non vide, <= 100 | — | — | `catalog.tax_category.name_empty`, `catalog.tax_category.name_too_long` |
| `TaxRate` | ValueObject | Fraction décimale `0..1` inclus (0.10 = 10 %), 6 décimales au plus | — | — | `catalog.tax_category.rate_out_of_range`, `catalog.tax_category.rate_too_precise` |
| `ITaxCategoryCodeUniquenessChecker` | Port | Répond « ce code est-il libre dans ce tenant ? » (catégories archivées comprises) | — | — | — |
| `CatalogName` | ValueObject | Nom trimé, non vide, <= 120 ; `Matches` = comparaison insensible à la casse pour les règles d'unicité | — | — | `catalog.name.empty`, `catalog.name.too_long` |
| `Sku` | ValueObject | Code article normalisé en majuscules, 1..64, `[A-Z0-9-_.]` | — | — | `catalog.sku.invalid` |
| `ModifierGroupReference`, `ProductReference`, `TaxCategoryReference` | Références (records, constructeur `internal`) | Portent id + `TenantId` (+ devise / indicateur archivé ; + code, nom, taux pour la taxe) pour les contrôles inter-agrégats ; produites uniquement par `ToReference()` | — | — | — |
| `SellableItem` | Record (constructeur `internal`) | Ce qu'une ligne de commande copie : `TenantId`, `ProductId`, `ProductName`, `VariantId?`, `VariantName?`, `UnitPrice` (base + delta), `TaxCategoryId` ; produit uniquement par `Product.DescribeForSale` | — | — | — |
| `LocationReference` | Référence (record public) vers le module Locations | `TenantId`, `LocationId`, `IsOperational` ; ids non vides | — | — | `common.id.empty` |
| `ModifierGroupId`, `MenuId`, `MenuSectionId`, `TaxCategoryId`, `AllergenId` | Ids typés locaux (`VariantId`, `ProductId`, `ModifierId` viennent du noyau) | — | — | — | `common.id.empty` |

## 3. Choix faits là où la spec était ouverte

- **Devise** : un produit garde la devise de son prix de base (`ChangeBasePrice` refuse une autre devise). Un groupe d'options reçoit sa devise à la création (permet de la contrôler même quand il est vide) ; un produit n'attache qu'un groupe de même devise.
- **Devise à la création** : `Product.Create` prend la devise du `Money` reçu ; c'est l'Application qui passe la devise par défaut du tenant. Le module Tenants refuse désormais un changement de devise dès qu'un catalogue tarifé existe : les prix existants ne changent donc jamais de devise.
- **Prix TTC / HT** : `BasePrice`, les deltas de variantes et les suppléments d'options sont stockés tels que saisis. Le catalogue ne décide pas s'ils incluent la taxe : c'est un réglage du tenant (décision owner : prix de carte TTC par défaut), interprété par le module Orders.
- **Taxe** : `TaxCategory` porte code, nom et taux au format exact attendu par le snapshot de taxe d'une ligne de commande (code 1..32 `[A-Z0-9_-]` en majuscules, nom <= 100, taux fraction `0..1` à 6 décimales au plus) : toute valeur du catalogue y est valide. Le code est unique par tenant, immuable et jamais réutilisé (catégories archivées comprises). `ChangeRate` ne vaut que pour les ventes futures : les lignes déjà commandées gardent leur snapshot. `Rename` ne lève pas d'événement. `Archive` est terminal ; une catégorie archivée ne peut plus être **nouvellement** affectée à un produit (`catalog.product.tax_category_archived`), mais les produits qui la portent déjà la gardent et restent vendables avec son dernier taux.
- **Même valeur = no-op silencieux** : `TaxCategory.Rename` / `ChangeRate` et `Product.ChangeTaxCategory` appelés avec la valeur déjà portée ne modifient rien et ne lèvent rien (pour `ChangeTaxCategory`, même si la catégorie a été archivée entre-temps). Les autres mutateurs « même valeur » du module n'avaient déjà ni rejet ni événement.
- **Variantes** : unicité par nom (insensible à la casse) et par SKU quand il est renseigné ; plusieurs variantes sans SKU sont permises. Le delta peut être négatif tant que `base + delta >= 0` ; un changement de prix de base revalide toutes les variantes et est refusé si l'une passerait sous zéro.
- **Vendabilité** : seul `Active` est vendable. `CanBeSold(variantId)` exige en plus une variante existante et disponible. `Unavailable` = indisponibilité temporaire, réversible par `Activate`. `PriceOf` refuse un produit archivé (`catalog.product.archived`) ; il reste utilisable sur un brouillon (aperçu du prix).
- **`DescribeForSale(variantId?)`** : renvoie le `SellableItem` ou refuse la vente — produit archivé (`catalog.product.archived`), non actif (`catalog.product.not_sellable`), variante inconnue (`catalog.product.variant_not_found`) ou indisponible (`catalog.product.variant_unavailable`). **Choix** : un produit qui a des variantes se vend par l'une d'elles (`variantId` nul refusé : `catalog.product.variant_required`) ; un produit sans variante se vend au prix de base avec `variantId` nul.
- **Longueurs garanties aux autres modules** : nom de produit, de variante et d'option = `CatalogName` <= 120 (Orders accepte 200 / 120 / 120).
- **Bornes des collections** (constantes publiques) : `Product.MaxVariants = 50`, `Product.MaxModifierGroups = 20`, `Product.MaxAllergens = 50`, `ModifierGroup.MaxModifiers = 100`, `Menu.MaxSections = 50`, `Menu.MaxProductsPerSection = 200` ; erreurs `too_many_*`.
- **Changements de prix / disponibilité visibles** : `ChangeVariantPriceDelta`, `SetVariantAvailability`, `ChangeModifierPrice`, `SetModifierAvailability` prennent un `IClock` et lèvent `ProductVariantPriceChanged` (avec le nouveau prix effectif), `ProductVariantAvailabilityChanged`, `ModifierPriceChanged`, `ModifierAvailabilityChanged` — uniquement sur un changement réel.
- **Archived** (produit et menu) : terminal, toute mutation est refusée (`catalog.product.archived` / `catalog.menu.archived`).
- **Opérations sans effet** : `ChangeBasePrice` vers le même prix ne lève aucun événement ; `SetVariantAvailability` / `SetModifierAvailability` / `ChangeVariantPriceDelta` / `ChangeModifierPrice` vers la valeur déjà portée sont idempotents et ne lèvent rien. `Activate` sur un produit déjà actif est refusé (transition d'état).
- **Règle min/max vs nombre d'options** : la règle est configurée indépendamment du contenu (un groupe est créé vide puis rempli ; rendre une option indisponible en plein service ne doit jamais être bloqué). `max` est borné par `SelectionRule.MaxSelectionsLimit = 50` et vaut au moins 1 ; il peut dépasser le nombre d'options (« jusqu'à N »). Le rapport entre `min` et le nombre d'options **disponibles** n'est vérifié que par `IsSatisfiable` / `EnsureSatisfiable()`, à appeler par l'Application (avant publication ou prise de commande).
- **Supplément** : le prix d'une option est >= 0 (0 = option gratuite).
- **Menu publié** : son contenu (nom, sections, produits, ordre) n'est modifiable qu'en `Draft` ; il faut `Unpublish` avant toute modification (`catalog.menu.published_is_read_only`). « Contenu valide » = au moins une section, aucune section vide, et aucun produit listé archivé : `Publish(listedProducts, clock)` reçoit les `ProductReference` **actuelles** des produits listés et refuse une référence manquante (`catalog.menu.product_reference_missing`), d'un autre tenant (`common.tenant.mismatch`) ou archivée (`catalog.menu.product_archived`). Les références de produits non listés sont ignorées. `CanBePublished` ne couvre que la partie structurelle.
- **Produits d'un menu** : uniques par section (un même produit peut figurer dans deux sections). Un produit archivé ne peut pas être ajouté ; un produit archivé après coup reste listé par id dans un brouillon mais bloque la publication tant qu'il n'est pas retiré. Sur un menu déjà publié, le filtrage à l'affichage se fait sur `Product.IsSellable`.
- **Canal** : la spec évoque « établissement/canal » sans propriété canal ; seul `LocationId` est modélisé. `Menu.Create` vérifie seulement que la location est du même tenant (un menu se prépare aussi pour un établissement pas encore ouvert : `IsOperational` n'est pas exigé).
- **Events ajoutés** : `ProductArchived`, `ProductVariantPriceChanged`, `ProductVariantAvailabilityChanged`, `ModifierRemoved`, `ModifierPriceChanged`, `ModifierAvailabilityChanged`, `MenuUnpublished`, `MenuArchived`. `ProductUnavailable` garde le nom imposé par la spec. Pas d'événement de création pour `ModifierGroup` et `Menu` (non demandé).
- **Méthodes ajoutées** : `Product.ChangeTaxCategory` (prend une `TaxCategoryReference`), `DetachModifierGroup`, `PriceOf`, `DescribeForSale` ; `ModifierGroup.Rename`, `RenameModifier`, `EnsureSelectionIsValid` ; `Menu.Rename`, `RenameSection`, `RemoveSection`, `MoveSection`, `AddProductToSection`, `RemoveProductFromSection`, `MoveProductInSection`.
- Description produit : optionnelle, trimée, <= 2000 caractères.
- **Formules (menus composés)** : une formule est un `Product` qui porte des `FormulaSteps` (jsonb `formula_steps`) et aucune variante (`catalog.formula.with_variants`, dans les deux sens). Au plus 10 étapes, noms uniques (insensibles à la casse) ; chaque étape propose 1 à 40 produits (`FormulaChoice` : produit, variante obligatoire pour un produit à variantes, supplément >= 0 dans la devise de la formule) et demande de `MinChoices` (0 = facultative) à `MaxChoices` (1..10) produits. Un produit proposé n'est ni archivé, ni une formule, ni la formule elle-même, et reste du même tenant (`Product.OfferInFormula` puis `SetFormulaSteps` ; une liste vide refait un produit ordinaire). Prix vendu = prix de la formule (grille de prix comprise) + suppléments + options de chaque produit choisi ; la taxe est celle de la formule. `ChooseInFormula` vérifie la composition (étape connue, produit proposé, nombre choisi dans les bornes) ; `GetSellableItemQuery.Components` passe en plus chaque produit choisi par `DescribeForSale` (disponibilité, variante) et valide ses propres options. Une formule est commandable en ligne si chaque étape a au moins `MinChoices` produits disponibles. Route : `PUT /catalog/products/{id}/formula` (`catalog.manage`).

## 4. Matrice invariant -> méthode -> test

| Invariant | Méthode | Test |
|---|---|---|
| Prix de base >= 0 | `Product.Create` | `ProductTests.Create_rejects_a_negative_price` |
| Prix de base >= 0 | `Product.ChangeBasePrice` | `ProductTests.ChangeBasePrice_rejects_a_negative_price` |
| Devise du produit immuable | `Product.ChangeBasePrice` | `ProductTests.ChangeBasePrice_rejects_another_currency` |
| Changement de prix revalide les variantes | `Product.ChangeBasePrice` | `ProductTests.ChangeBasePrice_revalidates_existing_variants` |
| Event prix seulement si changement | `Product.ChangeBasePrice` | `ProductTests.ChangeBasePrice_to_the_same_price_raises_nothing` |
| Tenant obligatoire (l'id de taxe vient d'une référence non forgeable) | `Product.Create` | `ProductTests.Create_rejects_an_empty_tenant` |
| Taxe du même tenant | `Product.Create`, `ChangeTaxCategory` | `ProductTests.Create_rejects_a_tax_category_of_another_tenant`, `ProductTests.ChangeTaxCategory_rejects_a_category_of_another_tenant` |
| Pas de taxe archivée nouvellement affectée | `Product.Create`, `ChangeTaxCategory` | `ProductTests.Create_rejects_an_archived_tax_category`, `ProductTests.ChangeTaxCategory_rejects_an_archived_category`, `TaxCategoryTests.Archived_category_cannot_be_assigned_to_a_product` |
| Changement de taxe : seul l'id est stocké | `Product.ChangeTaxCategory` | `ProductTests.ChangeTaxCategory_replaces_it`, `ProductTests.Create_accepts_the_reference_of_a_real_tax_category` |
| Même taxe = no-op | `Product.ChangeTaxCategory` | `ProductTests.ChangeTaxCategory_to_the_category_already_held_is_a_no_op_even_once_it_is_archived` |
| Description <= 2000 | `Product.Create`, `ChangeDescription` | `ProductTests.Create_normalizes_a_blank_description_to_null_and_rejects_a_too_long_one`, `ProductTests.ChangeDescription_trims_accepts_the_limit_and_rejects_beyond` |
| `Draft/Unavailable -> Active` | `Product.Activate` | `ProductTests.Activate_moves_a_draft_to_active_and_raises_ProductActivated`, `ProductTests.Activate_makes_an_unavailable_product_sellable_again` |
| Pas de double activation | `Product.Activate` | `ProductTests.Activate_rejects_an_already_active_product` |
| `Unavailable` seulement depuis `Active` | `Product.MarkUnavailable` | `ProductTests.MarkUnavailable_rejects_a_draft_product`, `ProductTests.MarkUnavailable_rejects_an_already_unavailable_product` |
| Archivage depuis tout état non archivé | `Product.Archive` | `ProductTests.Archive_is_allowed_from_every_non_archived_state` |
| Archived terminal | toutes les mutations de `Product` | `ProductTests.Archived_product_is_terminal_and_rejects_every_mutation` |
| Archived non vendable | `Product.IsSellable`, `CanBeSold` | `ProductTests.Archived_product_is_not_sellable` |
| Vendable = actif + variante connue et disponible | `Product.CanBeSold` | `ProductTests.CanBeSold_requires_an_active_product_and_a_known_available_variant` |
| Prix refusé sur un produit archivé | `Product.PriceOf` | `ProductTests.PriceOf_rejects_an_archived_product` |
| Ligne de commande : produit, variante, prix unitaire, taxe | `Product.DescribeForSale` | `ProductTests.DescribeForSale_returns_what_an_order_line_snapshots_for_a_variant`, `ProductTests.DescribeForSale_without_variant_sells_a_product_without_variants_at_its_base_price` |
| Vente refusée : produit non vendable / archivé | `Product.DescribeForSale` | `ProductTests.DescribeForSale_rejects_a_product_that_is_not_sellable` |
| Vente refusée : variante inconnue / indisponible / manquante | `Product.DescribeForSale` | `ProductTests.DescribeForSale_rejects_an_unknown_or_unavailable_variant`, `ProductTests.DescribeForSale_requires_a_variant_when_the_product_has_some` |
| Noms transmis à Orders <= 120 | `CatalogName.Create`, `Product.DescribeForSale` | `ProductTests.Names_handed_to_an_order_line_never_exceed_120_characters` |
| Event prix de variante seulement si changement | `Product.ChangeVariantPriceDelta` | `ProductTests.ChangeVariantPriceDelta_raises_ProductVariantPriceChanged_only_on_a_real_change` |
| Event disponibilité de variante seulement si changement | `Product.SetVariantAvailability` | `ProductTests.SetVariantAvailability_raises_ProductVariantAvailabilityChanged_only_on_a_real_change` |
| Variantes, groupes attachés, allergènes bornés | `Product.AddVariant`, `AttachModifierGroup`, `SetAllergens` | `ProductTests.Variants_modifier_groups_and_allergens_are_bounded` |
| Variante unique par nom | `Product.AddVariant` | `ProductTests.AddVariant_rejects_a_duplicate_name_ignoring_case` |
| Variante unique par SKU si présent | `Product.AddVariant` | `ProductTests.AddVariant_rejects_a_duplicate_sku_but_allows_several_variants_without_sku` |
| Delta dans la devise du produit | `Product.AddVariant`, `ChangeVariantPriceDelta` | `ProductTests.AddVariant_rejects_a_delta_in_another_currency`, `ProductTests.ChangeVariantPriceDelta_rejects_another_currency_a_negative_price_and_an_unknown_variant` |
| `base + delta >= 0` | `Product.AddVariant`, `ChangeVariantPriceDelta` | `ProductTests.AddVariant_accepts_a_negative_delta_down_to_a_zero_price_and_rejects_below`, `ProductTests.ChangeVariantPriceDelta_rejects_another_currency_a_negative_price_and_an_unknown_variant` |
| Variante inconnue refusée | `RemoveVariant`, `SetVariantAvailability`, `PriceOf` | `ProductTests.RemoveVariant_removes_it_and_rejects_an_unknown_variant`, `ProductTests.SetVariantAvailability_toggles_and_is_idempotent`, `ProductTests.PriceOf_adds_the_variant_delta_to_the_base_price` |
| Groupe d'options sans doublon | `Product.AttachModifierGroup` | `ProductTests.AttachModifierGroup_rejects_a_duplicate` |
| Groupe du même tenant | `Product.AttachModifierGroup` | `ProductTests.AttachModifierGroup_rejects_a_group_of_another_tenant` |
| Groupe de même devise | `Product.AttachModifierGroup` | `ProductTests.AttachModifierGroup_rejects_a_group_in_another_currency` |
| Détacher un groupe non attaché | `Product.DetachModifierGroup` | `ProductTests.DetachModifierGroup_removes_it_and_rejects_a_group_that_is_not_attached` |
| Allergènes sans doublon ni id vide | `Product.SetAllergens` | `ProductTests.SetAllergens_replaces_the_list_and_collapses_duplicates`, `ProductTests.SetAllergens_rejects_an_empty_id_and_keeps_the_previous_list` |
| `0 <= min` | `SelectionRule.Create` | `SelectionRuleTests.Create_rejects_a_negative_min` |
| `min <= max` | `SelectionRule.Create` | `SelectionRuleTests.Create_rejects_a_max_below_min` |
| `1 <= max <= 50` | `SelectionRule.Create` | `SelectionRuleTests.Create_rejects_a_max_outside_1_to_the_limit` |
| Bornes valides acceptées | `SelectionRule.Create` | `SelectionRuleTests.Create_accepts_zero_le_min_le_max` |
| Règle indépendante du nombre d'options | `ModifierGroup.ChangeSelectionRule` | `ModifierGroupTests.ChangeSelectionRule_replaces_the_rule_whatever_the_number_of_modifiers` |
| `min` <= options disponibles (validation) | `ModifierGroup.EnsureSatisfiable` | `ModifierGroupTests.EnsureSatisfiable_compares_min_with_the_available_modifiers`, `ModifierGroupTests.An_optional_group_is_satisfiable_even_when_empty` |
| Options uniques par nom | `ModifierGroup.AddModifier`, `RenameModifier` | `ModifierGroupTests.AddModifier_rejects_a_duplicate_name_ignoring_case`, `ModifierGroupTests.RenameModifier_keeps_names_unique_but_allows_recasing_itself` |
| Supplément >= 0 | `ModifierGroup.AddModifier`, `ChangeModifierPrice` | `ModifierGroupTests.AddModifier_rejects_a_negative_price`, `ModifierGroupTests.ChangeModifierPrice_rejects_negative_other_currency_and_unknown_modifier` |
| Une devise par groupe | `ModifierGroup.AddModifier`, `ChangeModifierPrice` | `ModifierGroupTests.AddModifier_rejects_a_price_in_another_currency`, `ModifierGroupTests.ChangeModifierPrice_rejects_negative_other_currency_and_unknown_modifier` |
| Option inconnue refusée | `RemoveModifier`, `SetModifierAvailability` | `ModifierGroupTests.RemoveModifier_rejects_an_unknown_modifier`, `ModifierGroupTests.SetModifierAvailability_toggles_and_rejects_an_unknown_modifier` |
| Event prix d'option seulement si changement | `ModifierGroup.ChangeModifierPrice` | `ModifierGroupTests.ChangeModifierPrice_raises_ModifierPriceChanged_only_on_a_real_change` |
| Event disponibilité d'option seulement si changement | `ModifierGroup.SetModifierAvailability` | `ModifierGroupTests.SetModifierAvailability_raises_ModifierAvailabilityChanged_only_on_a_real_change` |
| Options par groupe bornées | `ModifierGroup.AddModifier` | `ModifierGroupTests.AddModifier_rejects_more_modifiers_than_the_limit` |
| Sélection client dans `min..max` | `ModifierGroup.EnsureSelectionIsValid` | `ModifierGroupTests.EnsureSelectionIsValid_rejects_too_few_and_too_many` |
| Sélection : options connues, disponibles, distinctes | `ModifierGroup.EnsureSelectionIsValid` | `ModifierGroupTests.EnsureSelectionIsValid_rejects_duplicate_unknown_and_unavailable_modifiers` |
| Tenant / location non vides | `ModifierGroup.Create`, `Menu.Create`, `LocationReference` | `ModifierGroupTests.Create_rejects_an_empty_tenant`, `MenuTests.Create_rejects_empty_tenant_or_location` |
| Location du même tenant (opérationnelle ou non) | `Menu.Create` | `MenuTests.Create_rejects_a_location_of_another_tenant`, `MenuTests.Create_accepts_a_location_that_is_not_operational` |
| Sections et produits par section bornés | `Menu.AddSection`, `AddProductToSection` | `MenuTests.Sections_and_products_per_section_are_bounded` |
| Sections de nom unique | `Menu.AddSection`, `RenameSection` | `MenuTests.AddSection_rejects_a_duplicate_name_ignoring_case`, `MenuTests.RenameSection_keeps_names_unique_but_allows_recasing_itself` |
| Produit unique par section | `Menu.AddProductToSection` | `MenuTests.AddProductToSection_rejects_a_product_already_in_the_section` |
| Produit du même tenant | `Menu.AddProductToSection` | `MenuTests.AddProductToSection_rejects_a_product_of_another_tenant` |
| Pas de produit archivé | `Menu.AddProductToSection` | `MenuTests.AddProductToSection_rejects_an_archived_product_and_an_unknown_section` |
| Ordre des sections / produits, position valide | `Menu.MoveSection`, `MoveProductInSection` | `MenuTests.MoveSection_reorders_sections_and_rejects_an_out_of_range_position`, `MenuTests.MoveProductInSection_reorders_products_and_rejects_invalid_moves` |
| Publication : au moins une section | `Menu.Publish` | `MenuTests.Publish_rejects_a_menu_without_section` |
| Publication : aucune section vide | `Menu.Publish` | `MenuTests.Publish_rejects_a_menu_with_an_empty_section` |
| Publication : aucun produit listé archivé | `Menu.Publish` | `MenuTests.Publish_rejects_a_listed_product_archived_since_it_was_added` |
| Publication : référence actuelle de chaque produit listé | `Menu.Publish` | `MenuTests.Publish_rejects_a_listed_product_without_current_reference`, `MenuTests.Publish_ignores_references_of_products_the_menu_does_not_list_and_rejects_a_null_one` |
| Horodatage de publication = horodatage de l'event | `Menu.Publish` | `MenuTests.Publish_stamps_the_menu_with_the_event_timestamp` |
| Publication seulement depuis `Draft` | `Menu.Publish` | `MenuTests.Publish_rejects_an_already_published_menu` |
| Contenu publié en lecture seule | mutations de contenu de `Menu` | `MenuTests.Published_menu_content_is_read_only` |
| Dépublication seulement depuis `Published` | `Menu.Unpublish` | `MenuTests.Unpublish_rejects_a_draft_menu` |
| Modification après dépublication | `Menu.Unpublish` puis mutations | `MenuTests.Unpublished_menu_can_be_edited_and_published_again` |
| Archived terminal | toutes les mutations de `Menu` | `MenuTests.Archived_menu_is_terminal_and_rejects_every_mutation` |
| Code taxe 1..32, majuscules, `[A-Z0-9_-]` | `TaxCategoryCode.Create` | `TaxCategoryTests.Code_create_trims_upper_cases_and_accepts_the_whole_charset`, `TaxCategoryTests.Code_create_rejects_an_invalid_value` |
| Nom de taxe non vide, <= 100 | `TaxCategoryName.Create` | `TaxCategoryTests.Name_create_rejects_an_empty_value`, `TaxCategoryTests.Name_create_trims_and_accepts_the_maximum_length_and_rejects_one_more` |
| Taux dans `0..1` inclus | `TaxRate.Create` | `TaxCategoryTests.Rate_create_accepts_a_fraction_between_0_and_1_with_at_most_6_decimals`, `TaxCategoryTests.Rate_create_rejects_a_value_outside_0_to_1` |
| Taux : 6 décimales au plus | `TaxRate.Create` | `TaxCategoryTests.Rate_create_rejects_more_than_6_decimals` |
| Catégorie créée `Active` + event | `TaxCategory.Create` | `TaxCategoryTests.Create_builds_an_active_category_and_raises_TaxCategoryCreated`, `TaxCategoryTests.Create_accepts_a_zero_rate` |
| Code unique par tenant | `TaxCategory.Create` | `TaxCategoryTests.Create_rejects_a_code_already_used_in_the_tenant` |
| Tenant obligatoire | `TaxCategory.Create` | `TaxCategoryTests.Create_rejects_an_empty_tenant` |
| Renommage sans event, même nom = no-op | `TaxCategory.Rename` | `TaxCategoryTests.Rename_changes_the_name_without_event`, `TaxCategoryTests.Rename_to_the_same_name_is_a_no_op` |
| Event taux seulement si changement | `TaxCategory.ChangeRate` | `TaxCategoryTests.ChangeRate_updates_the_rate_and_raises_TaxCategoryRateChanged`, `TaxCategoryTests.ChangeRate_to_the_same_rate_is_a_no_op`, `TaxCategoryTests.Rate_ignores_trailing_zeros_when_compared` |
| Nouveau taux pour les ventes futures seulement | `TaxCategory.ChangeRate`, `ToReference` | `TaxCategoryTests.ChangeRate_does_not_alter_a_reference_taken_before` |
| `Active -> Archived` | `TaxCategory.Archive` | `TaxCategoryTests.Archive_moves_the_category_to_archived_and_raises_TaxCategoryArchived` |
| Archived terminal | toutes les mutations de `TaxCategory` | `TaxCategoryTests.Archived_category_is_terminal_and_rejects_every_mutation` |
| Référence = tenant, id, code, nom, taux, archivé | `TaxCategory.ToReference` | `TaxCategoryTests.ToReference_carries_tenant_id_code_name_rate_and_archived_flag` |
| Nom non vide, <= 120 | `CatalogName.Create` | `CatalogValueObjectTests.CatalogName_create_rejects_an_empty_value`, `CatalogValueObjectTests.CatalogName_create_accepts_the_maximum_length_and_rejects_one_more` |
| SKU au format contrôlé | `Sku.Create` | `CatalogValueObjectTests.Sku_create_rejects_an_invalid_value`, `CatalogValueObjectTests.Sku_create_accepts_the_maximum_length_and_rejects_one_more` |
| Id non vide | `XxxId.From` | `CatalogValueObjectTests.Module_ids_reject_an_empty_guid_and_round_trip` |

## 5. Ce qui relève de l'Application / Infrastructure

- Charger le `ModifierGroup` / `Product` / `TaxCategory` référencé (sous isolation tenant) et passer `ToReference()` aux méthodes `AttachModifierGroup` / `AddProductToSection` / `Product.Create` / `ChangeTaxCategory` : la référence ne doit jamais venir d'une saisie utilisateur.
- **Unicité du code de taxe par tenant** : l'Application appelle `ITaxCategoryCodeUniquenessChecker` (catégories archivées comprises) et passe le booléen à `TaxCategory.Create`. L'index unique en base `(TenantId, Code)` reste le garde-fou final.
- **Assemblage du snapshot d'une ligne de commande** (le module Orders ne lit jamais le catalogue vivant) : l'Application charge le `Product` et sa `TaxCategory`, puis copie
  - `Product.DescribeForSale(variantId?)` : un seul appel qui valide la vente et renvoie `SellableItem` (`TenantId`, `ProductId`, `ProductName`, `VariantId?`, `VariantName?`, `UnitPrice` = base + delta, `TaxCategoryId`) à recopier tel quel dans le snapshot d'Orders ; ne pas recomposer avec `CanBeSold` + `PriceOf` ;
  - pour chaque option choisie : `ModifierGroup.EnsureSelectionIsValid(ids)` puis nom (`<= 120`) et `PriceDelta` de chaque `Modifier` ;
  - la taxe depuis `TaxCategory.ToReference()` : `Code`, `Name`, `Rate`, passés tels quels au snapshot de taxe d'Orders (formats identiques) ;
  - le mode de prix (TTC ou HT) depuis le réglage du tenant, pas depuis le catalogue : TTC par défaut. Les montants du catalogue sont transmis tels que saisis ; Orders les interprète selon ce mode.
- **Devise des produits** : l'Application construit le `Money` de `Product.Create` avec la devise par défaut du tenant. Rien ne l'impose ici (un agrégat ne voit pas le tenant) ; côté Tenants, le changement de devise est refusé dès qu'un catalogue tarifé existe — l'Application doit lui fournir cette information (existence d'au moins un produit).
- Avant d'archiver une `TaxCategory` : rien n'est bloqué par le domaine ; les produits qui la portent continuent de se vendre avec son dernier taux. Si l'on veut l'interdire tant que des produits l'utilisent, c'est une requête applicative (`TaxCategoryArchived` permet aussi de signaler les produits à réaffecter).
- Existence de `AllergenId` : vérifiée par l'Application (référentiel non modélisé ici). L'appartenance de la location au tenant est contrôlée par `Menu.Create` à partir de la `LocationReference` fournie par l'Application.
- Aucune autre unicité inter-agrégats n'est demandée par la spec (nom de produit, de groupe ou de menu) ; si elle devient requise : port de vérification + index unique en base, la base restant le garde-fou final.
- **Publication d'un menu** : charger (sous isolation tenant) les `Product` listés par le menu et passer leurs `ToReference()` à `Menu.Publish(references, clock)` ; appeler aussi `ModifierGroup.EnsureSatisfiable()` sur les groupes attachés. Un produit archivé **après** la publication n'est pas vu par l'agrégat `Menu` : handler sur `ProductArchived` (dépublier / retirer) et filtrage à l'affichage sur `IsSellable`.
- **Création d'un menu** : construire `LocationReference` depuis la location chargée (tenant, id, `IsOperational` = location active).
- Prise de commande : `Product.DescribeForSale()` + `ModifierGroup.EnsureSelectionIsValid()`.
- Réaction aux événements (`ProductArchived` -> retirer le produit des menus brouillons, `ProductPriceChanged`, `ProductVariantPriceChanged`, `ProductVariantAvailabilityChanged`, `ModifierPriceChanged`, `ModifierAvailabilityChanged` -> invalider caches / écrans de prix, `MenuPublished`...) et détachement d'un groupe supprimé : handlers applicatifs.
- Concurrence (deux éditions simultanées du même agrégat) : verrouillage optimiste (version de ligne) côté Infrastructure.
- Persistance : constructeurs `private` sans paramètre réservés à l'ORM ; aucune dépendance EF Core / ASP.NET Core.

## 6. Application (`Catalog.Application`)

Le tenant d'un cas d'usage vient **toujours** d'`ITenantContext.RequireTenant()` (claim `tid` du jeton), jamais de la
requête. Sans restaurant sélectionné : `403 common.tenant.required`.

| Cas d'usage | Effet | Erreurs propres à l'Application |
|---|---|---|
| `CreateTaxCategory(code, name, rate)`, `RenameTaxCategory`, `ChangeTaxRate`, `ArchiveTaxCategory` | Référentiel de taxes ; code unique par tenant (archivées comprises), taux en fraction (`0.18`) | `catalog.tax_category.not_found`, `catalog.tenant.not_writable` |
| `CreateModifierGroup(name, min, max)`, `RenameModifierGroup`, `ChangeSelectionRule` | Groupe vide dans la **devise par défaut du tenant** | `catalog.modifier_group.not_found`, `catalog.tenant.not_writable` |
| `AddModifier(name, priceDelta)`, `RenameModifier`, `ChangeModifierPrice`, `SetModifierAvailability`, `RemoveModifier` | Options du groupe, montants dans la devise du groupe | idem |
| `CreateProduct(name, description?, basePrice, taxCategoryId)` | Produit `Draft` dans la devise du tenant ; la catégorie fiscale est chargée dans le tenant (`ToReference()`) | `catalog.tax_category.not_found`, `catalog.tenant.not_writable` |
| `RenameProduct`, `ChangeProductDescription`, `ChangeProductTaxCategory`, `ChangeProductBasePrice`, `ActivateProduct`, `MarkProductUnavailable`, `ArchiveProduct` | Réglages et cycle de vie (règles du domaine) | `catalog.product.not_found`, idem |
| `AddProductVariant(name, priceDelta, sku?)`, `ChangeVariantPriceDelta`, `SetVariantAvailability`, `RemoveProductVariant` | Variantes, montants dans la devise du produit | idem |
| `AttachModifierGroup(groupId)`, `DetachModifierGroup` | Le groupe est chargé dans le tenant (`ToReference()`) | `catalog.modifier_group.not_found`, idem |
| `CreateMenu(locationId, name)` | Menu `Draft` pour un établissement du tenant (port `ICatalogLocations`, réponse de Locations) | `catalog.location.not_found`, `catalog.tenant.not_writable` |
| `RenameMenu`, `AddMenuSection`, `RenameMenuSection`, `MoveMenuSection(position)`, `RemoveMenuSection`, `AddProductToMenuSection(productId)`, `MoveProductInMenuSection(position)`, `RemoveProductFromMenuSection` | Contenu, modifiable en `Draft` seulement ; positions en base zéro | `catalog.menu.not_found`, `catalog.product.not_found`, idem |
| `PublishMenu`, `UnpublishMenu`, `ArchiveMenu` | `PublishMenu` charge l'état **actuel** des produits listés (`FindManyAsync`) et vérifie `EnsureSatisfiable()` sur chaque groupe attaché à ces produits, avant la transition | idem |
| `Get…ById`, `ListTaxCategories(includeArchived)`, `ListModifierGroups`, `ListProducts(page, pageSize, status?)`, `ListMenus(locationId?)` | Lecture, même tenant suspendu. Produits par nom, archivés masqués sauf `status=Archived` | `…not_found`, `catalog.paging.invalid` |
| `GetSellableItem(productId, variantId?, modifierIds?)` | Pour Orders (via l'hôte) : décrit la vente (nom, variante, prix unitaire, taxe, options) ou la refuse ; chaque groupe attaché valide sa part du choix (`EnsureSelectionIsValid`) | `catalog.product.not_found`, `catalog.sale.modifier_not_offered` |
| `HasPricedCatalog(tenantId)` | Pour Tenants (via l'hôte, route plateforme) : au moins un produit ou un groupe d'options, archivés compris | — |

- **`CatalogWorkspace`** porte les étapes communes : tenant courant, tenant **qui accepte les écritures** (port
  `ICatalogTenants`, réponse de Tenants : suspendu, fermé ou inconnu = 422 `catalog.tenant.not_writable`), chargement
  **dans ce tenant**, sauvegarde. Aucune règle métier hors du domaine.
- **Ressource d'un autre tenant** (produit, groupe, menu, catégorie, établissement) : `404 …not_found`, exactement
  comme un id inconnu.
- **Montants** : des nombres ; la devise n'est jamais choisie par la requête (devise du tenant à la création, puis
  celle du produit ou du groupe). Un montant plus précis que la devise est refusé (`common.money.invalid_precision`,
  ex. `3500.5` en XOF).
- **Permissions** : lecture ouverte à tout membre ; toute écriture exige `catalog.manage`
  (`CatalogPermissions.Manage`). Owner l'a ; Manager l'a pour les tenants **créés après** l'ajout du module (les
  rôles système existants ne sont pas réécrits : l'Owner peut l'accorder à un rôle).
- **Hors périmètre pour l'instant** (documenté, pas codé) :
  - **allergènes** : `Product.SetAllergens` existe mais aucun cas d'usage ne l'expose tant que le référentiel des
    allergènes n'existe pas (rien pour vérifier qu'un `AllergenId` existe) ;
  - **prix par établissement / canal** : non modélisé par le domaine (un prix par produit) ;
  - **réactions aux événements** (`ProductArchived` -> retirer des menus brouillons, invalidation de caches) :
    viendront avec les consommateurs de l'outbox ;

## 7. Infrastructure (`Catalog.Infrastructure`)

- Schéma `catalog` : `tax_categories`, `modifier_groups`, `modifiers`, `products`, `product_variants`, `menus`,
  `menu_sections`, `outbox_messages`.
- **`CatalogDbContext : TenantDbContext`** : filtre EF sur le tenant de la requête (rien sans tenant), **en plus** du
  filtre explicite des repositories. Seul `PricedCatalogLookup` (appelé hors requête tenant) contourne le filtre,
  avec un tenant explicite.
- **Montants** : `Money` en type complexe EF, deux colonnes (`numeric(19,3)` + code devise `char(3)`) : couvre les
  devises à 0, 2 et 3 décimales. `SelectionRule` en type complexe (`min_selections`, `max_selections`), taux en
  `numeric(7,6)`.
- **Références par id vers d'autres agrégats** sans clé étrangère, en **jsonb** : `products.modifier_group_ids`,
  `products.allergen_ids`, `menu_sections.product_ids` (ordonnés).
- **Listes ordonnées** (variantes, options, sections) : colonne `position` posée par l'unit of work depuis l'ordre de
  la liste de l'agrégat, relue avec `Include(... OrderBy(position))`. Les ids v7 ne suffisent pas (même milliseconde).
- **Concurrence optimiste** sur `xmin` de chaque racine ; ajouter, modifier, déplacer ou retirer un enfant réécrit
  aussi la ligne de la racine : deux ajouts concurrents (même nom de variante...) ne passent pas tous les deux (409).
- Index unique `(tenant_id, code)` sur `tax_categories`, traduit en 422 `catalog.tax_category.code_not_unique`.
- Domaine touché : `Product._modifierGroupIds`, `Product._allergenIds` et `MenuSection._productIds` ne sont plus
  `readonly` (l'ORM les pose au chargement).
- **Migration initiale à générer** (une fois, sur ta machine) :
  `dotnet ef migrations add InitialCreate --project src/Modules/Catalog/Catalog.Infrastructure --startup-project src/Modules/Catalog/Catalog.Infrastructure --output-dir Persistence/Migrations`.
  `database update` lit `CATALOG_DB`.

## 8. API (`Catalog.Api`)

Groupe `/catalog`, protégé par l'hôte avec la policy `tenant-member`. Chaque route d'écriture exige en plus la
permission `catalog.manage` (`403` sinon). Les créations renvoient `201` + `{ id }`.

| Méthode | Route | Corps |
|---|---|---|
| `POST` | `/catalog/tax-categories` | `{ code, name, rate }` |
| `GET` | `/catalog/tax-categories?includeArchived` · `/catalog/tax-categories/{id}` | — |
| `PUT` | `/catalog/tax-categories/{id}/name` · `/rate` | `{ name }` · `{ rate }` |
| `POST` | `/catalog/tax-categories/{id}/archive` | — |
| `POST` | `/catalog/modifier-groups` | `{ name, minSelections, maxSelections }` |
| `GET` | `/catalog/modifier-groups` · `/catalog/modifier-groups/{id}` | — |
| `PUT` | `/catalog/modifier-groups/{id}/name` · `/selection-rule` | `{ name }` · `{ minSelections, maxSelections }` |
| `POST` | `/catalog/modifier-groups/{id}/modifiers` | `{ name, priceDelta }` |
| `PUT` | `/catalog/modifier-groups/{id}/modifiers/{modifierId}/name` · `/price` · `/availability` | `{ name }` · `{ priceDelta }` · `{ isAvailable }` |
| `DELETE` | `/catalog/modifier-groups/{id}/modifiers/{modifierId}` | — |
| `POST` | `/catalog/products` | `{ name, basePrice, taxCategoryId, description? }` |
| `GET` | `/catalog/products?page&pageSize&status` · `/catalog/products/{id}` | — |
| `PUT` | `/catalog/products/{id}/name` · `/description` · `/tax-category` · `/base-price` | `{ name }` · `{ description }` · `{ taxCategoryId }` · `{ basePrice }` |
| `POST` | `/catalog/products/{id}/activate` · `/mark-unavailable` · `/archive` | — |
| `POST` | `/catalog/products/{id}/variants` | `{ name, priceDelta, sku? }` |
| `PUT` | `/catalog/products/{id}/variants/{variantId}/price-delta` · `/availability` | `{ priceDelta }` · `{ isAvailable }` |
| `DELETE` | `/catalog/products/{id}/variants/{variantId}` | — |
| `PUT` | `/catalog/products/{id}/locations` | `{ locationIds }` (vide = tous les établissements) |
| `PUT` | `/catalog/products/{id}/locations/{locationId}/availability` | `{ isAvailable }` (rupture locale) |
| `POST` · `DELETE` | `/catalog/products/{id}/modifier-groups` · `/catalog/products/{id}/modifier-groups/{groupId}` | `{ modifierGroupId }` · — |
| `POST` | `/catalog/menus` | `{ locationId, name }` |
| `GET` | `/catalog/menus?locationId` · `/catalog/menus/{id}` | — |
| `PUT` | `/catalog/menus/{id}/name` | `{ name }` |
| `POST` | `/catalog/menus/{id}/sections` | `{ name }` |
| `PUT` | `/catalog/menus/{id}/sections/{sectionId}/name` · `/position` | `{ name }` · `{ position }` |
| `DELETE` | `/catalog/menus/{id}/sections/{sectionId}` | — |
| `POST` | `/catalog/menus/{id}/sections/{sectionId}/products` | `{ productId }` |
| `PUT` · `DELETE` | `/catalog/menus/{id}/sections/{sectionId}/products/{productId}/position` · `…/products/{productId}` | `{ position }` · — |
| `POST` | `/catalog/menus/{id}/publish` · `/unpublish` · `/archive` | — |

Erreurs : `401` non connecté, `403` sans restaurant sélectionné ou sans `catalog.manage`, `404` ressource inconnue ou
d'un autre tenant, `409` conflit de concurrence, `422` règle métier (code dans `code`).

Tests : `Catalog.Application.Tests` (fakes), `Catalog.Infrastructure.Tests` et `Catalog.Api.Tests` (vrai
PostgreSQL, Docker ; **la migration doit exister**).

## 9. Produits par établissement

Les produits forment une bibliothèque partagée par le restaurant ; chaque produit dit où il est vendu.

- `Product.LocationIds` : établissements où le produit est vendu. Vide = tous, y compris ceux ouverts plus tard.
  `SetLocations` vérifie que chaque établissement appartient au tenant (`LocationReference`), dédoublonne, 200 au plus.
- `Product.UnavailableAt` : ruptures locales. `SetAvailabilityAt` exige un établissement où le produit est vendu ; retirer
  un établissement efface sa rupture. Le statut global (`Unavailable`) reste la rupture partout.
- `IsSellableAt(location)` est la seule règle de vente : `DescribeForSale` refuse `catalog.product.not_sold_here` et
  `catalog.product.unavailable_here` ; `OrderableCatalog` (caisse, borne) et `PublicMenu` (site) masquent les produits
  non vendus et montrent la rupture locale ; une section de carte n'accepte que des produits vendus dans l'établissement
  de la carte.
- Événements : `ProductLocationsChanged`, `ProductAvailabilityAtLocationChanged` (identifiants seulement).
- Persistance : colonnes jsonb `location_ids` et `unavailable_at` (migration `ProductLocations`,
  `scripts/migrations-etablissements.sh`). Les produits existants restent vendus partout.
