# Promotions — Domain

But : appliquer des remises déterministes, bornées et auditables.

## 1. Frontières d'agrégat

- **Promotion** (racine) : la règle promotionnelle, son cycle de vie et son compteur d'usages. `PromotionRules`
  (période, conditions, avantage, limite) est un VO remplacé d'un bloc : toutes les règles changent ensemble,
  sous la même policy.
- **Coupon** (racine séparée) : il a son propre compteur (`UsageCount <= MaxUses`) et sa propre contention
  (dernier usage) ; le mettre dans Promotion verrouillerait toute la promotion à chaque utilisation d'un code.
  Il référence la promotion par `PromotionId` ; à la création et à chaque `Redeem` il reçoit un
  `PromotionReference` (tenant, id, expirée), **non falsifiable** : constructeur `internal`, propriétés en lecture
  seule (pas de `with`), obtenu uniquement par `Promotion.ToReference()`.
- **Pas de liste d'usages par commande dans les agrégats** (elle serait non bornée) : ils ne portent qu'un
  compteur. Le lien (promotion|coupon, commande) vit dans un registre d'usages tenu par l'Application, lu via le
  port `IPromotionUsageLedger` et transmis en booléen (`orderAlreadyCounted`, `orderHoldsUsage`).
- **DiscountPolicy** : service de domaine pur (statique, sans état, sans DB).
- Références externes par id uniquement : `ProductId`, `LocationId`, `OrderId` (kernel). La commande est vue à
  travers `OrderSnapshot`, snapshot local au module (tenant, id de commande, devise, location, type, lignes,
  coupon présenté). Il décrit l'agrégat d'un **autre** module : il reste constructible publiquement (c'est
  l'Application qui le bâtit), mais ses ids sont contrôlés (`Guard.NotEmpty`).

## 2. Classes

| Classe | Rôle DDD | Responsabilité | Transitions | Events | Erreurs (`promotions.…`) |
|---|---|---|---|---|---|
| `Promotion` | AggregateRoot, tenant-owned | Cycle de vie, usages, changement de règles | `Draft -> Active` (Activate) ; `Active -> Paused` (Pause) ; `Paused -> Active` (Resume) ; `Draft/Active/Paused -> Expired` (Expire) ; `Expired` terminal | `PromotionCreated`, `PromotionActivated`, `PromotionPaused`, `PromotionResumed`, `PromotionRulesChanged`, `PromotionApplied`, `PromotionUsageReverted`, `PromotionExpired` | `promotion.name_required`, `name_too_long`, `rules_incomplete`, `period_ended`, `not_draft`, `not_active`, `not_paused`, `expired`, `outside_validity_period`, `usage_limit_reached`, `already_applied_to_order`, `no_usage_for_order`, `rules_locked_after_usage`, `destructive_change_after_usage`, `usage_limit_below_usage`, `rule_change_policy_unknown`, `common.tenant.mismatch`, `common.id.empty`, raisons `discount.*` (via `Apply`) |
| `PromotionRules` | ValueObject | Période + conditions + avantage? + limite? + `RequiresCoupon` (défaut false) ; une seule devise | — | — | `rules.currency_mismatch`, `common.period.invalid` |
| `Benefit` | ValueObject | Pourcentage (0 < p <= 100, plafond optionnel) ou montant fixe (> 0) ; `ComputeDiscount` | — | — | `benefit.percentage_out_of_range`, `benefit.max_discount_not_positive`, `benefit.amount_not_positive` |
| `Conditions` | ValueObject | Minimum de commande, produits, locations, types de commande (ensemble vide = pas de restriction) | — | — | `conditions.minimum_order_amount_not_positive`, `conditions.order_type_invalid`, `common.id.empty` |
| `UsageLimit` | ValueObject | Nombre maximal d'usages (> 0) | — | — | `usage_limit.not_positive` |
| `RuleChangePolicy` | Enum (policy) | `NonDestructiveOnly` (défaut), `Frozen`, `Unrestricted` | — | — | — |
| `PromotionReference` | Snapshot (record, constructeur `internal`) | Tenant + id + expirée, pour Coupon (`Create`, `Redeem`) ; produit seulement par `Promotion.ToReference()` | — | — | — |
| `Coupon` | AggregateRoot, tenant-owned | Code à usages limités, lié à une promotion | `Active -> Disabled` (Disable) ; `Disabled` : plus de Redeem (Release reste permis) | `CouponCreated`, `CouponRedeemed`, `CouponReleased`, `CouponDisabled` | `coupon.code_not_unique`, `max_uses_not_positive`, `promotion_expired`, `promotion_mismatch`, `already_applied_to_order`, `no_usage_for_order`, `disabled`, `exhausted`, `already_disabled`, `common.tenant.mismatch`, `common.id.empty` |
| `CouponCode` | ValueObject | Code normalisé (trim, majuscules, 3–32, `A-Z 0-9 -`) | — | — | `coupon_code.required`, `length_invalid`, `characters_invalid` |
| `ICouponCodeUniquenessChecker` | Port | Unicité du code par tenant | — | — | — |
| `IPromotionUsageLedger` | Port | « Cette commande détient-elle déjà un usage ? » (promotion, coupon) | — | — | — |
| `OrderSnapshot` / `OrderLineSnapshot` | ValueObject (public, ids contrôlés) | Tenant, commande, devise, location, type, lignes (produit, total de ligne) en ordre canonique, `CouponPresented` | — | — | `order_snapshot.order_type_invalid`, `line_total_negative`, `line_currency_mismatch`, `common.id.empty` ; ligne `null` => `ArgumentNullException` |
| `DiscountPolicy` | DomainService | `Evaluate(order, rules, now)` | — | — | — |
| `DiscountEvaluation` | ValueObject | Éligible ou non + raison stable + total éligible + remise | — | — | raisons `discount.*` (`DiscountIneligibility`) |

Raisons d'inéligibilité, dans l'ordre d'évaluation : `discount.no_benefit`, `coupon_required`, `outside_validity_period`,
`location_not_eligible`, `order_type_not_eligible`, `currency_mismatch`, `minimum_order_amount_not_reached`,
`no_eligible_amount`, `zero_discount`.

## 3. Choix faits là où la spec était ouverte

- **« Règle valide avant activation »** : un brouillon peut ne pas avoir d'avantage (`Benefit` nul) ; l'activation
  exige des règles complètes et une période non terminée. Activer avant le début de la période est permis
  (promotion programmée) ; `Apply` reste refusé hors période.
- **Policy de modification après usage** (`RuleChangePolicy`, paramètre de `ChangeRules`) : tant que
  `UsageCount == 0`, tout changement valide passe. Ensuite, par défaut (`NonDestructiveOnly`) seuls sont acceptés
  une fin de période plus tardive et une limite d'usage plus haute (ou retirée) ; changer l'avantage, les
  conditions, le début, raccourcir la période, baisser ou introduire une limite est refusé. `Frozen` refuse tout,
  `Unrestricted` accepte tout — mais jamais une limite inférieure aux usages déjà enregistrés.
- **Expire** est explicite et possible à tout moment (fin de période constatée par un job, ou arrêt anticipé).
  Le domaine ne fait pas expirer tout seul : hors période, `Apply`, `Activate` et `Resume` sont simplement refusés.
- **Apply** (= « RecordUsage ») prend l'`OrderSnapshot` et **calcule lui-même** la remise par
  `DiscountPolicy.Evaluate` : aucun appelant ne peut enregistrer un montant arbitraire. Une évaluation inéligible
  est rejetée avec sa raison stable (`discount.*`). La remise calculée est renvoyée et portée par `PromotionApplied`.
- **Usage lié à la commande, idempotent et réversible** : `Apply`/`Redeem` reçoivent `orderAlreadyCounted`
  (true => `..._already_applied_to_order`, testé avant le statut pour qu'un rejeu d'événement donne toujours la
  même erreur) ; `RevertUsage`/`Release` reçoivent `orderHoldsUsage` (false => `..._no_usage_for_order`),
  décrémentent le compteur (jamais sous zéro) et lèvent `PromotionUsageReverted` / `CouponReleased`. Rendre un
  usage est la correction d'un compte : permis dans tout statut, y compris `Expired` / `Disabled`. Décision du
  propriétaire : l'annulation d'une commande libère l'usage de la promotion et du coupon.
- **Promotion réservée aux coupons** (`PromotionRules.RequiresCoupon`, défaut false) : sans coupon présenté
  (`OrderSnapshot.CouponPresented`), l'évaluation est inéligible (`discount.coupon_required`) ; la même promotion ne
  peut donc pas être appliquée automatiquement et par son coupon. Changer ce drapeau après usage est destructif.
- **Coupon.Redeem vérifie sa promotion** : même tenant, `promotion.PromotionId == PromotionId`, promotion non
  expirée (`coupon.promotion_expired`, la même erreur qu'à la création).
- **Même valeur = no-op silencieux** : `ChangeRules` avec des règles identiques ne fait rien (ni event ni erreur).
- **Format inter-modules** : la référence de remise côté Orders fait au plus 64 caractères, insensible à la casse.
  `PromotionId` (36) et `CouponCode` (<= 32, normalisé en majuscules) tiennent dans ce format.
- **Devise** : tous les montants d'une règle (fixe, plafond, minimum) partagent une devise. Un pourcentage sans
  plafond ni minimum n'a pas de devise et s'applique à toute commande. Une devise différente de celle de la
  commande est une raison d'inéligibilité stable, pas une exception.
- **Minimum de commande** comparé au total de la commande entière (`OrderSnapshot.Total`) **tel qu'affiché au
  client** : le sous-total de la commande dans son propre mode de prix (TTC quand les prix du tenant incluent la
  taxe). La remise, elle, se calcule sur les seules lignes éligibles (restriction produits).
- **Arrondi** : `Money.Rounded` (demi vers l'extérieur, précision de la devise). Une remise arrondie à zéro
  rend la commande inéligible (`zero_discount`).
- **DiscountPolicy ne regarde ni le statut ni le compteur** de la promotion : ce sont des gardes de `Promotion.Apply`.
- Coupon : pas de réactivation (la spec ne prévoit que `Disable`) ; créer un coupon sur une promotion expirée est refusé.
- **Ids vides refusés** (`common.id.empty`) : `Promotion.Create` et `Coupon.Create` (tenant), `OrderSnapshot.Create`
  (tenant, commande, location), `OrderLineSnapshot.Create` (produit), `Conditions.Create` (produits, locations —
  un id vide ne pourrait jamais correspondre) et `Coupon.Redeem` (commande : elle devient une ligne du registre
  d'usages). Pas de `LocationReference` ici : aucune fabrique d'agrégat ne reçoit de `LocationId` nu, les
  locations n'apparaissent que comme ensemble de restriction dans `Conditions`.
- **Policy inconnue** : `ChangeRules` valide `RuleChangePolicy` par `Enum.IsDefined` **avant tout** (même sur une
  promotion non utilisée ou des règles identiques, où la policy ne serait pas lue) et lève
  `promotion.rule_change_policy_unknown` — plus d'`ArgumentOutOfRangeException`.
- **Égalité de `OrderSnapshot` indépendante de l'ordre des lignes** : les lignes sont triées à la création par une
  clé stable (`ProductId`, puis total de ligne) ; deux snapshots de la même commande lus dans un ordre différent
  sont égaux (même hash). Deux lignes identiques restent deux lignes (multi-ensemble).
- **Restriction par produit seulement** : une ligne de commande peut porter côté Orders un `VariantId?` (kernel) ;
  ce module n'en a pas besoin. `Conditions` restreint par `ProductId` uniquement — une promotion sur un produit
  vaut pour toutes ses variantes — et `OrderLineSnapshot` ne porte donc pas la variante.
- **Horloge lue une fois par opération** : l'horodatage stocké (`CreatedAtUtc`, `ExpiredAtUtc`) est exactement
  l'`OccurredAtUtc` de l'event, et le contrôle de période utilise le même instant que l'évaluation de la remise.

## 4. Matrice invariant -> méthode -> test

| Invariant | Méthode | Test |
|---|---|---|
| Début de période < fin | `Period.Create` (via `PromotionRules.Create`) | `PromotionValueObjectsTests.Rules_reject_a_period_whose_start_is_not_before_its_end` |
| Pourcentage dans ]0 ; 100] | `Benefit.PercentageOff` | `PromotionValueObjectsTests.Percentage_benefit_rejects_out_of_range_values`, `Percentage_benefit_accepts_the_bounds` |
| Plafond > 0 | `Benefit.PercentageOff` | `PromotionValueObjectsTests.Percentage_benefit_rejects_a_non_positive_cap` |
| Montant fixe > 0 | `Benefit.FixedAmountOff` | `PromotionValueObjectsTests.Fixed_benefit_rejects_a_non_positive_amount` |
| Minimum de commande > 0 | `Conditions.Create` | `PromotionValueObjectsTests.Conditions_reject_a_non_positive_minimum_order_amount` |
| Type de commande connu | `Conditions.Create` | `PromotionValueObjectsTests.Conditions_reject_an_unknown_order_type` |
| Égalité des conditions indépendante de l'ordre/doublons | `Conditions.Create` | `PromotionValueObjectsTests.Conditions_ignore_duplicates_and_input_order` |
| Limite d'usage > 0 | `UsageLimit.Of` | `PromotionValueObjectsTests.UsageLimit_rejects_zero_or_negative` |
| Une seule devise par règle | `PromotionRules.Create` | `PromotionValueObjectsTests.Rules_reject_amounts_in_different_currencies` |
| Nom requis / borné | `Promotion.Create` | `PromotionTests.Create_rejects_a_missing_name`, `Create_rejects_a_too_long_name` |
| Création en Draft, sans usage | `Promotion.Create` | `PromotionTests.Create_starts_as_an_unused_draft` |
| Règle complète avant activation | `Promotion.Activate` | `PromotionTests.Activate_rejects_rules_without_benefit` |
| Période non terminée à l'activation | `Promotion.Activate` | `PromotionTests.Activate_rejects_a_period_that_has_ended`, `Activate_accepts_the_last_instant_of_the_period` |
| Activate seulement depuis Draft | `Promotion.Activate` | `PromotionTests.Activate_rejects_an_active_or_paused_promotion` |
| Pause seulement depuis Active | `Promotion.Pause` | `PromotionTests.Pause_rejects_a_draft_or_already_paused_promotion` |
| Resume seulement depuis Paused, période non terminée | `Promotion.Resume` | `PromotionTests.Resume_rejects_a_draft_or_active_promotion`, `Resume_rejects_a_period_that_has_ended` |
| Expire depuis tout état non expiré | `Promotion.Expire` | `PromotionTests.Expire_is_allowed_from_draft_active_and_paused` |
| Expired terminal | toutes | `PromotionTests.Expired_is_terminal` |
| Usage seulement si Active | `Promotion.Apply` | `PromotionTests.Apply_rejects_a_draft_or_paused_promotion` |
| Usage seulement dans la période | `Promotion.Apply` | `PromotionTests.Apply_rejects_usage_before_the_period_starts`, `Apply_rejects_usage_once_the_period_has_ended` |
| Usages <= limite | `Promotion.Apply` | `PromotionTests.Apply_stops_at_the_usage_limit`, `Apply_is_unlimited_without_usage_limit` |
| Remise calculée par le domaine, jamais fournie | `Promotion.Apply` | `PromotionTests.Apply_computes_the_discount_from_the_rules_not_from_the_caller`, `Apply_counts_the_usage_and_raises_PromotionApplied` |
| Commande du même tenant | `Promotion.Apply` | `PromotionTests.Apply_rejects_an_order_of_another_tenant` |
| Commande inéligible rejetée avec sa raison (remise > 0 incluse) | `Promotion.Apply` | `PromotionTests.Apply_rejects_an_ineligible_order_with_the_evaluation_reason` |
| Un seul usage par commande (rejeu sûr) | `Promotion.Apply` | `PromotionTests.Apply_rejects_an_order_already_counted` |
| Promotion réservée aux coupons | `DiscountPolicy.Evaluate`, `Promotion.Apply` | `DiscountPolicyTests.Coupon_only_promotion_is_not_eligible_without_its_coupon`, `Coupon_only_promotion_is_eligible_when_its_coupon_is_presented`, `Promotion_without_coupon_requirement_ignores_the_coupon_flag`, `PromotionTests.Apply_of_a_coupon_only_promotion_needs_the_coupon` |
| Usage rendu, compteur >= 0 | `Promotion.RevertUsage` | `PromotionTests.RevertUsage_gives_the_usage_back_and_raises_PromotionUsageReverted`, `RevertUsage_never_drives_the_counter_below_zero` |
| Pas de retour sans usage détenu | `Promotion.RevertUsage` | `PromotionTests.RevertUsage_rejects_an_order_that_holds_no_usage` |
| Retour permis dans tout statut | `Promotion.RevertUsage` | `PromotionTests.RevertUsage_is_allowed_in_every_status` |
| Modification libre tant que non utilisée | `Promotion.ChangeRules` | `PromotionTests.ChangeRules_is_free_while_the_promotion_is_unused`, `ChangeRules_frozen_policy_does_not_block_an_unused_promotion` |
| Pas de modification destructive après usage (policy par défaut) | `Promotion.ChangeRules` | `PromotionTests.ChangeRules_default_policy_rejects_destructive_changes_after_usage`, `ChangeRules_default_policy_rejects_introducing_a_limit_after_usage`, `ChangeRules_default_policy_accepts_a_later_end_and_a_higher_limit_after_usage` |
| Policy Frozen / Unrestricted | `Promotion.ChangeRules` | `PromotionTests.ChangeRules_frozen_policy_rejects_any_change_after_usage`, `ChangeRules_unrestricted_policy_accepts_destructive_changes_after_usage` |
| Limite jamais sous les usages enregistrés | `Promotion.ChangeRules` | `PromotionTests.ChangeRules_never_sets_a_limit_below_the_recorded_usages` |
| Une promotion activée reste valide | `Promotion.ChangeRules` | `PromotionTests.ChangeRules_keeps_an_activated_promotion_valid` |
| Changement identique = no-op silencieux | `Promotion.ChangeRules` | `PromotionTests.ChangeRules_with_identical_rules_is_a_silent_no_op` |
| `RequiresCoupon` non modifiable après usage (policy par défaut) | `Promotion.ChangeRules` | `PromotionTests.ChangeRules_default_policy_rejects_changing_the_coupon_requirement_after_usage` |
| Code normalisé, charset et longueur contrôlés | `CouponCode.Create` | `CouponTests.Code_is_trimmed_and_upper_cased`, `Code_is_required`, `Code_length_is_bounded`, `Code_rejects_characters_outside_the_charset` |
| Code unique par tenant | `Coupon.Create` | `CouponTests.Create_rejects_a_code_already_used_by_the_tenant` |
| Promotion du même tenant | `Coupon.Create` | `CouponTests.Create_rejects_a_promotion_of_another_tenant` |
| Pas de coupon sur promotion expirée | `Coupon.Create` | `CouponTests.Create_rejects_an_expired_promotion` |
| MaxUses > 0 | `Coupon.Create` | `CouponTests.Create_rejects_a_non_positive_max_uses` |
| UsageCount <= MaxUses | `Coupon.Redeem` | `CouponTests.Usage_count_never_exceeds_max_uses` |
| Dernier usage concurrent | `Coupon.Redeem` | `CouponTests.Redeem_of_the_last_use_succeeds_once_then_is_rejected` |
| Coupon désactivé non utilisable | `Coupon.Redeem` | `CouponTests.Redeem_rejects_a_disabled_coupon` |
| Redeem : promotion du même tenant, la sienne, non expirée | `Coupon.Redeem` | `CouponTests.Redeem_rejects_a_promotion_of_another_tenant`, `Redeem_rejects_another_promotion_than_its_own`, `Redeem_rejects_an_expired_promotion` |
| Un seul usage de coupon par commande (rejeu sûr) | `Coupon.Redeem` | `CouponTests.Redeem_rejects_an_order_already_counted` |
| Usage de coupon rendu, compteur >= 0, même désactivé | `Coupon.Release` | `CouponTests.Release_gives_the_use_back_and_raises_CouponReleased`, `Release_is_allowed_on_a_disabled_coupon_and_never_goes_below_zero` |
| Pas de retour sans usage détenu | `Coupon.Release` | `CouponTests.Release_rejects_an_order_that_holds_no_use` |
| Références de remise <= 64 caractères (format Orders) | `CouponCode.MaxLength`, `PromotionId.ToString` | `CouponTests.Discount_references_fit_the_orders_format` |
| Disable non rejouable | `Coupon.Disable` | `CouponTests.Disable_rejects_an_already_disabled_coupon` |
| Remise <= total éligible | `DiscountPolicy.Evaluate`, `Benefit.ComputeDiscount` | `DiscountPolicyTests.Fixed_discount_never_exceeds_the_eligible_total`, `Discount_never_exceeds_the_eligible_total_whatever_the_benefit`, `PromotionValueObjectsTests.ComputeDiscount_never_exceeds_the_eligible_total` |
| Remise <= plafond | `DiscountPolicy.Evaluate` | `DiscountPolicyTests.Percentage_discount_is_capped_by_the_maximum_discount`, `Cap_higher_than_the_eligible_total_is_bounded_by_the_eligible_total` |
| Remise calculée sur les lignes éligibles | `DiscountPolicy.Evaluate` | `DiscountPolicyTests.Percentage_applies_to_eligible_lines_only` |
| Arrondi à la précision de la devise | `DiscountPolicy.Evaluate` | `DiscountPolicyTests.Percentage_is_rounded_to_the_currency_precision`, `Percentage_is_rounded_for_a_zero_decimal_currency` |
| Devise incompatible = raison stable | `DiscountPolicy.Evaluate` | `DiscountPolicyTests.Fixed_benefit_in_another_currency_is_a_stable_ineligibility_reason`, `Cap_or_minimum_in_another_currency_is_not_eligible` |
| Éligibilité : période, location, type, minimum, lignes | `DiscountPolicy.Evaluate` | `DiscountPolicyTests.Order_outside_the_validity_period_is_not_eligible`, `Order_from_another_location_is_not_eligible`, `Order_of_another_type_is_not_eligible`, `Minimum_order_amount_is_compared_with_the_whole_order_total`, `Order_without_eligible_line_is_not_eligible`, `Rules_without_benefit_give_no_discount`, `Discount_rounding_to_zero_is_not_eligible` |
| Déterminisme | `DiscountPolicy.Evaluate` | `DiscountPolicyTests.Evaluate_is_deterministic`, `Evaluate_does_not_depend_on_line_order` |
| Snapshot cohérent (devise, lignes >= 0, type) | `OrderSnapshot.Create`, `OrderLineSnapshot.Create` | `DiscountPolicyTests.Snapshot_rejects_a_line_in_another_currency`, `Snapshot_rejects_a_negative_line_total`, `Snapshot_rejects_an_unknown_order_type`, `Snapshot_carries_the_tenant_the_order_and_the_coupon_flag` |
| `RequiresCoupon` fait partie de l'égalité des règles | `PromotionRules.Create` | `PromotionValueObjectsTests.Rules_keep_the_coupon_requirement_and_compare_it` |
| Tenant non vide à la création | `Promotion.Create`, `Coupon.Create` | `PromotionTests.Create_rejects_an_empty_tenant`, `CouponTests.Create_rejects_an_empty_tenant` |
| Ids du snapshot non vides (tenant, commande, location, produit) | `OrderSnapshot.Create`, `OrderLineSnapshot.Create` | `DiscountPolicyTests.Snapshot_rejects_empty_ids` |
| Ids de restriction non vides | `Conditions.Create` | `PromotionValueObjectsTests.Conditions_reject_empty_ids` |
| Commande non vide pour un usage de coupon | `Coupon.Redeem` | `CouponTests.Redeem_rejects_an_empty_order_id` |
| Ligne `null` => `ArgumentNullException` | `OrderSnapshot.Create` | `DiscountPolicyTests.Snapshot_rejects_a_null_line` |
| Égalité du snapshot indépendante de l'ordre des lignes | `OrderSnapshot.Create` | `DiscountPolicyTests.Snapshot_equality_does_not_depend_on_line_order` |
| Policy de modification connue (erreur stable) | `Promotion.ChangeRules` | `PromotionTests.ChangeRules_rejects_an_unknown_policy` |
| `PromotionReference` non falsifiable | `PromotionReference` (constructeur `internal`), `Promotion.ToReference` | `CouponTests.Promotion_reference_cannot_be_built_outside_the_module`, `PromotionTests.ToReference_exposes_tenant_id_and_expiry` |
| Horloge lue une fois par opération | toutes | `PromotionTests.Operations_read_the_clock_once`, `CouponTests.Create_stamps_the_coupon_and_its_event_with_the_same_instant` |

## 5. Ce qui relève de l'Application / Infrastructure

- **Unicité du code coupon** : l'Application appelle `ICouponCodeUniquenessChecker` et passe le booléen à
  `Coupon.Create` ; l'index unique `(TenantId, Code)` en base est la garde finale.
- **Concurrence** (dernier usage d'un coupon, dernière utilisation d'une promotion limitée) : l'agrégat rend la
  décision déterministe ; le verrouillage optimiste (version de ligne) ou pessimiste et le rejeu sont à la
  frontière Application/Infrastructure. Deux transactions concurrentes ne peuvent pas toutes deux valider.
- **Construction de `OrderSnapshot`** depuis Orders : `TenantId` et `OrderId` de la commande, ses lignes avec leur
  total de ligne et donc son `SubTotal` **dans le mode de prix de la commande** (TTC si les prix du tenant incluent
  la taxe) : c'est le montant affiché au client qui est comparé au minimum de commande. `CouponPresented` = le
  client a présenté un coupon dont `PromotionId` est la promotion évaluée (à renseigner promotion par promotion,
  `WithCouponPresented`).
- **Orchestration d'une remise** : `DiscountPolicy.Evaluate` sert à prévisualiser ; pour enregistrer, lire
  `IPromotionUsageLedger.HasUsageForOrderAsync`, appeler `Promotion.Apply(snapshot, orderAlreadyCounted, clock)`
  (qui renvoie la remise à poser sur la commande) et, s'il y a un code,
  `Coupon.Redeem(promotion.ToReference(), orderId, orderAlreadyCounted, clock)`, dans la même transaction.
- **Registre d'usages** : l'Application persiste une ligne `(PromotionId, OrderId)` et une ligne
  `(CouponId, OrderId)` — index unique sur chaque couple — dans la même transaction que l'agrégat, à partir des
  events `PromotionApplied` / `CouponRedeemed`, et les supprime sur `PromotionUsageReverted` / `CouponReleased`.
  L'index unique est la garde finale contre le double comptage concurrent.
- **Annulation d'une commande** (event Orders) : pour chaque usage détenu, `Promotion.RevertUsage` et
  `Coupon.Release` avec `orderHoldsUsage` lu dans le registre ; un rejeu donne `..._no_usage_for_order`, à traiter
  comme « déjà fait ».
- **Référence de remise sur la commande** : `PromotionId.ToString()` ou `CouponCode.Value` (<= 64 caractères,
  comparaison insensible à la casse côté Orders ; `CouponCode` est déjà en majuscules).
- **Expiration automatique** : un job appelle `Promotion.Expire` quand la période est terminée.
- `PromotionReference` s'obtient **uniquement** par `Promotion.ToReference()` sur la promotion chargée sous isolation
  tenant (constructeur `internal`) ; l'Application ne peut pas en fabriquer une.
- **Contrat avec Orders** : pour bâtir `OrderLineSnapshot`, l'Application passe le `ProductId` et le total de la
  ligne ; le `VariantId?` éventuel de la ligne est ignoré (restriction par produit uniquement). Les ids passés à
  `OrderSnapshot` doivent être réels : un id par défaut est refusé (`common.id.empty`).

## 6. Persistance (étape 1)

Schéma `promotions`, historique des migrations dans le même schéma. Toutes les tables portent un `tenant_id`
obligatoire et sont filtrées sur le tenant courant (`TenantDbContext`).

| Table | Contenu | Gardes en base |
|---|---|---|
| `promotions` | Agrégat `Promotion` ; règles en **une valeur jsonb** (`rules`), relue par les fabriques du domaine | CHECK statut, compteur >= 0, période, `expired_at_utc` présent si et seulement si `Expired` ; version `xmin` |
| `coupons` | Agrégat `Coupon` | Index unique `(tenant_id, code)` ; CHECK format du code, `0 <= usage_count <= max_uses` ; FK vers `promotions` ; version `xmin` |
| `promotion_usages` | Registre : une ligne par (promotion, commande), avec la remise accordée | Index unique `(promotion_id, order_id)` parmi les lignes non rendues ; remise > 0 |
| `coupon_usages` | Registre : une ligne par (coupon, commande) | Index unique `(coupon_id, order_id)` parmi les lignes non rendues |
| `outbox_messages` | Événements à publier | — |

Choix :

- **Règles en jsonb** : elles changent toujours d'un bloc (`ChangeRules`) et ne sont jamais interrogées champ par champ.
  Les noms des champs JSON sont fixés par attribut. Ce que les requêtes filtrent (`valid_from_utc`, `valid_until_utc`,
  `requires_coupon`) est **recopié** dans des colonnes par l'unit of work à chaque ajout ou modification.
- **Registre tenu par l'unit of work, à partir des événements** : `PromotionApplied` / `CouponRedeemed` ajoutent une
  ligne, `PromotionUsageReverted` / `CouponReleased` la marquent rendue (`reverted_at_utc`, `released_at_utc`). Aucune
  ligne n'est supprimée : toute remise accordée puis annulée reste tracée. Compteur et registre partent dans la même
  transaction ; une sauvegarde rejouée après un refus n'écrit pas deux fois la même ligne.
- **`IPromotionUsageLedger`** lit le registre et garde la ligne trouvée suivie : rendre un usage exige de l'avoir lu
  avant (sinon `InvalidOperationException`, bug d'orchestration). Une ligne ajoutée dans la même unité de travail
  compte déjà.
- **Erreurs de la base traduites** : code déjà pris -> `coupon.code_not_unique` ; second usage d'une même commande ->
  `promotion.already_applied_to_order` / `coupon.already_applied_to_order` ; version périmée (dernier usage disputé)
  -> `promotions.concurrent_update`.

Tests : `PromotionsModelTests` et `PromotionRulesJsonTests` (sans base), `PromotionsPersistenceTests` (PostgreSQL via
Testcontainers).

## 7. Application, intégration et API (étapes 2 à 6)

### Cas d'usage

| Cas d'usage | Déclencheur | Règles appliquées ici |
|---|---|---|
| `CreatePromotion`, `ChangePromotionRules`, `Activate/Pause/Resume/ExpirePromotion` | Personnel (`promotions.manage`) | Règles saisies (`PromotionRulesInput`) construites par les fabriques du domaine ; une seule devise pour tous les montants (`CurrencyCode`) ; avantage incohérent -> `promotions.benefit.invalid` |
| `CreateCoupon`, `DisableCoupon` | Personnel (`promotions.manage`) | Code normalisé, unicité par le port puis l'index |
| `ApplyPromotionToOrder` | Personnel (`promotions.apply`) | Soit une promotion (id), soit un code coupon, jamais les deux ; commande en brouillon lue dans Orders ; remise calculée par le domaine sur l'instantané de la commande ; une fois par commande |
| `PreviewOrderDiscounts` | Personnel (`promotions.view`) | Promotions automatiques + celle du coupon présenté ; rien n'est enregistré ; raison stable par promotion |
| `ReleaseOrderPromotions` | `orders.OrderCancelled` | Rend tous les usages (promotions et coupons) encore détenus par la commande |
| `RevertPromotionForOrder` | Compensation de `promotions.PromotionApplied` | Rend l'usage de CETTE promotion (et de son coupon) quand la commande refuse la remise |
| `ExpireEndedPromotions` | Job `PromotionExpiryWorker` | Promotions non expirées dont la période est terminée -> `Expired` |

Les réactions renvoient `Ignored`, `AlreadyDone` ou `Done` et sont idempotentes.

### Application d'une promotion : saga par l'outbox

1. `ApplyPromotionToOrder` compte l'usage (promotion, et coupon s'il y en a un) et lève `PromotionApplied` (remise).
2. L'hôte (`PromotionsFlow`) pose la remise sur la commande : `Orders.ApplyExternalDiscountCommand`, source
   `Promotion`, référence = id de la promotion (une promotion vaut une fois par commande, coupon ou pas). Déjà posée
   (rejeu) = rien à faire.
3. Si la commande la refuse (plus en brouillon, devise...), l'hôte rend l'usage (`RevertPromotionForOrder`).
4. Une commande annulée rend tous ses usages (`ReleaseOrderPromotions`).

Instantané de commande (`IPromotionOrders`, fourni par l'hôte depuis Orders) : établissement, canal, devise, lignes
(total de ligne = prix × quantité, options comprises, dans le mode de prix de la commande, avant remise). La variante
d'une ligne est ignorée.

### Routes (`/promotions`, tenant du jeton, `Tenancy.TenantMemberPolicy`)

| Route | Permission |
|---|---|
| `GET /`, `GET /{id}`, `GET /{id}/coupons`, `GET /coupons/{couponId}`, `GET /orders/{orderId}/preview[?couponCode]` | `promotions.view` |
| `POST /orders/{orderId}/apply` (`{ promotionId }` ou `{ couponCode }`) | `promotions.apply` |
| `POST /`, `PUT /{id}/rules`, `POST /{id}/activate`, `/pause`, `/resume`, `/expire`, `POST /{id}/coupons`, `POST /coupons/{couponId}/disable` | `promotions.manage` |

Migration appliquée au démarrage de l'hôte (`MigratePromotionsDatabaseAsync`) ; job coupable par
`Promotions:RunWorkers=false`. Aucune nouvelle table depuis l'étape 1 : pas de nouvelle migration.

## 8. Tests (étape 7)

| Projet | Contenu |
|---|---|
| `Promotions.Application.Tests` | `PromotionUseCaseTests` : création et règles, avantage incohérent, modification après usage, coupon unique et normalisé, application (une fois, coupon obligatoire, coupon épuisé, cible unique, brouillon, raisons stables, limite), prévisualisation, annulation et compensation, expiration, multi-tenant, tenant suspendu, pagination |
| `Promotions.Infrastructure.Tests` | Modèle, règles jsonb, registre, + `PromotionsRepositoriesTests` (promotions automatiques, promotions terminées et répertoire du job, coupon par code et unicité, usages détenus par une commande) |
| `Promotions.Api.Tests` | Authentification, permissions par action, cycle de vie, coupon sur commande (prévisualisation, une fois), erreurs stables, isolation entre restaurants, tenant suspendu |

## 9. Audit (étape 8)

| Point | Application | Vérification |
|---|---|---|
| TenantId toujours issu du contexte | Aucune route ni commande ne reçoit de tenant | `PromotionsEndpointsTests`, `PromotionUseCaseTests` |
| Lecture croisée impossible | Filtres EF sur promotions, coupons et registres ; 404 sur l'id ou le code d'un autre tenant | `PromotionsPersistenceTests`, `PromotionsEndpointsTests` |
| Montant non falsifiable | La remise est calculée par le domaine ; le client ne transmet qu'un id ou un code | `PromotionUseCaseTests` |
| Pas de double usage | Une fois par commande (port + index filtré) ; dernier usage disputé tranché par la version de ligne | `PromotionsPersistenceTests` |
| Usage rendu | Annulation de commande et refus de la remise | `PromotionUseCaseTests` |
| Codes coupons | Pas de recherche partielle ni de liste publique : un code inconnu donne 404 | `PromotionsEndpointsTests` |
| Journal | Ids, types et codes d'erreur seulement | Revue (`PromotionsFlow`, `PromotionExpiryWorker`) |

Limites connues : une commande dont les lignes changent voit ses remises retirées par Orders (`ClearDiscounts`) sans que
l'usage soit rendu (il l'est à l'annulation) ; pas de limitation du nombre d'essais de codes coupons (à ajouter au
niveau de l'hôte, par limitation de débit, si les codes deviennent saisissables par le client).
