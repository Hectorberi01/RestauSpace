using Billing.Application.Abstractions;
using Billing.Application.Queries;
using Billing.Domain;

namespace Billing.Application.Commands;

/// <summary>Un règlement en ligne vu par le restaurant. <see cref="CheckoutUrl"/> : la page du prestataire à ouvrir.</summary>
public sealed record SubscriptionCheckoutDto(
    Guid Id,
    SubscriptionCheckoutStatus Status,
    decimal Amount,
    string CurrencyCode,
    string Provider,
    string? CheckoutUrl,
    DateTimeOffset ExpiresAtUtc,
    Guid? PaymentId)
{
    public static SubscriptionCheckoutDto From(SubscriptionCheckout checkout)
    {
        ArgumentNullException.ThrowIfNull(checkout);
        return new(
            checkout.Id.Value, checkout.Status, checkout.Amount.Amount, checkout.Amount.Currency.Code, checkout.Provider, checkout.CheckoutUrl,
            checkout.ExpiresAtUtc, checkout.PaymentId?.Value);
    }
}

/// <summary>
/// Le règlement en ligne des abonnements : démarrage chez le prestataire de la plateforme, puis relecture de son état
/// (par le restaurant qui attend, ou par le job) jusqu'à la décision du prestataire ou l'expiration.
/// </summary>
public sealed class CheckoutDesk(
    BillingWorkspace workspace, ISubscriptionCheckoutRepository checkouts, ISubscriptionCollector collector, IOnlinePaymentRouteRepository routes, IClock clock)
{
    public const int MaxLabelLength = 60;

    public BillingWorkspace Workspace => workspace;

    public ISubscriptionCheckoutRepository Checkouts => checkouts;

    public ISubscriptionCollector Collector => collector;

    /// <summary>Les prestataires proposés pour cette devise, dans l'ordre : choix de la plateforme, sinon réglage de l'hôte.</summary>
    public async Task<IReadOnlyList<string>> ProvidersAsync(Currency currency, CancellationToken cancellationToken) =>
        (await routes.FindAsync(currency.Code, cancellationToken))?.Providers
        ?? (collector.DefaultProvider is { } fallback ? [OnlinePaymentRoute.Normalize(fallback)] : []);

    /// <summary>Ce que le restaurant peut choisir maintenant pour payer dans cette devise : les prestataires proposés et prêts.</summary>
    public async Task<IReadOnlyList<OnlinePaymentOptionDto>> OptionsAsync(Currency currency, CancellationToken cancellationToken)
    {
        var candidates = (await collector.CandidatesAsync(currency, cancellationToken)).ToDictionary(candidate => candidate.Provider, StringComparer.Ordinal);
        var options = new List<OnlinePaymentOptionDto>();
        foreach (var provider in await ProvidersAsync(currency, cancellationToken))
        {
            if (await collector.IsReadyAsync(provider, currency, cancellationToken))
            {
                var candidate = candidates.GetValueOrDefault(provider);
                options.Add(new OnlinePaymentOptionDto(provider, candidate?.Name ?? provider, candidate?.Methods ?? []));
            }
        }

        return options;
    }

    /// <summary>Ce que coûterait le règlement maintenant ; null s'il n'y a rien à payer en ligne (plan gratuit compris).</summary>
    public async Task<(Plan Plan, Money Amount)?> DueAsync(SaasSubscription subscription, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        if (subscription.Status == SubscriptionStatus.Cancelled || subscription.CancelAtPeriodEnd)
        {
            return null;
        }

        // La période suivante est facturée au plan programmé, s'il y en a un.
        var plan = await workspace.LoadNextPeriodPlanAsync(subscription, cancellationToken);
        if (SubscriptionDues.NextPaidPeriod(subscription, plan, clock.UtcNow) is null)
        {
            return null;
        }

        var total = await workspace.ChargeAsync(plan, subscription.TenantId, cancellationToken);
        return total.Amount == 0m ? null : (plan, total);
    }

    /// <summary>
    /// Relit le règlement chez le prestataire et le clôt s'il a tranché, ou s'il a expiré sans réponse. Un prestataire
    /// injoignable laisse le règlement en attente jusqu'au passage suivant. N'enregistre rien : à l'appelant de sauvegarder.
    /// </summary>
    public async Task SettleAsync(SubscriptionCheckout checkout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkout);
        if (checkout.Status != SubscriptionCheckoutStatus.Pending)
        {
            return;
        }

        if (checkout.ProviderReference is { } reference)
        {
            CollectionStatus? status;
            try
            {
                status = await collector.StatusAsync(checkout.Provider, reference, cancellationToken);
            }
            catch (DomainException)
            {
                status = null;
            }

            switch (status?.State)
            {
                case CollectionState.Paid:
                    var enough = status.Amount is not { } paid || paid >= checkout.Amount.Amount;
                    checkout.MarkPaid(enough ? await ApplyAsync(checkout, cancellationToken) : null, clock);
                    return;
                case CollectionState.Failed:
                    checkout.Fail(clock);
                    return;
            }
        }

        if (!checkout.IsOpenAt(clock.UtcNow))
        {
            checkout.Expire(clock);
        }
    }

    /// <summary>Le paiement de la période due, au montant réglé ; null si la période a été réglée entre-temps ou si le prix a changé.</summary>
    private async Task<SubscriptionPaymentId?> ApplyAsync(SubscriptionCheckout checkout, CancellationToken cancellationToken)
    {
        var subscription = await workspace.Subscriptions.FindAsync(checkout.TenantId, checkout.SubscriptionId, cancellationToken);
        if (subscription is null || await DueAsync(subscription, cancellationToken) is not { } due || !due.Amount.Equals(checkout.Amount))
        {
            return null;
        }

        SubscriptionDues.Apply(subscription, SubscriptionDues.NextPaidPeriod(subscription, due.Plan, clock.UtcNow)!, clock);
        var payment = SubscriptionPayment.Record(
            subscription, due.Plan, await workspace.ActiveAddOnPlansAsync(subscription.TenantId, cancellationToken), checkout.Provider, clock);
        workspace.Payments.Add(payment);
        return payment.Id;
    }
}

/// <summary>
/// Le restaurant règle en ligne la période due de son abonnement (essai, échéance, impayé, suspension). Un règlement
/// encore ouvert est repris au lieu d'en ouvrir un second. Possible même suspendu : c'est ce qui le rétablit.
/// </summary>
/// <param name="PayerPhone">Numéro pour un paiement poussé sur le téléphone (selon le prestataire). Jamais conservé.</param>
/// <param name="Provider">Le prestataire choisi parmi ceux proposés ; absent : le premier.</param>
public sealed record StartSubscriptionCheckoutCommand(string? PayerPhone = null, string? Provider = null) : ICommand<SubscriptionCheckoutDto>
{
    public override string ToString() => "StartSubscriptionCheckoutCommand (***)";
}

public sealed class StartSubscriptionCheckoutHandler(CheckoutDesk desk, IClock clock) : ICommandHandler<StartSubscriptionCheckoutCommand, SubscriptionCheckoutDto>
{
    public async Task<SubscriptionCheckoutDto> HandleAsync(StartSubscriptionCheckoutCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var workspace = desk.Workspace;
        var subscription = await workspace.LoadLiveSubscriptionAsync(cancellationToken);
        DomainException.ThrowIf(subscription.CancelAtPeriodEnd, BillingApplicationErrors.CancellationScheduled);
        var (plan, amount) = await desk.DueAsync(subscription, cancellationToken)
            ?? throw new DomainException(BillingApplicationErrors.NothingToPay);
        var options = await desk.OptionsAsync(amount.Currency, cancellationToken);
        DomainException.ThrowIf(options.Count == 0, BillingApplicationErrors.OnlinePaymentUnavailable);
        var provider = string.IsNullOrWhiteSpace(command.Provider)
            ? options[0].Provider
            : options.FirstOrDefault(option => option.Provider == command.Provider.Trim().ToLowerInvariant())?.Provider
                ?? throw new DomainException(BillingApplicationErrors.OnlinePaymentProviderNotOffered);

        if (await desk.Checkouts.FindPendingAsync(subscription.TenantId, cancellationToken) is { } pending)
        {
            await desk.SettleAsync(pending, cancellationToken);
            await workspace.SaveAsync(cancellationToken);
            if (pending.Status == SubscriptionCheckoutStatus.Pending || pending.Status == SubscriptionCheckoutStatus.Paid)
            {
                return SubscriptionCheckoutDto.From(pending);
            }
        }

        var checkout = SubscriptionCheckout.Start(subscription, amount, provider, clock);
        desk.Checkouts.Add(checkout);
        await workspace.SaveAsync(cancellationToken);

        CollectionStart started;
        try
        {
            var label = $"Abonnement {plan.Name}";
            started = await desk.Collector.StartAsync(
                provider,
                subscription.TenantId, checkout.Id, amount, label[..Math.Min(label.Length, CheckoutDesk.MaxLabelLength)], command.PayerPhone, cancellationToken);
        }
        catch (DomainException)
        {
            checkout.Fail(clock);
            await workspace.SaveAsync(cancellationToken);
            throw;
        }

        checkout.AttachReference(started.Reference, started.CheckoutUrl);
        await workspace.SaveAsync(cancellationToken);
        return SubscriptionCheckoutDto.From(checkout);
    }
}

/// <summary>Le restaurant qui attend : relit son règlement chez le prestataire et renvoie son état.</summary>
public sealed record RefreshSubscriptionCheckoutCommand(Guid CheckoutId) : ICommand<SubscriptionCheckoutDto>;

public sealed class RefreshSubscriptionCheckoutHandler(CheckoutDesk desk) : ICommandHandler<RefreshSubscriptionCheckoutCommand, SubscriptionCheckoutDto>
{
    public async Task<SubscriptionCheckoutDto> HandleAsync(RefreshSubscriptionCheckoutCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var checkout = (command.CheckoutId == Guid.Empty
                ? null
                : await desk.Checkouts.FindAsync(desk.Workspace.CurrentTenant, SubscriptionCheckoutId.From(command.CheckoutId), cancellationToken))
            ?? throw new NotFoundException(BillingApplicationErrors.CheckoutNotFound);
        await desk.SettleAsync(checkout, cancellationToken);
        await desk.Workspace.SaveAsync(cancellationToken);
        return SubscriptionCheckoutDto.From(checkout);
    }
}

/// <summary>Le job : relit un règlement en attente (tenant désigné par le job, jamais par une requête).</summary>
public sealed record SettleSubscriptionCheckoutCommand(Guid TenantId, Guid CheckoutId) : ICommand<SubscriptionCheckoutStatus>;

public sealed class SettleSubscriptionCheckoutHandler(CheckoutDesk desk) : ICommandHandler<SettleSubscriptionCheckoutCommand, SubscriptionCheckoutStatus>
{
    public async Task<SubscriptionCheckoutStatus> HandleAsync(SettleSubscriptionCheckoutCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var checkout = await desk.Checkouts.FindAsync(TenantId.From(command.TenantId), SubscriptionCheckoutId.From(command.CheckoutId), cancellationToken)
            ?? throw new NotFoundException(BillingApplicationErrors.CheckoutNotFound);
        await desk.SettleAsync(checkout, cancellationToken);
        await desk.Workspace.SaveAsync(cancellationToken);
        return checkout.Status;
    }
}

/// <param name="Available">La plateforme propose le paiement en ligne.</param>
/// <param name="Due">Une période est à régler maintenant (montant dans <see cref="Amount"/>).</param>
/// <param name="Provider">Le premier prestataire proposé.</param>
/// <param name="Pending">Le règlement en cours, s'il y en a un.</param>
/// <param name="Options">Les prestataires entre lesquels le restaurant choisit, dans l'ordre de la plateforme.</param>
public sealed record OnlinePaymentDto(
    bool Available, bool Due, decimal? Amount, string? CurrencyCode, string? Provider, SubscriptionCheckoutDto? Pending,
    IReadOnlyList<OnlinePaymentOptionDto>? Options = null);

/// <param name="Methods">Les moyens de paiement du prestataire (Card, MobileMoney, PayPal…).</param>
public sealed record OnlinePaymentOptionDto(string Provider, string Name, IReadOnlyList<string> Methods);

public sealed record GetOnlinePaymentQuery : IQuery<OnlinePaymentDto>;

public sealed class GetOnlinePaymentHandler(CheckoutDesk desk, IClock clock) : IQueryHandler<GetOnlinePaymentQuery, OnlinePaymentDto>
{
    public async Task<OnlinePaymentDto> HandleAsync(GetOnlinePaymentQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = desk.Workspace.CurrentTenant;
        var subscription = await desk.Workspace.Subscriptions.FindLiveAsync(tenant, cancellationToken);
        var due = subscription is null ? null : await desk.DueAsync(subscription, cancellationToken);
        var pending = await desk.Checkouts.FindPendingAsync(tenant, cancellationToken);
        var currency = due?.Amount.Currency ?? await desk.Workspace.Tenants.DefaultCurrencyAsync(tenant, cancellationToken);
        var options = currency is null ? [] : await desk.OptionsAsync(currency, cancellationToken);
        return new OnlinePaymentDto(
            options.Count > 0,
            due is not null,
            due?.Amount.Amount,
            due?.Amount.Currency.Code,
            options.FirstOrDefault()?.Provider,
            pending is not null && pending.IsOpenAt(clock.UtcNow) ? SubscriptionCheckoutDto.From(pending) : null,
            options);
    }
}

/// <summary>Les prestataires des abonnements d'une devise, vus par la plateforme.</summary>
/// <param name="Provider">Le premier prestataire proposé ; null si aucun.</param>
/// <param name="Chosen">Choisis par la plateforme (faux : réglage de l'hôte, ou rien).</param>
/// <param name="Ready">Au moins un prestataire proposé peut encaisser maintenant dans cette devise.</param>
/// <param name="Candidates">Les prestataires en ligne du catalogue et ce qui manque à chacun pour encaisser dans la devise.</param>
/// <param name="Providers">Les prestataires proposés au restaurant, dans l'ordre (choix de la plateforme, sinon réglage de l'hôte).</param>
public sealed record OnlinePaymentRouteDto(
    string CurrencyCode, string? Provider, bool Chosen, bool Ready, DateTimeOffset? UpdatedAtUtc, IReadOnlyList<CollectorCandidate>? Candidates = null,
    IReadOnlyList<string>? Providers = null);

/// <summary>
/// Plateforme : les prestataires proposés pour chaque devise vendue (devises des plans actifs et devises déjà réglées), et
/// s'ils sont prêts à encaisser.
/// </summary>
public sealed record ListOnlinePaymentRoutesQuery : IQuery<IReadOnlyList<OnlinePaymentRouteDto>>;

public sealed class ListOnlinePaymentRoutesHandler(BillingWorkspace workspace, IOnlinePaymentRouteRepository routes, ISubscriptionCollector collector)
    : IQueryHandler<ListOnlinePaymentRoutesQuery, IReadOnlyList<OnlinePaymentRouteDto>>
{
    public async Task<IReadOnlyList<OnlinePaymentRouteDto>> HandleAsync(ListOnlinePaymentRoutesQuery query, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> fallback = collector.DefaultProvider is { } byHost ? [OnlinePaymentRoute.Normalize(byHost)] : [];
        var chosen = (await routes.ListAsync(cancellationToken)).ToDictionary(route => route.CurrencyCode, StringComparer.Ordinal);
        var currencies = (await workspace.Plans.ListAsync(true, cancellationToken))
            .Select(plan => plan.Price.Currency.Code)
            .Concat(chosen.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var result = new List<OnlinePaymentRouteDto>(currencies.Count);
        foreach (var code in currencies)
        {
            var route = chosen.GetValueOrDefault(code);
            var providers = route?.Providers ?? fallback;
            var currency = Currency.FromCode(code);
            var ready = false;
            foreach (var provider in providers)
            {
                ready = ready || await collector.IsReadyAsync(provider, currency, cancellationToken);
            }

            result.Add(new OnlinePaymentRouteDto(
                code, providers.FirstOrDefault(), route is not null, ready, route?.UpdatedAtUtc, await collector.CandidatesAsync(currency, cancellationToken), providers));
        }

        return result;
    }
}

/// <summary>
/// Plateforme : choisit les prestataires proposés aux restaurants pour payer leur abonnement dans une devise, dans l'ordre
/// (aucun : retour au réglage de l'hôte). Chacun doit être prêt à encaisser dans cette devise.
/// </summary>
public sealed record SetOnlinePaymentRouteCommand(string CurrencyCode, IReadOnlyList<string>? Providers) : ICommand;

public sealed class SetOnlinePaymentRouteHandler(
    IOnlinePaymentRouteRepository routes, ISubscriptionCollector collector, IBillingUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<SetOnlinePaymentRouteCommand>
{
    public async Task HandleAsync(SetOnlinePaymentRouteCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        Currency currency;
        try
        {
            currency = Currency.FromCode(command.CurrencyCode?.Trim().ToUpperInvariant() ?? string.Empty);
        }
        catch (DomainException)
        {
            throw new DomainException(BillingApplicationErrors.CurrencyInvalid);
        }

        var route = await routes.FindAsync(currency.Code, cancellationToken);
        var requested = (command.Providers ?? []).Where(provider => !string.IsNullOrWhiteSpace(provider)).ToList();
        if (requested.Count == 0)
        {
            if (route is not null)
            {
                routes.Remove(route);
            }
        }
        else
        {
            var providers = OnlinePaymentRoute.NormalizeAll(requested);
            foreach (var provider in providers)
            {
                DomainException.ThrowIf(!await collector.IsReadyAsync(provider, currency, cancellationToken), BillingApplicationErrors.OnlinePaymentProviderNotReady);
            }

            if (route is null)
            {
                routes.Add(OnlinePaymentRoute.Create(currency, providers, clock));
            }
            else
            {
                route.Change(providers, clock);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
