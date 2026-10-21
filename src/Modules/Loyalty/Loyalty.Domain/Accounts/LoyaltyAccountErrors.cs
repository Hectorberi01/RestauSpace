namespace Loyalty.Domain;

public static class LoyaltyAccountErrors
{
    public static readonly DomainError PointsNegative = new("loyalty.points.negative", "Points cannot be negative.");
    public static readonly DomainError ReferenceRequired = new("loyalty.reference.required", "A transaction reference is required.");
    public static readonly DomainError ReferenceTooLong = new("loyalty.reference.too_long", "The transaction reference is too long.");
    public static readonly DomainError OverdraftNotPositive = new("loyalty.overdraft_policy.not_positive", "An overdraft allowance must be strictly positive.");

    public static readonly DomainError PointsNotPositive = new("loyalty.transaction.points_not_positive", "A ledger entry needs a strictly positive number of points.");
    public static readonly DomainError DirectionInvalid = new("loyalty.transaction.direction_invalid", "Unknown ledger direction.");

    public static readonly DomainError AlreadyExists = new("loyalty.account.already_exists", "This customer already has a loyalty account.", DomainErrorKind.Conflict);
    public static readonly DomainError CustomerAnonymized = new("loyalty.account.customer_anonymized", "An anonymized customer cannot have a loyalty account.");
    public static readonly DomainError Closed = new("loyalty.account.closed", "The loyalty account is closed.");
    public static readonly DomainError Suspended = new("loyalty.account.suspended", "The loyalty account is suspended.");
    public static readonly DomainError AlreadySuspended = new("loyalty.account.already_suspended", "The loyalty account is already suspended.");
    public static readonly DomainError NotSuspended = new("loyalty.account.not_suspended", "Only a suspended loyalty account can be reactivated.");
    public static readonly DomainError InsufficientBalance = new("loyalty.account.insufficient_balance", "The balance does not cover this number of points.");
    public static readonly DomainError DuplicateTransaction = new("loyalty.account.duplicate_transaction", "This reference has already been posted for this transaction type.", DomainErrorKind.Conflict);
    public static readonly DomainError AdjustmentReasonRequired = new("loyalty.account.adjustment_reason_required", "An adjustment needs a reason.");
    public static readonly DomainError AdjustmentReasonTooLong = new("loyalty.account.adjustment_reason_too_long", "The adjustment reason is too long.");
    public static readonly DomainError ReasonRequired = new("loyalty.account.reason_required", "A reason is required.");
    public static readonly DomainError ReasonTooLong = new("loyalty.account.reason_too_long", "The reason is too long.");
    public static readonly DomainError TransactionNotFound = new("loyalty.account.transaction_not_found", "No ledger entry of this type carries this reference.");
    public static readonly DomainError TransactionOfAnotherAccount = new("loyalty.account.transaction_of_another_account", "This ledger entry belongs to another loyalty account.");
    public static readonly DomainError TransactionTypeMismatch = new("loyalty.account.transaction_type_mismatch", "This ledger entry is not of the type this reversal cancels.");
    public static readonly DomainError AlreadyReversed = new("loyalty.account.already_reversed", "This ledger entry has already been reversed.", DomainErrorKind.Conflict);
    public static readonly DomainError NothingToExpire = new("loyalty.account.nothing_to_expire", "There is no point to expire.");
    public static readonly DomainError RewardInactive = new("loyalty.account.reward_inactive", "This reward is not available.");
}
