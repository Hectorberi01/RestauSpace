namespace Payments.Domain;

public static class PaymentErrors
{
    public static readonly DomainError NotADeposit = new("payments.payment.not_a_deposit", "This payment is not a reservation deposit.");
    public static readonly DomainError DepositAlreadyApplied = new("payments.payment.deposit_already_applied", "This deposit is already deducted from a bill.");
    public static readonly DomainError DepositNotCaptured = new("payments.payment.deposit_not_captured", "Only a collected deposit can be deducted from a bill.");
    public static readonly DomainError DepositAboveAmountDue = new("payments.payment.deposit_above_amount_due", "The deposit is larger than what is left to pay: refund the difference first.");
    public static readonly DomainError DepositOtherLocation = new("payments.payment.deposit_other_location", "The deposit was paid for another location.");
    public static readonly DomainError DepositMethodNotAllowed = new("payments.payment.deposit_method_not_allowed", "A deposit cannot be paid with a gift card or store credit.");
    public static readonly DomainError ProviderCodeInvalid = new("payments.provider_code.invalid", "Provider code must be 2 to 32 lowercase letters, digits, '_' or '-', starting with a letter.");
    public static readonly DomainError ProviderReferenceInvalid = new("payments.provider_reference.invalid", "Provider reference is required, bounded and without whitespace.");

    public static readonly DomainError OrderAmountDueNotPositive = new("payments.order_reference.amount_due_not_positive", "An order with nothing left to pay cannot be paid.");

    public static readonly DomainError AmountNotPositive = new("payments.payment.amount_not_positive", "Payment amount must be greater than zero.");
    public static readonly DomainError AmountExceedsAmountDue = new("payments.payment.amount_exceeds_amount_due", "Payment amount cannot exceed what is left to pay for the order.");
    public static readonly DomainError CurrencyMismatch = new("payments.payment.currency_mismatch", "Every amount of a payment must use the payment currency, which is the order currency.");
    public static readonly DomainError AttemptNotAllowed = new("payments.payment.attempt_not_allowed", "A new attempt can only be registered on a pending or failed payment.");
    public static readonly DomainError AttemptInProgress = new("payments.payment.attempt_in_progress", "Another attempt is still open; it must end before a new one is registered.");
    public static readonly DomainError AttemptReferenceDuplicate = new("payments.payment.attempt_reference_duplicate", "This provider reference is already used by an attempt of this payment.");
    public static readonly DomainError TooManyAttempts = new("payments.payment.too_many_attempts", "The payment has reached its maximum number of attempts; cancel it and start a new payment.");
    public static readonly DomainError AttemptNotFound = new("payments.payment.attempt_not_found", "No attempt of this payment has this provider reference.");
    public static readonly DomainError CaptureAmountNotPositive = new("payments.payment.capture_amount_not_positive", "Captured amount must be greater than zero.");
    public static readonly DomainError CaptureExceedsAmount = new("payments.payment.capture_exceeds_amount", "Captured amount cannot exceed the payment amount.");
    public static readonly DomainError CaptureConflict = new("payments.payment.capture_conflict", "This attempt was already captured for a different amount.");
    public static readonly DomainError LateCaptureNotAllowed = new("payments.payment.late_capture_not_allowed", "A late capture can only be recorded on a cancelled payment.");
    public static readonly DomainError FailureReasonInvalid = new("payments.payment.failure_reason_invalid", "A failure reason, when given, is bounded.");
    public static readonly DomainError FailureCodeInvalid = new("payments.payment.failure_code_invalid", "A failure code is lowercase letters, digits, '_', '.' or '-', starts with a letter, 64 characters at most.");
    public static readonly DomainError CancelNotAllowed = new("payments.payment.cancel_not_allowed", "A payment can only be cancelled before it is captured.");
    public static readonly DomainError AlreadyCancelled = new("payments.payment.already_cancelled", "A cancelled payment can no longer change.");
    public static readonly DomainError CancellationReasonInvalid = new("payments.payment.cancellation_reason_invalid", "A bounded cancellation reason is required.");
    public static readonly DomainError RefundNotAllowed = new("payments.payment.refund_not_allowed", "Only a captured, not fully refunded payment can be refunded.");
    public static readonly DomainError RefundExceedsRefundable = new("payments.payment.refund_exceeds_refundable", "Refund exceeds what is still refundable (captured minus refunded and pending refunds).");
    public static readonly DomainError TooManyRefunds = new("payments.payment.too_many_refunds", "The payment has reached its maximum number of refunds.");
    public static readonly DomainError RefundNotFound = new("payments.payment.refund_not_found", "Refund not found on this payment.");

    public static readonly DomainError AttemptAlreadySucceeded = new("payments.payment_attempt.already_succeeded", "A succeeded attempt is final.");
    public static readonly DomainError AttemptAlreadyFailed = new("payments.payment_attempt.already_failed", "A failed attempt is final; a new attempt is required.");

    public static readonly DomainError RefundAmountNotPositive = new("payments.refund.amount_not_positive", "Refund amount must be greater than zero.");
    public static readonly DomainError RefundReasonInvalid = new("payments.refund.reason_invalid", "A bounded refund reason is required.");
    public static readonly DomainError RefundOutcomeUnknown = new("payments.refund.outcome_unknown", "Unknown refund outcome.");
    public static readonly DomainError RefundOutcomeConflict = new("payments.refund.outcome_conflict", "This refund already has a different final outcome.");
    public static readonly DomainError OfflineNeedsOnSiteProvider = new("payments.payment.offline_needs_on_site_provider", "Money received at the counter is recorded with an on-site provider (cash, card terminal...).");
    public static readonly DomainError OnlineNeedsOnlineProvider = new("payments.payment.online_needs_online_provider", "An online payment needs an online provider; record counter payments as offline payments.");
    public static readonly DomainError RefundReferenceDuplicate = new("payments.refund.reference_duplicate", "Another refund of this payment already has this provider reference.");
    public static readonly DomainError ItemsAlreadyCovered = new("payments.payment.items_already_covered", "The lines paid by this payment are already set.");
    public static readonly DomainError TooManyItems = new("payments.payment.too_many_items", "A payment covers at most 200 order lines.");
    public static readonly DomainError ItemDuplicate = new("payments.payment.item_duplicate", "Each order line appears once in a payment.");
    public static readonly DomainError ItemQuantityInvalid = new("payments.payment.item_quantity_invalid", "A paid quantity is between 1 and 999.");
}
