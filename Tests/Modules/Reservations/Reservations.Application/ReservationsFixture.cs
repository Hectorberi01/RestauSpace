using Reservations.Application.Abstractions;
using Reservations.Application.Commands;
using Reservations.Application.Queries;
using Reservations.Domain;

namespace Reservations.Application;

/// <summary>
/// Ports en mémoire du module Reservations. Comme les vrais, ils ne répondent jamais que dans le tenant qui leur est
/// donné. Le tenant de « la requête » est <see cref="CurrentTenantId"/> ; l'horloge démarre le jeudi 2026-01-15 à 12:00 UTC.
/// </summary>
public sealed class ReservationsFixture
    : ITenantContext, IReservationsUnitOfWork, IReservationTenants, IReservationLocations, IReservationFloor, IReservationCapacityLock, IReservationCustomers
{
    public ReservationsFixture()
    {
        TenantA = TenantId.New();
        TenantB = TenantId.New();
        WritableTenants.Add(TenantA);
        WritableTenants.Add(TenantB);
        CurrentTenantId = TenantA;
        Location = GivenLocation();
    }

    public TenantId TenantA { get; }

    public TenantId TenantB { get; }

    public TenantId? CurrentTenantId { get; set; }

    /// <summary>Un établissement actif du tenant A, toujours ouvert, de 10 places.</summary>
    public Guid Location { get; }

    public HashSet<TenantId> WritableTenants { get; } = [];

    public List<LocationReference> Locations { get; } = [];

    /// <summary>Établissements fermés pendant tout créneau demandé.</summary>
    public HashSet<LocationId> Closed { get; } = [];

    /// <summary>Places des tables en service par établissement (module Floor) ; 10 par défaut.</summary>
    public Dictionary<LocationId, int> Seats { get; } = [];

    public List<FloorSession> Sessions { get; } = [];

    public InMemoryReservations Reservations { get; } = new();

    public InMemoryPolicies Policies { get; } = new();

    public FakeClock Clock { get; } = new();

    public int SaveCount { get; private set; }

    public int LocksTaken { get; private set; }

    /// <summary>Demain (vendredi) 20:00 UTC : dans la fenêtre de la politique par défaut.</summary>
    public DateTimeOffset Tomorrow8Pm => new(2026, 1, 16, 20, 0, 0, TimeSpan.Zero);

    public ReservationsWorkspace Workspace => new(this, this, this, Reservations, Waitlist, Policies, this, this, this, Clock);

    public InMemoryWaitlist Waitlist { get; } = new();

    public Currency TenantCurrency { get; set; } = Currency.XOF;

    Task<Currency> IReservationTenants.CurrencyAsync(TenantId tenantId, CancellationToken cancellationToken) => Task.FromResult(TenantCurrency);

    public SetDepositRuleHandler SetDepositRule => new(Workspace);

    public RecordDepositSettlementHandler RecordDeposit => new(Workspace);

    public ExpireUnpaidDepositHandler ExpireDeposit => new(Workspace);

    public WaiveDepositHandler WaiveDeposit => new(Workspace);

    public SetWaitlistSettingsHandler SetWaitlistSettings => new(Workspace);

    public JoinWaitlistHandler JoinWaitlist => new(Workspace, this);

    public OfferFreedCapacityHandler OfferFreed => new(Workspace);

    public AcceptWaitlistOfferHandler AcceptOffer => new(Workspace, this);

    public ResolveWaitlistEntryHandler ResolveWaitlist => new(Workspace);

    public CreateReservationHandler Create => new(Workspace, this, Clock);

    public AnonymizeCustomerReservationsHandler AnonymizeCustomer => new(Workspace, Clock);

    /// <summary>Fiches du faux module Customers : (tenant, anonymisée ?).</summary>
    public Dictionary<CustomerId, (TenantId Tenant, bool IsAnonymized)> Customers { get; } = [];

    public Guid GivenCustomer(bool isAnonymized = false, TenantId? tenantId = null)
    {
        var id = CustomerId.New();
        Customers[id] = (tenantId ?? TenantA, isAnonymized);
        return id.Value;
    }

    Task<CustomerReference?> IReservationCustomers.FindAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken) =>
        Task.FromResult<CustomerReference?>(Customers.TryGetValue(customerId, out var customer) && customer.Tenant == tenantId
            ? new CustomerReference(tenantId, customerId, customer.IsAnonymized)
            : null);

    public ConfirmReservationHandler Confirm => new(Workspace, Clock);

    public RescheduleReservationHandler Reschedule => new(Workspace, Clock);

    public ChangePartySizeHandler ChangePartySize => new(Workspace, Clock);

    public SeatReservationHandler Seat => new(Workspace, Clock);

    public CompleteReservationHandler Complete => new(Workspace, Clock);

    public CancelReservationHandler Cancel => new(Workspace, Clock);

    public ChangeReservationCustomerHandler ChangeCustomer => new(Workspace, this, Clock);

    public MarkReservationNoShowHandler NoShow => new(Workspace, Clock);

    public SetReservationPolicyHandler SetPolicy => new(Workspace);

    public GetReservationByIdHandler GetById => new(Workspace);

    public ListReservationsHandler List => new(Workspace);

    public ListCustomerReservationsHandler ListForCustomer => new(Workspace);

    public GetAvailabilityHandler Availability => new(Workspace);

    public GetReservationPolicyHandler GetPolicy => new(Workspace);

    public GetOnlineBookingRulesHandler OnlineRules => new(Workspace);

    public CheckOnlineSlotsHandler CheckSlots => new(Workspace, Clock);

    public BookOnlineHandler BookOnline => new(Workspace, this, Clock);

    public GetGuestReservationHandler GuestReservation => new(Workspace, Clock);

    public CancelGuestReservationHandler CancelAsGuest => new(Workspace, Clock);

    public Guid GivenLocation(TenantId? tenantId = null, bool isOperational = true)
    {
        var location = new LocationReference(tenantId ?? TenantA, LocationId.New(), isOperational);
        Locations.Add(location);
        Seats[location.LocationId] = 10;
        return location.LocationId.Value;
    }

    public CreateReservationCommand NewReservation(int partySize = 4, DateTimeOffset? startAt = null, Guid? locationId = null) =>
        new(locationId ?? Location, startAt ?? Tomorrow8Pm, partySize, "Awa Dossou", "+22990000000", Notes: "Anniversaire");

    /// <summary>Réservation créée dans le tenant courant, compteurs remis à zéro.</summary>
    public async Task<Guid> GivenReservationAsync(int partySize = 4, DateTimeOffset? startAt = null, bool confirmed = false)
    {
        var id = await Create.HandleAsync(NewReservation(partySize, startAt));
        if (confirmed)
        {
            await Confirm.HandleAsync(new ConfirmReservationCommand(id));
        }

        SaveCount = 0;
        LocksTaken = 0;
        return id;
    }

    public FloorSession GivenOpenSession(int guests, bool forReservation = false, DateTimeOffset? openedAt = null, Guid? locationId = null)
    {
        var session = new FloorSession(
            TableSessionId.New(), LocationId.From(locationId ?? Location), guests, openedAt ?? Clock.UtcNow, IsOpen: true, forReservation);
        Sessions.Add(session);
        return session;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    Task<bool> IReservationTenants.AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(WritableTenants.Contains(tenantId));

    Task<LocationReference?> IReservationLocations.FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken) =>
        Task.FromResult<LocationReference?>(Locations.SingleOrDefault(location => location.TenantId == tenantId && location.LocationId == locationId));

    Task<bool> IReservationLocations.IsOpenDuringAsync(TenantId tenantId, LocationId locationId, Period slot, CancellationToken cancellationToken) =>
        Task.FromResult(Locations.Any(location => location.TenantId == tenantId && location.LocationId == locationId) && !Closed.Contains(locationId));

    Task<FloorOccupancy> IReservationFloor.GetOccupancyAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken) =>
        Task.FromResult(new FloorOccupancy(
            Seats.GetValueOrDefault(locationId), Sessions.Where(session => session.LocationId == locationId).ToList()));

    Task<FloorSession?> IReservationFloor.FindSessionAsync(TenantId tenantId, TableSessionId sessionId, CancellationToken cancellationToken) =>
        Task.FromResult<FloorSession?>(Sessions.SingleOrDefault(session => session.Id == sessionId));

    Task IReservationCapacityLock.AcquireAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken)
    {
        LocksTaken++;
        return Task.CompletedTask;
    }

    public sealed class InMemoryReservations : IReservationRepository
    {
        public List<Reservation> Items { get; } = [];

        public Reservation Stored(Guid id) => Items.Single(reservation => reservation.Id.Value == id);

        public Task<Reservation?> FindAsync(TenantId tenantId, ReservationId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Reservation?>(Items.SingleOrDefault(reservation => reservation.TenantId == tenantId && reservation.Id == id));

        public Task<IReadOnlyList<Reservation>> ListHoldingCapacityAsync(
            TenantId tenantId, LocationId locationId, Period window, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Reservation>>(Items
                .Where(reservation => reservation.TenantId == tenantId && reservation.LocationId == locationId && reservation.HoldsCapacity)
                .Where(reservation => reservation.StartAtUtc >= window.StartUtc && reservation.StartAtUtc < window.EndUtc)
                .ToList());

        public Task<IReadOnlyList<Reservation>> ListAsync(
            TenantId tenantId, LocationId? locationId, Period window, ReservationStatus? status, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Reservation>>(Items
                .Where(reservation => reservation.TenantId == tenantId && (locationId == null || reservation.LocationId == locationId))
                .Where(reservation => status == null || reservation.Status == status)
                .Where(reservation => reservation.StartAtUtc >= window.StartUtc && reservation.StartAtUtc < window.EndUtc)
                .OrderBy(reservation => reservation.StartAtUtc)
                .ToList());

        public Task<IReadOnlyList<Reservation>> ListForCustomerAsync(
            TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Reservation>>(Items
                .Where(reservation => reservation.TenantId == tenantId && reservation.CustomerId == customerId)
                .ToList());

        public Task<ReservationPage> ListPageForCustomerAsync(
            TenantId tenantId, CustomerId customerId, int skip, int take, CancellationToken cancellationToken = default)
        {
            var matching = Items
                .Where(reservation => reservation.TenantId == tenantId && reservation.CustomerId == customerId)
                .OrderByDescending(reservation => reservation.StartAtUtc)
                .ToList();
            return Task.FromResult(new ReservationPage(matching.Skip(skip).Take(take).ToList(), matching.Count));
        }

        public void Add(Reservation reservation) => Items.Add(reservation);
    }

    public sealed class InMemoryWaitlist : IWaitlistRepository
    {
        public List<WaitlistEntry> Items { get; } = [];

        public WaitlistEntry Stored(Guid id) => Items.Single(entry => entry.Id.Value == id);

        public Task<WaitlistEntry?> FindAsync(TenantId tenantId, WaitlistEntryId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<WaitlistEntry?>(Items.SingleOrDefault(entry => entry.TenantId == tenantId && entry.Id == id));

        public Task<IReadOnlyList<WaitlistEntry>> ListOpenAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WaitlistEntry>>(Items
                .Where(entry => entry.TenantId == tenantId && entry.LocationId == locationId && entry.IsOpen)
                .OrderBy(entry => entry.CreatedAtUtc)
                .ToList());

        public Task<IReadOnlyList<WaitlistEntry>> ListAsync(TenantId tenantId, LocationId? locationId, Period window, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WaitlistEntry>>(Items
                .Where(entry => entry.TenantId == tenantId && (locationId == null || entry.LocationId == locationId))
                .Where(entry => entry.DesiredStartUtc >= window.StartUtc && entry.DesiredStartUtc < window.EndUtc)
                .ToList());

        public Task<IReadOnlyList<WaitlistEntry>> ListForCustomerAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WaitlistEntry>>(Items.Where(entry => entry.TenantId == tenantId && entry.CustomerId == customerId).ToList());

        public void Add(WaitlistEntry entry) => Items.Add(entry);
    }

    public sealed class InMemoryPolicies : IReservationPolicyStore
    {
        public Dictionary<(TenantId, LocationId), ReservationPolicy> Items { get; } = [];

        public Dictionary<(TenantId, LocationId), DepositRule> Deposits { get; } = [];

        public Dictionary<(TenantId, LocationId), WaitlistSettings> WaitlistSettings { get; } = [];

        public Task<DepositRule> GetDepositRuleAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Deposits.GetValueOrDefault((tenantId, locationId)) ?? DepositRule.Off);

        public Task SetDepositRuleAsync(TenantId tenantId, LocationId locationId, DepositRule rule, CancellationToken cancellationToken = default)
        {
            Deposits[(tenantId, locationId)] = rule;
            return Task.CompletedTask;
        }

        public Task<WaitlistSettings> GetWaitlistSettingsAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(WaitlistSettings.GetValueOrDefault((tenantId, locationId)) ?? Domain.WaitlistSettings.Default);

        public Task SetWaitlistSettingsAsync(TenantId tenantId, LocationId locationId, WaitlistSettings settings, CancellationToken cancellationToken = default)
        {
            WaitlistSettings[(tenantId, locationId)] = settings;
            return Task.CompletedTask;
        }

        public Task<ReservationPolicy> GetAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.GetValueOrDefault((tenantId, locationId)) ?? ReservationPolicy.Default);

        public Task SetAsync(TenantId tenantId, LocationId locationId, ReservationPolicy policy, CancellationToken cancellationToken = default)
        {
            Items[(tenantId, locationId)] = policy;
            return Task.CompletedTask;
        }
    }
}
