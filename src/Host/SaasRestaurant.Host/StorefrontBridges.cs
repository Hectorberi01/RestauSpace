using Billing.Application;
using Catalog.Application.Queries;
using Customers.Application.Commands;
using Customers.Application.Queries;
using Orders.Application.Commands;
using Orders.Application.Queries;
using Orders.Domain;
using Payments.Application.Commands;
using Payments.Application.Queries;
using Payments.Domain;
using Storefront.Application.Checkout;
using Catalog.Domain;
using Files.Application.Queries;
using Files.Application.Storage;
using Billing.Application.Queries;
using Locations.Application.Queries;
using SaasRestaurant.BuildingBlocks.Application;
using Storefront.Application.Abstractions;
using Storefront.Application.Queries;
using Storefront.Domain;
using Delivery.Application.Commands;
using Delivery.Application.Queries;
using Promotions.Application.Commands;
using Promotions.Application.Queries;

namespace SaasRestaurant.Host;

// Les ports déclarés par Storefront (vente en ligne) : le restaurant (Tenants), ses établissements et leur ouverture
// (Locations), ses options (Billing). Aucun module n'en référence un autre : seul le host les relie.

/// <summary>Storefront -> Tenants : enseigne, devise, mode de prix et acceptation des écritures. Inconnu = null.</summary>
internal sealed class StorefrontTenantsFromTenants(CachedTenants tenants) : IStorefrontTenants
{
    public async Task<StorefrontRestaurant?> FindAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await tenants.FindAsync(tenantId, cancellationToken) is { } tenant
            ? new StorefrontRestaurant(
                string.IsNullOrWhiteSpace(tenant.Branding.DisplayName) ? tenant.Name : tenant.Branding.DisplayName,
                tenant.Branding.LogoUrl,
                tenant.Branding.PrimaryColor,
                tenant.DefaultCurrencyCode,
                tenant.PricesIncludeTax,
                tenant.CanAcceptBusinessWrites,
                tenant.PublicProfile is { } profile
                    ? new StorefrontProfile(
                        profile.Tagline,
                        profile.Description,
                        profile.CoverImageUrl,
                        profile.Links.Select(link => new StorefrontLink(link.Network.ToString(), link.Url)).ToList())
                    : null)
            : null;
}

/// <summary>
/// Storefront -> Locations : les établissements non archivés et leur ouverture. Fail-closed : un tenant autre que celui
/// du contexte n'a aucun établissement.
/// </summary>
internal sealed class StorefrontLocationsFromLocations(
    ITenantContext tenantContext, IQueryHandler<ListLocationAvailabilityQuery, IReadOnlyList<LocationAvailabilityDto>> locations)
    : IStorefrontLocations
{
    public async Task<IReadOnlyList<StorefrontLocation>> ListAsync(TenantId tenantId, DateTimeOffset atUtc, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return [];
        }

        return (await locations.HandleAsync(new ListLocationAvailabilityQuery(atUtc), cancellationToken))
            .Select(location => new StorefrontLocation(
                LocationId.From(location.LocationId),
                location.Name,
                location.Street,
                location.City,
                location.TimeZoneId,
                location.IsOperational,
                location.IsOpenNow,
                location.NextOpeningUtc,
                location.WeeklyHours.Select(slot => new StorefrontOpeningSlot(slot.Day, slot.Start, slot.End, slot.EndsNextDay)).ToList(),
                location.TemporaryClosureReason,
                new StorefrontPlace(
                    location.PostalCode, location.CountryCode, location.Latitude, location.Longitude,
                    location.Contact?.Phone, location.Contact?.WhatsApp, location.Contact?.Email),
                (location.UpcomingExceptions ?? [])
                    .Select(day => new StorefrontSpecialDay(
                        day.Date, day.IsClosed, day.Ranges.Select(range => new StorefrontTimeRange(range.Start, range.End, range.EndsNextDay)).ToList(), day.Reason))
                    .ToList()))
            .ToList();
    }
}

/// <summary>
/// Storefront -> Billing : un canal est ouvert si le droit <c>storefront.web</c>, <c>storefront.mobile_app</c> ou
/// <c>storefront.kiosk</c> est activé (plan + options) ; sa limite est le nombre de bornes appairées. Fail-closed : un tenant autre que celui du contexte n'a droit à rien.
/// </summary>
internal sealed class StorefrontEntitlementsFromBilling(ITenantContext tenantContext, IQueryHandler<GetFeatureLimitQuery, FeatureLimitDto> limits)
    : IStorefrontEntitlements
{
    public async Task<bool> AllowsAsync(TenantId tenantId, SalesAppKind channel, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return false;
        }

        var feature = Feature(channel);
        return feature is not null && (await limits.HandleAsync(new GetFeatureLimitQuery(feature), cancellationToken)).Enabled;
    }

    public async Task<long?> LimitAsync(TenantId tenantId, SalesAppKind channel, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId || Feature(channel) is not { } feature)
        {
            return null;
        }

        var limit = await limits.HandleAsync(new GetFeatureLimitQuery(feature), cancellationToken);
        return limit.Enabled ? limit.Limit : null;
    }

    private static string? Feature(SalesAppKind channel) => channel switch
    {
        SalesAppKind.Web => BillingFeatures.StorefrontWeb,
        SalesAppKind.MobileApp => BillingFeatures.StorefrontMobileApp,
        SalesAppKind.Kiosk => BillingFeatures.StorefrontKiosk,
        _ => null,
    };
}

/// <summary>
/// Storefront -> Catalog et Files : le catalogue commandable de l'établissement pour le canal (même valeur numérique),
/// avec les liens publics des photos des produits. Fail-closed : un tenant autre que celui du contexte n'a aucun catalogue.
/// </summary>
internal sealed class StorefrontCatalogFromCatalog(
    ITenantContext tenantContext,
    IQueryHandler<GetOrderableCatalogQuery, OrderableCatalogDto> catalog,
    IQueryHandler<ListPublicImagesQuery, IReadOnlyList<PublicImageDto>> images) : IStorefrontCatalog
{
    public async Task<StorefrontCatalogContent> GetAsync(TenantId tenantId, LocationId locationId, SalesAppKind channel, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return new StorefrontCatalogContent([], [], []);
        }

        var orderable = await catalog.HandleAsync(new GetOrderableCatalogQuery(locationId.Value, (OnlineChannel)(int)channel), cancellationToken);
        var photos = (await images.HandleAsync(
                new ListPublicImagesQuery(FileOwnerTypes.CatalogProduct, orderable.Products.Select(product => product.Id).ToList()), cancellationToken))
            .ToLookup(image => image.OwnerId);

        return new StorefrontCatalogContent(
            orderable.Menus
                .Select(menu => new StorefrontMenuDto(
                    menu.Id, menu.Name, menu.Sections.Select(section => new StorefrontSectionDto(section.Id, section.Name, section.ProductIds)).ToList()))
                .ToList(),
            orderable.Products
                .Select(product => new StorefrontProductDto(
                    product.Id,
                    product.Name,
                    product.Description,
                    product.Price,
                    product.IsAvailable,
                    product.Variants.Select(variant => new StorefrontVariantDto(variant.Id, variant.Name, variant.Price, variant.IsAvailable)).ToList(),
                    product.ModifierGroupIds,
                    product.Allergens,
                    photos[product.Id].Select(image => image.Url).ToList(),
                    photos[product.Id].Select(image => image.ThumbnailUrl).ToList(),
                    (product.FormulaSteps ?? [])
                        .Select(step => new StorefrontFormulaStepDto(
                            step.Id,
                            step.Name,
                            step.MinChoices,
                            step.MaxChoices,
                            step.Choices
                                .Select(choice => new StorefrontFormulaChoiceDto(
                                    choice.ProductId, choice.VariantId, choice.Name, choice.VariantName, choice.Supplement, choice.IsAvailable,
                                    choice.ModifierGroupIds, choice.Allergens))
                                .ToList()))
                        .ToList()))
                .ToList(),
            orderable.ModifierGroups
                .Select(group => new StorefrontModifierGroupDto(
                    group.Id,
                    group.Name,
                    group.MinSelections,
                    group.MaxSelections,
                    group.Modifiers.Select(modifier => new StorefrontModifierDto(modifier.Id, modifier.Name, modifier.PriceDelta, modifier.IsAvailable)).ToList()))
                .ToList());
    }
}

/// <summary>
/// Storefront -> Orders (et Promotions, Delivery pour le checkout) : devis, création et validation de la commande,
/// acceptation, annulation, lecture. Les cas d'usage répondent pour le tenant du contexte (clé publique ou job) ;
/// fail-closed sur tout autre tenant. Le canal et l'application source sont posés ici, côté serveur, jamais par le
/// client. Coupon et frais de livraison sont posés de façon SYNCHRONE sur le brouillon, avant la validation : les flux
/// de l'outbox (PromotionApplied, FulfillmentCreated) les retrouvent déjà posés et ne changent rien.
/// </summary>
internal sealed class StorefrontOrdersFromOrders(
    ITenantContext tenantContext,
    IQueryHandler<QuoteOrderQuery, OrderQuoteDto> quote,
    ICommandHandler<CreateOrderCommand, Guid> create,
    ICommandHandler<AddOrderItemCommand, Guid> addItem,
    ICommandHandler<ApplyPromotionToOrderCommand, PromotionApplicationDto> applyPromotion,
    ICommandHandler<ApplyExternalDiscountCommand> applyDiscount,
    ICommandHandler<CreateDeliveryCommand, Guid> createDelivery,
    IQueryHandler<GetFulfillmentQuery, FulfillmentDto> fulfillments,
    ICommandHandler<ApplyDeliveryFeeCommand> applyDeliveryFee,
    IQueryHandler<Loyalty.Application.Queries.GetLoyaltyAccountByCustomerQuery, Loyalty.Application.Queries.LoyaltyAccountDto> loyaltyAccounts,
    ICommandHandler<Loyalty.Application.Commands.RedeemRewardForOrderCommand, Loyalty.Application.Commands.RewardRedemptionDto> redeemReward,
    IQueryHandler<ListOrdersQuery, SaasRestaurant.BuildingBlocks.Application.PagedResult<OrderSummaryDto>> listOrders,
    ICommandHandler<PlaceOrderCommand> place,
    ICommandHandler<AcceptOrderCommand> accept,
    ICommandHandler<CancelOrderCommand> cancel,
    IQueryHandler<GetOrderByIdQuery, OrderDto> orders) : IStorefrontOrders
{
    public const string AbandonedReason = "Validation en ligne interrompue";

    public async Task<CartQuote> QuoteAsync(
        TenantId tenantId, LocationId locationId, ServiceMode mode, SalesAppKind channel, IReadOnlyList<CartLine> lines, CartAdjustments? adjustments = null,
        CancellationToken cancellationToken = default)
    {
        EnsureContext(tenantId);
        var discounts = new List<QuoteDiscountInput>();
        if (adjustments?.Discount is { } discount)
        {
            discounts.Add(new QuoteDiscountInput(DiscountSource.Promotion, discount.Amount, discount.PromotionId.ToString(), discount.Label));
        }

        if (adjustments?.Reward is { } reward)
        {
            discounts.Add(new QuoteDiscountInput(DiscountSource.LoyaltyReward, reward.Amount, reward.RewardId.ToString(), reward.Label));
        }

        var result = await quote.HandleAsync(
            new QuoteOrderQuery(
                locationId.Value,
                (OrderType)(int)mode,
                lines.Select(line => new QuoteItemInput(line.ProductId, line.Quantity, line.VariantId, line.ModifierIds, line.Note, Components(line))).ToList(),
                discounts,
                adjustments?.DeliveryFee,
                channel switch
                {
                    SalesAppKind.MobileApp => OrderChannel.MobileApp,
                    SalesAppKind.Kiosk => OrderChannel.Kiosk,
                    _ => OrderChannel.Web,
                }),
            cancellationToken);
        return new CartQuote(
            result.CurrencyCode, result.PriceTaxMode == PriceTaxMode.TaxInclusive, result.Items.Select(Line).ToList(), result.SubTotal, result.DiscountTotal,
            result.TaxTotal, result.Total, result.DeliveryFee);
    }

    public async Task<PlacedOrder> PlaceAsync(TenantId tenantId, OrderToPlace order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        EnsureContext(tenantId);
        var channel = order.TableSessionId is not null ? OrderChannel.QrCode : order.Channel switch
        {
            SalesAppKind.Web => OrderChannel.Web,
            SalesAppKind.MobileApp => OrderChannel.MobileApp,
            SalesAppKind.Kiosk => OrderChannel.Kiosk,
            _ => throw new InvalidOperationException("Unknown sales channel."),
        };
        var id = await create.HandleAsync(
            new CreateOrderCommand(
                order.LocationId.Value, (OrderType)(int)order.Mode, order.TableSessionId?.Value, order.CustomerId, new OrderOrigin(channel, order.AppId.Value),
                order.ScheduledForUtc),
            cancellationToken);
        try
        {
            foreach (var line in order.Lines)
            {
                await addItem.HandleAsync(
                    new AddOrderItemCommand(id, line.ProductId, line.Quantity, line.VariantId, line.ModifierIds, line.Note, Components(line)), cancellationToken);
            }

            if (order.CouponCode is { } code)
            {
                // Compté par Promotions (usage du coupon et de sa promotion), puis posé sur la commande avec la même
                // provenance que le flux PromotionApplied : celui-ci le trouvera déjà posé.
                var applied = await applyPromotion.HandleAsync(new ApplyPromotionToOrderCommand(id, CouponCode: code), cancellationToken);
                await applyDiscount.HandleAsync(
                    new ApplyExternalDiscountCommand(id, DiscountSource.Promotion, applied.DiscountAmount, applied.CurrencyCode, applied.DiscountReference, "Coupon " + code),
                    cancellationToken);
            }

            if (order.RewardId is { } rewardId && order.CustomerId is { } customerId)
            {
                // Points débités par Loyalty (référence = la commande), puis remise posée avec la même provenance que le
                // flux RewardRedeemed : celui-ci la trouvera déjà posée.
                var account = await loyaltyAccounts.HandleAsync(new Loyalty.Application.Queries.GetLoyaltyAccountByCustomerQuery(customerId), cancellationToken);
                var redeemed = await redeemReward.HandleAsync(new Loyalty.Application.Commands.RedeemRewardForOrderCommand(account.Id, rewardId, id), cancellationToken);
                await applyDiscount.HandleAsync(
                    new ApplyExternalDiscountCommand(id, DiscountSource.LoyaltyReward, redeemed.DiscountAmount, redeemed.CurrencyCode, rewardId.ToString(), "Récompense fidélité"),
                    cancellationToken);
            }

            if (order.Delivery is { } delivery)
            {
                var fulfillmentId = await createDelivery.HandleAsync(
                    new CreateDeliveryCommand(
                        id, delivery.ZoneId, delivery.CustomerName, delivery.CustomerPhone, delivery.AddressLine, delivery.Landmark, delivery.Instructions,
                        delivery.DeliverAtUtc),
                    cancellationToken);
                var fulfillment = await fulfillments.HandleAsync(new GetFulfillmentQuery(fulfillmentId), cancellationToken);
                if (fulfillment.DeliveryFee is { } fee && fee > 0 && fulfillment.FeeCurrencyCode is { } currency)
                {
                    await applyDeliveryFee.HandleAsync(new ApplyDeliveryFeeCommand(id, fee, currency, fulfillmentId.ToString("D")), cancellationToken);
                }
            }

            await place.HandleAsync(new PlaceOrderCommand(id), cancellationToken);
        }
        catch
        {
            // Le brouillon ne doit pas rester ouvert : il est annulé, puis l'erreur d'origine remonte au client.
            try
            {
                await cancel.HandleAsync(new CancelOrderCommand(id, AbandonedReason), CancellationToken.None);
            }
            catch (Exception exception) when (exception is DomainException or NotFoundException or ConcurrencyException)
            {
            }

            throw;
        }

        var placed = await orders.HandleAsync(new GetOrderByIdQuery(id), cancellationToken);
        return new PlacedOrder(OrderId.From(id), placed.Number, placed.CurrencyCode, placed.Total);
    }

    public async Task<IReadOnlyList<StorefrontOrderSummary>> ListForCustomerAsync(
        TenantId tenantId, CustomerId customerId, int take, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return [];
        }

        var page = await listOrders.HandleAsync(new ListOrdersQuery(1, Math.Clamp(take, 1, ListOrdersQuery.MaxPageSize), CustomerId: customerId.Value), cancellationToken);
        return page.Items
            .Where(order => order.Status != OrderStatus.Draft)
            .Select(order => new StorefrontOrderSummary(
                OrderId.From(order.Id), order.Number, LocationId.From(order.LocationId), (ServiceMode)(int)order.Type, StatusOf(order.Status), order.CurrencyCode,
                order.Total, order.CreatedAtUtc))
            .ToList();
    }

    private static GuestOrderStatus StatusOf(OrderStatus status) => status switch
    {
        OrderStatus.Accepted => GuestOrderStatus.Accepted,
        OrderStatus.Preparing => GuestOrderStatus.Preparing,
        OrderStatus.Ready => GuestOrderStatus.Ready,
        OrderStatus.Completed => GuestOrderStatus.Completed,
        OrderStatus.Cancelled => GuestOrderStatus.Cancelled,
        _ => GuestOrderStatus.Received,
    };

    public Task AcceptAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default)
    {
        EnsureContext(tenantId);
        return accept.HandleAsync(new AcceptOrderCommand(orderId.Value), cancellationToken);
    }

    public Task CancelAsync(TenantId tenantId, OrderId orderId, string reason, CancellationToken cancellationToken = default)
    {
        EnsureContext(tenantId);
        return cancel.HandleAsync(new CancelOrderCommand(orderId.Value, reason), cancellationToken);
    }

    public async Task<StorefrontOrderView?> FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        OrderDto order;
        try
        {
            order = await orders.HandleAsync(new GetOrderByIdQuery(orderId.Value), cancellationToken);
        }
        catch (NotFoundException)
        {
            return null;
        }

        var status = StatusOf(order.Status);
        return new StorefrontOrderView(
            OrderId.From(order.Id), LocationId.From(order.LocationId), (ServiceMode)(int)order.Type, order.Number, status, order.CurrencyCode, order.Items.Select(Line).ToList(),
            order.Total, order.PlacedAtUtc, order.ScheduledForUtc, order.DiscountTotal, order.DeliveryFee);
    }

    private static IReadOnlyList<Orders.Application.Abstractions.OrderComponentInput>? Components(CartLine line) =>
        line.Components?.Select(component => new Orders.Application.Abstractions.OrderComponentInput(
            component.StepId, component.ProductId, component.VariantId, component.ModifierIds)).ToList();

    private static CartQuoteLine Line(OrderItemDto item) =>
        new(
            item.ProductId, item.VariantId, item.ProductName, item.VariantName, item.Quantity, item.UnitPrice,
            item.Modifiers.Select(modifier => new CartQuoteModifier(modifier.ModifierId, modifier.Name, modifier.PriceDelta)).ToList(),
            item.Note, item.LineTotal,
            (item.Components ?? [])
                .Select(component => new CartQuoteComponent(
                    component.StepName, component.ProductId, component.ProductName, component.VariantName, component.Supplement,
                    component.Modifiers.Select(modifier => new CartQuoteModifier(modifier.ModifierId, modifier.Name, modifier.PriceDelta)).ToList()))
                .ToList());

    private void EnsureContext(TenantId tenantId)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            throw new InvalidOperationException("Online orders can only be handled for the tenant of the current context.");
        }
    }
}

/// <summary>
/// Storefront -> Customers : la fiche du client du site, choisie par <see cref="Storefront.Application.Customers.CustomerContactMatch"/>
/// (jamais une fiche qui porte un contact non donné), sinon créée selon la règle de contact du restaurant. Une fiche
/// existante n'est jamais modifiée ; le consentement marketing n'est enregistré que sur une fiche créée ici, pour chaque
/// moyen de contact fourni (source « online-ordering »). Fail-closed sur le tenant.
/// </summary>
internal sealed class StorefrontCustomersFromCustomers(
    ITenantContext tenantContext,
    IQueryHandler<SearchCustomersQuery, IReadOnlyList<CustomerDto>> search,
    IQueryHandler<GetCustomerByIdQuery, CustomerDto> byId,
    ICommandHandler<CreateCustomerCommand, Guid> create,
    ICommandHandler<GrantConsentCommand> grant) : IStorefrontCustomers
{
    public const string ConsentSource = "online-ordering";

    public async Task<Guid> FindOrCreateAsync(TenantId tenantId, GuestCustomer customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);
        if (tenantContext.CurrentTenantId != tenantId)
        {
            throw new InvalidOperationException("A customer can only be found or created for the tenant of the current context.");
        }

        var email = string.IsNullOrWhiteSpace(customer.Email) ? null : customer.Email;
        var phone = string.IsNullOrWhiteSpace(customer.Phone) ? null : customer.Phone;
        if (await MatchAsync(email, phone, cancellationToken) is { } existing)
        {
            return existing;
        }

        // Langue du site retenue pour une nouvelle fiche seulement, et seulement si elle est l'une des langues du produit.
        var language = customer.Language?.Trim().ToLowerInvariant() is { } asked && Customers.Domain.CustomerPreference.SupportedLanguages.Contains(asked) ? asked : null;
        var id = await create.HandleAsync(
            new CreateCustomerCommand(customer.Name, email, phone, Preferences: language is null ? null : new PreferencesInput(language)), cancellationToken);

        if (customer.MarketingConsent)
        {
            foreach (var channel in new[] { (email, CommunicationChannel.Email), (phone, CommunicationChannel.Sms) }.Where(item => item.Item1 is not null).Select(item => item.Item2))
            {
                try
                {
                    await grant.HandleAsync(new GrantConsentCommand(id, channel, ConsentSource), cancellationToken);
                }
                catch (DomainException)
                {
                    // Déjà accordé, ou fiche sans ce moyen de contact : la commande n'en dépend pas.
                }
            }
        }

        return id;
    }

    public async Task<CustomerId?> FindByContactAsync(TenantId tenantId, string? email, string? phone, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        var found = await MatchAsync(
            string.IsNullOrWhiteSpace(email) ? null : email, string.IsNullOrWhiteSpace(phone) ? null : phone, cancellationToken);
        return found is { } id ? CustomerId.From(id) : null;
    }

    private async Task<Guid?> MatchAsync(string? email, string? phone, CancellationToken cancellationToken) =>
        Storefront.Application.Customers.CustomerContactMatch.Pick(
            email is null ? [] : await CandidatesAsync(new SearchCustomersQuery(Email: email, Take: SearchCustomersHandler.MaxTake), cancellationToken),
            phone is null ? [] : await CandidatesAsync(new SearchCustomersQuery(Phone: phone, Take: SearchCustomersHandler.MaxTake), cancellationToken));

    private async Task<IReadOnlyList<Storefront.Application.Customers.CustomerContactCandidate>> CandidatesAsync(
        SearchCustomersQuery query, CancellationToken cancellationToken) =>
        (await search.HandleAsync(query, cancellationToken))
            .Where(found => !found.IsAnonymized)
            .Select(found => new Storefront.Application.Customers.CustomerContactCandidate(found.Id, found.Email is not null, found.Phone is not null))
            .ToList();

    public async Task<CustomerProfile?> ProfileAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var customer = await byId.HandleAsync(new GetCustomerByIdQuery(customerId.Value), cancellationToken);
            return customer.IsAnonymized ? null : new CustomerProfile(customerId, customer.Name, customer.Email, customer.Phone);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    public async Task GrantMarketingConsentAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default)
    {
        if (await ProfileAsync(tenantId, customerId, cancellationToken) is not { } profile)
        {
            return;
        }

        foreach (var channel in new[] { (profile.Email, CommunicationChannel.Email), (profile.Phone, CommunicationChannel.Sms) }.Where(item => item.Item1 is not null))
        {
            try
            {
                await grant.HandleAsync(new GrantConsentCommand(customerId.Value, channel.Item2, ConsentSource), cancellationToken);
            }
            catch (DomainException)
            {
                // Déjà accordé : rien à faire.
            }
        }
    }
}

/// <summary>
/// Storefront -> Notifications : code de connexion du client (envoi, limites, vérification). Le code n'existe qu'en
/// mémoire de Notifications ; rien ici ne le garde ni ne le journalise. Fail-closed hors du tenant du contexte.
/// </summary>
internal sealed class StorefrontOtpFromNotifications(
    ITenantContext tenantContext,
    ICommandHandler<Notifications.Application.Commands.RequestOtpCommand, Notifications.Application.Commands.OtpRequested> request,
    ICommandHandler<Notifications.Application.Commands.VerifyOtpCommand> verify) : IStorefrontOtp
{
    public async Task<OtpChallengeIssued> RequestAsync(
        TenantId tenantId, CustomerSignInChannel channel, string address, string? language, CancellationToken cancellationToken = default)
    {
        EnsureContext(tenantId);
        var notificationChannel = channel switch
        {
            CustomerSignInChannel.Sms => Notifications.Domain.NotificationChannel.Sms,
            CustomerSignInChannel.WhatsApp => Notifications.Domain.NotificationChannel.WhatsApp,
            CustomerSignInChannel.Email => Notifications.Domain.NotificationChannel.Email,
            _ => throw new DomainException(Storefront.Application.Customers.CustomerAccountErrors.AddressInvalid),
        };
        var issued = await request.HandleAsync(
            new Notifications.Application.Commands.RequestOtpCommand(
                Storefront.Application.Customers.RequestCustomerCodeHandler.Purpose, notificationChannel, address, language),
            cancellationToken);
        return new OtpChallengeIssued(issued.ChallengeId, issued.ExpiresAtUtc);
    }

    public Task VerifyAsync(TenantId tenantId, Guid challengeId, string code, CancellationToken cancellationToken = default)
    {
        EnsureContext(tenantId);
        return verify.HandleAsync(new Notifications.Application.Commands.VerifyOtpCommand(challengeId, code), cancellationToken);
    }

    private void EnsureContext(TenantId tenantId)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            throw new InvalidOperationException("Sign-in codes can only be handled for the tenant of the current context.");
        }
    }
}

/// <summary>
/// Storefront -> Loyalty : programme, solde et récompenses actives (dans la devise du restaurant) d'un client. Sans compte
/// de fidélité, le solde est nul ; un compte suspendu ou fermé ne peut rien utiliser. Fail-closed hors du tenant du contexte.
/// </summary>
internal sealed class StorefrontLoyaltyFromLoyalty(
    ITenantContext tenantContext,
    IQueryHandler<Loyalty.Application.Queries.GetLoyaltyProgramQuery, Loyalty.Application.Queries.LoyaltyProgramDto> program,
    IQueryHandler<Loyalty.Application.Queries.GetLoyaltyAccountByCustomerQuery, Loyalty.Application.Queries.LoyaltyAccountDto> accounts,
    IQueryHandler<Loyalty.Application.Queries.ListRewardsQuery, IReadOnlyList<Loyalty.Application.Queries.RewardDto>> rewards) : IStorefrontLoyalty
{
    private static readonly CustomerLoyalty None = new(false, 0, 0m, []);

    public async Task<CustomerLoyalty> SummaryAsync(TenantId tenantId, CustomerId customerId, string currencyCode, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return None;
        }

        Loyalty.Application.Queries.LoyaltyProgramDto settings;
        try
        {
            settings = await program.HandleAsync(new Loyalty.Application.Queries.GetLoyaltyProgramQuery(), cancellationToken);
        }
        catch (NotFoundException)
        {
            return None;
        }

        if (!settings.IsEnabled)
        {
            return None;
        }

        Loyalty.Application.Queries.LoyaltyAccountDto? account;
        try
        {
            account = await accounts.HandleAsync(new Loyalty.Application.Queries.GetLoyaltyAccountByCustomerQuery(customerId.Value), cancellationToken);
        }
        catch (NotFoundException)
        {
            account = null;
        }

        var usable = account is { Status: Loyalty.Domain.LoyaltyAccountStatus.Active };
        var balance = account?.Balance ?? 0;
        var offered = (await rewards.HandleAsync(new Loyalty.Application.Queries.ListRewardsQuery(ActiveOnly: true), cancellationToken))
            .Where(reward => string.Equals(reward.CurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(reward => reward.PointCost)
            .Select(reward => new LoyaltyRewardOption(reward.Id, reward.Name, reward.PointCost, reward.DiscountAmount, usable && balance >= reward.PointCost))
            .ToList();
        return new CustomerLoyalty(true, balance, settings.PointsPerCurrencyUnit, offered);
    }
}

/// <summary>
/// Storefront -> Payments : prestataires en ligne acceptés dans l'établissement, démarrage du paiement d'une commande
/// invité, état de ses paiements, annulation des paiements en attente et remboursement. Les cas d'usage de Payments
/// répondent pour le tenant du contexte (clé publique ou job) ; fail-closed sur tout autre tenant. La page de retour,
/// déjà vérifiée par Storefront, n'est jamais une valeur que le client impose à Payments sans ce contrôle.
/// </summary>
internal sealed class StorefrontPaymentsFromPayments(
    ITenantContext tenantContext,
    IQueryHandler<GetAvailablePaymentMethodsQuery, IReadOnlyList<AvailablePaymentMethodDto>> available,
    IQueryHandler<GetOrderPaymentsQuery, OrderPaymentsDto> orderPayments,
    ICommandHandler<StartOnlinePaymentCommand, OnlinePaymentStarted> start,
    ICommandHandler<RetryPaymentCommand, OnlinePaymentStarted> retry,
    ICommandHandler<CancelPaymentCommand> cancel,
    ICommandHandler<RequestRefundCommand, Guid> refund) : IStorefrontPayments
{
    public const string ReplacedReason = "Nouvel essai du client";

    public async Task<IReadOnlyList<OnlinePaymentOption>> OptionsAsync(
        TenantId tenantId, LocationId locationId, string currencyCode, decimal amount, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return [];
        }

        // Les moyens que Payments propose pour CETTE commande : établissement, montant, devise, pays, en ligne seulement.
        return (await available.HandleAsync(new GetAvailablePaymentMethodsQuery(locationId.Value, amount, currencyCode, PaymentChannel.Online), cancellationToken))
            .GroupBy(method => (method.Provider, method.DisplayName))
            .Select(group => new OnlinePaymentOption(group.Key.Provider, group.Key.DisplayName, group.Select(method => method.Method.ToString()).Distinct().ToList()))
            .OrderBy(option => option.DisplayName, StringComparer.CurrentCulture)
            .ToList();
    }

    public async Task<StartedPayment> StartAsync(TenantId tenantId, OrderId orderId, OnlinePaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureContext(tenantId);
        PaymentMethodType? method = Enum.TryParse<PaymentMethodType>(request.Method, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : null;
        var existing = await orderPayments.HandleAsync(new GetOrderPaymentsQuery(orderId.Value), cancellationToken);

        // Un paiement échoué garde son montant réservé : on le reprend. Un paiement resté en attente (client revenu sans
        // payer) est remplacé, pour qu'un seul lien reste valable.
        var failed = existing.Payments.FirstOrDefault(payment => !payment.IsOffline && payment.Status == PaymentStatus.Failed
            && string.Equals(payment.Provider, request.Provider, StringComparison.OrdinalIgnoreCase));
        if (failed is not null)
        {
            var retried = await retry.HandleAsync(new RetryPaymentCommand(failed.Id, request.PayerPhone, request.ReturnUrl), cancellationToken);
            return new StartedPayment(retried.PaymentId, retried.CheckoutUrl);
        }

        foreach (var payment in existing.Payments.Where(payment => !payment.IsOffline && payment.Status is PaymentStatus.Pending or PaymentStatus.Failed))
        {
            await cancel.HandleAsync(new CancelPaymentCommand(payment.Id, ReplacedReason), cancellationToken);
        }

        var started = await start.HandleAsync(
            new StartOnlinePaymentCommand(orderId.Value, request.Provider, null, method, request.PayerPhone, request.ReturnUrl), cancellationToken);
        return new StartedPayment(started.PaymentId, started.CheckoutUrl);
    }

    public async Task<OrderPaymentSummary> SummaryAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default)
    {
        EnsureContext(tenantId);
        var payments = await orderPayments.HandleAsync(new GetOrderPaymentsQuery(orderId.Value), cancellationToken);
        return new OrderPaymentSummary(
            payments.SettledAmount,
            payments.AmountDue,
            payments.Payments.Any(payment => !payment.IsOffline && payment.Status is PaymentStatus.Pending or PaymentStatus.Authorized),
            payments.Payments.Any(payment => payment.RefundedAmount > 0 || payment.PendingRefundAmount > 0));
    }

    public async Task CancelPendingAsync(TenantId tenantId, OrderId orderId, string reason, CancellationToken cancellationToken = default)
    {
        EnsureContext(tenantId);
        var payments = await orderPayments.HandleAsync(new GetOrderPaymentsQuery(orderId.Value), cancellationToken);
        foreach (var payment in payments.Payments.Where(payment => !payment.IsOffline && payment.Status is PaymentStatus.Pending or PaymentStatus.Failed))
        {
            await cancel.HandleAsync(new CancelPaymentCommand(payment.Id, reason), cancellationToken);
        }
    }

    public async Task RefundAllAsync(TenantId tenantId, OrderId orderId, string reason, CancellationToken cancellationToken = default)
    {
        EnsureContext(tenantId);
        var payments = await orderPayments.HandleAsync(new GetOrderPaymentsQuery(orderId.Value), cancellationToken);
        foreach (var payment in payments.Payments.Where(payment => payment.RefundableAmount > 0))
        {
            await refund.HandleAsync(new RequestRefundCommand(payment.Id, reason), cancellationToken);
        }
    }

    private void EnsureContext(TenantId tenantId)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            throw new InvalidOperationException("Online payments can only be handled for the tenant of the current context.");
        }
    }
}

/// <summary>
/// Payments -> Storefront, par l'outbox : un changement d'encaissement d'une commande (capture, remboursement) est
/// signalé à la vente en ligne, pour le tenant du message. Une commande qui attendait son paiement en ligne et n'a plus
/// rien à payer est alors acceptée, ou mise en attente d'acceptation. Toute autre commande est ignorée.
/// </summary>
internal sealed class StorefrontPaymentFlow(IServiceScopeFactory scopes) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if ((message.Module, message.Type) is not ("payments", "PaymentSettlementChanged"))
        {
            return;
        }

        var changed = message.Read<SaasRestaurant.Contracts.Payments.PaymentSettlementChanged>();
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(changed.TenantId));
        await services.GetRequiredService<ICommandHandler<RecordOnlinePaymentCommand>>()
            .HandleAsync(new RecordOnlinePaymentCommand(changed.OrderId), cancellationToken);
    }
}

/// <summary>
/// Storefront -> Delivery : les zones actives d'un établissement qui livre, et l'avancement de la fiche de livraison d'une
/// commande, sans adresse ni téléphone ni livreur. Fail-closed : un tenant autre que celui du contexte n'a rien.
/// </summary>
internal sealed class StorefrontDeliveryFromDelivery(
    ITenantContext tenantContext,
    IQueryHandler<GetDeliverySettingsQuery, DeliverySettingsDto> settings,
    IQueryHandler<GetOrderFulfillmentQuery, FulfillmentDto> fulfillments) : IStorefrontDelivery
{
    public async Task<StorefrontDeliveryOffer?> OfferAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        DeliverySettingsDto found;
        try
        {
            found = await settings.HandleAsync(new GetDeliverySettingsQuery(locationId.Value), cancellationToken);
        }
        catch (NotFoundException)
        {
            return null;
        }

        var zones = found.Zones
            .Where(zone => zone.IsActive)
            .Select(zone => new StorefrontDeliveryZone(zone.Id, zone.Name, zone.Fee, zone.MinimumOrder, zone.EstimatedMinutes))
            .ToList();
        return found.DeliveryEnabled && zones.Count > 0 ? new StorefrontDeliveryOffer(found.DeliveryLeadMinutes, zones) : null;
    }

    public async Task<StorefrontDeliveryTracking?> TrackAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var fulfillment = await fulfillments.HandleAsync(new GetOrderFulfillmentQuery(orderId.Value), cancellationToken);
            return fulfillment.Mode == Delivery.Domain.FulfillmentMode.Delivery
                ? new StorefrontDeliveryTracking(fulfillment.ZoneName ?? string.Empty, fulfillment.DeliveryFee ?? 0m, fulfillment.Status.ToString(), fulfillment.PromisedAtUtc)
                : null;
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}

/// <summary>Storefront -> Promotions : ce que vaudrait un coupon sur un panier, sans rien compter. Fail-closed : refusé hors du tenant du contexte.</summary>
internal sealed class StorefrontCouponsFromPromotions(ITenantContext tenantContext, IQueryHandler<PreviewCartCouponQuery, CartCouponPreviewDto> preview)
    : IStorefrontCoupons
{
    public async Task<CouponPreview> PreviewAsync(
        TenantId tenantId, LocationId locationId, ServiceMode mode, string currencyCode, IReadOnlyList<CartQuoteLine> lines, string code,
        CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return new CouponPreview(code, false, Promotions.Application.PromotionsApplicationErrors.CouponNotFound.Code, null, null, 0m);
        }

        var result = await preview.HandleAsync(
            new PreviewCartCouponQuery(
                locationId.Value,
                (Promotions.Domain.OrderType)(int)mode,
                currencyCode,
                lines.Select(line => new CartLineInput(line.ProductId, line.LineTotal)).ToList(),
                code),
            cancellationToken);
        return new CouponPreview(result.Code, result.IsValid, result.RefusalCode, result.PromotionId, result.PromotionName, result.Discount);
    }
}

/// <summary>
/// Storefront -> Floor : la table d'un QR code et la session où rattacher une commande passée à table (celle en cours,
/// sinon une nouvelle, pour un convive ; deux commandes simultanées se retrouvent sur la même session).
/// </summary>
internal sealed class StorefrontTablesFromFloor(
    ITenantContext tenantContext,
    IQueryHandler<Floor.Application.Commands.FindTableByQrTokenQuery, Floor.Application.Commands.TableByQrDto?> find,
    ICommandHandler<Floor.Application.Commands.OpenTableSessionCommand, Guid> open,
    IQueryHandler<Floor.Application.Queries.ListOpenTableSessionsQuery, IReadOnlyList<Floor.Application.Queries.TableSessionDto>> sessions)
    : IStorefrontTables
{
    public async Task<StorefrontTable?> FindByQrTokenAsync(TenantId tenantId, string token, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        var table = await find.HandleAsync(new Floor.Application.Commands.FindTableByQrTokenQuery(token), cancellationToken);
        return table is null ? null : new StorefrontTable(table.TableId, LocationId.From(table.LocationId), table.Label, table.AreaName, table.IsInService);
    }

    public async Task<TableSessionId> SeatAsync(TenantId tenantId, Guid tableId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            throw new InvalidOperationException("Another tenant's table.");
        }

        if (await OpenSessionOfAsync(tableId, cancellationToken) is { } current)
        {
            return current;
        }

        try
        {
            return TableSessionId.From(await open.HandleAsync(new Floor.Application.Commands.OpenTableSessionCommand(tableId, 1), cancellationToken));
        }
        catch (DomainException conflict) when (conflict.Error == Floor.Domain.TableSessionErrors.TableAlreadyOccupied)
        {
            return await OpenSessionOfAsync(tableId, cancellationToken)
                ?? throw new DomainException(Floor.Domain.TableSessionErrors.TableAlreadyOccupied, conflict);
        }
    }

    private async Task<TableSessionId?> OpenSessionOfAsync(Guid tableId, CancellationToken cancellationToken) =>
        (await sessions.HandleAsync(new Floor.Application.Queries.ListOpenTableSessionsQuery(null), cancellationToken))
            .FirstOrDefault(session => session.TableId == tableId) is { } session
            ? TableSessionId.From(session.Id)
            : null;
}

/// <summary>
/// Storefront -> Reviews : questionnaires publiés ou clos (jamais un brouillon) et dépôt d'une réponse anonyme, pour le
/// tenant de la clé publique. Les erreurs de validation des réponses remontent telles quelles au site.
/// </summary>
internal sealed class StorefrontSurveysFromReviews(
    ITenantContext tenantContext,
    IQueryHandler<Reviews.Application.Commands.GetPublicSurveyQuery, Reviews.Application.Commands.PublicSurveyDto> find,
    IQueryHandler<Reviews.Application.Commands.GetActiveSurveyQuery, Reviews.Application.Commands.PublicSurveyDto?> active,
    ICommandHandler<Reviews.Application.Commands.SubmitSurveyResponseCommand, Guid> submit) : IStorefrontSurveys
{
    public async Task<StorefrontSurvey?> FindAsync(TenantId tenantId, Guid surveyId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            return Map(await find.HandleAsync(new Reviews.Application.Commands.GetPublicSurveyQuery(surveyId), cancellationToken));
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    public async Task<StorefrontSurvey?> ActiveAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        tenantContext.CurrentTenantId == tenantId
        && await active.HandleAsync(new Reviews.Application.Commands.GetActiveSurveyQuery(), cancellationToken) is { } survey
            ? Map(survey)
            : null;

    public async Task SubmitAsync(
        TenantId tenantId, Guid surveyId, OrderId? orderId, Guid? locationId, IReadOnlyList<StorefrontSurveyAnswer> answers,
        CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            throw new NotFoundException(Storefront.Application.Surveys.StorefrontSurveyErrors.SurveyNotFound);
        }

        try
        {
            await submit.HandleAsync(
                new Reviews.Application.Commands.SubmitSurveyResponseCommand(
                    surveyId,
                    locationId,
                    orderId?.Value,
                    answers.Select(answer => new Reviews.Domain.SurveyAnswerInput(answer.QuestionId, answer.Rating, answer.Text, answer.Choices)).ToList()),
                cancellationToken);
        }
        catch (NotFoundException exception) when (exception.Code == Reviews.Application.ReviewsApplicationErrors.SurveyNotFound.Code)
        {
            throw new NotFoundException(Storefront.Application.Surveys.StorefrontSurveyErrors.SurveyNotFound);
        }
    }

    private static StorefrontSurvey Map(Reviews.Application.Commands.PublicSurveyDto survey) => new(
        survey.Id,
        survey.Title,
        survey.IsOpen,
        survey.Questions.Select(question => new StorefrontSurveyQuestion(
            question.Id, question.Order, question.Text, question.Type.ToString(), question.IsRequired, question.Choices)).ToList());
}

/// <summary>
/// Storefront -> Reservations : règles, disponibilités, réservation et annulation en ligne, pour le tenant de la clé
/// publique. Les refus métier (créneau pris, fermeture, délai d'annulation) remontent tels quels au site.
/// </summary>
internal sealed class StorefrontReservationsFromReservations(
    ITenantContext tenantContext,
    IQueryHandler<Reservations.Application.Commands.GetOnlineBookingRulesQuery, Reservations.Application.Commands.OnlineBookingRulesDto> rules,
    IQueryHandler<Reservations.Application.Commands.CheckOnlineSlotsQuery, IReadOnlyList<Reservations.Application.Commands.SlotAvailabilityDto>> check,
    ICommandHandler<Reservations.Application.Commands.BookOnlineCommand, Guid> book,
    IQueryHandler<Reservations.Application.Commands.GetGuestReservationQuery, Reservations.Application.Commands.GuestReservationDto> find,
    ICommandHandler<Reservations.Application.Commands.CancelGuestReservationCommand> cancel,
    IQueryHandler<Reservations.Application.Commands.GetWaitlistSettingsQuery, Reservations.Application.Commands.WaitlistSettingsDto> waitlistSettings,
    IQueryHandler<Reservations.Application.Commands.GetDepositRuleQuery, Reservations.Application.Commands.DepositRuleDto> depositRule,
    ICommandHandler<Reservations.Application.Commands.JoinWaitlistCommand, Guid> joinWaitlist,
    IQueryHandler<Reservations.Application.Commands.GetGuestWaitlistEntryQuery, Reservations.Application.Commands.GuestWaitlistEntryDto> findWaitlist,
    ICommandHandler<Reservations.Application.Commands.AcceptWaitlistOfferCommand, Guid> acceptWaitlist,
    ICommandHandler<Reservations.Application.Commands.CancelWaitlistEntryCommand> cancelWaitlist,
    IQueryHandler<GetDepositPaymentsQuery, DepositPaymentsDto> depositPayments,
    ICommandHandler<StartDepositPaymentCommand, OnlinePaymentStarted> startDeposit,
    ICommandHandler<CancelPaymentCommand> cancelPayment) : IStorefrontReservations
{
    public async Task<StorefrontBookingRules> RulesAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default)
    {
        EnsureTenant(tenantId);
        var found = await rules.HandleAsync(new Reservations.Application.Commands.GetOnlineBookingRulesQuery(locationId.Value), cancellationToken);
        var waitlist = await waitlistSettings.HandleAsync(new Reservations.Application.Commands.GetWaitlistSettingsQuery(locationId.Value), cancellationToken);
        var deposit = await depositRule.HandleAsync(new Reservations.Application.Commands.GetDepositRuleQuery(locationId.Value), cancellationToken);
        return new StorefrontBookingRules(
            found.AcceptsOnlineBookings, found.MinLeadTimeMinutes, found.MaxAdvanceDays, found.DurationMinutes, found.CancellationCutoffMinutes, found.MaxPartySize,
            found.AcceptsOnlineBookings && waitlist.AcceptsOnline,
            deposit.Mode != Reservations.Domain.DepositMode.Off && deposit.Amount is { } amount
                ? new StorefrontDepositRule(
                    amount, deposit.CurrencyCode!, deposit.Basis == Reservations.Domain.DepositBasis.PerGuest, deposit.MinPartySize, deposit.PaymentWindowMinutes)
                : null);
    }

    public async Task<IReadOnlyList<(DateTimeOffset StartAtUtc, bool IsAvailable)>> CheckAsync(
        TenantId tenantId, LocationId locationId, int partySize, IReadOnlyList<DateTimeOffset> starts, CancellationToken cancellationToken = default)
    {
        EnsureTenant(tenantId);
        return (await check.HandleAsync(new Reservations.Application.Commands.CheckOnlineSlotsQuery(locationId.Value, partySize, starts), cancellationToken))
            .Select(slot => (slot.StartAtUtc, slot.IsAvailable))
            .ToList();
    }

    public Task<Guid> BookAsync(
        TenantId tenantId, LocationId locationId, DateTimeOffset startAt, int partySize, string? name, string? phone, string? email, string? notes,
        CustomerId? customerId, CancellationToken cancellationToken = default)
    {
        EnsureTenant(tenantId);
        return book.HandleAsync(
            new Reservations.Application.Commands.BookOnlineCommand(locationId.Value, startAt, partySize, name, phone, email, notes, customerId?.Value),
            cancellationToken);
    }

    public async Task<StorefrontReservation?> FindAsync(TenantId tenantId, Guid reservationId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var found = await find.HandleAsync(new Reservations.Application.Commands.GetGuestReservationQuery(reservationId), cancellationToken);
            StorefrontDeposit? deposit = null;
            if (found.Deposit is { } owed)
            {
                var payments = await depositPayments.HandleAsync(new GetDepositPaymentsQuery(found.Id), cancellationToken);
                deposit = new StorefrontDeposit(owed.Amount, owed.CurrencyCode, owed.Status.ToString(), owed.DueBeforeUtc, payments.HasPending);
            }

            return new StorefrontReservation(
                found.Id, LocationId.From(found.LocationId), found.Status.ToString(), found.StartAtUtc, found.DurationMinutes, found.PartySize,
                found.GuestName, found.CanCancel, found.CancelBeforeUtc, deposit);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    public async Task CancelAsync(TenantId tenantId, Guid reservationId, CancellationToken cancellationToken = default)
    {
        EnsureTenant(tenantId);
        try
        {
            await cancel.HandleAsync(new Reservations.Application.Commands.CancelGuestReservationCommand(reservationId), cancellationToken);
        }
        catch (NotFoundException)
        {
            throw new NotFoundException(Storefront.Application.Reservations.OnlineReservationErrors.ReservationNotFound);
        }
    }

    public async Task<StartedPayment> StartDepositAsync(TenantId tenantId, Guid reservationId, OnlinePaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureTenant(tenantId);
        // Un seul lien de paiement valable : un essai resté en attente ou échoué est remplacé.
        var existing = await depositPayments.HandleAsync(new GetDepositPaymentsQuery(reservationId), cancellationToken);
        foreach (var payment in existing.Payments.Where(payment => payment.Status is PaymentStatus.Pending or PaymentStatus.Failed))
        {
            await cancelPayment.HandleAsync(new CancelPaymentCommand(payment.Id, StorefrontPaymentsFromPayments.ReplacedReason), cancellationToken);
        }

        PaymentMethodType? method = Enum.TryParse<PaymentMethodType>(request.Method, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : null;
        var started = await startDeposit.HandleAsync(
            new StartDepositPaymentCommand(reservationId, request.Provider, method, request.PayerPhone, request.ReturnUrl), cancellationToken);
        return new StartedPayment(started.PaymentId, started.CheckoutUrl);
    }

    public Task<Guid> JoinWaitlistAsync(
        TenantId tenantId, LocationId locationId, DateTimeOffset desiredStart, int partySize, int flexibilityMinutes, string? name, string? phone,
        string? email, CustomerId? customerId, CancellationToken cancellationToken = default)
    {
        EnsureTenant(tenantId);
        return joinWaitlist.HandleAsync(
            new Reservations.Application.Commands.JoinWaitlistCommand(
                locationId.Value, desiredStart, partySize, flexibilityMinutes, name, phone, email, null, customerId?.Value, Online: true),
            cancellationToken);
    }

    public async Task<StorefrontWaitlistEntry?> FindWaitlistAsync(TenantId tenantId, Guid entryId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var found = await findWaitlist.HandleAsync(new Reservations.Application.Commands.GetGuestWaitlistEntryQuery(entryId), cancellationToken);
            return new StorefrontWaitlistEntry(
                found.Id, LocationId.From(found.LocationId), found.Status.ToString(), found.DesiredStartUtc, found.FlexibilityMinutes, found.PartySize,
                found.GuestName, found.OfferedStartUtc, found.OfferExpiresAtUtc, found.ReservationId);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    public Task<Guid> AcceptWaitlistOfferAsync(TenantId tenantId, Guid entryId, CancellationToken cancellationToken = default)
    {
        EnsureTenant(tenantId);
        return acceptWaitlist.HandleAsync(new Reservations.Application.Commands.AcceptWaitlistOfferCommand(entryId), cancellationToken);
    }

    public Task CancelWaitlistAsync(TenantId tenantId, Guid entryId, CancellationToken cancellationToken = default)
    {
        EnsureTenant(tenantId);
        return cancelWaitlist.HandleAsync(new Reservations.Application.Commands.CancelWaitlistEntryCommand(entryId), cancellationToken);
    }

    private void EnsureTenant(TenantId tenantId)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            throw new NotFoundException(Storefront.Application.Reservations.OnlineReservationErrors.ReservationNotFound);
        }
    }
}

/// <summary>
/// Storefront -> Reviews : note globale et avis publiés (jamais l'auteur ni la commande). Fail-closed : un tenant autre
/// que celui du contexte n'a aucun avis.
/// </summary>
internal sealed class StorefrontReviewsFromReviews(
    ITenantContext tenantContext,
    IQueryHandler<Reviews.Application.Queries.GetPublicReviewsQuery, Reviews.Application.Queries.PublicReviewsDto> reviews,
    ICommandHandler<Reviews.Application.Commands.SubmitFeedbackCommand, Guid> submit) : IStorefrontReviews
{
    public Task<Guid> SubmitAsync(TenantId tenantId, LocationId locationId, OrderId orderId, int rating, string? comment, CancellationToken cancellationToken = default) =>
        tenantContext.CurrentTenantId != tenantId
            ? throw new NotFoundException(Storefront.Application.Checkout.CheckoutErrors.OrderNotFound)
            : submit.HandleAsync(
                new Reviews.Application.Commands.SubmitFeedbackCommand(locationId.Value, rating, comment, OrderId: orderId.Value, AuthorIsOrderCustomer: true),
                cancellationToken);

    public async Task<StorefrontReviews> GetAsync(TenantId tenantId, IReadOnlyList<LocationId>? locations, int take, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return new StorefrontReviews(0, null, Enumerable.Range(1, 5).Select(rating => new StorefrontRatingCount(rating, 0)).ToList(), []);
        }

        var result = await reviews.HandleAsync(
            new Reviews.Application.Queries.GetPublicReviewsQuery(locations?.Select(location => location.Value).ToList(), take), cancellationToken);
        return new StorefrontReviews(
            result.Count,
            result.AverageRating,
            result.CountByRating.Select(count => new StorefrontRatingCount(count.Rating, count.Count)).ToList(),
            result.Reviews.Select(review => new StorefrontReview(review.Id, review.LocationId, review.Rating, review.Comment, review.SubmittedAtUtc)).ToList());
    }
}
