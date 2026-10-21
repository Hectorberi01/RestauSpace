using Loyalty.Application.Abstractions;
using Loyalty.Domain;

namespace Loyalty.Application.Commands;

// Cartes cadeaux et avoirs. Émettre et annuler sont des gestes de gestion ; utiliser, recréditer et annuler un
// mouvement sont appelés par l'encaissement (module Payments, via l'hôte) avec une référence qui rend le rejeu sans effet.

/// <summary>Le code complet n'est renvoyé qu'ici, une seule fois : il faut le remettre au client.</summary>
public sealed record IssuedStoredValueCardDto(Guid Id, string Code, string CodeHint);

/// <summary>
/// Émet une carte cadeau ou un avoir. <paramref name="CustomerId"/> : facultatif, le client à qui l'avoir est rendu
/// (contrôlé dans le tenant). <paramref name="ExpiresAtUtc"/> : facultatif.
/// </summary>
public sealed record IssueStoredValueCardCommand(
    StoredValueKind Kind,
    decimal Amount,
    string? CurrencyCode,
    DateTimeOffset? ExpiresAtUtc = null,
    Guid? CustomerId = null,
    string? Note = null) : ICommand<IssuedStoredValueCardDto>;

public sealed class IssueStoredValueCardHandler(
    LoyaltyWorkspace workspace, IStoredValueCardRepository cards, ILoyaltyCustomers customers, IClock clock)
    : ICommandHandler<IssueStoredValueCardCommand, IssuedStoredValueCardDto>
{
    public async Task<IssuedStoredValueCardDto> HandleAsync(IssueStoredValueCardCommand command, CancellationToken cancellationToken = default)
    {
        var amount = Money.Of(command.Amount, Currency.FromCode(command.CurrencyCode));
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;

        CustomerId? customer = null;
        if (command.CustomerId is { } requested)
        {
            var reference = requested == Guid.Empty ? null : await customers.FindAsync(tenant, CustomerId.From(requested), cancellationToken);
            DomainException.ThrowIf(reference is null || reference.TenantId != tenant || reference.IsAnonymized, LoyaltyApplicationErrors.CustomerNotFound);
            customer = reference!.CustomerId;
        }

        var code = StoredValueCodes.Generate();
        var normalized = StoredValueCodes.Normalize(code)!;
        var card = StoredValueCard.Issue(
            tenant, command.Kind, StoredValueCodes.Hash(normalized), StoredValueCodes.Hint(normalized), amount, clock, customer, command.ExpiresAtUtc, command.Note);
        cards.Add(card);
        await workspace.SaveAsync(cancellationToken);
        return new IssuedStoredValueCardDto(card.Id.Value, code, card.CodeHint);
    }
}

public sealed record CancelStoredValueCardCommand(Guid CardId, string? Reason) : ICommand;

public sealed class CancelStoredValueCardHandler(LoyaltyWorkspace workspace, IStoredValueCardRepository cards, IClock clock)
    : ICommandHandler<CancelStoredValueCardCommand>
{
    public async Task HandleAsync(CancelStoredValueCardCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var card = await StoredValueLoading.LoadAsync(workspace, cards, command.CardId, cancellationToken);
        card.Cancel(command.Reason ?? string.Empty, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Le résultat d'une utilisation : jamais le code, seulement ses 4 derniers caractères.</summary>
public sealed record StoredValueRedemptionDto(Guid CardId, string CodeHint, decimal BalanceAfter, string CurrencyCode);

/// <summary>Débite la carte de ce code (paiement d'une commande). Rejeu de la même référence : sans effet.</summary>
public sealed record RedeemStoredValueCommand(string? Code, decimal Amount, string? CurrencyCode, string Reference) : ICommand<StoredValueRedemptionDto>;

public sealed class RedeemStoredValueHandler(LoyaltyWorkspace workspace, IStoredValueCardRepository cards, IClock clock)
    : ICommandHandler<RedeemStoredValueCommand, StoredValueRedemptionDto>
{
    public async Task<StoredValueRedemptionDto> HandleAsync(RedeemStoredValueCommand command, CancellationToken cancellationToken = default)
    {
        var amount = Money.Of(command.Amount, Currency.FromCode(command.CurrencyCode));
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var card = await StoredValueLoading.FindByCodeAsync(workspace, cards, command.Code, cancellationToken)
            ?? throw new NotFoundException(StoredValueApplicationErrors.CardNotFound);
        var movement = card.Redeem(amount, command.Reference, clock);
        await workspace.SaveAsync(cancellationToken);
        return new StoredValueRedemptionDto(card.Id.Value, card.CodeHint, movement.BalanceAfter, card.Currency.Code);
    }
}

/// <summary>Recrédite la carte (remboursement d'un paiement fait avec elle). Rejeu de la même référence : sans effet.</summary>
public sealed record CreditStoredValueCommand(Guid CardId, decimal Amount, string? CurrencyCode, string Reference) : ICommand;

public sealed class CreditStoredValueHandler(LoyaltyWorkspace workspace, IStoredValueCardRepository cards, IClock clock)
    : ICommandHandler<CreditStoredValueCommand>
{
    public async Task HandleAsync(CreditStoredValueCommand command, CancellationToken cancellationToken = default)
    {
        var amount = Money.Of(command.Amount, Currency.FromCode(command.CurrencyCode));
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var card = await StoredValueLoading.LoadAsync(workspace, cards, command.CardId, cancellationToken);
        card.Credit(amount, command.Reference, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Annule le mouvement de cette référence (compensation quand l'encaissement échoue après l'utilisation). Pas de
/// contrôle « tenant accepte les écritures » : une compensation doit toujours pouvoir passer.
/// </summary>
public sealed record ReverseStoredValueMovementCommand(Guid CardId, string Reference) : ICommand;

public sealed class ReverseStoredValueMovementHandler(LoyaltyWorkspace workspace, IStoredValueCardRepository cards, IClock clock)
    : ICommandHandler<ReverseStoredValueMovementCommand>
{
    public async Task HandleAsync(ReverseStoredValueMovementCommand command, CancellationToken cancellationToken = default)
    {
        var card = await StoredValueLoading.LoadAsync(workspace, cards, command.CardId, cancellationToken);
        card.Reverse(command.Reference, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

internal static class StoredValueLoading
{
    public static async Task<StoredValueCard> LoadAsync(
        LoyaltyWorkspace workspace, IStoredValueCardRepository cards, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await cards.FindAsync(workspace.CurrentTenant, StoredValueCardId.From(id), cancellationToken))
        ?? throw new NotFoundException(StoredValueApplicationErrors.CardNotFound);

    public static async Task<StoredValueCard?> FindByCodeAsync(
        LoyaltyWorkspace workspace, IStoredValueCardRepository cards, string? code, CancellationToken cancellationToken) =>
        StoredValueCodes.Normalize(code) is { } normalized
            ? await cards.FindByCodeHashAsync(workspace.CurrentTenant, StoredValueCodes.Hash(normalized), cancellationToken)
            : null;
}
