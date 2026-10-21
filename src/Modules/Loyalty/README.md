# Loyalty — Domain

But : tenir un ledger fiable de points et de récompenses.

## 1. Frontières d'agrégat

- **LoyaltyAccount** (racine) écrit son ledger : les `LoyaltyTransaction` sont des entités enfants, créées
  uniquement par le compte (`internal`), jamais modifiées ni supprimées (une annulation est une nouvelle écriture
  `Reversal` liée à l'originale). Le ledger reste la vérité ; `Balance` en est la projection **stockée**, déplacée
  dans la même transaction par exactement les écritures que l'agrégat ajoute.
- **L'agrégat ne charge pas son historique.** Le ledger est non borné : le charger à chaque opération serait
  coûteux, et un chargement partiel désactiverait silencieusement l'idempotence si elle reposait sur un parcours de
  la collection. Aucune règle ne relit donc la collection :
  - `Transactions` = **les écritures ajoutées depuis le chargement de l'agrégat**, rien d'autre. L'Infrastructure
    les insère et **ne réhydrate pas** l'historique (à la création du compte, c'est tout le ledger ; après un
    rechargement, la collection est vide).
  - « cette référence est-elle déjà postée ? » et « quelle écriture annuler ? » sont lus dans le ledger stocké par
    le port `ILoyaltyLedgerLookup` et transmis à l'agrégat (`bool referenceAlreadyPosted`, `PostedTransaction`).
  - chaque `LoyaltyTransaction` porte `TenantId` et `AccountId` : elle se stocke et se relit seule.
  - invariant vérifiable sur l'agrégat : **solde après = solde avant + somme des écritures ajoutées** ; « solde =
    somme du ledger » se vérifie sur le ledger stocké.
- **Reward** (racine séparée) : catalogue de récompenses, cycle de vie indépendant des comptes. Le compte ne la
  connaît que par un `RewardReference` (tenant, id, coût, valeur de remise, active) au moment de l'échange ;
  ce snapshot est **non falsifiable** (constructeur `internal`, propriétés en lecture seule, produit uniquement par
  `Reward.ToReference()`).
- Références externes par id : `CustomerId` (kernel), reçu dans un `CustomerReference` portant son tenant et
  son état d'anonymisation. Il décrit l'agrégat d'un autre module : il reste constructible publiquement, ses ids
  sont contrôlés (`Guard.NotEmpty`) et non remplaçables ensuite.
  `TransactionReference` est une chaîne opaque (id de commande, id d'échange…).

## 2. Classes

| Classe | Rôle DDD | Responsabilité | Transitions | Events | Erreurs (`loyalty.…`) |
|---|---|---|---|---|---|
| `LoyaltyAccount` | AggregateRoot, tenant-owned | Ledger, solde, statut | `Active -> Suspended` (Suspend) ; `Suspended -> Active` (Reactivate) ; `Active/Suspended -> Closed` (Close) ; `Closed` terminal | `LoyaltyAccountOpened`, `PointsEarned`, `PointsRedeemed`, `RewardRedeemed`, `PointsEarnReversed`, `PointsRedeemReversed`, `LoyaltyAdjusted`, `PointsExpired`, `LoyaltyAccountSuspended`, `LoyaltyAccountReactivated`, `LoyaltyAccountClosed` | `account.already_exists`, `customer_anonymized`, `closed`, `transaction_not_found`, `transaction_of_another_account`, `transaction_type_mismatch`, `already_reversed`, `reason_required`, `reason_too_long`, `suspended`, `already_suspended`, `not_suspended`, `insufficient_balance`, `duplicate_transaction`, `adjustment_reason_required`, `adjustment_reason_too_long`, `nothing_to_expire`, `reward_inactive`, `common.tenant.mismatch`, `common.id.empty` |
| `LoyaltyTransaction` | Entity enfant (ledger) | Écriture immuable et autonome : tenant, compte, type (`Earn`, `Redeem`, `Adjust`, `Expire`, `Reversal`, `Closure`), sens, points, référence, raison, écriture annulée (`ReversedTransactionId`), date ; `ToPostedReference(isAlreadyReversed)` (projection pure) | aucune | (levés par la racine) | `transaction.points_not_positive`, `transaction.direction_invalid` |
| `Points` | ValueObject | Quantité de points >= 0 | — | — | `points.negative` |
| `TransactionReference` | ValueObject | Source opaque, clé d'idempotence | — | — | `reference.required`, `reference.too_long` |
| `OverdraftPolicy` | ValueObject (policy) | Découvert autorisé : `None` (défaut) ou `UpTo(points)` | — | — | `overdraft_policy.not_positive` |
| `LedgerDirection` | Enum | `Credit` / `Debit` | — | — | — |
| `CustomerReference` | Snapshot d'un autre module (record public, ids contrôlés) | Contrôle de tenant, anonymisation | — | — | `common.id.empty` |
| `RewardReference` | Snapshot du même module (constructeur `internal`) | Coût, valeur de remise, disponibilité, `DiscountReference` ; produit seulement par `Reward.ToReference()` | — | — | — |
| `PostedTransaction` | Snapshot du même module (constructeur `internal`) | Écriture déjà stockée vue par le compte pour l'annuler : compte, id, type, points, référence, `IsAlreadyReversed` ; produit seulement par `LoyaltyTransaction.ToPostedReference(bool)` | — | — | — |
| `ILoyaltyLedgerLookup` | Port | Lecture du ledger stocké : `IsReferencePostedAsync` (garde de doublon), `FindPostedAsync` (écriture à annuler) | — | — | — |
| `ILoyaltyAccountUniquenessChecker` | Port | Un compte par (tenant, client) | — | — | — |
| `Reward` | AggregateRoot, tenant-owned | Récompense échangeable, son coût en points et sa valeur de remise | `Active -> Inactive` (Deactivate) ; `Inactive -> Active` (Activate) | `RewardCreated`, `RewardActivated`, `RewardDeactivated`, `RewardCostChanged`, `RewardDiscountValueChanged` | `reward.name_required`, `name_too_long`, `cost_not_positive`, `discount_value_not_positive`, `already_active`, `already_inactive`, `common.id.empty` |

## 3. Choix faits là où la spec était ouverte

- **Points non négatifs par opération** : chaque opération prend un `Points` strictement positif ; le signe vient
  du type (`Earn` crédite, `Redeem`/`Expire` débitent) ou, pour `Adjust`, d'un `LedgerDirection` explicite.
- **`Balance` est un `long` signé** : c'est la seule façon de représenter honnêtement le « sauf policy ». Sans
  policy explicite il ne passe jamais sous zéro.
- **« redeem <= balance sauf policy »** : `OverdraftPolicy` est un paramètre optionnel de `Redeem`,
  `RedeemReward` et `Adjust` ; défaut `OverdraftPolicy.None`. `UpTo(n)` autorise un solde jusqu'à `-n`.
- **Compte suspendu** : `Earn`, `Redeem`, `RedeemReward` refusés. `Adjust` et `ExpirePoints` restent permis : une
  correction manuelle (reprise de points pendant une enquête de fraude) et l'expiration (règle de temps) ne sont
  pas des actions du client.
- **Adjust** : raison obligatoire (<= 250 caractères). Un débit d'ajustement obéit à la même règle de découvert
  qu'un échange.
- **Idempotence** : la même `TransactionReference` ne peut être postée deux fois pour le même type (`Earn`,
  `Redeem`, et — référence optionnelle — `Adjust`, `Expire`) ; le second appel est **rejeté**
  (`duplicate_transaction`), pas ignoré : l'Application décide d'en faire un succès idempotent. La même référence
  peut servir une fois par type. **La garde ne parcourt pas la collection** : `Earn`, `Redeem`, `RedeemReward` et
  les surcharges à référence de `Adjust` / `ExpirePoints` reçoivent `bool referenceAlreadyPosted`, réponse de
  `ILoyaltyLedgerLookup.IsReferencePostedAsync` pour (compte, type, référence) — `RedeemReward` interroge le type
  `Redeem`. Les surcharges sans référence (`Adjust(points, sens, raison, clock)`, `ExpirePoints(points, clock)`) ne
  prennent pas de booléen et ne sont pas dédupliquées.
- **Annulation liée à l'écriture d'origine** (décision du propriétaire : l'annulation ou le remboursement d'une
  commande reprend les points) : `ReverseEarn(PostedTransaction? original, raison)` reçoit l'écriture `Earn`
  stockée (lue par `ILoyaltyLedgerLookup.FindPostedAsync`) et poste un débit `Reversal` des mêmes points, portant
  la même référence et lié par `ReversedTransactionId` ; `ReverseRedeem` recrédite de même un `Redeem` (commande
  annulée avant usage). Le compte vérifie, dans cet ordre : `null` (rien trouvé) => `transaction_not_found` ;
  écriture d'un autre compte => `transaction_of_another_account` ; mauvais type (un `Redeem` passé à
  `ReverseEarn`, un `Adjust`…) => `transaction_type_mismatch` ; `IsAlreadyReversed` => `already_reversed`
  (idempotence par rejet). `PostedTransaction` n'est pas falsifiable : seul un vrai `LoyaltyTransaction` en produit
  un, donc points, type et compte sont ceux du ledger. Annulation totale uniquement. **Exception documentée à la règle de découvert** : `ReverseEarn` s'applique même si le solde devient
  négatif (points déjà dépensés) — comme l'expiration, ce n'est pas une action du client. Permis sur un compte
  suspendu. Une référence annulée ne peut pas être regagnée (`duplicate_transaction`).
- **Récompense = remise** : `Reward.DiscountValue` (`Money` > 0, obligatoire) ; `RedeemReward` lève
  `PointsRedeemed` **et** `RewardRedeemed` (id récompense, points, montant de remise, `DiscountReference`,
  référence de transaction). `DiscountReference` = `RewardId` en chaîne (36 caractères, <= 64 : format des
  références de remise d'Orders). Changer la valeur n'affecte que les échanges futurs. **Devise** : ni le compte
  ni la récompense n'ont de notion de devise (les points n'en ont pas, et `DiscountValue` porte simplement la
  devise dans laquelle elle a été saisie) ; rien n'est contrôlé ici. C'est l'Application qui compare la devise de
  `DiscountValue` à celle de la commande avant de poser la remise.
- **Client anonymisé et fermeture** : `Open` refuse un client anonymisé. `Close(raison)` (client anonymisé ou
  parti) est terminal, permis depuis `Active` et `Suspended` : le solde restant est soldé par une écriture
  `Closure` (débit d'un solde positif = points perdus, crédit d'un solde négatif = passé en perte), donc
  solde = somme du ledger = 0 ; compte vide => aucune écriture. Ensuite toute mutation est rejetée (`closed`),
  y compris une annulation tardive.
- **Même valeur = no-op silencieux** : `Reward.ChangeCost` et `Reward.ChangeDiscountValue` avec la valeur déjà
  portée ne font rien (ni event ni erreur).
- **ExpirePoints** : plafonné au solde, retourne les points réellement expirés ; refusé si le solde est <= 0.
- **RedeemReward** : la référence identifie l'échange (pas la récompense) ; `PointsRedeemed.RewardId` est renseigné.
- **Reward** : créée `Active` ; transitions sans effet rejetées ; un
  changement de coût n'affecte pas les écritures passées (le ledger porte les points postés).
- Pas de suppression de récompense (non demandée ; `Deactivate` suffit).
- **Ids vides refusés** (`common.id.empty`) : `LoyaltyAccount.Open` et `Reward.Create` (tenant),
  `CustomerReference` (tenant, client). Pas de `LocationId` dans ce module.
- **Horloge lue une fois par opération** : l'écriture du ledger, `ClosedAtUtc` / `OpenedAtUtc` et tous les events
  d'une même opération (`PointsRedeemed` + `RewardRedeemed`, écriture `Closure` + `LoyaltyAccountClosed`) portent
  le même instant.
- **Enums en entrée** : le seul est `LedgerDirection` (`Adjust`), validé par `Enum.IsDefined` =>
  `transaction.direction_invalid`.

## 4. Matrice invariant -> méthode -> test

| Invariant | Méthode | Test |
|---|---|---|
| Points jamais négatifs | `Points.Of` | `LoyaltyValueObjectsTests.Points_reject_a_negative_value` |
| Référence requise / bornée / exacte | `TransactionReference.Create` | `LoyaltyValueObjectsTests.Reference_is_required`, `Reference_length_is_bounded`, `Reference_is_trimmed_and_case_sensitive` |
| Découvert : None = jamais sous zéro ; UpTo borné, > 0 | `OverdraftPolicy.Allows`, `UpTo` | `LoyaltyValueObjectsTests.Overdraft_none_never_lets_the_balance_go_below_zero`, `Overdraft_up_to_allows_a_bounded_negative_balance`, `Overdraft_up_to_rejects_a_zero_allowance` |
| Un compte par (tenant, client) | `LoyaltyAccount.Open` | `LoyaltyAccountTests.Open_rejects_a_second_account_for_the_same_customer` |
| Client du même tenant | `LoyaltyAccount.Open` | `LoyaltyAccountTests.Open_rejects_a_customer_of_another_tenant` |
| Pas de compte pour un client anonymisé | `LoyaltyAccount.Open` | `LoyaltyAccountTests.Open_rejects_an_anonymized_customer` |
| Earn crédite ledger et solde | `LoyaltyAccount.Earn` | `LoyaltyAccountTests.Earn_adds_points_to_the_ledger_and_the_balance`, `Earn_accumulates_over_several_references` |
| Points strictement positifs par opération | `Earn`, `Redeem`, `Adjust`, `ExpirePoints` | `LoyaltyAccountTests.Earn_rejects_zero_points`, `Redeem_rejects_zero_points`, `Adjust_rejects_zero_points_and_an_unknown_direction`, `ExpirePoints_rejects_zero_points` |
| Pas de double écriture (référence + type) | `Earn`, `Redeem`, `RedeemReward` | `LoyaltyAccountTests.Earn_rejects_the_same_reference_twice`, `Redeem_rejects_the_same_reference_twice`, `RedeemReward_rejects_the_same_redemption_twice_and_a_suspended_account`, `Same_reference_can_be_used_once_per_transaction_type` |
| Garde de doublon indépendante des écritures en mémoire (booléen du port) | `Earn`, `Redeem`, `RedeemReward`, `Adjust`, `ExpirePoints` | `LoyaltyAccountTests.Duplicate_guard_follows_the_ledger_answer_not_the_entries_in_memory` |
| Redeem <= solde (policy par défaut) | `LoyaltyAccount.Redeem` | `LoyaltyAccountTests.Redeem_rejects_more_than_the_balance`, `Redeem_rejects_any_amount_on_an_empty_account`, `Redeem_accepts_the_whole_balance` |
| « Sauf policy » : découvert explicite et borné | `LoyaltyAccount.Redeem` | `LoyaltyAccountTests.Redeem_beyond_the_balance_is_allowed_by_an_explicit_overdraft_policy`, `Redeem_beyond_the_overdraft_allowance_is_rejected` |
| Compte suspendu : Earn/Redeem refusés | `Earn`, `Redeem`, `RedeemReward` | `LoyaltyAccountTests.Earn_rejects_a_suspended_account`, `Redeem_rejects_a_suspended_account` |
| Compte suspendu : Adjust/Expire permis | `Adjust`, `ExpirePoints` | `LoyaltyAccountTests.Adjust_is_allowed_on_a_suspended_account`, `ExpirePoints_is_allowed_on_a_suspended_account` |
| Récompense active, du même tenant, payable | `LoyaltyAccount.RedeemReward` | `LoyaltyAccountTests.RedeemReward_rejects_an_inactive_reward`, `RedeemReward_rejects_a_reward_of_another_tenant`, `RedeemReward_rejects_a_reward_the_balance_cannot_pay`, `RedeemReward_spends_the_reward_cost` |
| Échange de récompense => remise pour la commande | `LoyaltyAccount.RedeemReward` | `LoyaltyAccountTests.RedeemReward_raises_RewardRedeemed_with_the_discount_to_put_on_the_order`, `Rejected_RedeemReward_raises_no_RewardRedeemed`, `Plain_Redeem_raises_no_RewardRedeemed` |
| Annulation d'un Earn liée à l'origine, mêmes points | `LoyaltyAccount.ReverseEarn` | `LoyaltyAccountTests.ReverseEarn_takes_the_earned_points_back_and_links_the_original_entry` |
| Annulation d'un Earn hors règle de découvert | `LoyaltyAccount.ReverseEarn` | `LoyaltyAccountTests.ReverseEarn_may_drive_the_balance_negative_when_the_points_were_already_spent` |
| Annulation : écriture d'origine requise | `ReverseEarn`, `ReverseRedeem` | `LoyaltyAccountTests.ReverseEarn_rejects_an_unknown_reference`, `ReverseRedeem_rejects_an_unknown_reference_and_a_second_reversal`, `Reversal_rejects_a_missing_or_already_reversed_entry` |
| Annulation : écriture de ce compte et du bon type | `ReverseEarn`, `ReverseRedeem` | `LoyaltyAccountTests.Reversal_rejects_an_entry_of_another_account`, `Reversal_rejects_an_entry_of_the_wrong_type` |
| Annulation sans historique chargé (depuis l'écriture stockée) | `ReverseEarn`, `LoyaltyTransaction.ToPostedReference` | `LoyaltyAccountTests.Reversal_works_on_a_freshly_loaded_account_from_the_posted_entry`, `Posted_reference_describes_the_stored_entry` |
| Une seule annulation par écriture | `ReverseEarn`, `ReverseRedeem` | `LoyaltyAccountTests.ReverseEarn_rejects_a_second_reversal_of_the_same_entry`, `ReverseRedeem_rejects_an_unknown_reference_and_a_second_reversal`, `Earn_and_redeem_sharing_a_reference_are_reversed_independently` |
| Annulation d'un Redeem recrédite | `LoyaltyAccount.ReverseRedeem` | `LoyaltyAccountTests.ReverseRedeem_credits_the_redeemed_points_back` |
| Annulation : raison obligatoire et bornée | `ReverseEarn`, `ReverseRedeem` | `LoyaltyAccountTests.Reversals_require_a_reason` |
| Annulation permise sur compte suspendu | `ReverseEarn`, `ReverseRedeem` | `LoyaltyAccountTests.Reversals_are_allowed_on_a_suspended_account` |
| Référence annulée non regagnable | `LoyaltyAccount.Earn` | `LoyaltyAccountTests.A_reversed_reference_cannot_be_earned_again` |
| Clé d'idempotence optionnelle (Adjust, Expire) | `Adjust`, `ExpirePoints` | `LoyaltyAccountTests.Adjust_with_a_reference_is_posted_once`, `ExpirePoints_with_a_reference_is_posted_once` |
| Fermeture : solde soldé par le ledger (positif ou négatif) | `LoyaltyAccount.Close` | `LoyaltyAccountTests.Close_forfeits_the_remaining_balance_through_the_ledger`, `Close_writes_off_a_negative_balance`, `Close_of_an_empty_account_writes_no_ledger_entry`, `Close_is_allowed_on_a_suspended_account` |
| Fermeture : raison obligatoire et bornée | `LoyaltyAccount.Close` | `LoyaltyAccountTests.Close_requires_a_reason` |
| Closed terminal | toutes | `LoyaltyAccountTests.Closed_is_terminal` |
| Ajustement : sens explicite, raison obligatoire | `LoyaltyAccount.Adjust` | `LoyaltyAccountTests.Adjust_credit_adds_points_with_a_reason`, `Adjust_debit_removes_points`, `Adjust_requires_a_reason`, `Adjust_rejects_a_too_long_reason` |
| Débit d'ajustement <= solde sauf policy | `LoyaltyAccount.Adjust` | `LoyaltyAccountTests.Adjust_debit_cannot_exceed_the_balance_without_overdraft_policy` |
| Expiration plafonnée au solde | `LoyaltyAccount.ExpirePoints` | `LoyaltyAccountTests.ExpirePoints_is_capped_at_the_balance`, `ExpirePoints_removes_the_requested_points`, `ExpirePoints_rejects_an_empty_or_overdrawn_account` |
| Transitions de statut explicites | `Suspend`, `Reactivate` | `LoyaltyAccountTests.Suspend_and_reactivate_toggle_the_status`, `Suspend_rejects_an_already_suspended_account`, `Reactivate_rejects_an_active_account` |
| Solde après = solde avant + somme des écritures ajoutées (longues séquences aléatoires, rechargements simulés) ; solde = somme du ledger stocké | toutes | `LoyaltyAccountTests.Balance_moves_by_exactly_the_appended_entries_over_a_long_random_sequence`, `Balance_moves_by_the_appended_entries_with_reversals_idempotence_keys_and_closure` |
| Ids non vides (tenant, client) | `LoyaltyAccount.Open`, `CustomerReference`, `Reward.Create` | `LoyaltyAccountTests.Open_rejects_an_empty_tenant_and_an_empty_customer`, `RewardTests.Create_rejects_an_empty_tenant` |
| Horloge lue une fois par opération | toutes | `LoyaltyAccountTests.Operations_read_the_clock_once` |
| Ledger append-only, non modifiable de l'extérieur | `Transactions` | `LoyaltyAccountTests.Ledger_is_append_only_and_read_only_from_outside` |
| Écriture immuable, portant son tenant et son compte | `LoyaltyTransaction` | `LoyaltyTransactionTests.Transaction_properties_have_no_setter`, `Transaction_fields_are_all_read_only`, `Transaction_exposes_no_mutator_method`, `Transaction_inherited_id_cannot_be_set_from_outside`, `Posted_transaction_keeps_its_values_whatever_happens_next` |
| Écriture créée seulement par l'agrégat | `LoyaltyTransaction.Create` (internal) | `LoyaltyTransactionTests.Transaction_cannot_be_created_outside_its_aggregate` |
| Coût d'une récompense > 0 | `Reward.Create`, `Reward.ChangeCost` | `RewardTests.Create_rejects_a_zero_cost`, `Create_accepts_the_smallest_cost`, `ChangeCost_rejects_a_zero_cost` |
| Même coût = no-op silencieux | `Reward.ChangeCost` | `RewardTests.ChangeCost_to_the_same_cost_is_a_silent_no_op` |
| Valeur de remise > 0 | `Reward.Create`, `Reward.ChangeDiscountValue` | `RewardTests.Create_rejects_a_non_positive_discount_value`, `ChangeDiscountValue_rejects_a_non_positive_value`, `ChangeDiscountValue_updates_the_value`, `ChangeDiscountValue_is_allowed_while_inactive` |
| Même valeur de remise = no-op silencieux | `Reward.ChangeDiscountValue` | `RewardTests.ChangeDiscountValue_to_the_same_value_is_a_silent_no_op` |
| Référence de remise <= 64 caractères (format Orders) | `RewardReference.DiscountReference` | `RewardTests.Reference_exposes_an_order_discount_reference_within_64_characters` |
| Nom requis / borné | `Reward.Create` | `RewardTests.Create_rejects_a_missing_name`, `Create_rejects_a_too_long_name` |
| Transitions sans effet rejetées | `Reward.Activate`, `Reward.Deactivate` | `RewardTests.Activate_rejects_an_already_active_reward`, `Deactivate_rejects_an_already_inactive_reward`, `Deactivate_then_activate_toggle_the_status` |
| Référence de récompense fidèle (coût, valeur, disponibilité) | `Reward.ToReference` | `RewardTests.ToReference_reflects_cost_and_availability`, `Reward_reference_can_be_redeemed_by_an_account_of_the_same_tenant` |
| `RewardReference` / `PostedTransaction` non falsifiables | constructeurs `internal` | `RewardTests.Reward_reference_cannot_be_built_outside_the_module`, `LoyaltyAccountTests.Posted_reference_describes_the_stored_entry` |

## 5. Ce qui relève de l'Application / Infrastructure

- **Unicité du compte** : l'Application appelle `ILoyaltyAccountUniquenessChecker` et passe le booléen à `Open` ;
  l'index unique `(TenantId, CustomerId)` est la garde finale.
- **Persistance du ledger** : table propre, en insertion seule (pas d'UPDATE/DELETE), une ligne par
  `LoyaltyTransaction` avec `TenantId` et `AccountId`. À l'enregistrement d'un compte, l'Infrastructure **insère**
  `Transactions` (les écritures ajoutées depuis le chargement) et met à jour `Balance` dans la même transaction.
  Au chargement elle **ne réhydrate pas** la collection (pas de `Include` du ledger). `Transactions` n'est donc pas
  une vue de l'historique : pour l'afficher, lire la table (read model).
- **Port `ILoyaltyLedgerLookup`** (à implémenter sur cette table, filtré par tenant et compte) :
  - `IsReferencePostedAsync(tenant, compte, type, référence)` => `referenceAlreadyPosted`. `Earn` interroge le
    type `Earn`, `Redeem` et `RedeemReward` le type `Redeem`, `Adjust` le type `Adjust`, `ExpirePoints` le type
    `Expire`.
  - `FindPostedAsync(tenant, compte, type, référence)` => charger la ligne `Earn` / `Redeem` et renvoyer
    `ligne.ToPostedReference(isAlreadyReversed)` où `isAlreadyReversed` = « il existe une ligne dont
    `ReversedTransactionId` est l'id de celle-ci » ; `null` si aucune ligne. Le résultat se passe tel quel à
    `ReverseEarn` / `ReverseRedeem`.
- **Index uniques que la base DOIT porter** (gardes finales, le domaine ne voyant que la réponse du port) :
  1. `(AccountId, Type, Reference)` **filtré** : `Reference IS NOT NULL` et `Type` ni `Reversal` ni `Closure`
     (une annulation porte la référence de l'écriture annulée ; une clôture n'a pas de référence). C'est lui qui
     arrête deux postages concurrents — ou deux postages de la même référence dans la même unité de travail, que
     l'agrégat ne détecte plus puisqu'il ne relit pas ses écritures.
  2. `(AccountId, ReversedTransactionId)` **filtré** `ReversedTransactionId IS NOT NULL` : une seule annulation
     par écriture (un `Earn` et un `Redeem` de même référence donnent deux `Reversal` portant cette référence, d'où
     l'index sur l'écriture annulée et non sur la référence).
  Une violation de ces index se traduit côté Application par `duplicate_transaction` / `already_reversed`.
- **Concurrence** : deux échanges simultanés sur le même solde sont départagés par le verrouillage optimiste du
  compte (l'agrégat est l'unité de cohérence du solde) ; le second est rejoué sur l'état à jour — port relu — et
  rejeté par le domaine.
- **Barèmes** (combien de points pour une commande), **règles d'expiration** (quels points, quand) et choix de
  l'`OverdraftPolicy` : décidés par l'Application, le domaine reçoit le résultat.
- `CustomerReference` (avec `IsAnonymized` lu dans Customers) est construit par l'Application après chargement
  sous isolation tenant ; `RewardReference` s'obtient uniquement par `Reward.ToReference()`.
- **Devise de la remise de récompense** : l'Application vérifie que `RewardRedeemed.DiscountValue` (ou
  `RewardReference.DiscountValue`, avant l'échange) est dans la devise de la commande, et renonce à l'échange
  sinon ; le domaine Loyalty ne contrôle rien d'autre.
- **Remise de récompense sur la commande** : sur `RewardRedeemed`, l'Application ajoute à la commande une remise de
  montant `DiscountValue` et de référence `DiscountReference` (id de récompense, 36 caractères) ; la
  `TransactionReference` de l'échange est l'`OrderId` en chaîne pour pouvoir l'annuler.
- **Annulation / remboursement d'une commande** (events Orders/Payments) : `FindPostedAsync(..., Earn, orderId)`
  puis `ReverseEarn(résultat, raison)` et, si une récompense avait été échangée, la même chose avec `Redeem` /
  `ReverseRedeem` ; `transaction_not_found` (rien à reprendre) et `already_reversed` (rejeu) sont à traiter comme
  des succès idempotents, `closed` aussi.
- **Anonymisation / départ d'un client** (event Customers) : appeler `LoyaltyAccount.Close`.
- **Clés d'idempotence** de `Adjust` et `ExpirePoints` : fournies par l'Application (id de commande d'ajustement,
  id du lot d'expiration) quand l'appel peut être rejoué.
- **Contrôle périodique** : `Balance` = somme des `SignedPoints` de la table ledger pour le compte ; un job peut
  le vérifier (le domaine ne recalcule jamais le solde depuis la collection).

## 6. Persistance (étape 1)

Schéma `loyalty`, historique des migrations dans le même schéma. Toutes les tables portent un `tenant_id` obligatoire
et sont filtrées sur le tenant courant, **registre compris** (filtre explicite : une écriture n'est pas une racine).

| Table | Contenu | Gardes en base |
|---|---|---|
| `loyalty_accounts` | Agrégat `LoyaltyAccount`, solde stocké (`bigint` signé) | Index unique `(tenant_id, customer_id)` ; CHECK statut, `closed_at_utc` présent si et seulement si `Closed`, compte clos à zéro ; version `xmin` |
| `loyalty_transactions` | Le registre, en insertion seule | Index unique `(account_id, type, reference)` filtré `reference IS NOT NULL AND type NOT IN (Reversal, Closure)` ; index unique `(reversed_transaction_id)` filtré ; CHECK type, sens, points > 0, lien de contre-passation présent si et seulement si `Reversal`, clôture sans référence ; FK vers le compte et vers l'écriture annulée (Restrict) |
| `rewards` | Agrégat `Reward` | CHECK statut, coût > 0, valeur de remise > 0, devise ; version `xmin` |
| `outbox_messages` | Événements à publier | — |

Choix :

- **Le registre n'est jamais réhydraté** : la navigation `Transactions` n'est jamais chargée (`Include` interdit) ;
  les lectures du registre (`LoyaltyLedgerLookup`, historique) sont faites en `AsNoTracking`, pour qu'aucune écriture
  stockée ne vienne se greffer à la collection d'un compte chargé.
- **Insertion seule** : l'unit of work refuse avant d'écrire toute écriture modifiée ou supprimée
  (`InvalidOperationException`). Une protection en base (trigger refusant UPDATE et DELETE) pourra s'ajouter à la main
  dans la migration si besoin.
- **Index d'annulation sur la seule écriture annulée** (et non `(account_id, reversed_transaction_id)` comme prévu au
  §5) : les ids d'écriture sont uniques, la garde est la même, et l'index sert aussi à la clé étrangère.
- **`LoyaltyLedgerLookup`** implémente `ILoyaltyLedgerLookup` et `ILoyaltyAccountUniquenessChecker`. Il compte aussi
  les écritures ajoutées et pas encore écrites : deux postages de la même référence dans une même requête sont refusés
  par le domaine, pas seulement par l'index.
- **Erreurs de la base traduites** : second compte -> `account.already_exists` ; référence déjà postée ->
  `account.duplicate_transaction` ; seconde annulation -> `account.already_reversed` ; version périmée ->
  `loyalty.concurrent_update`.

Tests : `LoyaltyModelTests` (sans base), `LoyaltyPersistenceTests` (PostgreSQL via Testcontainers).

## 7. Application, intégration et API (étapes 2 à 6)

### Barème (`LoyaltyProgram`, domaine)

Un par restaurant (table `loyalty_programs`), **désactivé par défaut**. `PointsPerCurrencyUnit` (> 0, <= 1000,
4 décimales) : points = total de la commande × barème, **arrondi à l'entier inférieur**. `AutoEnroll` : la première
commande terminée d'un client sans compte lui en ouvre un. `InactivityExpiryDays` (30 à 3650, ou jamais) : le solde
expire après ce délai sans gain ni échange.

### Cas d'usage

| Cas d'usage | Déclencheur | Règles appliquées ici |
|---|---|---|
| `OpenLoyaltyAccount` | Personnel (`loyalty.operate`) | Client lu dans Customers (tenant du contexte) ; unicité par le port puis l'index |
| `RedeemRewardForOrder` | Personnel (`loyalty.operate`) | Commande du même client, en brouillon, dans la devise de la récompense ; référence = id de la commande (une récompense par commande) |
| `AdjustLoyaltyPoints`, `Suspend`, `Reactivate`, `Close` | Personnel (`loyalty.manage`) | Référence facultative de l'ajustement = clé de rejeu |
| `CreateReward`, `ChangeRewardCost`, `ChangeRewardDiscountValue`, `Activate/DeactivateReward`, `SetLoyaltyProgram` | Personnel (`loyalty.configure`) | — |
| `EarnPointsForOrder` | `orders.OrderCompleted` | Ignoré sans client, programme désactivé, 0 point, tenant non modifiable, compte suspendu ou clos ; rejeu = `AlreadyDone` |
| `ReverseOrderLoyalty` | `orders.OrderCancelled` | Reprend le gain ET rend la récompense de la commande ; déjà annulé = `AlreadyDone` ; compte clos ignoré |
| `CloseCustomerLoyaltyAccount` | `customers.CustomerAnonymized` | Clôture (solde perdu au registre) |
| `CancelRewardRedemption` | Compensation de `loyalty.RewardRedeemed` | Points rendus quand la commande refuse la remise |
| `ExpireInactivePoints` | Job `LoyaltyExpiryWorker` (étape 5) | Solde entier d'un compte inactif ; référence `expiry:{instant de la dernière activité}` : une expiration par période d'inactivité |

Toutes les réactions renvoient `Ignored`, `AlreadyDone` ou `Done` et sont **idempotentes** ; un refus métier est
journalisé et ignoré par l'hôte pour ne jamais bloquer l'outbox d'un autre module.

### Échange d'une récompense : saga par l'outbox

1. `RedeemRewardForOrder` débite les points et lève `RewardRedeemed` (remise, référence = id de la commande).
2. L'hôte (`LoyaltyFlow`) pose la remise sur la commande : `Orders.ApplyExternalDiscountCommand` (source
   `LoyaltyReward`, référence = id de la récompense). Déjà posée (rejeu) = rien à faire.
3. Si la commande la refuse (plus en brouillon, devise...), l'hôte rend les points (`CancelRewardRedemption`).
4. Une commande annulée ensuite rend aussi les points (`ReverseOrderLoyalty`).

Dernière activité d'un compte (expiration) : dernier `Earn` ou `Redeem`, à défaut l'ouverture du compte. Les
ajustements et contre-passations ne prolongent pas les points (ce ne sont pas des actions du client).

### Routes (`/loyalty`, tenant du jeton, `Tenancy.TenantMemberPolicy`)

| Route | Permission |
|---|---|
| `GET /program`, `GET /rewards[?activeOnly]`, `GET /rewards/{id}`, `GET /accounts[?status,skip,take]`, `GET /accounts/{id}`, `GET /accounts/by-customer/{customerId}`, `GET /accounts/{id}/transactions` | `loyalty.view` |
| `POST /accounts`, `POST /accounts/{id}/rewards/{rewardId}/redeem` | `loyalty.operate` |
| `POST /accounts/{id}/adjustments`, `/suspend`, `/reactivate`, `/close` | `loyalty.manage` |
| `PUT /program`, `POST /rewards`, `PUT /rewards/{id}/cost`, `PUT /rewards/{id}/discount-value`, `POST /rewards/{id}/activate`, `/deactivate` | `loyalty.configure` |

Gagner, reprendre et expirer des points n'ont pas de route : ce sont des réactions et un job. Migration appliquée au
démarrage de l'hôte (`MigrateLoyaltyDatabaseAsync`) ; job coupable par `Loyalty:RunWorkers=false`.

## 8. Tests (étape 7)

| Projet | Contenu |
|---|---|
| `Loyalty.Domain.Tests` | + `LoyaltyProgramTests` (barème, arrondi, bornes, expiration) |
| `Loyalty.Application.Tests` | `LoyaltyUseCaseTests` : ouverture, gain idempotent, inscription automatique, reprise (solde négatif compris), échange (client, brouillon, devise, une fois par commande), compensation, ajustement, clôture à l'anonymisation, expiration par période, catalogue, barème, multi-tenant, tenant suspendu, pagination |
| `Loyalty.Infrastructure.Tests` | Modèle, registre, + `LoyaltyRepositoriesTests` (repositories, historique, barème, candidats à l'expiration, répertoire du job) |
| `Loyalty.Api.Tests` | Authentification, permissions par action, barème, compte et historique, récompense sur commande, isolation entre restaurants, tenant suspendu, cycle de vie du compte |

## 9. Audit (étape 8)

| Point | Application | Vérification |
|---|---|---|
| TenantId toujours issu du contexte | Aucune route ni commande ne reçoit de tenant ; `RequireTenant()` | `LoyaltyEndpointsTests`, `LoyaltyUseCaseTests` |
| Lecture croisée impossible | Filtres EF sur comptes, récompenses, barème ET registre ; 404 sur l'id d'un autre tenant | `LoyaltyPersistenceTests`, `LoyaltyEndpointsTests` |
| Commande et client d'un autre tenant | Ports de l'hôte : réponse null hors du tenant du contexte | `LoyaltyUseCaseTests` |
| Registre fiable | Insertion seule (unit of work) ; solde = somme du registre ; index uniques d'idempotence et d'annulation | `LoyaltyPersistenceTests` |
| Concurrence | Version `xmin` sur compte, récompense, barème | `LoyaltyPersistenceTests` |
| Pas de points non mérités | Arrondi inférieur ; gain une fois par commande ; reprise à l'annulation | `LoyaltyProgramTests`, `LoyaltyUseCaseTests` |
| Données personnelles | Aucune : le module ne stocke que l'id client ; clôture à l'anonymisation | — |
| Journal | Ids, types et codes d'erreur seulement | Revue (`LoyaltyFlow`, `LoyaltyExpiryWorker`) |
| Job multi-tenant | Le répertoire ne renvoie que des ids de tenant ; chaque tenant est traité dans son scope (`BackgroundTenant`) | `LoyaltyRepositoriesTests` |

Limites connues : pas de remboursement partiel (Payments) relié aux points ; pas de trigger SQL interdisant UPDATE et
DELETE sur le registre (la garde est dans l'unit of work) ; expiration du solde entier (pas de lots FIFO).

## Cartes cadeaux et avoirs (P2)

- **StoredValueCard** (racine) : carte cadeau ou avoir d'un restaurant.
  - Le code (16 caractères, 80 bits, sans 0, 1, I ni O) n'est rendu qu'à l'émission. Seuls son empreinte SHA-256 (recherche, unique par restaurant) et ses 4 derniers caractères sont gardés.
  - Le code ne figure dans aucun événement, journal ni réponse ensuite.
- **Solde** : projection des mouvements (émission, utilisation, crédit, annulation d'un mouvement, annulation de la carte), gardés avec la carte (500 au plus).
  - Le solde reste entre 0 et le montant initial.
  - Utiliser, créditer et annuler un mouvement sont idempotents par référence. Une même référence pour un autre montant est refusée.
- **États** : une carte expirée ne se débite plus mais peut être recréditée (remboursement). Une carte annulée perd son solde et ne sert plus.
- **Encaissement** (Payments, par l'hôte) :
  - débit par code avant l'enregistrement du paiement, annulé si l'enregistrement échoue ;
  - remboursement recrédité sur la carte ;
  - remboursement d'un autre moyen en avoir.
- **Routes** `/loyalty/stored-value-cards` :
  - liste et détail (`loyalty.view`) ;
  - recherche par code en `POST /lookup`, le code dans le corps, jamais dans l'adresse (`loyalty.view`) ;
  - émission et annulation (`loyalty.manage`).
