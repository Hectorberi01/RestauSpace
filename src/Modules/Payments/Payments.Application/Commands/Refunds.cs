using Payments.Application.Abstractions;
using Payments.Domain;

namespace Payments.Application.Commands;

/// <summary>
/// Rembourse tout ou partie d'un paiement encaissé (tout ce qui reste remboursable si le montant est omis). Encaissé au
/// comptoir : l'argent est rendu de la main à la main, le remboursement est donc réussi aussitôt. En ligne : le montant
/// est réservé, la demande part chez le prestataire après la sauvegarde (relais de l'outbox) et son issue arrive par
/// webhook. Renvoie l'id du remboursement.
/// </summary>
public sealed record RequestRefundCommand(Guid PaymentId, string? Reason, decimal? Amount = null) : ICommand<Guid>;

/// <remarks>Payé par carte cadeau ou avoir : le montant est recrédité sur la carte (annulé si la sauvegarde échoue).</remarks>
public sealed class RequestRefundHandler(PaymentsWorkspace workspace, IPaymentStoredValue storedValue, IClock clock) : ICommandHandler<RequestRefundCommand, Guid>
{
    public async Task<Guid> HandleAsync(RequestRefundCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var payment = await workspace.LoadAsync(tenant, command.PaymentId, cancellationToken);
        var amount = command.Amount is { } requested ? Money.Of(requested, payment.Currency) : payment.RefundableAmount;

        var refundId = payment.RequestRefund(amount, command.Reason ?? string.Empty, clock);
        // Nature du moyen lue au catalogue de la plateforme (un code n'en change jamais) ; inconnu = en ligne, par prudence.
        var provider = await workspace.Providers.FindByCodeAsync(payment.Provider, cancellationToken);
        if (provider is not { Type: PaymentProviderType.OnSite })
        {
            await workspace.SaveAsync(cancellationToken);
            return refundId.Value;
        }

        payment.RegisterRefund(refundId, RefundOutcome.Succeeded, clock);
        if (payment.Method != PaymentMethodType.GiftCard)
        {
            await workspace.SaveAsync(cancellationToken);
            return refundId.Value;
        }

        var card = GiftCards.Parse(payment.Attempts.FirstOrDefault()?.ProviderReference.Value)
            ?? throw new DomainException(PaymentsApplicationErrors.GiftCardReferenceInvalid);
        var movement = GiftCards.RefundMovement(refundId.Value);
        await storedValue.CreditAsync(tenant, card.CardId, amount, movement, cancellationToken);
        try
        {
            await workspace.SaveAsync(cancellationToken);
        }
        catch (Exception)
        {
            await storedValue.ReverseAsync(tenant, card.CardId, movement, CancellationToken.None);
            throw;
        }

        return refundId.Value;
    }
}

/// <summary>
/// Rembourse en avoir plutôt qu'en argent : un avoir du montant est émis (son code est renvoyé une seule fois, à remettre
/// au client) et le remboursement est enregistré réussi, sans passer par le prestataire. Pas pour un paiement par
/// carte cadeau : il se rembourse sur sa carte.
/// </summary>
public sealed record RefundAsStoreCreditCommand(Guid PaymentId, string? Reason, decimal? Amount = null) : ICommand<StoreCreditRefundDto>;

public sealed record StoreCreditRefundDto(Guid RefundId, Guid CardId, string Code, string CodeHint, decimal Amount, string CurrencyCode);

public sealed class RefundAsStoreCreditHandler(PaymentsWorkspace workspace, IPaymentStoredValue storedValue, IClock clock)
    : ICommandHandler<RefundAsStoreCreditCommand, StoreCreditRefundDto>
{
    public const string Note = "Avoir sur remboursement";

    public async Task<StoreCreditRefundDto> HandleAsync(RefundAsStoreCreditCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var payment = await workspace.LoadAsync(tenant, command.PaymentId, cancellationToken);
        DomainException.ThrowIf(payment.Method == PaymentMethodType.GiftCard, PaymentsApplicationErrors.StoreCreditNotForGiftCard);
        var amount = command.Amount is { } requested ? Money.Of(requested, payment.Currency) : payment.RefundableAmount;

        var refundId = payment.RequestRefund(amount, command.Reason ?? string.Empty, clock);
        payment.RegisterRefund(refundId, RefundOutcome.Succeeded, clock);
        var credit = await storedValue.IssueStoreCreditAsync(tenant, amount, Note, cancellationToken);
        try
        {
            await workspace.SaveAsync(cancellationToken);
        }
        catch (Exception)
        {
            await storedValue.CancelAsync(tenant, credit.CardId, "Remboursement non enregistré", CancellationToken.None);
            throw;
        }

        return new StoreCreditRefundDto(refundId.Value, credit.CardId, credit.Code, credit.CodeHint, amount.Amount, amount.Currency.Code);
    }
}

/// <summary>Ce qu'un prestataire a confirmé, une fois sa signature vérifiée par l'Infrastructure.</summary>
public enum ProviderResultKind
{
    Authorized = 1,
    Captured = 2,
    Failed = 3,
    RefundSucceeded = 4,
    RefundFailed = 5,
}

/// <summary>
/// Applique un résultat VÉRIFIÉ du prestataire au paiement dont une tentative porte <paramref name="Reference"/>.
/// Une capture exige montant et devise ; un résultat de remboursement, l'id du remboursement (le nôtre, transmis au
/// prestataire à la demande). Le rejeu exact d'un résultat est sans effet (domaine) ; une contradiction est refusée.
/// Une capture reçue pour un paiement annulé est enregistrée comme capture tardive (argent à rendre).
/// Échec : <paramref name="FailureCode"/> (code du prestataire, normalisé) ou, à défaut, le code tiré de
/// <paramref name="Reason"/> ; <paramref name="Reason"/> est gardé, borné, comme message. Remboursement :
/// <paramref name="RefundReference"/> est la référence du remboursement chez le prestataire, s'il en donne une.
/// </summary>
public sealed record ApplyProviderResultCommand(
    string Provider,
    ProviderResultKind Kind,
    string Reference,
    decimal? Amount = null,
    string? CurrencyCode = null,
    Guid? RefundId = null,
    string? Reason = null,
    string? FailureCode = null,
    string? RefundReference = null) : ICommand;

public sealed class ApplyProviderResultHandler(PaymentsWorkspace workspace, IClock clock) : ICommandHandler<ApplyProviderResultCommand>
{
    public async Task HandleAsync(ApplyProviderResultCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        // Pas de contrôle « tenant accepte les écritures » : ce qui s'est passé chez le prestataire doit être enregistré,
        // même pour un restaurant suspendu (de l'argent a pu bouger).
        var tenant = workspace.CurrentTenant;
        var reference = ProviderReference.Create(command.Reference);
        var payment = await workspace.Payments.FindByProviderReferenceAsync(
            tenant, PaymentProviderCode.Create(command.Provider), reference, cancellationToken)
            ?? throw new NotFoundException(PaymentsApplicationErrors.PaymentNotFound);

        switch (command.Kind)
        {
            case ProviderResultKind.Authorized:
                payment.Authorize(reference, clock);
                break;

            case ProviderResultKind.Captured:
                var captured = CapturedAmount(command);
                if (payment.Status == PaymentStatus.Cancelled)
                {
                    payment.RegisterLateCapture(reference, captured, clock);
                }
                else
                {
                    payment.Capture(reference, captured, clock);
                }

                break;

            case ProviderResultKind.Failed:
                var reason = command.Reason?.Trim();
                payment.Fail(
                    reference,
                    PaymentFailureCode.FromProvider(command.FailureCode ?? reason),
                    clock,
                    reason is { Length: > PaymentAttempt.MaxFailureReasonLength } ? reason[..PaymentAttempt.MaxFailureReasonLength] : reason);
                break;

            case ProviderResultKind.RefundSucceeded or ProviderResultKind.RefundFailed:
                var refundId = command.RefundId is { } id && id != Guid.Empty
                    ? RefundId.From(id)
                    : throw new DomainException(PaymentsApplicationErrors.ProviderResultInvalid);
                payment.RegisterRefund(
                    refundId,
                    command.Kind == ProviderResultKind.RefundSucceeded ? RefundOutcome.Succeeded : RefundOutcome.Failed,
                    clock,
                    string.IsNullOrWhiteSpace(command.RefundReference) ? null : ProviderReference.Create(command.RefundReference));
                break;

            default:
                throw new DomainException(PaymentsApplicationErrors.ProviderResultInvalid);
        }

        await workspace.SaveAsync(cancellationToken);
    }

    private static Money CapturedAmount(ApplyProviderResultCommand command) =>
        command is { Amount: { } amount, CurrencyCode: { } currency }
            ? Money.Of(amount, Currency.FromCode(currency))
            : throw new DomainException(PaymentsApplicationErrors.ProviderResultInvalid);
}
