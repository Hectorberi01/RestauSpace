using Billing.Application;
using Billing.Application.Abstractions;
using Billing.Domain;
using Billing.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Billing.Infrastructure.Persistence;

/// <summary>
/// Unit of work du module. Un paiement d'abonnement nouveau reçoit son numéro de facture dans la transaction qui
/// l'enregistre : la ligne de l'année dans <c>invoice_sequences</c> est incrémentée et verrouillée jusqu'au commit, ce qui
/// sérialise les numérotations et garde la suite sans trou (un échec annule aussi l'incrément).
/// </summary>
internal sealed class BillingUnitOfWork(BillingDbContext context) : IBillingUnitOfWork
{
    private readonly BillingDbContext _context = context;
    private readonly Outbox _outbox = new(context);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var unnumbered = _context.ChangeTracker.Entries<SubscriptionPayment>()
            .Where(entry => entry.State == EntityState.Added && entry.Entity.InvoiceNumber is null)
            .Select(entry => entry.Entity)
            .OrderBy(payment => payment.RecordedAtUtc)
            .ToList();
        if (unnumbered.Count == 0 || _context.Database.CurrentTransaction is not null)
        {
            await NumberAsync(unnumbered, cancellationToken);
            await _outbox.SaveChangesAsync(cancellationToken);
            return;
        }

        await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await NumberAsync(unnumbered, cancellationToken);
        await _outbox.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task NumberAsync(IReadOnlyList<SubscriptionPayment> payments, CancellationToken cancellationToken)
    {
        foreach (var payment in payments)
        {
            var year = payment.RecordedAtUtc.UtcDateTime.Year;
            var rank = await _context.Database
                .SqlQuery<long>($"INSERT INTO billing.invoice_sequences (year, last_number) VALUES ({year}, 1) ON CONFLICT (year) DO UPDATE SET last_number = invoice_sequences.last_number + 1 RETURNING last_number AS \"Value\"")
                .ToListAsync(cancellationToken);
            payment.AssignInvoiceNumber(SubscriptionPayment.FormatInvoiceNumber(year, rank.Single()));
        }
    }

    private sealed class Outbox(BillingDbContext outboxContext) : OutboxUnitOfWork<BillingDbContext>(outboxContext)
    {
        protected override DomainError ConcurrentUpdate => BillingApplicationErrors.ConcurrentUpdate;

        /// <summary>
        /// Changer un droit n'écrit que la ligne du droit. Le plan est aussi réécrit, pour que sa version protège la décision
        /// prise d'après le plan tel que chargé (deux changements simultanés du même plan).
        /// </summary>
        protected override void BeforeSave()
        {
            foreach (var plan in Context.ChangeTracker.Entries<Plan>().ToList())
            {
                var entitlementsChanged = Context.ChangeTracker.Entries<Entitlement>()
                    .Any(entry => entry.State is EntityState.Added or EntityState.Deleted or EntityState.Modified
                        && Equals(entry.Property(PlanConfiguration.EntitlementOwnerProperty).CurrentValue
                            ?? entry.Property(PlanConfiguration.EntitlementOwnerProperty).OriginalValue, plan.Entity.Id));
                if (plan.State == EntityState.Unchanged && entitlementsChanged)
                {
                    plan.Property(candidate => candidate.Name).IsModified = true;
                }
            }
        }

        /// <summary>Les index uniques, gardes finales derrière les contrôles faits avant d'écrire.</summary>
        protected override Exception? Translate(DbUpdateException exception) =>
            (exception.InnerException as PostgresException) switch
            {
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: SubscriptionConfiguration.LiveIndex } =>
                    new DomainException(SubscriptionErrors.AlreadySubscribed),
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: UsageCounterConfiguration.TenantFeatureIndex } =>
                    new ConcurrencyException(BillingApplicationErrors.ConcurrentUpdate),
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: TenantAddOnConfiguration.ActiveIndex } =>
                    new DomainException(TenantAddOnErrors.AlreadyActive),
                _ => null,
            };
    }
}
