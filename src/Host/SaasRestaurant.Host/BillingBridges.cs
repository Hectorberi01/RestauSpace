using System.Globalization;
using System.Text.Json;
using Identity.Application.Queries;
using Notifications.Application.Commands;
using Staff.Application.Queries;
using Staff.Domain;
using Billing.Application;
using Billing.Application.Abstractions;
using Billing.Application.Queries;
using Billing.Domain;
using Payments.Application.Commands;
using Notifications.Application.Abstractions;
using Notifications.Domain;
using SaasRestaurant.BuildingBlocks.Application;
using Tenants.Application.Commands;
using Tenants.Application.Queries;
using Tenants.Domain.Tenants;

namespace SaasRestaurant.Host;

// Les ports déclarés par Billing (servis par Tenants), le quota de Notifications servi par Billing, et la répercussion
// des événements d'abonnement sur le statut du restaurant (par l'outbox). Aucun module n'en référence un autre : seul
// le host les relie.

/// <summary>Billing -> Tenants : existence du tenant et acceptation des écritures métier. Inconnu = non.</summary>
internal sealed class BillingTenantsFromTenants(CachedTenants tenants) : IBillingTenants
{
    public async Task<bool> ExistsAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await tenants.FindAsync(tenantId, cancellationToken) is not null;

    public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        tenants.AcceptsBusinessWritesAsync(tenantId, cancellationToken);

    public async Task<Currency?> DefaultCurrencyAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await tenants.FindAsync(tenantId, cancellationToken) is { } tenant ? Currency.FromCode(tenant.DefaultCurrencyCode) : null;

    public async Task<string?> NameAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        (await tenants.FindAsync(tenantId, cancellationToken))?.Name;
}

/// <summary>
/// Payments -> Billing : un webhook signé du compte de la plateforme annonce une transaction. Le règlement d'abonnement
/// qui la porte est relu tout de suite chez le prestataire (sinon le job s'en charge). Une transaction inconnue est ignorée.
/// </summary>
internal sealed class SubscriptionCheckoutsFromPlatformWebhooks(IServiceScopeFactory scopes, ILogger<SubscriptionCheckoutsFromPlatformWebhooks> logger)
    : Payments.Application.Abstractions.IPlatformCollectionListener
{
    public async Task TransactionChangedAsync(string provider, string providerReference, CancellationToken cancellationToken = default)
    {
        (TenantId TenantId, SubscriptionCheckoutId Id)? pending;
        await using (var lookup = scopes.CreateAsyncScope())
        {
            pending = await lookup.ServiceProvider.GetRequiredService<ISubscriptionCheckoutRepository>()
                .FindPendingByReferenceAsync(provider.Trim().ToLowerInvariant(), providerReference, cancellationToken);
        }

        if (pending is not { } checkout)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(checkout.TenantId);
        try
        {
            var status = await scope.ServiceProvider.GetRequiredService<ICommandHandler<Billing.Application.Commands.SettleSubscriptionCheckoutCommand, SubscriptionCheckoutStatus>>()
                .HandleAsync(new Billing.Application.Commands.SettleSubscriptionCheckoutCommand(checkout.TenantId.Value, checkout.Id.Value), cancellationToken);
            logger.LogInformation("Subscription checkout {CheckoutId} after platform webhook: {Status}.", checkout.Id.Value, status);
        }
        catch (Exception exception) when (exception is ConcurrencyException or DomainException)
        {
            // Le job relira le règlement.
            logger.LogWarning("Subscription checkout {CheckoutId} postponed: {Error}.", checkout.Id.Value, exception.GetType().Name);
        }
    }
}

/// <summary>
/// Billing -> Payments : les abonnements sont encaissés sur le compte marchand de la PLATEFORME, chez le prestataire que le
/// restaurant choisit parmi ceux proposés par le super-admin pour la devise (à défaut, <c>Billing:OnlinePayment:Provider</c>),
/// dont les identifiants sont rangés au coffre. Sans prestataire prêt, le paiement en ligne n'est pas proposé. Le prestataire fait foi : l'état est relu chez lui.
/// </summary>
internal sealed class SubscriptionCollectorFromPayments(
    BillingOnlinePayment settings,
    ICommandHandler<StartPlatformCollectionCommand, PlatformCollectionStart> start,
    IQueryHandler<GetPlatformCollectionStatusQuery, PlatformCollectionStatus> status,
    IQueryHandler<CheckPlatformCollectionQuery, bool> check,
    IQueryHandler<ListPlatformCollectionReadinessQuery, IReadOnlyList<PlatformCollectionReadiness>> readiness) : ISubscriptionCollector
{
    public string? DefaultProvider => settings.Provider;

    public Task<bool> IsReadyAsync(string provider, Currency currency, CancellationToken cancellationToken = default) =>
        check.HandleAsync(new CheckPlatformCollectionQuery(provider, currency.Code), cancellationToken);

    public async Task<IReadOnlyList<CollectorCandidate>> CandidatesAsync(Currency currency, CancellationToken cancellationToken = default) =>
        (await readiness.HandleAsync(new ListPlatformCollectionReadinessQuery(currency.Code), cancellationToken))
            .Select(candidate => new CollectorCandidate(candidate.Provider, candidate.Name, candidate.Ready, candidate.Missing, candidate.Methods))
            .ToList();

    public async Task<CollectionStart> StartAsync(
        string provider, TenantId tenantId, SubscriptionCheckoutId checkoutId, Money amount, string label, string? payerPhone,
        CancellationToken cancellationToken = default)
    {
        var started = await start.HandleAsync(
            new StartPlatformCollectionCommand(provider, checkoutId.Value, tenantId.Value, amount.Amount, amount.Currency.Code, label, payerPhone, settings.ReturnUrl),
            cancellationToken);
        return new CollectionStart(started.Provider, started.ProviderReference, started.CheckoutUrl);
    }

    public async Task<CollectionStatus> StatusAsync(string provider, string reference, CancellationToken cancellationToken = default)
    {
        var read = await status.HandleAsync(new GetPlatformCollectionStatusQuery(provider, reference), cancellationToken);
        return new CollectionStatus(
            read.State switch
            {
                PlatformCollectionState.Paid => CollectionState.Paid,
                PlatformCollectionState.Failed => CollectionState.Failed,
                _ => CollectionState.Pending,
            },
            read.Amount);
    }
}

/// <summary>Section <c>Billing:OnlinePayment</c> : prestataire des abonnements et page de retour de la console.</summary>
internal sealed record BillingOnlinePayment(string? Provider, Uri? ReturnUrl);

/// <summary>
/// Notifications -> Billing : la limite mensuelle d'un canal est le droit <c>notifications.{canal}.monthly</c> du plan
/// du restaurant. Désactivé (pas d'abonnement vivant, ou canal absent du plan) = 0 ; activé sans limite = null.
/// Fail-closed : un tenant autre que celui du contexte n'a droit à rien.
/// </summary>
internal sealed class NotificationEntitlementsFromBilling(ITenantContext tenantContext, IQueryHandler<GetFeatureLimitQuery, FeatureLimitDto> limits)
    : INotificationEntitlementChecker
{
    public async Task<long?> MonthlyLimitAsync(TenantId tenantId, NotificationChannel channel, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return 0;
        }

        var limit = await limits.HandleAsync(new GetFeatureLimitQuery(BillingFeatures.NotificationsMonthly(channel.ToString())), cancellationToken);
        return limit.Enabled ? limit.Limit : 0;
    }
}

/// <summary>
/// Saga Billing -> Tenants, par l'outbox (après commit, au moins une fois), dans un scope DI par message :
/// <list type="bullet">
/// <item>billing.SubscriptionTrialStarted -> un restaurant suspendu (ancien abonnement résilié) est réactivé ;</item>
/// <item>billing.SubscriptionActivated -> essai : le restaurant est activé ; suspendu : il est réactivé ;</item>
/// <item>billing.SubscriptionSuspended -> le restaurant est suspendu pour le même motif ;</item>
/// <item>billing.SubscriptionCancelled -> le restaurant est suspendu (jamais fermé : la fermeture reste une décision
/// de la plateforme, et un nouvel abonnement le rouvre).</item>
/// </list>
/// Le statut du restaurant est lu d'abord : un rejeu, ou un restaurant déjà dans l'état voulu, ne change rien. Un
/// restaurant fermé n'est jamais modifié. Un refus métier part en file morte ; les autres erreurs sont retentées.
/// Journal : ids et codes seulement.
/// </summary>
internal sealed class BillingFlow(IServiceScopeFactory scopes, ILogger<BillingFlow> logger) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Module != "billing"
            || message.Type is not ("SubscriptionTrialStarted" or "SubscriptionActivated" or "SubscriptionSuspended" or "SubscriptionCancelled"))
        {
            return;
        }

        var (tenantId, reason) = message.Type switch
        {
            "SubscriptionSuspended" => Of(message.Read<SaasRestaurant.Contracts.Billing.SubscriptionSuspended>()),
            "SubscriptionCancelled" => Of(message.Read<SaasRestaurant.Contracts.Billing.SubscriptionCancelled>()),
            "SubscriptionActivated" => (message.Read<SaasRestaurant.Contracts.Billing.SubscriptionActivated>().TenantId, (string?)null),
            _ => (message.Read<SaasRestaurant.Contracts.Billing.SubscriptionTrialStarted>().TenantId, (string?)null),
        };

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(tenantId));

        TenantDto tenant;
        try
        {
            tenant = await services.GetRequiredService<IQueryHandler<GetTenantByIdQuery, TenantDto>>()
                .HandleAsync(new GetTenantByIdQuery(tenantId), cancellationToken);
        }
        catch (NotFoundException)
        {
            logger.LogWarning("Outbox message billing/{MessageId} ({Type}) ignored: unknown tenant.", message.Id, message.Type);
            return;
        }

        var applied = await ApplyAsync(services, message.Type, reason, tenant, cancellationToken);
        if (applied)
        {
            logger.LogInformation(
                "Outbox message billing/{MessageId} ({Type}) applied to tenant {TenantId}.", message.Id, message.Type, tenantId);
        }
    }

    private static (Guid TenantId, string? Reason) Of(SaasRestaurant.Contracts.Billing.SubscriptionSuspended suspended) =>
        (suspended.TenantId, suspended.Reason);

    private static (Guid TenantId, string? Reason) Of(SaasRestaurant.Contracts.Billing.SubscriptionCancelled cancelled) =>
        (cancelled.TenantId, cancelled.Reason);

    private static async Task<bool> ApplyAsync(IServiceProvider services, string type, string? reason, TenantDto tenant, CancellationToken cancellationToken)
    {
        var status = tenant.Status;
        if (status == TenantStatus.Closed)
        {
            return false;
        }

        switch (type)
        {
            case "SubscriptionTrialStarted" when status == TenantStatus.Suspended:
            case "SubscriptionActivated" when status == TenantStatus.Suspended:
                await services.GetRequiredService<ICommandHandler<ReactivateTenantCommand>>()
                    .HandleAsync(new ReactivateTenantCommand(tenant.Id), cancellationToken);
                return true;

            case "SubscriptionActivated" when status == TenantStatus.Trial:
                await services.GetRequiredService<ICommandHandler<ActivateTenantCommand>>()
                    .HandleAsync(new ActivateTenantCommand(tenant.Id), cancellationToken);
                return true;

            case "SubscriptionSuspended" when status != TenantStatus.Suspended:
                await SuspendAsync(services, tenant.Id, SuspensionFor(reason), cancellationToken);
                return true;

            case "SubscriptionCancelled" when status != TenantStatus.Suspended:
                await SuspendAsync(services, tenant.Id, CancellationFor(reason), cancellationToken);
                return true;

            default:
                return false;
        }
    }

    private static Task SuspendAsync(IServiceProvider services, Guid tenantId, TenantSuspensionReason reason, CancellationToken cancellationToken) =>
        services.GetRequiredService<ICommandHandler<SuspendTenantCommand>>().HandleAsync(new SuspendTenantCommand(tenantId, reason), cancellationToken);

    /// <summary>Motif de suspension de l'abonnement (nom de l'enum Billing) -> motif de suspension du restaurant.</summary>
    private static TenantSuspensionReason SuspensionFor(string? reason) => reason switch
    {
        "TrialExpired" => TenantSuspensionReason.TrialExpired,
        "PaymentOverdue" => TenantSuspensionReason.PaymentOverdue,
        "PolicyViolation" => TenantSuspensionReason.PolicyViolation,
        _ => TenantSuspensionReason.Other,
    };

    /// <summary>Résiliation programmée par le restaurant -> à sa demande ; résiliation immédiate (plateforme) -> autre.</summary>
    private static TenantSuspensionReason CancellationFor(string? reason) =>
        reason == "AtPeriodEnd" ? TenantSuspensionReason.RequestedByTenant : TenantSuspensionReason.Other;
}

/// <summary>
/// Billing -> Notifications, par l'outbox, aux membres actifs qui gèrent l'abonnement (<c>billing.manage</c>), dans
/// l'application et par email : préavis de fin d'essai ou d'offre (<c>staff.subscription_ending</c>), et offres de la
/// plateforme sur le plan ou une option, accordées, renouvelées ou terminées (<c>staff.subscription_offer</c>). Le message
/// donne le plan ou l'option, la date et le lien vers la page Abonnement ; aucun montant. Une clé d'idempotence par membre
/// et par canal rend un rejeu sans effet. Journal : rien de personnel.
/// </summary>
internal sealed class SubscriptionNoticesFlow(IServiceScopeFactory scopes, BillingOnlinePayment settings, IConfiguration configuration) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Module != "billing")
        {
            return;
        }

        switch (message.Type)
        {
            case "SubscriptionEndingSoon":
            {
                var ending = message.Read<SaasRestaurant.Contracts.Billing.SubscriptionEndingSoon>();
                await NotifyAsync(message, ending.TenantId, ending.PlanId, NotificationTypes.StaffSubscriptionEnding, (plan, french) => new()
                {
                    ["subscription.plan"] = plan,
                    ["subscription.endDate"] = Day(ending.EndsAtUtc, french),
                    ["subscription.reason"] = (ending.Complimentary, french) switch
                    {
                        (true, true) => "l'offre du plan",
                        (true, false) => "the offer of the plan",
                        (false, true) => "votre essai du plan",
                        _ => "your trial of the plan",
                    },
                }, cancellationToken);
                return;
            }

            case "SubscriptionComplimentaryGranted":
            {
                var granted = message.Read<SaasRestaurant.Contracts.Billing.SubscriptionComplimentaryGranted>();
                await NotifyOfferAsync(message, granted.TenantId, granted.PlanId, addOn: false,
                    french => OfferDetail(addOn: false, granted.UntilUtc, granted.RenewalMonths, then: null, french), cancellationToken);
                return;
            }

            case "SubscriptionComplimentaryRenewed":
            {
                var renewed = message.Read<SaasRestaurant.Contracts.Billing.SubscriptionComplimentaryRenewed>();
                await NotifyOfferAsync(message, renewed.TenantId, renewed.PlanId, addOn: false, french => Renewed(addOn: false, renewed.UntilUtc, french), cancellationToken);
                return;
            }

            case "SubscriptionComplimentaryEnded":
            {
                var ended = message.Read<SaasRestaurant.Contracts.Billing.SubscriptionComplimentaryEnded>();
                if (ended.PlanId is { } planId)
                {
                    await NotifyOfferAsync(message, ended.TenantId, planId, addOn: false, french => french
                        ? "n'est plus offert : réglez la période suivante depuis Abonnement"
                        : "is no longer offered: pay for the next period in Subscription", cancellationToken);
                }

                return;
            }

            case "TenantAddOnComplimentaryGranted":
            {
                var granted = message.Read<SaasRestaurant.Contracts.Billing.TenantAddOnComplimentaryGranted>();
                await NotifyOfferAsync(message, granted.TenantId, granted.PlanId, addOn: true,
                    french => OfferDetail(addOn: true, granted.UntilUtc, granted.RenewalMonths, granted.Then, french), cancellationToken);
                return;
            }

            case "TenantAddOnComplimentaryRenewed":
            {
                var renewed = message.Read<SaasRestaurant.Contracts.Billing.TenantAddOnComplimentaryRenewed>();
                await NotifyOfferAsync(message, renewed.TenantId, renewed.PlanId, addOn: true, french => Renewed(addOn: true, renewed.UntilUtc, french), cancellationToken);
                return;
            }

            case "TenantAddOnComplimentaryEnded":
            {
                var ended = message.Read<SaasRestaurant.Contracts.Billing.TenantAddOnComplimentaryEnded>();
                var billed = ended.Then == nameof(AddOnAfterOffer.Bill);
                await NotifyOfferAsync(message, ended.TenantId, ended.PlanId, addOn: true, french => (billed, french) switch
                {
                    (true, true) => "n'est plus offerte : elle sera facturée avec votre prochaine période",
                    (true, false) => "is no longer offered: it will be billed with your next period",
                    (false, true) => "n'est plus offerte : elle est arrêtée",
                    _ => "is no longer offered: it has been stopped",
                }, cancellationToken);
                return;
            }
        }
    }

    // Offre : « Le plan Pro » / « L'option Borne » + le détail.
    private Task NotifyOfferAsync(
        OutboxEnvelope message, Guid tenantId, Guid planId, bool addOn, Func<bool, string> detail, CancellationToken cancellationToken) =>
        NotifyAsync(message, tenantId, planId, NotificationTypes.StaffSubscriptionOffer, (plan, french) => new()
        {
            ["offer.subject"] = (addOn, french) switch
            {
                (true, true) => $"L'option {plan}",
                (true, false) => $"The {plan} add-on",
                (false, true) => $"Le plan {plan}",
                _ => $"The {plan} plan",
            },
            ["offer.detail"] = detail(french),
        }, cancellationToken);

    private static string OfferDetail(bool addOn, DateTimeOffset? untilUtc, int? renewalMonths, string? then, bool french)
    {
        var offered = french ? (addOn ? "vous est offerte par RestauSpace" : "vous est offert par RestauSpace") : "is offered to you by RestauSpace";
        var period = untilUtc is not { } until
            ? french ? " sans limite de durée" : " with no end date"
            : french ? $" jusqu'au {Day(until, true)}" : $" until {Day(until, false)}";
        var renewal = renewalMonths is { } months
            ? french ? $", puis renouvelé{(addOn ? "e" : string.Empty)} automatiquement tous les {months} mois" : $", then renewed automatically every {months} month(s)"
            : string.Empty;
        var after = untilUtc is null || renewalMonths is not null || then is null
            ? string.Empty
            : (then == nameof(AddOnAfterOffer.Bill), french) switch
            {
                (true, true) => ". Ensuite, elle devient payante",
                (true, false) => ". After that, it becomes paid",
                (false, true) => ". Ensuite, elle s'arrête",
                _ => ". After that, it stops",
            };
        return offered + period + renewal + after;
    }

    private static string Renewed(bool addOn, DateTimeOffset untilUtc, bool french) => french
        ? $"reste offert{(addOn ? "e" : string.Empty)} par RestauSpace jusqu'au {Day(untilUtc, true)} (renouvellement automatique)"
        : $"remains offered by RestauSpace until {Day(untilUtc, false)} (automatic renewal)";

    private static string Day(DateTimeOffset value, bool french) =>
        value.UtcDateTime.ToString(french ? "dd/MM/yyyy" : "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task NotifyAsync(
        OutboxEnvelope message,
        Guid tenant,
        Guid planId,
        NotificationTypeDefinition type,
        Func<string, bool, Dictionary<string, string>> describe,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantId.From(tenant);
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(tenantId);

        var restaurant = await services.GetRequiredService<IQueryHandler<GetTenantByIdQuery, TenantDto>>()
            .HandleAsync(new GetTenantByIdQuery(tenantId.Value), cancellationToken);
        var plan = await services.GetRequiredService<IQueryHandler<GetPlanQuery, PlanDto>>()
            .HandleAsync(new GetPlanQuery(planId), cancellationToken);
        var send = services.GetRequiredService<ICommandHandler<SendNotificationCommand, SendNotificationResult>>();
        var key = $"billing:{message.Id.ToString(CultureInfo.InvariantCulture)}";
        foreach (var userId in await ManagersAsync(services, tenantId, cancellationToken))
        {
            UserDto user;
            try
            {
                user = await services.GetRequiredService<IQueryHandler<GetUserByIdQuery, UserDto>>().HandleAsync(new GetUserByIdQuery(userId), cancellationToken);
            }
            catch (NotFoundException)
            {
                continue;
            }

            if (user.ErasedAtUtc is not null)
            {
                continue;
            }

            var french = user.Language.StartsWith("fr", StringComparison.OrdinalIgnoreCase);
            var variables = describe(plan.Name, french);
            variables["restaurant.name"] = restaurant.Name;
            variables["location.name"] = string.Empty;
            variables["subscription.url"] = SubscriptionUrl();
            _ = await send.HandleAsync(
                new SendNotificationCommand(
                    type.Key.Value, NotificationChannel.InApp, UserId: userId, Variables: variables,
                    IdempotencyKey: $"{key}:{userId:N}:InApp"),
                cancellationToken);
            _ = await send.HandleAsync(
                new SendNotificationCommand(
                    type.Key.Value, NotificationChannel.Email, UserId: userId, Address: user.Email, Variables: variables,
                    IdempotencyKey: $"{key}:{userId:N}:Email"),
                cancellationToken);
        }
    }

    private string SubscriptionUrl()
    {
        if (settings.ReturnUrl is { } back)
        {
            return back.ToString();
        }

        var console = configuration["Staff:ConsoleBaseUrl"] is { Length: > 0 } configured
            ? configured.TrimEnd('/')
            : Uri.TryCreate(configuration["Identity:Emails:Links:AcceptInvitation"], UriKind.Absolute, out var link) ? link.GetLeftPart(UriPartial.Authority) : string.Empty;
        return $"{console}/abonnement";
    }

    /// <summary>Les membres actifs du restaurant qui gèrent l'abonnement.</summary>
    private static async Task<List<Guid>> ManagersAsync(IServiceProvider services, TenantId tenantId, CancellationToken cancellationToken)
    {
        var list = services.GetRequiredService<IQueryHandler<ListStaffQuery, PagedResult<StaffMemberDto>>>();
        var permissions = services.GetRequiredService<IPermissionChecker>();
        var managers = new List<Guid>();
        for (var page = 1; ; page++)
        {
            var members = await list.HandleAsync(new ListStaffQuery(page, ListStaffQuery.MaxPageSize), cancellationToken);
            foreach (var member in members.Items.Where(member => member.Status == StaffStatus.Active))
            {
                if (await permissions.HasTenantPermissionAsync(UserId.From(member.UserId), tenantId, BillingPermissions.Manage, cancellationToken))
                {
                    managers.Add(member.UserId);
                }
            }

            if (!members.HasMore || members.Items.Count == 0)
            {
                return managers;
            }
        }
    }
}
