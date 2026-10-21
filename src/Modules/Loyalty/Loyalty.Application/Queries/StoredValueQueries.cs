using Loyalty.Application.Abstractions;
using Loyalty.Application.Commands;
using Loyalty.Domain;

namespace Loyalty.Application.Queries;

public sealed record StoredValueMovementDto(
    Guid Id, StoredValueMovementType Type, decimal Amount, decimal BalanceAfter, Guid? ReversedMovementId, DateTimeOffset OccurredAtUtc);

/// <summary>Une carte, sans son code (seulement ses 4 derniers caractères). <paramref name="IsUsable"/> : active, non expirée, avec un solde.</summary>
public sealed record StoredValueCardDto(
    Guid Id,
    StoredValueKind Kind,
    string CodeHint,
    decimal InitialAmount,
    decimal Balance,
    string CurrencyCode,
    StoredValueStatus Status,
    bool IsExpired,
    bool IsUsable,
    DateTimeOffset? ExpiresAtUtc,
    Guid? CustomerId,
    string? Note,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    string? CancellationReason,
    IReadOnlyList<StoredValueMovementDto> Movements)
{
    public static StoredValueCardDto From(StoredValueCard card, DateTimeOffset nowUtc, bool withMovements)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new(
            card.Id.Value, card.Kind, card.CodeHint, card.InitialAmount, card.BalanceAmount, card.Currency.Code, card.Status,
            card.IsExpiredAt(nowUtc), card.IsUsableAt(nowUtc), card.ExpiresAtUtc, card.CustomerId?.Value, card.Note, card.IssuedAtUtc,
            card.CancelledAtUtc, card.CancellationReason,
            withMovements
                ? card.Movements.OrderByDescending(movement => movement.OccurredAtUtc)
                    .Select(movement => new StoredValueMovementDto(
                        movement.Id, movement.Type, movement.Amount, movement.BalanceAfter, movement.ReversedMovementId, movement.OccurredAtUtc))
                    .ToList()
                : []);
    }
}

public sealed record GetStoredValueCardQuery(Guid CardId) : IQuery<StoredValueCardDto>;

public sealed class GetStoredValueCardHandler(LoyaltyWorkspace workspace, IStoredValueCardRepository cards, IClock clock)
    : IQueryHandler<GetStoredValueCardQuery, StoredValueCardDto>
{
    public async Task<StoredValueCardDto> HandleAsync(GetStoredValueCardQuery query, CancellationToken cancellationToken = default) =>
        StoredValueCardDto.From(await StoredValueLoading.LoadAsync(workspace, cards, query.CardId, cancellationToken), clock.UtcNow, withMovements: true);
}

/// <summary>La carte de ce code (solde avant d'encaisser). Code inconnu ou mal saisi : 404.</summary>
public sealed record LookupStoredValueCardQuery(string? Code) : IQuery<StoredValueCardDto>;

public sealed class LookupStoredValueCardHandler(LoyaltyWorkspace workspace, IStoredValueCardRepository cards, IClock clock)
    : IQueryHandler<LookupStoredValueCardQuery, StoredValueCardDto>
{
    public async Task<StoredValueCardDto> HandleAsync(LookupStoredValueCardQuery query, CancellationToken cancellationToken = default)
    {
        var card = await StoredValueLoading.FindByCodeAsync(workspace, cards, query.Code, cancellationToken)
            ?? throw new NotFoundException(StoredValueApplicationErrors.CardNotFound);
        return StoredValueCardDto.From(card, clock.UtcNow, withMovements: true);
    }
}

public sealed record ListStoredValueCardsQuery(StoredValueKind? Kind = null, Guid? CustomerId = null, int Skip = 0, int Take = 50)
    : IQuery<IReadOnlyList<StoredValueCardDto>>;

public sealed class ListStoredValueCardsHandler(LoyaltyWorkspace workspace, IStoredValueCardRepository cards, IClock clock)
    : IQueryHandler<ListStoredValueCardsQuery, IReadOnlyList<StoredValueCardDto>>
{
    public async Task<IReadOnlyList<StoredValueCardDto>> HandleAsync(ListStoredValueCardsQuery query, CancellationToken cancellationToken = default)
    {
        LoyaltyWorkspace.EnsurePage(query.Skip, query.Take);
        var customer = query.CustomerId is { } id && id != Guid.Empty ? CustomerId.From(id) : (CustomerId?)null;
        var now = clock.UtcNow;
        return (await cards.ListAsync(workspace.CurrentTenant, query.Kind, customer, query.Skip, query.Take, cancellationToken))
            .Select(card => StoredValueCardDto.From(card, now, withMovements: false))
            .ToList();
    }
}
