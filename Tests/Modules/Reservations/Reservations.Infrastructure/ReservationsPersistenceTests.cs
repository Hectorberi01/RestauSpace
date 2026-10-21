using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reservations.Application.Abstractions;
using Reservations.Domain;
using Reservations.Infrastructure.Persistence;

namespace Reservations.Infrastructure;

public sealed class ReservationsPersistenceTests(ReservationsDatabase database) : IClassFixture<ReservationsDatabase>
{
    private IClock Clock => database.Clock;

    /// <summary>Le lendemain de l'horloge de test, 20:00 UTC.</summary>
    private DateTimeOffset Tomorrow8Pm => new(database.Clock.UtcNow.Date.AddDays(1).AddHours(20), TimeSpan.Zero);

    private Reservation NewReservation(TenantId tenantId, LocationId locationId, int partySize = 4, DateTimeOffset? startAt = null)
    {
        var slot = ReservationPolicy.Default.SlotFor(startAt ?? Tomorrow8Pm);
        var availability = ReservationAvailabilityPolicy.Evaluate(locationId, slot, partySize, 100, []);
        return Reservation.Create(
            tenantId,
            new LocationReference(tenantId, locationId, IsOperational: true),
            customer: null,
            GuestContact.Create("Awa Dossou", PhoneNumber.Create("+22990000000"), Email.Create("awa@example.com")),
            partySize,
            slot,
            "Anniversaire",
            ReservationPolicy.Default,
            availability,
            Clock);
    }

    private async Task<Reservation> SaveAsync(Reservation reservation)
    {
        await using var scope = database.NewScope(reservation.TenantId);
        scope.ServiceProvider.GetRequiredService<IReservationRepository>().Add(reservation);
        await scope.ServiceProvider.GetRequiredService<IReservationsUnitOfWork>().SaveChangesAsync();
        return reservation;
    }

    private async Task<T> InScopeAsync<T>(TenantId? tenantId, Func<IServiceProvider, Task<T>> read)
    {
        await using var scope = database.NewScope(tenantId);
        return await read(scope.ServiceProvider);
    }

    [Fact]
    public async Task A_reservation_comes_back_whole_and_anonymizing_clears_the_contact()
    {
        var tenant = TenantId.New();
        var reservation = await SaveAsync(NewReservation(tenant, LocationId.New()));

        var loaded = await InScopeAsync(tenant, services => services.GetRequiredService<IReservationRepository>().FindAsync(tenant, reservation.Id));

        Assert.NotNull(loaded);
        Assert.Equal(reservation.GuestContact, loaded.GuestContact);
        Assert.Equal(reservation.Slot, loaded.Slot);
        Assert.Equal(ReservationStatus.Pending, loaded.Status);
        Assert.Equal("Anniversaire", loaded.Notes);
        Assert.Null(loaded.CustomerId);
        Assert.Null(loaded.TableSessionId);

        var session = TableSessionId.New();
        await using (var scope = database.NewScope(tenant))
        {
            var toChange = await scope.ServiceProvider.GetRequiredService<IReservationRepository>().FindAsync(tenant, reservation.Id);
            toChange!.Confirm(Clock);
            toChange.Seat(session, Clock);
            toChange.AnonymizeGuest(Clock);
            await scope.ServiceProvider.GetRequiredService<IReservationsUnitOfWork>().SaveChangesAsync();
        }

        var reloaded = await InScopeAsync(tenant, services => services.GetRequiredService<IReservationRepository>().FindAsync(tenant, reservation.Id));
        Assert.True(reloaded!.IsGuestAnonymized);
        Assert.Equal(GuestContact.AnonymizedName, reloaded.GuestContact.Name);
        Assert.Null(reloaded.Notes);
        Assert.Equal(session, reloaded.TableSessionId);
        Assert.Equal(ReservationStatus.Seated, reloaded.Status);

        var types = await InScopeAsync(tenant, services => services.GetRequiredService<ReservationsDbContext>().OutboxMessages
            .Select(message => message.Type)
            .ToListAsync());
        Assert.Contains(nameof(ReservationCreated), types);
        Assert.Contains(nameof(GuestSeated), types);
        Assert.Contains(nameof(ReservationGuestAnonymized), types);
    }

    [Fact]
    public async Task A_reservation_of_another_tenant_cannot_be_loaded_even_with_its_id()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var reservation = await SaveAsync(NewReservation(tenantA, LocationId.New()));

        // Le filtre du repository ET le query filter refusent.
        Assert.Null(await InScopeAsync(tenantB, services => services.GetRequiredService<IReservationRepository>().FindAsync(tenantA, reservation.Id)));
        Assert.Null(await InScopeAsync(tenantB, services => services.GetRequiredService<IReservationRepository>().FindAsync(tenantB, reservation.Id)));
        // Sans restaurant sélectionné, rien n'est visible.
        Assert.Empty(await InScopeAsync(null, services => services.GetRequiredService<ReservationsDbContext>().Reservations.ToListAsync()));
    }

    [Fact]
    public async Task Only_reservations_holding_capacity_near_the_slot_are_read_for_a_decision()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();
        var holding = await SaveAsync(NewReservation(tenant, location));
        var cancelled = NewReservation(tenant, location);
        cancelled.Cancel(ReservationActor.Staff, null, ReservationPolicy.Default, Clock);
        await SaveAsync(cancelled);
        await SaveAsync(NewReservation(tenant, location, startAt: Tomorrow8Pm.AddDays(3)));
        await SaveAsync(NewReservation(tenant, LocationId.New()));

        var window = Period.Create(Tomorrow8Pm.AddHours(-24), Tomorrow8Pm.AddHours(2));
        var read = await InScopeAsync(
            tenant, services => services.GetRequiredService<IReservationRepository>().ListHoldingCapacityAsync(tenant, location, window));
        var agenda = await InScopeAsync(
            tenant, services => services.GetRequiredService<IReservationRepository>().ListAsync(tenant, location, window, null));

        Assert.Equal(holding.Id, Assert.Single(read).Id);
        Assert.Equal(2, agenda.Count);
    }

    [Fact]
    public async Task The_policy_of_a_location_is_stored_and_the_default_answers_otherwise()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();
        var custom = ReservationPolicy.Create(
            TimeSpan.Zero, TimeSpan.FromDays(30), TimeSpan.FromMinutes(90), TimeSpan.FromHours(1),
            TimeSpan.FromMinutes(30), TimeSpan.FromHours(3), maxPartySize: 12);

        Assert.Equal(ReservationPolicy.Default, await InScopeAsync(
            tenant, services => services.GetRequiredService<IReservationPolicyStore>().GetAsync(tenant, location)));
        for (var round = 0; round < 2; round++)
        {
            // Deux écritures : la seconde met à jour la ligne existante.
            await using var scope = database.NewScope(tenant);
            await scope.ServiceProvider.GetRequiredService<IReservationPolicyStore>().SetAsync(tenant, location, custom);
            await scope.ServiceProvider.GetRequiredService<IReservationsUnitOfWork>().SaveChangesAsync();
        }

        Assert.Equal(custom, await InScopeAsync(
            tenant, services => services.GetRequiredService<IReservationPolicyStore>().GetAsync(tenant, location)));
        Assert.Equal(ReservationPolicy.Default, await InScopeAsync(
            TenantId.New(), services => services.GetRequiredService<IReservationPolicyStore>().GetAsync(tenant, location)));
    }

    [Fact]
    public async Task The_capacity_lock_makes_the_second_request_wait_for_the_first_to_commit()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();

        await using var first = database.NewScope(tenant);
        await using var second = database.NewScope(tenant);
        await first.ServiceProvider.GetRequiredService<IReservationCapacityLock>().AcquireAsync(tenant, location);

        var waiting = second.ServiceProvider.GetRequiredService<IReservationCapacityLock>().AcquireAsync(tenant, location);
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.False(waiting.IsCompleted);

        // Un autre établissement n'attend pas.
        await using (var other = database.NewScope(tenant))
        {
            await other.ServiceProvider.GetRequiredService<IReservationCapacityLock>().AcquireAsync(tenant, LocationId.New());
        }

        first.ServiceProvider.GetRequiredService<IReservationRepository>().Add(NewReservation(tenant, location));
        await first.ServiceProvider.GetRequiredService<IReservationsUnitOfWork>().SaveChangesAsync();
        await waiting.WaitAsync(TimeSpan.FromSeconds(10));

        // Après le verrou, la seconde voit la réservation de la première.
        var window = Period.Create(Tomorrow8Pm.AddHours(-24), Tomorrow8Pm.AddHours(2));
        Assert.Single(await second.ServiceProvider.GetRequiredService<IReservationRepository>().ListHoldingCapacityAsync(tenant, location, window));
    }
}
