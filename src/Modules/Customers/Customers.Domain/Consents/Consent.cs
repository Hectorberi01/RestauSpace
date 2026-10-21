namespace Customers.Domain;

/// <summary>
/// Preuve légale du consentement marketing d'un client sur un canal. Vit hors de <see cref="Customer"/>
/// pour survivre aux modifications du profil et pouvoir être audité seul.
/// </summary>
public sealed class Consent : AggregateRoot<ConsentId>, ITenantOwned
{
    private readonly List<ConsentChange> _history = [];

    private Consent(ConsentId id, TenantId tenantId, CustomerId customerId, CommunicationChannel channel) : base(id)
    {
        TenantId = tenantId;
        CustomerId = customerId;
        Channel = channel;
    }

    // Réservé à l'ORM.
    private Consent()
    {
    }

    public TenantId TenantId { get; private set; }

    public CustomerId CustomerId { get; private set; }

    public CommunicationChannel Channel { get; private set; }

    public ConsentStatus Status { get; private set; }

    /// <summary>Date du dernier octroi.</summary>
    public DateTimeOffset GrantedAtUtc { get; private set; }

    /// <summary>Source du dernier octroi.</summary>
    public ConsentSource Source { get; private set; } = null!;

    public DateTimeOffset? WithdrawnAtUtc { get; private set; }

    public ConsentSource? WithdrawalSource { get; private set; }

    public WithdrawalReason? WithdrawalReason { get; private set; }

    /// <summary>Chaque octroi et chaque retrait, du plus ancien au plus récent. Ajout seul.</summary>
    public IReadOnlyCollection<ConsentChange> History => _history.AsReadOnly();

    public bool IsGranted => Status == ConsentStatus.Granted;

    /// <summary>Premier octroi pour un couple (client, canal) : crée l'agrégat.</summary>
    public static Consent Grant(
        CustomerReference customer,
        CommunicationChannel channel,
        ConsentSource source,
        bool isUniqueForCustomerAndChannel,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(clock);

        DomainException.ThrowIf(!Enum.IsDefined(channel), ConsentErrors.ChannelNotSupported);
        DomainException.ThrowIf(customer.IsAnonymized, ConsentErrors.CustomerAnonymized);
        DomainException.ThrowIf(!isUniqueForCustomerAndChannel, ConsentErrors.NotUnique);

        var consent = new Consent(ConsentId.New(), customer.TenantId, customer.CustomerId, channel);
        consent.ApplyGrant(source, clock.UtcNow);

        return consent;
    }

    /// <summary>Octroie de nouveau un consentement retiré. Le retrait précédent reste dans <see cref="History"/>.</summary>
    public void Regrant(CustomerReference customer, ConsentSource source, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(clock);

        this.EnsureSameTenant(customer.TenantId);
        DomainException.ThrowIf(customer.CustomerId != CustomerId, ConsentErrors.CustomerMismatch);
        DomainException.ThrowIf(customer.IsAnonymized, ConsentErrors.CustomerAnonymized);
        DomainException.ThrowIf(IsGranted, ConsentErrors.AlreadyGranted);

        ApplyGrant(source, clock.UtcNow);
    }

    /// <summary>Toujours possible s'il est octroyé, même client anonymisé : un retrait ne doit JAMAIS être bloqué.</summary>
    public void Withdraw(ConsentSource source, WithdrawalReason reason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(clock);

        DomainException.ThrowIf(!Enum.IsDefined(reason), ConsentErrors.ReasonInvalid);
        DomainException.ThrowIf(!IsGranted, ConsentErrors.AlreadyWithdrawn);

        ApplyWithdrawal(source, reason, clock.UtcNow);
    }

    /// <summary>
    /// Le consentement suit l'anonymisation de son client : retrait s'il est encore octroyé, rien (et aucun
    /// événement) s'il est déjà retiré, pour pouvoir être rejoué sans risque. Rien d'autre à effacer : sources
    /// et motifs sont des codes, l'agrégat ne contient aucun texte libre.
    /// </summary>
    public void WithdrawBecauseCustomerAnonymized(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (!IsGranted)
        {
            return;
        }

        ApplyWithdrawal(ConsentSource.CustomerAnonymization, Customers.Domain.WithdrawalReason.CustomerAnonymized, clock.UtcNow);
    }

    private void ApplyWithdrawal(ConsentSource source, WithdrawalReason reason, DateTimeOffset now)
    {
        Status = ConsentStatus.Withdrawn;
        WithdrawnAtUtc = now;
        WithdrawalSource = source;
        WithdrawalReason = reason;
        _history.Add(new ConsentChange(ConsentStatus.Withdrawn, now, source, reason));

        Raise(new ConsentWithdrawn(TenantId, Id, CustomerId, Channel, source.Value, reason, now));
    }

    private void ApplyGrant(ConsentSource source, DateTimeOffset now)
    {
        Status = ConsentStatus.Granted;
        GrantedAtUtc = now;
        Source = source;
        WithdrawnAtUtc = null;
        WithdrawalSource = null;
        WithdrawalReason = null;
        _history.Add(new ConsentChange(ConsentStatus.Granted, now, source, reason: null));

        Raise(new ConsentGranted(TenantId, Id, CustomerId, Channel, source.Value, now));
    }
}
