using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Payments.Application;
using Payments.Application.Abstractions;
using Payments.Domain;
using Payments.Infrastructure.Persistence.Configurations;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Payments.Infrastructure.Persistence;

/// <summary>
/// Unit of work du module, et verrou des créations de paiement par commande. <see cref="LockAsync"/> ouvre une
/// transaction et y prend un verrou consultatif PostgreSQL sur la commande ; <see cref="SaveChangesAsync"/> écrit puis
/// valide cette transaction, ce qui libère le verrou. Une requête qui échoue avant la sauvegarde l'abandonne (le
/// contexte est libéré en fin de requête) : le verrou tombe avec elle.
/// </summary>
internal sealed class PaymentsUnitOfWork(PaymentsDbContext context) : IPaymentsUnitOfWork, IPaymentOrderLock, ICashSessionLock
{
    // Champs explicites : un paramètre du constructeur principal à la fois capturé et recopié dans un champ est un
    // avertissement (CS9124), donc une erreur ici.
    private readonly PaymentsDbContext _context = context;
    private readonly Outbox _outbox = new(context);
    private IDbContextTransaction? _transaction;

    public async Task LockAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default)
    {
        _transaction ??= await _context.Database.BeginTransactionAsync(cancellationToken);
        var key = $"payments:order:{tenantId.Value:N}:{orderId.Value:N}";
        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }

    public async Task LockAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default)
    {
        _transaction ??= await _context.Database.BeginTransactionAsync(cancellationToken);
        var key = $"payments:cash:{tenantId.Value:N}:{locationId.Value:N}";
        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _outbox.SaveChangesAsync(cancellationToken);
        if (_transaction is { } transaction)
        {
            _transaction = null;
            await transaction.CommitAsync(cancellationToken);
            await transaction.DisposeAsync();
        }
    }

    private sealed class Outbox(PaymentsDbContext outboxContext) : OutboxUnitOfWork<PaymentsDbContext>(outboxContext)
    {
        protected override DomainError ConcurrentUpdate => PaymentsApplicationErrors.ConcurrentUpdate;

        /// <summary>Les index uniques, garde-fous derrière les contrôles faits avant d'écrire (deux requêtes simultanées).</summary>
        protected override Exception? Translate(DbUpdateException exception) =>
            (exception.InnerException as PostgresException) switch
            {
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: PaymentProviderConfiguration.CodeIndex } =>
                    new DomainException(PaymentProviderErrors.CodeNotUnique),
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: RestaurantPaymentConfigurationConfiguration.ProviderIndex } =>
                    new DomainException(PaymentConfigurationErrors.AlreadyExists),
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: CashSessionConfiguration.OpenIndex or CashSessionConfiguration.NumberIndex } =>
                    new DomainException(CashSessionsErrors.AlreadyOpen),
                _ => null,
            };

        /// <summary>
        /// 1. Tentatives, remboursements et affectations aux établissements gardent leur ordre : leur position est
        ///    recopiée avant l'écriture.
        /// 2. Un changement qui ne touche qu'un enfant (remboursement échoué, priorité d'une affectation...) réécrit aussi
        ///    la racine, pour que sa version protège la décision prise d'après l'agrégat tel que chargé.
        /// </summary>
        /// <remarks>
        /// Un acompte pas encore déduit d'une addition ne règle aucune commande : son changement est signalé à sa réservation
        /// (<see cref="DepositSettlementChanged"/>). Déduit, il règle la commande comme tout paiement.
        /// </remarks>
        protected override IEnumerable<IDomainEvent> DerivedEvents(IReadOnlyList<IAggregateRoot> changed) =>
            changed.OfType<Payment>()
                .Select(payment => (payment, last: payment.DomainEvents.LastOrDefault(IsSettlementChange)))
                .Where(change => change.last is not null)
                .Select(change => change.payment.IsUnappliedDeposit
                    ? (IDomainEvent)new DepositSettlementChanged(
                        change.payment.TenantId, change.payment.Id, change.payment.ReservationId!.Value, change.payment.SettledAmount, change.last!.OccurredAtUtc)
                    : new PaymentSettlementChanged(
                        change.payment.TenantId, change.payment.Id, change.payment.OrderId, change.payment.SettledAmount, change.last!.OccurredAtUtc));

        private static bool IsSettlementChange(IDomainEvent domainEvent) =>
            domainEvent is PaymentCaptured or PaymentCapturedAfterCancellation or RefundRequested or RefundRegistered or DepositApplied;

        protected override void BeforeSave()
        {
            foreach (var payment in Context.ChangeTracker.Entries<Payment>().Where(entry => entry.State != EntityState.Deleted).ToList())
            {
                Number(payment.Entity.Attempts, PaymentAttemptConfiguration.PositionProperty);
                Number(payment.Entity.Refunds, RefundConfiguration.PositionProperty);
                Stamp(payment.Entity.Attempts, payment.Entity.TenantId);
                Stamp(payment.Entity.Refunds, payment.Entity.TenantId);
            }

            var owners = Owners(Context.ChangeTracker.Entries<PaymentAttempt>())
                .Concat(Owners(Context.ChangeTracker.Entries<Refund>()))
                .ToHashSet();

            // Matérialisé : marquer un paiement modifié pendant le parcours du change tracker le modifierait.
            foreach (var payment in Context.ChangeTracker.Entries<Payment>().ToList())
            {
                if (payment.State == EntityState.Unchanged && owners.Contains(payment.Entity.Id))
                {
                    payment.Property(candidate => candidate.Status).IsModified = true;
                }
            }

            foreach (var configuration in Context.ChangeTracker.Entries<RestaurantPaymentConfiguration>()
                .Where(entry => entry.State != EntityState.Deleted)
                .ToList())
            {
                Number(configuration.Entity.Assignments, PaymentMethodAssignmentConfiguration.PositionProperty);
                Stamp(configuration.Entity.Assignments, configuration.Entity.TenantId);
            }

            var configurations = Context.ChangeTracker.Entries<PaymentMethodAssignment>()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(entry => entry.Property<RestaurantPaymentConfigurationId>(RestaurantPaymentConfigurationConfiguration.OwnerProperty))
                .Select(owner => owner.EntityEntry.State == EntityState.Deleted ? owner.OriginalValue : owner.CurrentValue)
                .ToHashSet();
            foreach (var configuration in Context.ChangeTracker.Entries<RestaurantPaymentConfiguration>().ToList())
            {
                if (configuration.State == EntityState.Unchanged && configurations.Contains(configuration.Entity.Id))
                {
                    configuration.Property(candidate => candidate.UpdatedAtUtc).IsModified = true;
                }
            }
        }

        private static IEnumerable<PaymentId> Owners<T>(IEnumerable<EntityEntry<T>> children)
            where T : class =>
            children
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(entry => entry.Property<PaymentId>(PaymentConfiguration.OwnerProperty))
                .Select(owner => owner.EntityEntry.State == EntityState.Deleted ? owner.OriginalValue : owner.CurrentValue)
                .ToList();

        /// <summary>3. Chaque enfant porte le tenant de sa racine (colonne <c>tenant_id</c> des tables enfants).</summary>
        private void Stamp<T>(IEnumerable<T> children, TenantId tenantId)
            where T : class
        {
            foreach (var child in children)
            {
                var property = Context.Entry(child).Property<TenantId>(ChildTenant.Property);
                if (property.CurrentValue != tenantId)
                {
                    property.CurrentValue = tenantId;
                }
            }
        }

        private void Number<T>(IEnumerable<T> children, string positionProperty)
            where T : class
        {
            var position = 0;
            foreach (var child in children)
            {
                var property = Context.Entry(child).Property<int>(positionProperty);
                if (property.CurrentValue != position)
                {
                    property.CurrentValue = position;
                }

                position++;
            }
        }
    }
}
