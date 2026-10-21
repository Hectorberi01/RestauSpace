using Loyalty.Application;
using Loyalty.Application.Abstractions;
using Loyalty.Domain;
using Loyalty.Infrastructure.Persistence.Configurations;
using Npgsql;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Loyalty.Infrastructure.Persistence;

/// <summary>
/// Unit of work du module : le compte, son solde et les écritures ajoutées partent dans la même transaction. Le
/// registre est en insertion seule : toute modification ou suppression d'une écriture est refusée AVANT d'écrire.
/// </summary>
internal sealed class LoyaltyUnitOfWork(LoyaltyDbContext context) : OutboxUnitOfWork<LoyaltyDbContext>(context), ILoyaltyUnitOfWork
{
    protected override DomainError ConcurrentUpdate => LoyaltyApplicationErrors.ConcurrentUpdate;

    protected override void BeforeSave()
    {
        var altered = Context.ChangeTracker.Entries<LoyaltyTransaction>()
            .FirstOrDefault(entry => entry.State is EntityState.Modified or EntityState.Deleted);
        if (altered is not null)
        {
            throw new InvalidOperationException(
                "The loyalty ledger is append-only: entry " + altered.Entity.Id + " cannot be " + altered.State + ".");
        }
    }

    /// <summary>Les index uniques, gardes finales derrière les réponses du port lues avant d'écrire.</summary>
    protected override Exception? Translate(DbUpdateException exception) =>
        (exception.InnerException as PostgresException) switch
        {
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: LoyaltyAccountConfiguration.TenantCustomerIndex } =>
                new DomainException(LoyaltyAccountErrors.AlreadyExists),
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: LoyaltyTransactionConfiguration.ReferenceIndex } =>
                new DomainException(LoyaltyAccountErrors.DuplicateTransaction),
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: LoyaltyTransactionConfiguration.ReversalIndex } =>
                new DomainException(LoyaltyAccountErrors.AlreadyReversed),
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: StoredValueCardConfiguration.CodeHashIndex } =>
                new DomainException(StoredValueErrors.CodeInvalid),
            _ => null,
        };
}
