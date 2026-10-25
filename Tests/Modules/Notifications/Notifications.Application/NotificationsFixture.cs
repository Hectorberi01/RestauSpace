using System.Security.Cryptography;
using System.Text;
using Notifications.Application.Abstractions;
using Notifications.Application.Commands;
using Notifications.Application.Queries;
using Notifications.Application.Sending;
using Notifications.Domain;

namespace Notifications.Application;

/// <summary>
/// Ports en mémoire du module Notifications. Comme les vrais, ils ne répondent jamais que dans le tenant qui leur est
/// donné. Le tenant de « la requête » est <see cref="CurrentTenantId"/> ; un prestataire « TESTMAIL » (email) et
/// « TESTSMS » (SMS, WhatsApp) existent dans le code, leurs envois sont notés dans <see cref="Sent"/>.
/// </summary>
public sealed class NotificationsFixture
    : ITenantContext, INotificationTenants, INotificationProviderRepository, IRestaurantNotificationConfigurationRepository,
      INotificationTemplateRepository, INotificationPreferenceRepository, INotificationRepository, INotificationDirectory,
      INotificationSecretStore, INotificationInbox, INotificationUsage, INotificationEntitlementChecker, INotificationLocations,
      INotificationContacts, INotificationConsents, INotificationsCurrentUser, IDeviceRegistrationRepository, IOtpChallengeRepository,
      IOtpHasher, INotificationsUnitOfWork, INotificationSenderResolver, IUnsubscribeLinks, IPlanFeatureGate
{
    public static readonly NotificationProviderCode TestMail = NotificationProviderCode.Create("TESTMAIL");
    public static readonly NotificationProviderCode TestSms = NotificationProviderCode.Create("TESTSMS");
    public static readonly NotificationProviderCode OtherMail = NotificationProviderCode.Create("OTHERMAIL");
    public static readonly CountryCode Benin = CountryCode.Create("BJ");

    private readonly List<(Notification Notification, string? Key)> _notifications = [];

    public NotificationsFixture()
    {
        TenantA = TenantId.New();
        TenantB = TenantId.New();
        Writable[TenantA] = true;
        Writable[TenantB] = true;
        CurrentTenantId = TenantA;
        Integrations =
        [
            new NotificationIntegration(TestMail, [NotificationChannel.Email], ["api_key"]),
            new NotificationIntegration(OtherMail, [NotificationChannel.Email], ["api_key"]),
            new NotificationIntegration(TestSms, [NotificationChannel.Sms, NotificationChannel.WhatsApp], ["api_key"]),
            new NotificationIntegration(NotificationProviderCode.InApp, [NotificationChannel.InApp], []),
        ];
    }

    public TenantId TenantA { get; }

    public TenantId TenantB { get; }

    public TenantId? CurrentTenantId { get; set; }

    public UserId? UserId { get; set; } = SaasRestaurant.BuildingBlocks.Domain.UserId.New();

    public Dictionary<TenantId, bool> Writable { get; } = [];

    public List<NotificationProvider> ProviderRows { get; } = [];

    public List<RestaurantNotificationConfiguration> ConfigurationRows { get; } = [];

    public List<NotificationTemplate> TemplateRows { get; } = [];

    public List<NotificationPreference> PreferenceRows { get; } = [];

    public IEnumerable<Notification> NotificationRows => _notifications.Select(row => row.Notification);

    public List<DeviceRegistration> DeviceRows { get; } = [];

    public List<OtpChallenge> OtpRows { get; } = [];

    public Dictionary<string, IReadOnlyDictionary<string, string>> Secrets { get; } = [];

    public HashSet<(string Consumer, string EventId)> Inbox { get; } = [];

    public Dictionary<(TenantId, NotificationChannel), long> UsageRows { get; } = [];

    public Dictionary<NotificationChannel, long> Limits { get; } = [];

    public Dictionary<(TenantId, LocationId), LocationReference> Locations { get; } = [];

    public Dictionary<(TenantId, CustomerId), CustomerContact> Customers { get; } = [];

    public Dictionary<(TenantId, CustomerId, CommunicationChannel), bool> Consents { get; } = [];

    /// <summary>Ce que les prestataires de test ont reçu (contenu compris : c'est le « téléphone » du client).</summary>
    public List<NotificationSendRequest> Sent { get; } = [];

    /// <summary>Les réponses des prochains envois (sinon : accepté).</summary>
    public Queue<NotificationSendResult> NextResults { get; } = new();

    /// <summary>Le contenu de chaque notification au moment de chaque sauvegarde : ce qui serait écrit en base.</summary>
    public List<string> SavedBodies { get; } = [];

    public FakeClock Clock { get; } = new();

    public List<Campaign> CampaignRows { get; } = [];

    public List<MarketingRules> RulesRows { get; } = [];

    /// <summary>Tags des clients (audience d'une campagne).</summary>
    public Dictionary<CustomerId, string> CustomerTags { get; } = [];

    /// <summary>Clients ayant commandé dans un établissement (comme Orders, pour cibler une campagne).</summary>
    public HashSet<(LocationId Location, CustomerId Customer)> LocationCustomers { get; } = [];

    public NotificationsSettings Settings { get; } = new() { UnsubscribePageUrl = "https://site.example.com/desinscription" };

    public int SaveCount { get; private set; }

    public IReadOnlyList<NotificationIntegration> Integrations { get; }

    // ---- Composition

    public NotificationsWorkspace Workspace => new(this, this, this, this, this, this, this, this, this);

    /// <summary>Fonctionnalités refusées par le plan (faux gate) : vide = tout est permis.</summary>
    public HashSet<string> DeniedFeatures { get; } = [];

    Task<bool> IPlanFeatureGate.AllowsAsync(TenantId tenantId, string feature, CancellationToken cancellationToken) =>
        Task.FromResult(!DeniedFeatures.Contains(feature));

    public NotificationRouting Routing => new(this, this, this, this);

    public NotificationPolicy Policy => new(this, this, this, this, this, this, Routing, Settings, Clock, new RulesStore(this), new HistoryStore(this), this);

    public NotificationSending Sending => new(Routing, this, this, this, this, this, Clock);

    public NotificationRequests Requests => new(this, this, this, this, Policy, Settings, this);

    public GetUnsubscribeLinkHandler UnsubscribePreview => new(this, this);

    public UnsubscribeHandler Unsubscribe => new(this, this, this, this, Clock);

    public SendNotificationHandler Send => new(Workspace, Requests);

    public TriggerNotificationsHandler Trigger => new(Workspace, Requests, this, Clock);

    public DispatchNotificationHandler Dispatch => new(Workspace, Sending);

    public CreateNotificationConfigurationHandler CreateConfiguration => new(Workspace, Clock);

    public SetConfigurationCredentialsHandler SetCredentials => new(Workspace, new CredentialsCheck(this), this, Clock);

    public EnableNotificationConfigurationHandler EnableConfiguration => new(Workspace, Clock);

    public TestNotificationConfigurationHandler TestConfiguration => new(Workspace, this, Clock);

    public DisableNotificationConfigurationHandler DisableConfiguration => new(Workspace, Clock);

    public CreateTemplateHandler CreateTemplate => new(Workspace, Clock);

    public UpdateTemplateHandler UpdateTemplate => new(Workspace, Clock);

    public ChangeTemplateHandler ChangeTemplate => new(Workspace, Clock);

    public SetNotificationPreferenceHandler SetPreference => new(Workspace, this, this, Clock);

    public SetAlertTypeHandler SetAlertType => new(Workspace, this, Clock);

    public GetNotificationPreferencesHandler GetPreferences => new(Workspace, this, this);

    public RequestOtpHandler RequestOtp => new(this, this, this, this, Policy, Sending, this, Settings, Clock);

    public VerifyOtpHandler VerifyOtp => new(this, this, this, this, Clock);

    public SaveCampaignHandler SaveCampaign => new(Workspace, new CampaignStore(this), this, Clock);

    public ScheduleCampaignHandler ScheduleCampaign => new(Workspace, new CampaignStore(this), Clock);

    public CancelCampaignHandler CancelCampaign => new(Workspace, new CampaignStore(this), Clock);

    public ProcessCampaignHandler ProcessCampaign => new(Workspace, new CampaignStore(this), new AudienceStore(this), this, Requests, Settings, Clock);

    public PreviewCampaignAudienceHandler PreviewAudience => new(Workspace, new AudienceStore(this));

    public SetMarketingRulesHandler SetMarketingRules => new(Workspace, new RulesStore(this), Clock);

    public ListNotificationConfigurationsHandler ListConfigurations => new(Workspace);

    public GetNotificationHandler GetNotification => new(Workspace);

    public ListNotificationsHandler ListNotifications => new(Workspace);

    // ---- Mise en place

    public NotificationProvider Provider(NotificationProviderCode code, NotificationChannel channel, bool enabled = true)
    {
        var provider = ProviderRows.SingleOrDefault(candidate => candidate.Code == code);
        if (provider is null)
        {
            provider = NotificationProvider.Create(
                code, code.Value, channel, NotificationProviderConfigurationType.PlatformOrTenant,
                NotificationProviderCapability.Create(channel, [NotificationFeature.Text]), isCodeUnique: true, Clock);
            ProviderRows.Add(provider);
            if (enabled)
            {
                provider.Enable(Clock);
            }
        }

        return provider;
    }

    /// <summary>Le restaurant utilise ce prestataire avec son propre compte, actif.</summary>
    public RestaurantNotificationConfiguration Configured(TenantId tenant, NotificationProviderCode code, NotificationChannel channel)
    {
        var provider = Provider(code, channel);
        var configuration = RestaurantNotificationConfiguration.Create(tenant, provider, NotificationProviderMode.TenantManaged, isFirstForTenant: true, Clock);
        var reference = NotificationCredentialReference.ForTenant(tenant, code);
        Secrets[reference.Value] = new Dictionary<string, string> { ["api_key"] = "key-" + tenant.Value.ToString("N") };
        configuration.Configure(reference, Clock);
        configuration.Enable(provider, Clock);
        ConfigurationRows.Add(configuration);
        return configuration;
    }

    /// <summary>Le restaurant utilise ce prestataire avec le compte de la PLATEFORME, actif.</summary>
    public RestaurantNotificationConfiguration PlatformConfigured(TenantId tenant, NotificationProviderCode code, NotificationChannel channel)
    {
        var provider = Provider(code, channel);
        var configuration = RestaurantNotificationConfiguration.Create(tenant, provider, NotificationProviderMode.PlatformManaged, isFirstForTenant: true, Clock);
        Secrets[NotificationCredentialReference.ForPlatform(code).Value] = new Dictionary<string, string> { ["api_key"] = "platform-key" };
        configuration.Enable(provider, Clock);
        ConfigurationRows.Add(configuration);
        return configuration;
    }

    public NotificationTemplate Template(TenantId? tenant, NotificationTypeDefinition type, NotificationChannel channel, string body, string? subject = null, string language = "fr")
    {
        var template = tenant is { } owner
            ? NotificationTemplate.Create(owner, type.Key, channel, NotificationLanguage.Create(language), subject, body, true, Clock)
            : NotificationTemplate.CreatePlatformTemplate(type.Key, channel, NotificationLanguage.Create(language), subject, body, true, Clock);
        template.Publish(Clock);
        TemplateRows.Add(template);
        return template;
    }

    public CustomerId Customer(TenantId tenant, string? email = "awa@example.com", string? phone = "+22997000000", string firstName = "Awa")
    {
        var id = CustomerId.New();
        Customers[(tenant, id)] = new CustomerContact(
            id, email is null ? null : Email.Create(email), phone is null ? null : PhoneNumber.Create(phone), firstName, "fr", IsAnonymized: false);
        return id;
    }

    public LocationReference Location(TenantId tenant)
    {
        var location = new LocationReference(tenant, LocationId.New(), Benin);
        Locations[(tenant, location.LocationId)] = location;
        return location;
    }

    public Notification Single() => Assert.Single(NotificationRows);

    // ---- ITenantContext, INotificationTenants, INotificationsCurrentUser

    public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Writable.TryGetValue(tenantId, out var writable) && writable);

    public Task<string?> DisplayNameAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Writable.ContainsKey(tenantId) ? (tenantId == TenantA ? "Chez A" : "Chez B") : null);

    // ---- IUnsubscribeLinks : un jeton n'est valable que s'il a été émis ici (la signature des vrais jetons).

    public Dictionary<string, UnsubscribeClaim> IssuedLinks { get; } = [];

    public string Issue(UnsubscribeClaim claim)
    {
        var token = $"t{IssuedLinks.Count + 1}";
        IssuedLinks[token] = claim;
        return token;
    }

    public UnsubscribeClaim? Read(string? token) => token is not null && IssuedLinks.TryGetValue(token, out var claim) ? claim : null;

    // ---- INotificationProviderRepository

    Task<NotificationProvider?> INotificationProviderRepository.FindAsync(NotificationProviderId id, CancellationToken cancellationToken) =>
        Task.FromResult(ProviderRows.SingleOrDefault(provider => provider.Id == id));

    public Task<NotificationProvider?> FindByCodeAsync(NotificationProviderCode code, CancellationToken cancellationToken = default) =>
        Task.FromResult(ProviderRows.SingleOrDefault(provider => provider.Code == code));

    Task<IReadOnlyList<NotificationProvider>> INotificationProviderRepository.ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NotificationProvider>>(ProviderRows.ToList());

    public void Add(NotificationProvider provider) => ProviderRows.Add(provider);

    // ---- IRestaurantNotificationConfigurationRepository

    public Task<RestaurantNotificationConfiguration?> FindAsync(
        TenantId tenantId, RestaurantNotificationConfigurationId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(ConfigurationRows.SingleOrDefault(configuration => configuration.TenantId == tenantId && configuration.Id == id));

    public Task<RestaurantNotificationConfiguration?> FindByProviderAsync(
        TenantId tenantId, NotificationProviderCode provider, CancellationToken cancellationToken = default) =>
        Task.FromResult(ConfigurationRows.SingleOrDefault(configuration => configuration.TenantId == tenantId && configuration.Provider == provider));

    Task<IReadOnlyList<RestaurantNotificationConfiguration>> IRestaurantNotificationConfigurationRepository.ListAsync(
        TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RestaurantNotificationConfiguration>>(ConfigurationRows.Where(configuration => configuration.TenantId == tenantId).ToList());

    public Task<IReadOnlyList<RestaurantNotificationConfiguration>> ListForChannelAsync(
        TenantId tenantId, NotificationChannel channel, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RestaurantNotificationConfiguration>>(
            ConfigurationRows.Where(configuration => configuration.TenantId == tenantId && configuration.Channel == channel).ToList());

    public void Add(RestaurantNotificationConfiguration configuration) => ConfigurationRows.Add(configuration);

    // ---- INotificationTemplateRepository

    public Task<NotificationTemplate?> FindAsync(TenantId? owner, TemplateId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(TemplateRows.SingleOrDefault(template => template.Id == id && template.TenantId == owner));

    public Task<IReadOnlyList<NotificationTemplate>> ListCandidatesAsync(
        TenantId? tenantId, TemplateKey key, NotificationChannel channel, NotificationLanguage language, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<NotificationTemplate>>(TemplateRows
            .Where(template => (template.TenantId == null || template.TenantId == tenantId)
                && template.Key == key && template.Channel == channel && template.Language == language && template.Status == TemplateStatus.Published)
            .ToList());

    Task<IReadOnlyList<NotificationTemplate>> INotificationTemplateRepository.ListAsync(TenantId? owner, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NotificationTemplate>>(TemplateRows.Where(template => template.TenantId == owner).ToList());

    public Task<bool> IsKeyUniqueAsync(
        TenantId? tenantId, TemplateKey key, NotificationChannel channel, NotificationLanguage language, CancellationToken cancellationToken = default) =>
        Task.FromResult(!TemplateRows.Any(template => template.TenantId == tenantId && template.Key == key && template.Channel == channel && template.Language == language));

    public Task<bool> IsVersionUniqueAsync(
        TenantId? tenantId, TemplateKey key, NotificationChannel channel, NotificationLanguage language, int version, CancellationToken cancellationToken = default) =>
        Task.FromResult(!TemplateRows.Any(template => template.TenantId == tenantId && template.Key == key && template.Channel == channel
            && template.Language == language && template.Version == version));

    public void Add(NotificationTemplate template) => TemplateRows.Add(template);

    // ---- INotificationPreferenceRepository

    public Task<NotificationPreference?> FindAsync(TenantId tenantId, RecipientId owner, CancellationToken cancellationToken = default) =>
        Task.FromResult(PreferenceRows.SingleOrDefault(preference => preference.TenantId == tenantId && preference.Owner == owner));

    public Task<NotificationPreference?> FindForSignedLinkAsync(TenantId tenantId, RecipientId owner, CancellationToken cancellationToken = default) =>
        Task.FromResult(PreferenceRows.SingleOrDefault(preference => preference.TenantId == tenantId && preference.Owner == owner));

    public void Add(NotificationPreference preference) => PreferenceRows.Add(preference);

    // ---- INotificationRepository, INotificationDirectory

    public Task<Notification?> FindAsync(TenantId? owner, NotificationId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(NotificationRows.SingleOrDefault(notification => notification.Id == id && notification.TenantId == owner));

    Task<IReadOnlyList<Notification>> INotificationRepository.ListAsync(
        TenantId tenantId, NotificationFilter filter, int skip, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Notification>>(NotificationRows
            .Where(notification => notification.TenantId == tenantId && (filter.Status is null || notification.Status == filter.Status))
            .Skip(skip).Take(take).ToList());

    public Task<IReadOnlyList<Notification>> ListInAppAsync(
        TenantId tenantId, UserId userId, bool includeArchived, int skip, int take, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Notification>>(NotificationRows
            .Where(notification => notification.TenantId == tenantId && notification.RecipientId == RecipientId.ForUser(userId)
                && notification.Channel == NotificationChannel.InApp && (includeArchived || notification.ArchivedAtUtc is null))
            .Skip(skip).Take(take).ToList());

    public Task<bool> ExistsAsync(TenantId? owner, string idempotencyKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(_notifications.Any(row => row.Notification.TenantId == owner && row.Key == idempotencyKey));

    public Task<Notification?> FindByIdempotencyKeyAsync(TenantId? owner, string idempotencyKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(_notifications.Where(row => row.Notification.TenantId == owner && row.Key == idempotencyKey).Select(row => row.Notification).SingleOrDefault());

    public void Add(Notification notification, string? idempotencyKey = null) => _notifications.Add((notification, idempotencyKey));

    public Task<NotificationWindowCount> CountCreatedSinceAsync(
        TenantId tenantId, IReadOnlyCollection<NotificationChannel> channels, IReadOnlyCollection<TemplateKey> types, DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        var rows = NotificationRows
            .Where(notification => notification.TenantId == tenantId && channels.Contains(notification.Channel)
                && types.Contains(notification.TemplateKey) && notification.CreatedAtUtc > since)
            .ToList();
        return Task.FromResult(new NotificationWindowCount(rows.Count, rows.Count == 0 ? null : rows.Min(notification => notification.CreatedAtUtc)));
    }

    public Task<IReadOnlyList<NotificationLocator>> ListDueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<NotificationLocator>>(NotificationRows.Where(notification => notification.IsDueAt(now))
            .OrderBy(notification => notification.Priority).Take(take).Select(notification => new NotificationLocator(notification.Id, notification.TenantId)).ToList());

    public Task<IReadOnlyList<NotificationLocator>> ListStuckAsync(DateTimeOffset startedBefore, int take, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<NotificationLocator>>([]);

    public Task<IReadOnlyList<NotificationLocator>> ListToRedactAsync(DateTimeOffset before, int take, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<NotificationLocator>>([]);

    public Task<NotificationLocator?> FindByProviderMessageAsync(
        NotificationProviderCode provider, ProviderMessageId messageId, TenantId? signedBy, CancellationToken cancellationToken = default) =>
        Task.FromResult(NotificationRows
            .Where(notification => (signedBy == null || notification.TenantId == signedBy)
                && notification.Deliveries.Any(delivery => delivery.ProviderCode == provider && delivery.ProviderMessageId == messageId))
            .Select(notification => new NotificationLocator(notification.Id, notification.TenantId))
            .SingleOrDefault());

    public Task<IReadOnlyList<Notification>> ListRecentAsync(
        NotificationChannel? channel, NotificationStatus? status, int skip, int take, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Notification>>(NotificationRows
            .Where(notification => (channel == null || notification.Channel == channel) && (status == null || notification.Status == status))
            .OrderByDescending(notification => notification.CreatedAtUtc).Skip(skip).Take(take).ToList());

    public Task<IReadOnlyList<NotificationCount>> CountSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<NotificationCount>>(NotificationRows
            .Where(notification => notification.CreatedAtUtc >= since)
            .GroupBy(notification => (notification.Channel, notification.Status))
            .Select(group => new NotificationCount(group.Key.Channel, group.Key.Status, group.Count()))
            .ToList());

    public Task<IReadOnlyList<TimeSpan>> SendDelaysSinceAsync(DateTimeOffset since, int take, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TimeSpan>>(NotificationRows
            .Where(notification => notification.CreatedAtUtc >= since && notification.SentAtUtc != null)
            .OrderByDescending(notification => notification.SentAtUtc).Take(take)
            .Select(notification => notification.SentAtUtc!.Value - (notification.ScheduledAtUtc ?? notification.CreatedAtUtc))
            .ToList());

    public GetNotificationsOverviewHandler Overview => new(this, Clock);

    public ListPlatformNotificationsHandler SendLog => new(this);

    public ListNotificationProvidersHandler ListProviders => new(this, this, this);

    public Task<IReadOnlyList<Notification>> ListAbandonedAsync(int skip, int take, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Notification>>(NotificationRows.Where(notification => notification.IsDeliveryAbandoned).Skip(skip).Take(take).ToList());

    public Task<NotificationLocator?> LocateAsync(NotificationId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(NotificationRows.Where(notification => notification.Id == id)
            .Select(notification => new NotificationLocator(notification.Id, notification.TenantId)).SingleOrDefault());

    public Task<Notification?> LoadAnyAsync(NotificationId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(NotificationRows.SingleOrDefault(notification => notification.Id == id));

    // ---- INotificationSecretStore

    public Task StoreAsync(NotificationCredentialReference reference, IReadOnlyDictionary<string, string> credentials, CancellationToken cancellationToken = default)
    {
        Secrets[reference.Value] = credentials;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>?> ReadAsync(NotificationCredentialReference reference, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>?>(Secrets.TryGetValue(reference.Value, out var credentials) ? credentials : null);

    // ---- INotificationInbox, INotificationUsage, INotificationEntitlementChecker

    public Task<bool> HasProcessedAsync(string consumer, string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Inbox.Contains((consumer, eventId)));

    public void MarkProcessed(string consumer, string eventId, string eventType, DateTimeOffset processedAtUtc) => Inbox.Add((consumer, eventId));

    public Task RecordAsync(TenantId tenantId, NotificationChannel channel, NotificationProviderCode provider, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        UsageRows[(tenantId, channel)] = UsageRows.GetValueOrDefault((tenantId, channel)) + 1;
        return Task.CompletedTask;
    }

    public Task<long> CountAsync(TenantId tenantId, NotificationChannel channel, DateTimeOffset monthOf, CancellationToken cancellationToken = default) =>
        Task.FromResult(UsageRows.GetValueOrDefault((tenantId, channel)));

    public Task<long?> MonthlyLimitAsync(TenantId tenantId, NotificationChannel channel, CancellationToken cancellationToken = default) =>
        Task.FromResult(Limits.TryGetValue(channel, out var limit) ? (long?)limit : null);

    // ---- INotificationLocations, INotificationContacts, INotificationConsents

    public Task<LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
        Task.FromResult<LocationReference?>(Locations.GetValueOrDefault((tenantId, locationId)));

    public Task<IReadOnlyList<CountryCode>> CountriesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CountryCode>>(Locations.Where(pair => pair.Key.Item1 == tenantId).Select(pair => pair.Value.Country).Distinct().ToList());

    public Task<CustomerContact?> FindCustomerAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default) =>
        Task.FromResult<CustomerContact?>(Customers.GetValueOrDefault((tenantId, customerId)));

    public Task<ConsentProof?> FindAsync(TenantId tenantId, CustomerId customerId, CommunicationChannel channel, CancellationToken cancellationToken = default) =>
        Task.FromResult<ConsentProof?>(Customers.ContainsKey((tenantId, customerId))
            ? Consents.GetValueOrDefault((tenantId, customerId, channel)) ? ConsentProof.Granted(customerId, channel) : ConsentProof.Denied(customerId, channel)
            : null);

    // ---- IDeviceRegistrationRepository

    public Task<DeviceRegistration?> FindAsync(UserId userId, string deviceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(DeviceRows.SingleOrDefault(device => device.UserId == userId && device.DeviceId == deviceId));

    public Task<IReadOnlyList<DeviceRegistration>> ListActiveAsync(UserId userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DeviceRegistration>>(DeviceRows.Where(device => device.UserId == userId && device.IsActive).ToList());

    public Task<IReadOnlyList<DeviceRegistration>> ListByTokenAsync(string pushToken, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DeviceRegistration>>(DeviceRows.Where(device => device.PushToken == pushToken && device.IsActive).ToList());

    public void Add(DeviceRegistration registration) => DeviceRows.Add(registration);

    // ---- IOtpChallengeRepository, IOtpHasher

    public Task<OtpChallenge?> FindAsync(TenantId? owner, OtpChallengeId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(OtpRows.SingleOrDefault(challenge => challenge.Id == id && challenge.TenantId == owner));

    public Task<IReadOnlyList<OtpChallenge>> ListRecentAsync(OtpHash subjectKey, OtpPurpose purpose, DateTimeOffset since, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OtpChallenge>>(OtpRows
            .Where(challenge => challenge.SubjectKey == subjectKey && challenge.Purpose == purpose && challenge.CreatedAtUtc >= since)
            .OrderByDescending(challenge => challenge.CreatedAtUtc)
            .ToList());

    public void Add(OtpChallenge challenge) => OtpRows.Add(challenge);

    public string NewCode(int digits) => string.Concat(Enumerable.Range(0, digits).Select(_ => RandomNumberGenerator.GetInt32(10)));

    public OtpHash HashSubject(NotificationChannel channel, string address) => Hash($"s|{channel}|{address}");

    public OtpHash HashCode(OtpHash subjectKey, OtpPurpose purpose, string code) => Hash($"c|{subjectKey.Value}|{purpose.Value}|{code}");

    private static OtpHash Hash(string value) => OtpHash.Create(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))));

    // ---- INotificationsUnitOfWork

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        SavedBodies.AddRange(NotificationRows.Select(notification => notification.Content.Body));
        foreach (var notification in NotificationRows)
        {
            notification.ClearDomainEvents();
        }

        return Task.CompletedTask;
    }

    // ---- INotificationSenderResolver

    public NotificationIntegration? FindIntegration(NotificationProviderCode provider) =>
        Integrations.FirstOrDefault(integration => integration.Provider == provider);

    public async Task<INotificationSender?> ResolveAsync(
        NotificationProviderCode provider, NotificationChannel channel, RestaurantNotificationConfiguration? configuration, CancellationToken cancellationToken = default)
    {
        if (FindIntegration(provider) is not { } integration || !integration.Channels.Contains(channel))
        {
            return null;
        }

        if (integration.CredentialFields.Count > 0)
        {
            var reference = configuration is { Mode: NotificationProviderMode.TenantManaged }
                ? configuration.SecretReference
                : NotificationCredentialReference.ForPlatform(provider);
            if (reference is null || await ReadAsync(reference, cancellationToken) is null)
            {
                return null;
            }
        }

        return new RecordingSender(provider, channel, this);
    }

    private sealed class CampaignStore(NotificationsFixture fixture) : ICampaignRepository
    {
        public Task<Campaign?> FindAsync(TenantId tenantId, CampaignId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(fixture.CampaignRows.SingleOrDefault(campaign => campaign.TenantId == tenantId && campaign.Id == id));

        public Task<IReadOnlyList<Campaign>> ListAsync(TenantId tenantId, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Campaign>>(fixture.CampaignRows.Where(campaign => campaign.TenantId == tenantId).Take(take).ToList());

        public void Add(Campaign campaign) => fixture.CampaignRows.Add(campaign);
    }

    private sealed class RulesStore(NotificationsFixture fixture) : IMarketingRulesRepository
    {
        public Task<MarketingRules?> FindAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(fixture.RulesRows.SingleOrDefault(rules => rules.TenantId == tenantId));

        public void Add(MarketingRules rules) => fixture.RulesRows.Add(rules);
    }

    private sealed class HistoryStore(NotificationsFixture fixture) : IMarketingHistory
    {
        public Task<bool> ReceivedSinceAsync(TenantId tenantId, RecipientId recipient, DateTimeOffset since, CancellationToken cancellationToken = default) =>
            Task.FromResult(fixture.NotificationRows.Any(notification => notification.TenantId == tenantId
                && notification.RecipientId == recipient
                && notification.Purpose == NotificationPurpose.Marketing
                && notification.CreatedAtUtc > since
                && notification.Status is not (NotificationStatus.Cancelled or NotificationStatus.Failed)));
    }

    /// <summary>Comme Customers : clients non anonymisés, consentement accordé sur le canal, adresse connue, tag éventuel ; par id.</summary>
    private sealed class AudienceStore(NotificationsFixture fixture) : ICampaignAudience
    {
        public Task<int> CountAsync(
            TenantId tenantId, CommunicationChannel channel, string? tag, LocationId? customersOf = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Matching(tenantId, channel, tag, customersOf).Count());

        public Task<IReadOnlyList<CustomerId>> ListAsync(
            TenantId tenantId, CommunicationChannel channel, string? tag, int skip, int take, LocationId? customersOf = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CustomerId>>(Matching(tenantId, channel, tag, customersOf).Skip(skip).Take(take).ToList());

        private IEnumerable<CustomerId> Matching(TenantId tenantId, CommunicationChannel channel, string? tag, LocationId? customersOf) =>
            fixture.Customers
                .Where(entry => customersOf is null || fixture.LocationCustomers.Contains((customersOf.Value, entry.Key.Item2)))
                .Where(entry => entry.Key.Item1 == tenantId && !entry.Value.IsAnonymized
                    && fixture.Consents.TryGetValue((tenantId, entry.Key.Item2, channel), out var granted) && granted
                    && (channel == CommunicationChannel.Email ? entry.Value.Email is not null : entry.Value.Phone is not null)
                    && (tag is null || fixture.CustomerTags.GetValueOrDefault(entry.Key.Item2) == tag))
                .Select(entry => entry.Key.Item2)
                .OrderBy(id => id.Value);
    }

    private sealed class RecordingSender(NotificationProviderCode provider, NotificationChannel channel, NotificationsFixture fixture) : INotificationSender
    {
        public NotificationProviderCode Provider => provider;

        public NotificationChannel Channel => channel;

        public Task<NotificationSendResult> SendAsync(NotificationSendRequest request, CancellationToken cancellationToken = default)
        {
            fixture.Sent.Add(request);
            if (fixture.NextResults.TryDequeue(out var scripted))
            {
                return Task.FromResult(scripted);
            }

            return Task.FromResult(provider == NotificationProviderCode.InApp
                ? NotificationSendResult.Delivered()
                : NotificationSendResult.Accepted(ProviderMessageId.Create("msg-" + Guid.NewGuid().ToString("N"))));
        }
    }
}
