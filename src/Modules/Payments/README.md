# Module Payments

Vérité métier d'un paiement, indépendante du prestataire. Le Domain n'appelle jamais le prestataire : il enregistre des
**résultats déjà validés** (signature du webhook vérifiée en Infrastructure) et garantit les montants.

## 1. Frontière d'agrégat

- **`Payment` (racine)** possède ses `PaymentAttempt` (20 max) et ses `Refund` (100 max). Les invariants de montant
  (`capturé <= demandé`, `remboursé + en attente <= capturé`) portent sur l'ensemble : ils doivent être dans une seule
  unité de cohérence.
- **`PaymentAttempt`, `Refund`** : entités enfants, factories et mutateurs `internal`. Seule la racine lève des events.
- **Par id uniquement** : `OrderId`. La commande n'est pas chargée : `Payment.Create` reçoit un VO local `OrderReference`
  (tenant de la commande, `OrderId`, reste à payer) ; `Amount` est le montant demandé par ce paiement.
- **Plusieurs paiements par commande** (addition partagée) : chaque `Payment` est son propre agrégat.
- **Aucun type SDK** : le prestataire est un `PaymentProviderCode`, une tentative est désignée par sa `ProviderReference`.

## 2. Classes

| Classe | Rôle DDD | Responsabilité | Events | Erreurs (`payments.*`) |
|---|---|---|---|---|
| `Payment` | AggregateRoot, tenant-owned | Montants, statut, tentatives, remboursements, idempotence des résultats | `PaymentCreated`, `PaymentAttemptRegistered`, `PaymentAuthorized`, `PaymentCaptured`, `PaymentFailed`, `PaymentCancelled`, `PaymentCapturedAfterCancellation`, `RefundRequested`, `RefundRegistered`, `RefundSucceeded` | `payment.amount_not_positive`, `payment.amount_exceeds_amount_due`, `payment.late_capture_not_allowed`, `common.tenant.mismatch`, `common.id.empty`, `payment.too_many_attempts`, `payment.too_many_refunds`, `payment.currency_mismatch`, `payment.attempt_not_allowed`, `payment.attempt_in_progress`, `payment.attempt_reference_duplicate`, `payment.attempt_not_found`, `payment.capture_amount_not_positive`, `payment.capture_exceeds_amount`, `payment.capture_conflict`, `payment.failure_code_invalid`, `payment.failure_reason_invalid`, `payment.cancel_not_allowed`, `payment.already_cancelled`, `payment.cancellation_reason_invalid`, `payment.refund_not_allowed`, `payment.refund_exceeds_refundable`, `payment.refund_not_found` |
| `PaymentAttempt` | Entity enfant | Un essai chez le prestataire ; terminal irréversible ; échec = `FailureCode` (`PaymentFailureCode`, code technique normalisé : `card_declined`, `payment_cancelled`, `provider_declined`...) + `FailureReason` facultatif borné (message du prestataire, jamais de donnée bancaire) ; `CapturedAfterCancellationAtUtc` marque la capture tardive sans rouvrir la tentative | (racine) | `payment_attempt.already_succeeded`, `payment_attempt.already_failed` |
| `OrderReference` | ValueObject (référence locale à un agrégat d'un autre module : constructible par l'Application) | Tenant de la commande, `OrderId`, `LocationId` de son établissement (ids non vides), reste à payer (`AmountDue` > 0) | — | `order_reference.amount_due_not_positive`, `common.id.empty` |
| `Refund` | Entity enfant | Remboursement d'une partie capturée ; terminal irréversible ; `ProviderReference` facultative (unique dans le paiement), `CompletedAtUtc` à l'issue ; une autre issue ou une autre référence pour un remboursement déjà réglé est refusée | (racine) | `refund.amount_not_positive`, `refund.reason_invalid`, `refund.outcome_unknown`, `refund.outcome_conflict`, `refund.reference_duplicate` |
| `PaymentProviderCode` | ValueObject | Code technique du prestataire (2..32, `a-z0-9_-`, commence par une lettre, minuscules) ; `PaymentProviderCode.Cash` = `cash` | — | `provider_code.invalid` |
| `ProviderReference` | ValueObject | Identifiant de la tentative chez le prestataire (1..255, sans espace, sensible à la casse) | — | `provider_reference.invalid` |
| `PaymentId`, `PaymentAttemptId`, `RefundId` | ids typés locaux | — | — | `common.id.empty` |

Montants de `Payment` (une seule devise, celle de `Amount`) :

| Propriété | Sens |
|---|---|
| `Amount` | montant demandé, fixé à la création, `0 < Amount <= OrderReference.AmountDue` |
| `CapturedAmount` | montant réellement encaissé ; `0 < capturé <= Amount`, **seule exception** : une capture tardive (`HasUnexpectedCapture`) est enregistrée pour ce que le prestataire a réellement pris, même au-delà de `Amount` |
| `RefundedAmount` | somme des remboursements `Succeeded` (dérivée) |
| `PendingRefundAmount` | somme des remboursements `Requested` (dérivée) |
| `RefundableAmount` | `CapturedAmount − RefundedAmount − PendingRefundAmount` : ce qu'une nouvelle demande peut encore viser |
| `NetPaidAmount` | `CapturedAmount − RefundedAmount` : ce que le paiement détient encore — **vue comptable**. Vaut 0 si `HasUnexpectedCapture`. Ne pas l'utiliser pour solder une commande |
| `SettledAmount` | `CapturedAmount − RefundedAmount − PendingRefundAmount` : **terme par paiement du règlement de la commande** (le règlement d'une commande = Σ `SettledAmount` de ses paiements, fourni à `Order.Complete`). Un remboursement en cours est déjà déduit ; s'il échoue, son montant compte de nouveau. Vaut 0 si `HasUnexpectedCapture` |
| `HasUnexpectedCapture` | vrai si le montant capturé l'a été après annulation (argent à rendre, ne solde rien) |

Transitions :

```
Payment        Pending -> Authorized -> Captured          (Authorize puis Capture)
               Pending -> Captured                        (capture directe, sans autorisation préalable)
               Pending | Authorized -> Failed             (Fail)
               Failed -> Pending                          (RegisterAttempt : nouvelle tentative)
               (création) -> Captured                     (RecordOffline : espèces / terminal externe, capturé d'emblée)
               Pending | Authorized | Failed -> Cancelled (Cancel, uniquement avant capture)
               Cancelled -> Captured + HasUnexpectedCapture (RegisterLateCapture uniquement ; Capture reste rejeté)
               Captured | PartiallyRefunded -> PartiallyRefunded | Refunded   (RegisterRefund réussi)
               Cancelled : terminal sauf capture tardive enregistrée ; Refunded : terminal
PaymentAttempt Pending -> Succeeded | Failed   (l'autorisation laisse la tentative Pending : AuthorizedAtUtc renseigné)
Refund         Requested -> Succeeded | Failed
```

Events de remboursement — quel fait nomme chacun :
- `RefundRequested` : un remboursement est demandé et son montant réservé ; aucun argent n'a bougé.
- `RefundRegistered` : la réponse finale du prestataire est enregistrée sur le paiement, **succès ou échec** (`Outcome`,
  `PaymentStatus` résultant).
- `RefundSucceeded` : l'argent est effectivement rendu (succès uniquement, levé en plus de `RefundRegistered`), avec le
  cumul remboursé et `IsFullRefund`.

## 3. Idempotence (webhook dupliqué) et contradictions

Un résultat prestataire vise toujours une tentative (par `ProviderReference`) ou un remboursement (par `RefundId`).

| Résultat reçu | État de la cible | Effet |
|---|---|---|
| `Capture(ref, m)` | tentative `Succeeded`, même montant | no-op : ni changement, ni event, ni exception |
| `Capture(ref, m')` | tentative `Succeeded`, montant différent | `payment.capture_conflict` |
| `Capture` / `Authorize` | tentative `Failed` | `payment_attempt.already_failed` |
| `Authorize(ref)` | déjà autorisée, ou déjà capturée (webhook en retard) | no-op |
| `Fail(ref)` | tentative `Failed` | no-op (la première raison est conservée) ; **le rejeu est testé avant la raison** : rejouer avec une raison vide ou trop longue reste un no-op |
| `Fail(ref)` | tentative `Succeeded` | `payment_attempt.already_succeeded` |
| `RegisterRefund(id, x)` | remboursement déjà dans l'état `x` | no-op |
| `RegisterRefund(id, x)` | remboursement dans l'autre état final | `refund.outcome_conflict` |
| `RegisterLateCapture(ref, m)` | capture tardive déjà enregistrée sur cette tentative, même montant (même après remboursement) | no-op |
| `RegisterLateCapture(ref, m')` | capture tardive déjà enregistrée, autre montant ou autre tentative | `payment.capture_conflict` |
| `RegisterLateCapture` | paiement non `Cancelled` (sans capture tardive) | `payment.late_capture_not_allowed` |
| `Capture(ref, m)` | paiement `Cancelled`, y compris après capture tardive enregistrée | `payment_attempt.already_failed` |
| référence / id inconnu | — | `payment.attempt_not_found` / `payment.refund_not_found` |

La racine absorbe la répétition exacte ; les entités enfants, elles, sont strictes (`PaymentAttempt.Succeed` deux fois =>
erreur). Les tests accèdent aux enfants via `InternalsVisibleTo` (déclaré dans `AssemblyInfo.cs`) pour prouver cette garde.

## 4. Choix faits là où la spec était ouverte

- **Ids vides** (`default`) : `OrderReference.Create` refuse un tenant, un `OrderId` ou un `LocationId` vide
  (`common.id.empty`) ; le tenant d'un paiement est celui de sa configuration, jamais vide.
- **Collections bornées** : `Payment.MaxAttempts` = 20 tentatives (`payment.too_many_attempts` ; issue : `Cancel` puis
  un nouveau `Payment`) et `Payment.MaxRefunds` = 100 remboursements, échoués compris car ils restent en historique
  (`payment.too_many_refunds`). Au-delà de 100 remboursements, le reliquat se traite hors système (cas non attendu : un
  paiement est une part d'addition).
- **Une heure par opération** : chaque méthode lit l'horloge une fois ; `RecordOffline` crée, ouvre la tentative et
  capture au même instant (trois events, une seule heure).
- **Une seule tentative ouverte à la fois** (`attempt_in_progress`) : deux tentatives ne peuvent donc jamais réussir toutes
  les deux, d'où `capturé <= demandé` par construction. Une tentative sans réponse doit être clôturée par `Fail` (timeout
  décidé par l'Application) avant d'en ouvrir une autre.
- **"Failed ne devient pas Captured sans tentative valide"** : la tentative échouée est terminale. Seul `RegisterAttempt`
  (nouvelle `ProviderReference`, unique dans le paiement) remet le paiement en `Pending`, puis `Authorize`/`Capture` sur
  cette nouvelle tentative.
- **Capture directe depuis `Pending`** autorisée (prestataires sans phase d'autorisation, espèces, mobile money).
- **Capture partielle** (`capturé < demandé`) autorisée, statut `Captured` ; une seule capture par paiement.
  `Refunded` = remboursé == capturé (et non == demandé).
- **Référence de commande et configuration** (étape 3) : `Payment.Create(configuration, method, OrderReference, amount,
  clock)`. Le tenant et le prestataire sont ceux de la `RestaurantPaymentConfiguration` ; la référence porte le tenant
  et l'établissement de la commande : un écart de tenant lève `common.tenant.mismatch`. La configuration doit ensuite
  accepter le paiement (`EnsureCanTake`) : activée (`payments.configuration.disabled`), prête
  (`payments.configuration.credentials_missing`), moyen retenu (`payments.configuration.method_not_accepted`),
  établissement affecté s'il y a des affectations (`payments.configuration.not_assigned_to_location`), montant dans ses
  bornes (`payments.configuration.amount_out_of_bounds`). `Create` exige un prestataire en ligne
  (`payments.payment.online_needs_online_provider`), `RecordOffline` un prestataire sur place
  (`payments.payment.offline_needs_on_site_provider`). Le paiement garde `LocationId` et `Method`
  (`PaymentMethodType`), et `AuthorizedAtUtc` / `FailedAtUtc` de la tentative en cours (remis à null par un nouvel
  essai). La devise du
  montant doit être celle de `AmountDue` (`payment.currency_mismatch`), et `0 < amount <= AmountDue`
  (`payment.amount_not_positive`, `payment.amount_exceeds_amount_due`). `AmountDue` est le **reste à payer** calculé par
  l'Application (total de la commande moins ce que ses autres paiements couvrent déjà). Le Domain borne chaque paiement ;
  **la somme des paiements d'une commande (addition partagée) est une affaire d'Application/base** (voir §6).
- **Espèces / paiement manuel** (décision produit) : `Payment.RecordOffline(configuration, method, order, amount,
  operatorReference, clock)` crée le paiement et le capture aussitôt pour tout son montant, via **une tentative réussie**
  portant la référence saisie par l'opérateur (n° de ticket/caisse, obligatoire : c'est une `ProviderReference`). C'est
  exactement `Create` + `RegisterAttempt` + `Capture` : mêmes règles, mêmes events (`PaymentCreated`,
  `PaymentAttemptRegistered`, `PaymentCaptured`), remboursable ensuite comme tout paiement capturé (pour des espèces,
  l'Application enregistre `RegisterRefund(Succeeded)` dès que l'argent est rendu). Le code prestataire est libre (`cash`
  via `PaymentProviderCode.Cash`, `external_terminal`...) ; aucun drapeau « hors ligne » n'est stocké, le code suffit.
- **`Cancel`** : depuis `Pending`, `Authorized` ou `Failed`, raison obligatoire. La tentative ouverte est clôturée en échec ;
  `PaymentCancelled` porte son id et sa `ProviderReference` (`null` s'il n'y avait pas de tentative ouverte) pour que
  l'Application annule l'autorisation chez le prestataire. Un succès tardif du prestataire reste rejeté par `Capture`
  (`already_failed`) au lieu de ressusciter le paiement.
- **Capture reçue après annulation** : l'argent est pris chez le prestataire, il doit avoir une trace. L'Application appelle
  `RegisterLateCapture(providerReference, capturedAmount, clock)` : le paiement passe de `Cancelled` à `Captured` avec le
  **drapeau `HasUnexpectedCapture`** (choix : un drapeau plutôt qu'un statut dédié, car il doit survivre aux statuts
  `PartiallyRefunded`/`Refunded` du remboursement) ; `CancelledAtUtc`/`CancellationReason` sont conservés, la tentative reste
  `Failed` et reçoit `CapturedAfterCancellationAtUtc`. Event dédié `PaymentCapturedAfterCancellation` — **pas**
  `PaymentCaptured`, pour qu'aucun consommateur ne considère la commande comme payée. Le montant doit être > 0 et dans la
  devise du paiement, mais **il n'est pas plafonné à `Amount`** : c'est la seule exception documentée à
  `capturé <= demandé`. Refuser un montant supérieur ne laisserait aucune trace d'un argent réellement pris ; on
  enregistre donc ce que le prestataire a capturé, signalé par `HasUnexpectedCapture`, et **intégralement remboursable**
  par le flux normal `RequestRefund`/`RegisterRefund` (`RefundableAmount` part du montant réellement capturé).
  Idempotent au rejeu. **`NetPaidAmount` et `SettledAmount` restent à 0** pour un tel paiement : cet argent est destiné à
  être rendu, il ne doit pas permettre de clôturer une commande. `Capture` (flux normal) refuse toujours un montant
  supérieur à `Amount`.
- **Remboursement en cours et règlement** : un remboursement `Requested` n'est pas déduit de `NetPaidAmount` (l'argent
  est encore là) mais l'est de `SettledAmount` : une commande ne peut pas être clôturée sur de l'argent en train d'être
  rendu. `SettledAmount` vaut `RefundableAmount` hors capture tardive.
- **Réservation des remboursements en attente** : `RequestRefund` compare à `RefundableAmount`, pas au seul remboursé.
  Un remboursement échoué libère sa réservation.
- **`RegisterRefund(RefundId, RefundOutcome)`** : `RefundOutcome` (Succeeded/Failed) est un enum local.
- **Events ajoutés** : `PaymentAttemptRegistered`, `PaymentCancelled`, `PaymentCapturedAfterCancellation`, `RefundRequested`.
- **Montant > 0** : une commande à total nul (ou déjà soldée) ne donne pas lieu à un `Payment` ; `OrderReference` refuse un
  reste à payer <= 0.
- **Pas de montant autorisé distinct** : une autorisation porte sur `Amount`.

## 5. Matrice invariant -> méthode -> test

| Invariant | Méthode | Test |
|---|---|---|
| Ids vides refusés | `Payment.Create`, `Payment.RecordOffline`, `OrderReference.Create` | `PaymentHardeningTests.Create_rejects_an_order_of_another_restaurant_than_the_configuration`, `.Order_reference_rejects_an_empty_tenant_order_or_location` |
| Au plus 20 tentatives et 100 remboursements par paiement | `RegisterAttempt`, `RequestRefund` | `PaymentHardeningTests.Payment_accepts_the_maximum_number_of_attempts_and_rejects_one_more`, `.Payment_accepts_the_maximum_number_of_refunds_and_rejects_one_more` |
| Rejeu d'un échec absorbé avant validation de la raison | `Payment.Fail` | `PaymentHardeningTests.Failure_replayed_without_a_reason_is_absorbed_like_any_replay` (3 cas), `.Failure_without_a_reason_is_still_rejected_on_an_open_attempt_and_on_a_succeeded_one` |
| Horodatage d'état = heure de l'event ; un seul instant par opération | toutes | `PaymentHardeningTests.Offline_payment_is_created_attempted_and_captured_at_one_single_instant`, `.Timestamps_of_an_operation_equal_the_time_of_its_event`, `.Timestamps_of_a_cancellation_and_of_a_late_capture_equal_the_time_of_their_events` |
| Capture tardive au-delà du montant demandé : enregistrée, signalée, remboursable en entier, ne solde rien | `Payment.RegisterLateCapture` | `PaymentLateCaptureTests.RegisterLateCapture_records_what_the_provider_really_took_even_above_the_requested_amount`, `.Late_capture_above_the_requested_amount_is_fully_refundable_and_never_counts_as_paid`, `.Late_capture_above_the_requested_amount_replays_like_any_other`, `.Normal_capture_still_rejects_more_than_the_requested_amount` |
| `SettledAmount` = capturé − remboursé − en cours (0 si capture tardive) | `Payment.SettledAmount` | `PaymentSettledAmountTests.SettledAmount_equals_the_net_paid_amount_when_no_refund_is_in_flight` (7 statuts), `.SettledAmount_sets_aside_a_refund_as_soon_as_it_is_requested`, `.SettledAmount_keeps_a_succeeded_refund_deducted`, `.SettledAmount_counts_again_the_amount_of_a_refund_that_failed`, `.SettledAmount_deducts_succeeded_and_pending_refunds_together`, `.SettledAmount_is_zero_while_the_whole_capture_is_being_refunded`, `.SettledAmount_of_a_capture_received_after_cancellation_is_always_zero`, `.Split_bill_settlement_is_the_sum_of_the_settled_amounts_of_the_order_payments` |
| Montant du paiement > 0 | `Payment.Create` | `PaymentFlowTests.Create_rejects_a_zero_or_negative_amount` |
| Commande du même tenant | `Payment.Create` | `PaymentFlowTests.Create_rejects_an_order_of_another_tenant` |
| Devise du paiement = devise de la commande | `Payment.Create` | `PaymentFlowTests.Create_rejects_an_amount_in_another_currency_than_the_order` |
| Montant <= reste à payer ; plusieurs paiements par commande | `Payment.Create` | `PaymentFlowTests.Create_rejects_more_than_the_amount_due_and_accepts_exactly_it`, `.Create_accepts_several_partial_payments_for_the_same_order` |
| Reste à payer > 0 | `OrderReference.Create` | `OrderReferenceTests.Create_rejects_an_order_with_nothing_left_to_pay` |
| Paiement hors ligne : créé et capturé d'emblée, une tentative réussie, `PaymentCaptured` | `Payment.RecordOffline` | `PaymentOfflineTests.RecordOffline_creates_a_payment_already_captured_for_its_whole_amount`, `.RecordOffline_raises_the_same_events_as_a_provider_payment_captured_at_once`, `.RecordOffline_accepts_any_on_site_provider_of_the_restaurant`, `.RecordOffline_can_pay_only_a_part_of_what_is_due` |
| Paiement hors ligne : mêmes règles que `Create`, référence opérateur obligatoire | `Payment.RecordOffline` | `PaymentOfflineTests.RecordOffline_applies_the_rules_of_Create`, `.RecordOffline_requires_the_operator_reference` |
| Paiement hors ligne : capturé donc ni relançable ni annulable, remboursable | `RegisterAttempt`, `Cancel`, `RequestRefund`, `RegisterRefund` | `PaymentOfflineTests.Offline_payment_accepts_no_further_attempt_and_cannot_be_cancelled`, `.Offline_payment_is_partially_then_fully_refunded_like_any_captured_payment` |
| Capture tardive enregistrée sur un paiement annulé, event dédié | `Payment.RegisterLateCapture` | `PaymentLateCaptureTests.RegisterLateCapture_records_the_money_taken_on_a_cancelled_payment`, `.RegisterLateCapture_raises_a_dedicated_event_and_no_PaymentCaptured` |
| Capture tardive seulement sur `Cancelled`, tentative connue, montant > 0 dans la devise | `Payment.RegisterLateCapture` | `PaymentLateCaptureTests.RegisterLateCapture_is_rejected_on_a_payment_that_is_not_cancelled` (6 statuts), `.RegisterLateCapture_rejects_an_unknown_reference`, `.RegisterLateCapture_rejects_an_invalid_amount` |
| Capture tardive idempotente, contradiction rejetée | `Payment.RegisterLateCapture` | `PaymentLateCaptureTests.RegisterLateCapture_is_idempotent_on_replay`, `.RegisterLateCapture_replayed_after_the_refund_does_not_undo_the_refund`, `.RegisterLateCapture_replayed_with_another_amount_or_attempt_is_a_conflict` |
| `Capture` normal toujours rejeté sur un paiement annulé | `Payment.Capture` | `PaymentLateCaptureTests.Normal_capture_keeps_rejecting_a_cancelled_payment_even_after_the_late_capture_is_recorded`, `PaymentFlowTests.Late_capture_for_an_attempt_closed_by_the_cancellation_is_rejected_loudly` |
| Capture tardive remboursable, ne compte pas comme payée, ni relance ni ré-annulation | `RequestRefund`, `RegisterRefund`, `NetPaidAmount` | `PaymentLateCaptureTests.Late_capture_is_refunded_through_the_normal_refund_flow`, `.Late_capture_does_not_count_as_paid_for_the_order`, `.Payment_with_a_late_capture_cannot_be_retried_or_cancelled_again` |
| Tentative : marquage de capture tardive seulement si `Failed` | `PaymentAttempt.MarkCapturedAfterCancellation` | `PaymentAttemptTests.MarkCapturedAfterCancellation_flags_a_failed_attempt_without_reopening_it`, `.MarkCapturedAfterCancellation_is_rejected_on_an_open_or_succeeded_attempt` |
| `NetPaidAmount` (vue comptable) = capturé − remboursements réussis | `Payment.NetPaidAmount` | `PaymentNetPaidAmountTests.NetPaidAmount_is_captured_minus_succeeded_refunds` (7 statuts), `.NetPaidAmount_ignores_refunds_that_are_pending_or_failed`, `.NetPaidAmount_of_a_partial_capture_is_what_was_really_taken`, `.Split_bill_settlement_is_the_sum_of_the_net_paid_amounts_of_the_order_payments` (somme comptable ; le règlement, lui, somme `SettledAmount`) |
| `PaymentCancelled` nomme la tentative clôturée | `Payment.Cancel` | `PaymentFlowTests.Cancel_is_allowed_before_capture`, `.Cancel_is_allowed_before_any_attempt`, `.Cancel_names_the_open_retry_not_the_attempt_that_failed_before_it` |
| Devise unique | `Capture`, `RequestRefund` | `PaymentFlowTests.Capture_rejects_another_currency`, `PaymentRefundTests.RequestRefund_rejects_another_currency` |
| `captured <= amount` (hors capture tardive) | `Payment.Capture` | `PaymentFlowTests.Capture_rejects_more_than_the_payment_amount`, `.Capture_of_less_than_the_amount_is_recorded_as_captured_for_that_lower_amount`, `.Captured_amount_never_exceeds_the_amount_whatever_the_path` |
| Capture > 0 | `Payment.Capture` | `PaymentFlowTests.Capture_rejects_a_zero_or_negative_amount` |
| Capture (avec ou sans autorisation) | `Authorize`, `Capture` | `PaymentFlowTests.Authorize_moves_a_pending_payment_to_authorized_without_capturing`, `.Capture_after_authorization_captures_the_payment`, `.Capture_directly_from_pending_is_allowed` |
| Résultat appliqué à une tentative connue | `Authorize`, `Capture`, `Fail` | `PaymentFlowTests.Provider_results_for_an_unknown_reference_are_rejected`, `.Provider_results_cannot_be_applied_before_any_attempt_is_registered` |
| `ProviderReference` unique dans le paiement | `RegisterAttempt` | `PaymentFlowTests.RegisterAttempt_rejects_a_provider_reference_already_used_by_this_payment` |
| Une seule tentative ouverte | `RegisterAttempt` | `PaymentFlowTests.RegisterAttempt_rejects_a_second_attempt_while_one_is_open`, `.RegisterAttempt_rejects_a_second_attempt_while_the_payment_is_authorized` |
| Pas de tentative après capture | `RegisterAttempt` | `PaymentFlowTests.RegisterAttempt_rejects_a_payment_that_was_already_captured` |
| Failed ne devient pas Captured sans tentative valide | `Capture`, `RegisterAttempt` | `PaymentFlowTests.Failed_payment_cannot_be_captured_through_its_failed_attempt`, `.Failed_payment_cannot_be_captured_through_a_reference_that_was_never_registered`, `.Failed_payment_is_captured_only_through_a_new_registered_attempt_that_succeeds`, `.Old_failed_attempt_cannot_capture_once_a_retry_is_open`, `.Retry_that_fails_again_leaves_the_payment_failed_and_can_be_retried_once_more` |
| Échec : raison obligatoire, statut `Failed` | `Payment.Fail` | `PaymentFlowTests.Fail_moves_the_payment_to_failed_with_the_reason`, `.Fail_requires_a_reason`, `.Fail_rejects_a_too_long_reason` |
| Webhook dupliqué sans double effet (capture) | `Payment.Capture` | `PaymentWebhookIdempotenceTests.Duplicate_capture_webhook_has_no_second_effect`, `.Duplicate_capture_webhook_raises_PaymentCaptured_exactly_once_overall`, `.Duplicate_capture_webhook_arriving_after_a_refund_does_not_undo_the_refund` |
| Webhook dupliqué (autorisation, y compris en retard) | `Payment.Authorize` | `PaymentWebhookIdempotenceTests.Duplicate_authorization_webhook_has_no_second_effect`, `.Authorization_webhook_arriving_after_the_capture_does_not_move_the_payment_back` |
| Webhook dupliqué (échec) | `Payment.Fail` | `PaymentWebhookIdempotenceTests.Duplicate_failure_webhook_has_no_second_effect_and_keeps_the_first_reason`, `.Duplicate_failure_webhook_of_an_old_attempt_does_not_fail_the_retry_in_progress`, `.Duplicate_failure_webhook_on_a_cancelled_payment_changes_nothing` |
| Résultat contradictoire rejeté | `Capture`, `Fail` | `PaymentWebhookIdempotenceTests.Capture_replayed_with_a_different_amount_is_a_conflict`, `.Failure_reported_for_a_succeeded_attempt_is_rejected`, `.Success_reported_for_a_failed_attempt_is_rejected` |
| Tentative : terminal non réversible (double success) | `PaymentAttempt.Succeed`/`Fail`/`MarkAuthorized` | `PaymentAttemptTests.Double_success_is_rejected_because_a_succeeded_attempt_is_final`, `.Succeeded_attempt_cannot_fail_or_be_authorized_again`, `.Failed_attempt_cannot_succeed_fail_again_or_be_authorized` |
| Annulation seulement avant capture | `Payment.Cancel` | `PaymentFlowTests.Cancel_is_allowed_before_capture`, `.Cancel_is_allowed_before_any_attempt`, `.Cancel_is_rejected_once_the_payment_is_captured`, `.Cancel_requires_a_reason` |
| `Cancelled` terminal (hors `RegisterLateCapture`) | tous les autres mutateurs | `PaymentFlowTests.Cancelled_payment_rejects_every_way_forward_and_stays_untouched`, `.Late_capture_for_an_attempt_closed_by_the_cancellation_is_rejected_loudly` |
| Remboursement : montant > 0, raison | `Refund.Request`, `Payment.RequestRefund` | `RefundTests.Request_rejects_a_zero_or_negative_amount`, `.Request_requires_a_reason`, `PaymentRefundTests.RequestRefund_rejects_a_zero_or_negative_amount`, `.RequestRefund_requires_a_reason` |
| `refund total <= captured` (over-refund) | `Payment.RequestRefund` | `PaymentRefundTests.RequestRefund_rejects_more_than_the_captured_amount`, `.RequestRefund_rejects_more_than_what_is_left_after_succeeded_refunds`, `.Refund_of_a_partial_capture_is_measured_against_the_captured_amount_not_the_requested_one`, `.Refund_total_never_exceeds_the_captured_amount_over_many_sequences` |
| Deux remboursements en attente ne dépassent pas le capturé | `Payment.RequestRefund` | `PaymentRefundTests.Two_pending_refunds_cannot_together_exceed_the_captured_amount`, `.Failed_refund_frees_its_amount_for_a_new_request` |
| Remboursement seulement si capturé et non totalement remboursé | `Payment.RequestRefund` | `PaymentRefundTests.RequestRefund_is_rejected_unless_money_is_captured_and_not_fully_refunded` |
| `PartiallyRefunded` / `Refunded` dérivés des remboursements réussis | `Payment.RegisterRefund` | `PaymentRefundTests.RegisterRefund_success_partially_refunds_the_payment`, `.RegisterRefund_success_of_the_whole_captured_amount_refunds_the_payment`, `.Successive_partial_refunds_end_in_refunded_when_they_reach_the_captured_amount`, `.Two_reserved_refunds_that_both_succeed_end_in_refunded_with_nothing_pending` |
| Échec de remboursement : réservation libérée, statut inchangé | `Payment.RegisterRefund` | `PaymentRefundTests.RegisterRefund_failure_releases_the_reservation_and_leaves_the_status`, `.RegisterRefund_failure_on_a_partially_refunded_payment_keeps_it_partially_refunded` |
| Résultat de remboursement idempotent | `Payment.RegisterRefund` | `PaymentWebhookIdempotenceTests.Duplicate_refund_success_refunds_the_money_only_once`, `.Duplicate_refund_failure_changes_nothing`, `.Duplicate_refund_success_on_a_fully_refunded_payment_changes_nothing` |
| Remboursement : terminal non réversible | `RegisterRefund`, `Refund.Succeed`/`Fail` | `PaymentWebhookIdempotenceTests.Contradictory_refund_outcome_is_rejected`, `RefundTests.Closed_refund_is_final` |
| Remboursement / issue inconnus | `Payment.RegisterRefund` | `PaymentRefundTests.RegisterRefund_rejects_an_unknown_refund_or_outcome` |
| `Refunded` terminal | tous les mutateurs | `PaymentRefundTests.Refunded_payment_rejects_every_way_forward_and_stays_untouched` |
| `PaymentProviderCode` / `ProviderReference` valides | `Create` | `ProviderCodeTests.Create_rejects_an_invalid_code`, `ProviderReferenceTests.Create_rejects_an_empty_or_malformed_reference` |
| Aucun setter public | — | `PaymentFlowTests.Payment_and_its_children_expose_no_public_setter` |

## 6. Ce qui relève de l'Application / Infrastructure

- **Appels au prestataire** (créer l'intention, capturer, annuler, rembourser) et **validation des webhooks** (signature,
  correspondance montant/devise) : Infrastructure. Le Domain ne reçoit que des résultats validés.
- **Retrouver le `Payment` d'un webhook** : par `ProviderReference` ; index unique `(Provider, ProviderReference)` recommandé
  (l'agrégat ne garantit l'unicité qu'à l'intérieur d'un paiement).
- **Concurrence** : deux webhooks simultanés sur le même paiement => verrou optimiste (rowversion) sur `Payment` et rejeu ;
  au rejeu, l'idempotence du Domain rend le second sans effet. Une table d'idempotence des webhooks reste utile en amont.
- **Construction de `OrderReference`** depuis la commande chargée : son `TenantId`, son `OrderId` et le reste à payer
  (`Order.Total − Σ SettledAmount` des paiements existants, moins les montants des paiements encore en cours si l'on veut
  empêcher le sur-encaissement). Le Domain vérifie tenant, devise et `montant <= reste à payer` ; il ne peut pas voir les
  autres paiements.
- **Total des paiements d'une commande (addition partagée)** : deux paiements créés en parallèle peuvent chacun respecter
  le reste à payer et le dépasser ensemble. Garde-fou : sérialiser la création par commande (verrou sur la commande ou
  contrainte en base) — Application/Infrastructure.
- **Règlement de la commande** : avant `Order.Complete`, l'Application somme **`SettledAmount`** (et non `NetPaidAmount`)
  des paiements de la commande et construit le `OrderSettlement` du module Orders. `SettledAmount` retire déjà les
  remboursements en cours : sans cela, une commande pourrait être clôturée alors que l'argent repart. `NetPaidAmount`
  reste la valeur à utiliser en comptabilité/reporting (argent encore détenu).
- **Annulation chez le prestataire** : sur `PaymentCancelled` avec `ClosedAttemptProviderReference` non nul, annuler
  (void) l'autorisation/intention.
- **Capture reçue après annulation** : quand un résultat de capture arrive pour un paiement `Cancelled`, appeler
  `RegisterLateCapture` (et non `Capture`), puis sur `PaymentCapturedAfterCancellation` déclencher le remboursement
  (`RequestRefund` + appel prestataire). La décision de rembourser automatiquement ou d'alerter un humain est applicative.
  Passer le montant **réellement capturé** par le prestataire, même s'il dépasse `Amount` ; pour rembourser, viser
  `RefundableAmount`, pas `Amount`.
- **Capture normale supérieure au montant demandé** (tentative encore ouverte) : toujours rejetée par `Capture`
  (`payment.capture_exceeds_amount`) ; l'Application doit alerter, le Domain n'en garde pas de trace.
- **Paiement hors ligne** : l'Application choisit le `PaymentProviderCode` (`cash`, terminal externe) et exige la référence
  opérateur ; le rendu de monnaie et le fond de caisse ne sont pas modélisés.
- **Timeout d'une tentative sans réponse**, relance d'un remboursement échoué : politiques applicatives.

## 7. Deux niveaux de configuration

La **plateforme** définit ce qui est supporté techniquement ; chaque **restaurant** définit ce qu'il accepte réellement.

| | `PaymentProvider` (plateforme) | `RestaurantPaymentConfiguration` (restaurant) |
|---|---|---|
| Rôle | Catalogue : code (`PaymentProviderCode`), nature (`PaymentProviderType` : `OnSite` / `Online`), nom affiché, capacités (`PaymentProviderCapability` : moyens `PaymentMethodType`, pays, devises, autorisation, remboursement total/partiel, webhook, récurrence, expiration, min/max par devise, mode plateforme autorisé), proposé ou retiré | Un prestataire du catalogue accepté par UN restaurant (index unique tenant + prestataire) : `PaymentMode` (`TenantManaged` : compte du restaurant / `PlatformManaged` : compte de la plateforme), `PaymentConfigurationStatus` (`AwaitingCredentials` / `Configured`), actif, moyens retenus, `SecretReference`, affectations aux établissements (`PaymentMethodAssignment` : établissement, moyen, actif, priorité, min/max) |
| Agrégat | De la plateforme (pas de tenant ; déclaré dans les `PlatformAggregates` des tests d'architecture) | `ITenantOwned` ; `PaymentMethodAssignment` est une entité enfant |
| Règles | Code unique ; en ligne seulement si son **intégration existe dans le code** ; retirer n'efface rien : plus de NOUVEAU paiement, ceux en cours restent suivis ; sur place : ni mode plateforme, ni webhook, ni autorisation | Création depuis un prestataire proposé, avec tous ses moyens. Sur place : prête et active aussitôt, toujours `TenantManaged`. En ligne : inactive ; prête aussitôt en `PlatformManaged` (si la capacité l'autorise), après `Configure(référence)` en `TenantManaged`. `Enable` exige `Configured`. Passer en `TenantManaged` sans identifiants désactive. Au moins un moyen retenu ; désactiver un moyen retire ses affectations. Sans affectation : valable dans tous les établissements ; sinon seulement dans les établissements affectés, dans leurs bornes. Un établissement d'un autre tenant est refusé (`common.tenant.mismatch`) ; 500 affectations au plus |
| Événements | `PaymentProviderRegistered`, `...Renamed`, `...CapabilitiesChanged`, `...Enabled`, `...Disabled` | `PaymentConfigurationCreated`, `...Configured`, `...ModeChanged`, `...Enabled`, `...Disabled`, `...MethodActivated`, `...MethodDeactivated`, `PaymentMethodAssignedToLocation`, `PaymentMethodRemovedFromLocation` : AUCUN ne porte de secret |

- **Un nouveau paiement** exige : configuration activée et `Configured` chez le restaurant, prestataire proposé par la
  plateforme, devise de la commande gérée, bonne nature (sinon `payments.method.not_accepted` /
  `payments.method.type_mismatch`). La résolution par établissement, pays, montant et moyen viendra avec la policy de
  disponibilité (étape suivante).
- **Identifiants** : JAMAIS dans un agrégat, une table d'agrégat, un événement, un log, une exception ou une réponse.
  Ils sont rangés par l'Application dans le coffre (`IPaymentSecretStore`) ; la configuration ne garde que la
  `PaymentCredentialReference` : `payments/tenant/{tenantId}/{prestataire}` (compte du restaurant) ou
  `payments/platform/{prestataire}` (compte de la plateforme, saisi par l'administrateur). L'API dit seulement
  `hasCredentials`.
- **Moyens du comptoir intégrés** (`BuiltInPaymentProviders`) : `cash`, `card_terminal`, `mobile_money`,
  `bank_transfer`, posés au catalogue par `MigratePaymentsDatabaseAsync` s'ils manquent ; la plateforme peut en ajouter
  d'autres (`meal_voucher`...), les renommer, les restreindre à des devises ou les retirer.
- **Une intégration en ligne** (Infrastructure, `IPaymentIntegration`) déclare ses champs d'identifiants, crée le
  `IPaymentGateway` (appels sortants) et le `IPaymentWebhookReader` (signature) branchés sur le compte marchand lu au
  coffre.

## 8. Application (`Payments.Application`)

Le tenant d'un cas d'usage vient **toujours** d'`ITenantContext.RequireTenant()` : claim `tid` du jeton pendant une
requête, `BackgroundTenant` du webhook ou du message traité en arrière-plan. Sans tenant : `403 common.tenant.required`.

| Cas d'usage | Effet | Erreurs propres à l'Application |
|---|---|---|
| Plateforme : `RegisterPaymentProvider(code, kind, displayName, currencies?)`, `UpdatePaymentProvider`, `Enable/DisablePaymentProvider` | Catalogue | `payments.provider.integration_missing`, `payments.provider.not_found` |
| Plateforme : `SetPlatformCredentials(providerId, { champ: valeur })` | Compte marchand de la plateforme rangé au coffre (`payments/platform/{code}`) ; refusé si le prestataire n'autorise pas le mode plateforme | `payments.configuration.platform_mode_not_allowed`, `payments.configuration.credential_fields_invalid` |
| Restaurant : `CreatePaymentConfiguration(provider, mode?)`, `ConfigurePaymentConfiguration(id, { champ: valeur })`, `ChangePaymentMode(id, mode)`, `Enable/DisablePaymentConfiguration(id)`, `SetPaymentMethodActive(id, method, isActive)` | Ce que le restaurant accepte ; identifiants : tous les champs de l'intégration, eux seuls, non vides, rangés au coffre AVANT la sauvegarde de la configuration | `payments.provider.not_found`, `payments.configuration.not_found`, `payments.configuration.credential_fields_invalid` |
| `RecordOfflinePayment(orderId, provider, reference, amount?, paymentMethod?)` | Argent déjà reçu au comptoir : `Payment.RecordOffline` | `payments.method.not_accepted`, `payments.method.kind_mismatch`, `payments.order.not_found`, `payments.order.not_payable`, `payments.order.nothing_due`, `payments.tenant.not_writable` |
| `StartOnlinePayment(orderId, provider, amount?, paymentMethod?)` | Crée le paiement, ouvre l'intention sur le compte marchand du restaurant, enregistre la tentative | idem + `payments.provider.call_failed`, `payments.configuration.credentials_unreadable` |
| `RetryPayment(paymentId)` | Paiement en ligne `Failed`, moyen toujours accepté : nouvelle intention | `payments.payment.retry_not_allowed` |
| `CancelPayment(paymentId, reason)` | Avant encaissement ; libère sa part de la commande | domaine |
| `RequestRefund(paymentId, reason, amount?)` | Comptoir : réussi aussitôt. En ligne : réservé, transmis au prestataire après la sauvegarde, issue par webhook | domaine |
| `ApplyProviderResult(...)` | Résultat **vérifié** ; capture sur un paiement annulé = capture tardive ; **pas** de contrôle « tenant accepte les écritures », ni du moyen (de l'argent a pu bouger) | `payments.payment.not_found`, `payments.provider_result.invalid` |
| `ListPaymentProviders`, `ListPaymentIntegrations` (plateforme) ; `ListOfferedPaymentProviders`, `ListPaymentConfigurations` (restaurant) | Catalogue et configurations ; `ListPaymentConfigurations` donne, pour une configuration en ligne, l'adresse de webhook à déclarer chez le prestataire | — |
| `GetPaymentById`, `GetOrderPayments(orderId)`, `GetOrderSettledAmount(orderId, currency)` | Détail, écran de caisse, règlement pour Orders | `payments.payment.not_found`, `payments.order.not_found` |

- **Reste à payer** (`OrderBalance`) = total − part de chaque paiement : `SettledAmount` s'il est encaissé, **tout son
  montant** s'il est en attente, autorisé ou en échec ; rien s'il est annulé. Le port `IPaymentOrderLock` sérialise
  les créations de paiement d'une même commande.
- **Permissions** : lecture ouverte à tout membre ; `payments.collect`, `payments.refund`, `payments.configure`
  (configurations et identifiants). Catalogue : administrateur de la plateforme (policy de l'hôte).
- **Hors périmètre pour l'instant** : remboursement automatique d'une capture tardive ; annulation automatique des
  paiements d'une commande annulée ; expiration des tentatives sans réponse ; endpoints d'affectation aux établissements (avec le port vers Locations).

## 9. Infrastructure (`Payments.Infrastructure`)

- Schéma `payments` : `payment_providers` (capacités en jsonb), `restaurant_payment_configurations` (moyens retenus en
  jsonb, `secret_reference`), `payment_method_assignments` (bornes en jsonb `{ amount, currency }`), `payments`,
  `payment_attempts`, `refunds`, `provider_webhook_events`, `payment_secrets`, `outbox_messages`. Toute table d'un
  restaurant porte `tenant_id` (celles des enfants sont recopiées de la racine par l'unit of work) ; contraintes CHECK
  sur les montants, statuts, mode et priorité.
- **Coffre** (`PostgresPaymentSecretStore`, derrière `IPaymentSecretStore`) : table `payment_secrets` (référence ->
  chiffré), AES-256-GCM, clé `PaymentsOptions.CredentialsKey` (32 octets, fournie par l'hôte), la référence servant de
  données authentifiées : un chiffré recopié sous une autre référence ne se déchiffre pas. Écriture immédiate par un
  `INSERT ... ON CONFLICT` paramétré, hors des agrégats. Un Vault ou le gestionnaire de secrets d'un cloud pourra le
  remplacer sans rien changer d'autre. CHANGER LA CLÉ rend les identifiants illisibles : ils devront être ressaisis.
- **Verrou par commande** : `pg_advisory_xact_lock` dans une transaction validée par la sauvegarde. ATTENTION : pour un
  paiement en ligne, l'appel au prestataire a lieu verrou tenu.
- **Webhooks** : une adresse PAR CONFIGURATION, `/payments/webhooks/{prestataire}/{configuration}`, car chaque compte
  marchand signe avec son propre secret. `PaymentWebhookReceiver` retrouve la configuration, lit ses identifiants au coffre, fait
  vérifier la signature par le lecteur de l'intégration et conserve le corps brut avec le tenant, unique par
  `(configuration_id, event_id)` dans `provider_webhook_events`. `PaymentWebhookProcessor` (arrière-plan) applique ensuite pour CE tenant : `applied`,
  `rejected:<code>` (jamais retenté), référence encore inconnue retentée 10 minutes puis rejetée, erreur technique
  retentée. Un webhook signé par un restaurant ne peut donc jamais toucher le paiement d'un autre.
- **Relais vers le prestataire** (consommateur d'outbox) : annulation d'une intention ouverte, demande de remboursement,
  avec les identifiants du compte qui a encaissé ; identifiants absents ou illisibles : journalisé (la référence
  seulement), non retenté.
- **Intégration `sandbox`** (développement, recette, tests ; `PaymentsOptions.EnableSandbox`) : champ
  `webhook_secret` ; intentions `sbx_...` sans appel externe ; webhooks `Sandbox-Signature: t=<unix>,v1=<hex
  HMAC-SHA256(secret, "t.corps")>`, 5 minutes de tolérance ; `SandboxSignature.Sign` signe.
- **Brancher un vrai prestataire** (FedaPay, KKiaPay, CinetPay, Stripe...) : une classe `IPaymentIntegration`
  (champs d'identifiants, `IPaymentGateway`, `IPaymentWebhookReader`) enregistrée dans `AddPaymentsInfrastructure`, puis
  la plateforme l'ajoute à son catalogue.
- **Prestataires intégrés** (`Gateways/Providers`, posés au catalogue au démarrage s'ils manquent) : FedaPay, KKiaPay,
  CinetPay, MTN MoMo, Stripe, PayPal, Wave, Flutterwave, Paystack, PayDunya, Mollie. Moov Money passe par FedaPay,
  CinetPay ou PayDunya. Particularités des derniers :
  - Flutterwave : `secret_key`, `webhook_hash`, `account_email` (envoyé comme client, jamais celui du payeur) ; une
    référence `tx_ref` par tentative ; webhook authentifié par `verif-hash`, puis relecture `verify_by_reference`.
  - Paystack : `secret_key`, `account_email` ; montant en unités mineures ; webhook `x-paystack-signature`
    (HMAC-SHA512 du corps par la clé secrète), puis relecture `/transaction/verify`.
  - PayDunya : `master_key`, `private_key`, `token`, `environment` ; la référence est le jeton de facture ; IPN en
    formulaire, `data[hash]` = SHA-512 de la clé principale, puis relecture `/checkout-invoice/confirm` ; aucun appel
    sans effet pour tester le compte (`not_verifiable`).
  - Mollie : `api_key` (`test_…` = sandbox) ; webhook non signé (`id=tr_…`), le paiement relu avec la clé fait foi ;
    `webhookUrl` transmis seulement si l'API publique est en HTTPS ; remboursement par API avec `metadata.refund_id`
    et `Idempotency-Key`, résultat relu par le même webhook.
  - Remboursements Flutterwave, Paystack et PayDunya : depuis leur tableau de bord (refus définitif côté API).
- **Migration** : régénérer `InitialCreate` (le schéma a changé) :
  `dotnet ef migrations add InitialCreate --project src/Modules/Payments/Payments.Infrastructure --startup-project src/Modules/Payments/Payments.Infrastructure --output-dir Persistence/Migrations`.
  `database update` lit `PAYMENTS_DB`.

## 10. API (`Payments.Api`)

Trois groupes : `/payments` (personnel, policy `tenant-member`), `/payments/webhooks` (anonyme : l'authenticité vient
de la signature) et `/platform/payment-providers` (l'hôte y chaîne la policy d'administrateur de la plateforme).

| Méthode | Route | Corps | Permission |
|---|---|---|---|
| `GET` | `/platform/payment-providers` · `/platform/payment-providers/integrations` | — | admin plateforme |
| `POST` | `/platform/payment-providers` | `{ code, type: OnSite/Online, displayName, capability: { methods, countries?, currencies?, supportsAuthorization, supportsRefund, supportsPartialRefund, supportsWebhooks, supportsRecurring, paymentExpirationMinutes?, amountLimits? } }` -> `{ id }` | admin plateforme |
| `PUT` | `/platform/payment-providers/{id}` | `{ displayName, capability }` | admin plateforme |
| `POST` | `/platform/payment-providers/{id}/enable` · `/disable` | — | admin plateforme |
| `PUT` | `/platform/payment-providers/{id}/credentials` | `{ credentials: { champ: valeur } }` (compte de la plateforme) | admin plateforme |
| `GET` | `/payments/providers` (proposés) · `/payments/configurations` (acceptés) | — | membre |
| `POST` | `/payments/configurations` | `{ provider, mode?: TenantManaged/PlatformManaged }` -> `{ id }` | `payments.configure` |
| `PUT` | `/payments/configurations/{id}/credentials` | `{ credentials: { champ: valeur } }` | `payments.configure` |
| `PUT` | `/payments/configurations/{id}/mode` | `{ mode }` | `payments.configure` |
| `PUT` | `/payments/configurations/{id}/methods` | `{ method, isActive }` | `payments.configure` |
| `POST` | `/payments/configurations/{id}/enable` · `/disable` | — | `payments.configure` |
| `POST` | `/payments/offline` | `{ orderId, provider, reference, amount?, paymentMethod? }` -> `{ id }` | `payments.collect` |
| `POST` | `/payments/online` | `{ orderId, provider, amount?, paymentMethod? }` -> `{ paymentId, providerReference, checkoutUrl }` | `payments.collect` |
| `GET` | `/payments?orderId` · `/payments/{id}` | — | membre |
| `POST` | `/payments/{id}/retry` · `/cancel` (`{ reason }`) | | `payments.collect` |
| `POST` | `/payments/{id}/refunds` | `{ reason, amount? }` -> `{ id }` | `payments.refund` |
| `POST` | `/payments/webhooks/{provider}/{methodId}` | corps brut signé (256 Kio max) | anonyme |
| `POST` | `/payments/webhooks/{provider}` | webhook du compte de la PLATEFORME : vérifié avec ses identifiants, rien n'est conservé ; l'hôte relit aussitôt le règlement d'abonnement qui porte la transaction | anonyme |

Webhooks : `200` conservé, déjà reçu ou sans intérêt ; `400` signature invalide ; `404` adresse inconnue ; `413` trop
gros. Erreurs du personnel : `401`, `403`, `404`, `409`, `422` (code dans `code`).

Tests : `Payments.Domain.Tests`, `Payments.Application.Tests` (fakes), `Payments.Infrastructure.Tests` et
`Payments.Api.Tests` (vrai PostgreSQL, Docker ; **la migration doit exister**).

## 11. Acompte de réservation

- Un acompte est un `Payment` avec `Purpose = ReservationDeposit` et `ReservationId`. Tant qu'il n'est pas déduit,
  `OrderId` porte l'identifiant de la réservation (`DepositReference.AsHolder()`) et `IsUnappliedDeposit` est vrai.
- L'unité de travail émet alors `DepositSettlementChanged` au lieu de `PaymentSettlementChanged` : Orders,
  Storefront et Fiscal ne voient pas cet argent avant la déduction.
- `StartDepositPayment` (en ligne, prestataires configurés) et `RecordOfflineDeposit` (équipe ; carte cadeau
  refusée) encaissent le montant exact demandé par Reservations (`IPaymentReservations`).
- `ApplyDeposit(reservationId, orderId)` déplace les paiements capturés vers la commande du repas (`ApplyToOrder`,
  événement `DepositApplied`) ; refusé si l'acompte dépasse le reste à payer ou vient d'un autre établissement.
- `RefundDepositPayments` annule les paiements non capturés et rembourse les capturés (annulation à temps ou
  remboursement demandé par l'équipe).
- Limite connue : dans les webhooks, `order_id` d'un acompte non déduit est l'identifiant de la réservation.

| Méthode | Route | Corps | Permission |
|---|---|---|---|
| `GET` | `/payments/deposits/{reservationId}` | — | membre |
| `POST` | `/payments/deposits/{reservationId}/offline` | `{ provider, reference, paymentMethod? }` + `Idempotency-Key` | `payments.collect` |
| `POST` | `/payments/deposits/{reservationId}/apply` | `{ orderId }` | `payments.collect` |

Migration : `ReservationDeposits` (colonnes `purpose`, `reservation_id`, `applied_at_utc`).
