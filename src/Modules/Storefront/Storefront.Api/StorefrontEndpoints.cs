using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using SaasRestaurant.BuildingBlocks.Api;
using Storefront.Application;
using Storefront.Application.Abstractions;
using Storefront.Application.Checkout;
using Storefront.Application.Commands;
using Storefront.Application.Customers;
using Storefront.Application.Kiosks;
using Storefront.Application.Platform;
using Storefront.Application.Queries;
using Storefront.Application.Receipts;
using Storefront.Application.Reservations;
using Storefront.Application.Surveys;
using Storefront.Domain;

namespace Storefront.Api;

public sealed record CreateSalesAppRequest(
    string? Name, SalesAppKind Kind, IReadOnlyList<string>? AllowedOrigins = null, IReadOnlyList<Guid>? LocationIds = null, IReadOnlyList<string>? ReturnUrls = null,
    bool RequireHumanCheck = false);

public sealed record UpdateSalesAppRequest(
    string? Name, IReadOnlyList<string>? AllowedOrigins = null, IReadOnlyList<Guid>? LocationIds = null, IReadOnlyList<string>? ReturnUrls = null,
    bool? RequireHumanCheck = null);

public sealed record SaveOnlineOrderingSettingsRequest(
    bool IsPaused,
    IReadOnlyList<ServiceMode>? ServiceModes,
    int PreparationMinutes,
    decimal? MinimumOrderAmount,
    int ManualAcceptanceMinutes,
    int UnpaidExpiryMinutes,
    IReadOnlyList<ChannelPolicyInput>? Policies,
    int? SlotCapacity = null);

public sealed record PauseRequest(bool IsPaused);

/// <param name="ReturnUrl">Page où le prestataire renvoie le client ; elle doit être déclarée pour l'application.</param>
public sealed record StartPaymentRequest(string? Provider, string? Method = null, string? PayerPhone = null, string? ReturnUrl = null)
{
    public override string ToString() => $"StartPaymentRequest ({Provider})";
}

public sealed record QuoteCartRequest(
    Guid LocationId, ServiceMode Mode, IReadOnlyList<CartLine>? Lines, string? CouponCode = null, Guid? DeliveryZoneId = null, Guid? RewardId = null);

public sealed record CustomerCodeRequest(CustomerSignInChannel Channel, string? Address, string? Language = null)
{
    public override string ToString() => $"CustomerCodeRequest({Channel}, ***)";
}

public sealed record CustomerVerifyRequest(string? Ticket, string? Code, string? Name = null, bool MarketingConsent = false, string? Language = null)
{
    public override string ToString() => "CustomerVerifyRequest(***)";
}

/// <param name="ExpectedTotal">Le total affiché au client ; s'il diffère du total recalculé, la commande est refusée (409).</param>
/// <param name="CouponCode">Coupon accepté au devis : il est compté à la validation.</param>
/// <param name="Delivery">Obligatoire en livraison : zone, adresse, repère, consignes. <paramref name="ScheduledForUtc"/> est alors l'heure de livraison.</param>
public sealed record PlaceOrderRequest(
    Guid LocationId,
    ServiceMode Mode,
    IReadOnlyList<CartLine>? Lines,
    PaymentMode Payment,
    GuestContactInput? Customer = null,
    DateTimeOffset? ScheduledForUtc = null,
    decimal? ExpectedTotal = null,
    string? CouponCode = null,
    DeliveryAddressInput? Delivery = null,
    Guid? RewardId = null,
    string? TableToken = null)
{
    public override string ToString() => "PlaceOrderRequest (***)";
}

/// <param name="StartAt">Heure choisie parmi celles proposées (instant avec décalage).</param>
/// <param name="Phone">Obligatoire : la confirmation part par SMS ou WhatsApp.</param>
public sealed record BookTableRequest(
    Guid LocationId, DateTimeOffset StartAt, int PartySize, string? Name, string? Phone, string? Email = null, string? Notes = null)
{
    public override string ToString() => "BookTableRequest (***)";
}

/// <param name="FlexibilityMinutes">Minutes acceptées avant et après l'heure souhaitée (0 à 180).</param>
/// <param name="Phone">Obligatoire : l'offre de table part par WhatsApp ou SMS.</param>
public sealed record JoinWaitlistRequest(
    Guid LocationId, DateTimeOffset DesiredStart, int PartySize, int FlexibilityMinutes, string? Name, string? Phone, string? Email = null)
{
    public override string ToString() => "JoinWaitlistRequest (***)";
}

/// <param name="OrderToken">Jeton de suivi d'une commande terminée (lien proposé après la commande) ; sinon réponse libre.</param>
/// <param name="LocationId">Facultatif, sans commande : l'établissement concerné.</param>
public sealed record SurveyResponseRequest(IReadOnlyList<StorefrontSurveyAnswer>? Answers, string? OrderToken = null, Guid? LocationId = null)
{
    public override string ToString() => "SurveyResponseRequest (***)";
}

/// <param name="OrderToken">Jeton de suivi d'une commande terminée.</param>
/// <param name="Rating">Note de 1 à 5.</param>
/// <param name="Comment">Commentaire facultatif (2 000 caractères au plus).</param>
public sealed record StorefrontReviewRequest(string? OrderToken, int Rating, string? Comment = null)
{
    public override string ToString() => $"StorefrontReviewRequest({Rating}, ***)";
}

public sealed record StorefrontReviewCreated(Guid Id);

public sealed record PairKioskRequest(string? Code, Guid LocationId, string? Name)
{
    public override string ToString() => $"PairKioskRequest({LocationId}, ***)";
}

public sealed record ClaimKioskPairingRequest(string? Secret)
{
    public override string ToString() => "ClaimKioskPairingRequest (***)";
}

/// <summary>Une clé publique, renvoyée une seule fois (création, rotation).</summary>
public sealed record IssuedKeyResponse(Guid Id, string Key);

/// <summary>
/// Deux groupes. <c>/online-sales</c> : la console du restaurant (applications de vente, clés, réglages par
/// établissement, bornes), sous <c>Tenancy.TenantMemberPolicy</c> chaînée par l'hôte ; lecture <see cref="StorefrontPermissions.View"/>,
/// gestion <see cref="StorefrontPermissions.Manage"/>. <c>/storefront</c> : les applications de vente elles-mêmes, par
/// clé publique (politique <see cref="StorefrontKeyAuthentication.Policy"/>) : catalogue, devis, commande invité, suivi,
/// annulation, et l'appairage des bornes.
/// </summary>
public static class StorefrontEndpoints
{
    private const string View = StorefrontPermissions.View;
    private const string Manage = StorefrontPermissions.Manage;
    public const string DocumentName = "storefront";

    public static (RouteGroupBuilder Console, RouteGroupBuilder Client, RouteGroupBuilder Admin) MapStorefrontEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        return (MapConsole(routes), MapClient(routes), MapAdmin(routes));
    }

    /// <summary>
    /// <c>/admin/online-sales</c> : la vente en ligne de tous les restaurants, pour le back-office de la plateforme
    /// (politique d'administration chaînée par l'hôte). Lecture seule : des comptages, sans clé, jeton ni donnée client.
    /// </summary>
    private static RouteGroupBuilder MapAdmin(IEndpointRouteBuilder routes)
    {
        var admin = routes.MapGroup("/admin/online-sales").WithTags("Online sales administration");

        admin.MapGet("/overview", async (
            DateOnly from, DateOnly to, IQueryHandler<GetOnlineSalesOverviewQuery, OnlineSalesOverviewDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetOnlineSalesOverviewQuery(from, to), ct)));

        admin.MapGet("/tenants", async (
            DateOnly from, DateOnly to, int? page, int? pageSize,
            IQueryHandler<ListOnlineSalesTenantsQuery, IReadOnlyList<OnlineSalesTenantDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) =>
                handler.HandleAsync(new ListOnlineSalesTenantsQuery(from, to, skip, take), ct))));

        admin.MapGet("/tenants/{tenantId:guid}", async (
            Guid tenantId, DateOnly from, DateOnly to, IQueryHandler<GetOnlineSalesTenantQuery, OnlineSalesTenantDetailDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetOnlineSalesTenantQuery(tenantId, from, to), ct)));

        return admin;
    }

    private static RouteGroupBuilder MapConsole(IEndpointRouteBuilder routes)
    {
        var sales = routes.MapGroup("/online-sales").WithTags("Online sales");

        sales.MapGet("/", async (IQueryHandler<GetStorefrontOverviewQuery, StorefrontOverviewDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetStorefrontOverviewQuery(), ct)))
            .RequirePermission(View);

        sales.MapPost("/apps", async (CreateSalesAppRequest request, ICommandHandler<CreateSalesAppCommand, IssuedSalesAppKey> handler, CancellationToken ct) =>
        {
            var issued = await handler.HandleAsync(
                new CreateSalesAppCommand(request.Name, request.Kind, request.AllowedOrigins, request.LocationIds, request.ReturnUrls, request.RequireHumanCheck), ct);
            return TypedResults.Created($"/online-sales/apps/{issued.Id}", new IssuedKeyResponse(issued.Id, issued.Key));
        }).RequirePermission(Manage);

        sales.MapPut("/apps/{id:guid}", async (Guid id, UpdateSalesAppRequest request, ICommandHandler<UpdateSalesAppCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new UpdateSalesAppCommand(id, request.Name, request.AllowedOrigins, request.LocationIds, request.ReturnUrls, request.RequireHumanCheck), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Manage);

        sales.MapPost("/apps/{id:guid}/key", async (Guid id, ICommandHandler<RotateSalesAppKeyCommand, IssuedSalesAppKey> handler, CancellationToken ct) =>
        {
            var issued = await handler.HandleAsync(new RotateSalesAppKeyCommand(id), ct);
            return TypedResults.Ok(new IssuedKeyResponse(issued.Id, issued.Key));
        }).RequirePermission(Manage);

        sales.MapPost("/apps/{id:guid}/disable", async (Guid id, ICommandHandler<DisableSalesAppCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new DisableSalesAppCommand(id), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Manage);

        sales.MapPost("/apps/{id:guid}/enable", async (Guid id, ICommandHandler<EnableSalesAppCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new EnableSalesAppCommand(id), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Manage);

        sales.MapPut("/locations/{locationId:guid}", async (
            Guid locationId, SaveOnlineOrderingSettingsRequest request, ICommandHandler<SaveOnlineOrderingSettingsCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new SaveOnlineOrderingSettingsCommand(
                locationId, request.IsPaused, request.ServiceModes, request.PreparationMinutes, request.MinimumOrderAmount,
                request.ManualAcceptanceMinutes, request.UnpaidExpiryMinutes, request.Policies, request.SlotCapacity), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Manage);

        sales.MapGet("/locations/{locationId:guid}/waiting-orders", async (
            Guid locationId, IQueryHandler<ListWaitingOnlineOrdersQuery, IReadOnlyList<WaitingOnlineOrderDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListWaitingOnlineOrdersQuery(locationId), ct)));

        sales.MapGet("/kiosks", async (IQueryHandler<ListKiosksQuery, KiosksDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListKiosksQuery(), ct)))
            .RequirePermission(View);

        sales.MapPost("/kiosks/pair", async (PairKioskRequest request, ICommandHandler<ApproveKioskPairingCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new ApproveKioskPairingCommand(request.Code, request.LocationId, request.Name), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Manage);

        sales.MapPost("/kiosks/{id:guid}/revoke", async (Guid id, ICommandHandler<RevokeKioskCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new RevokeKioskCommand(id), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Manage);

        // Reçus : le lien et le texte du reçu d'une commande, ou son envoi par le compte WhatsApp/SMS du restaurant.
        sales.MapGet("/receipts/{orderId:guid}", async (Guid orderId, IQueryHandler<GetReceiptLinkQuery, ReceiptLinkDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetReceiptLinkQuery(orderId), ct)))
            .RequirePermission(StorefrontPermissions.SendReceipts);

        sales.MapPost("/receipts/{orderId:guid}/send", async (
            Guid orderId, SendReceiptRequest request, ICommandHandler<SendReceiptCommand, ReceiptSendResult> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new SendReceiptCommand(orderId, request.Channel, request.Phone), ct)))
            .RequirePermission(StorefrontPermissions.SendReceipts);

        // Liens à envoyer au client : gestion de sa réservation (paiement de l'acompte) et offre de la liste d'attente.
        sales.MapGet("/reservations/{reservationId:guid}/link", async (
            Guid reservationId, IQueryHandler<GetReservationLinkQuery, GuestLinkDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetReservationLinkQuery(reservationId), ct)))
            .RequirePermission("reservations.manage");

        sales.MapGet("/waitlist/{entryId:guid}/link", async (
            Guid entryId, IQueryHandler<GetWaitlistLinkQuery, GuestLinkDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetWaitlistLinkQuery(entryId), ct)))
            .RequirePermission("reservations.manage");

        sales.MapPut("/locations/{locationId:guid}/pause", async (
            Guid locationId, PauseRequest request, ICommandHandler<PauseOnlineOrderingCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new PauseOnlineOrderingCommand(locationId, request.IsPaused), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Manage);

        return sales;
    }

    private static RouteGroupBuilder MapClient(IEndpointRouteBuilder routes)
    {
        var storefront = routes.MapGroup("/storefront")
            .WithTags("Storefront")
            .WithGroupName(DocumentName)
            .RequireAuthorization(StorefrontKeyAuthentication.Policy)
            .RequireCors(StorefrontKeyAuthentication.CorsPolicy)
            .RequireRateLimiting(StorefrontKeyAuthentication.RateLimitPolicy);

        storefront.MapGet("/", async (IQueryHandler<GetStorefrontQuery, StorefrontDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetStorefrontQuery(), ct)))
            .WithSummary("Le restaurant, l'application et ses établissements (ouverture, modes, paiements).");

        storefront.MapGet("/locations/{locationId:guid}", async (
            Guid locationId, IQueryHandler<GetStorefrontLocationQuery, StorefrontLocationDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetStorefrontLocationQuery(locationId), ct)))
            .WithSummary("Un établissement servi par l'application.");

        storefront.MapGet("/locations/{locationId:guid}/catalog", async Task<Results<Ok<StorefrontCatalogDto>, StatusCodeHttpResult>> (
            Guid locationId, HttpContext http, IQueryHandler<GetStorefrontCatalogQuery, StorefrontCatalogDto> handler, CancellationToken ct) =>
        {
            var catalog = await handler.HandleAsync(new GetStorefrontCatalogQuery(locationId), ct);
            var etag = $"\"{catalog.Version}\"";
            http.Response.Headers.ETag = etag;
            http.Response.Headers.CacheControl = "private, no-cache";
            return http.Request.Headers.IfNoneMatch.Any(value => value is not null && value.Split(',').Any(tag => tag.Trim() == etag))
                ? TypedResults.StatusCode(StatusCodes.Status304NotModified)
                : TypedResults.Ok(catalog);
        })
        .WithSummary("Le catalogue commandable de l'établissement pour le canal de l'application, avec ETag (304 si inchangé).");

        storefront.MapGet("/tables/{token}", async (string token, IQueryHandler<GetStorefrontTableQuery, StorefrontTableDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetStorefrontTableQuery(token), ct)))
            .WithSummary("La table d'un QR code : établissement et nom de la table, pour commander à table.");

        storefront.MapGet("/locations/{locationId:guid}/booking", async (
            Guid locationId, DateOnly date, int? partySize, HttpContext http, IQueryHandler<GetBookingTimesQuery, BookingTimesDto> handler, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(await handler.HandleAsync(new GetBookingTimesQuery(locationId, date, partySize ?? 2), ct));
        })
        .WithSummary("Réservation de table : les heures d'un jour (heure locale) et leur disponibilité pour ce groupe.");

        storefront.MapPost("/reservations", async (BookTableRequest request, ICommandHandler<BookTableCommand, BookedTableDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(
                new BookTableCommand(request.LocationId, request.StartAt, request.PartySize, request.Name, request.Phone, request.Email, request.Notes), ct)))
            .RequireIdempotencyKey()
            .RequireHumanCheck()
            .WithSummary("Réserve une table (confirmée tout de suite, ou au paiement de l'acompte demandé) ; renvoie le lien de gestion de la réservation.");

        storefront.MapGet("/reservations/{token}", async (string token, HttpContext http, IQueryHandler<GetGuestBookingQuery, GuestBookingDto> handler, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(await handler.HandleAsync(new GetGuestBookingQuery(token), ct));
        })
        .WithSummary("Une réservation par son lien de gestion.");

        storefront.MapPost("/reservations/{token}/cancel", async (string token, ICommandHandler<CancelGuestBookingCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new CancelGuestBookingCommand(token), ct);
            return TypedResults.NoContent();
        })
        .WithSummary("Annulation par le client, jusqu'au délai fixé par le restaurant (acompte remboursé).");

        storefront.MapPost("/reservations/{token}/deposit/pay", async (
            string token, StartPaymentRequest request, ICommandHandler<PayGuestDepositCommand, GuestPaymentStartedDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new PayGuestDepositCommand(token, request.Provider, request.Method, request.PayerPhone, request.ReturnUrl), ct)))
            .RequireIdempotencyKey()
            .WithSummary("Paie l'acompte de la réservation (montant fixé par le restaurant) ; la réservation est confirmée au paiement.");

        storefront.MapPost("/waitlist", async (JoinWaitlistRequest request, ICommandHandler<JoinWaitlistOnlineCommand, JoinedWaitlistDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(
                new JoinWaitlistOnlineCommand(request.LocationId, request.DesiredStart, request.PartySize, request.FlexibilityMinutes, request.Name, request.Phone, request.Email), ct)))
            .RequireIdempotencyKey()
            .RequireHumanCheck()
            .WithSummary("Inscription en liste d'attente sur un créneau complet ; renvoie le lien de suivi.");

        storefront.MapGet("/waitlist/{token}", async (string token, HttpContext http, IQueryHandler<GetGuestWaitlistQuery, GuestWaitlistDto> handler, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(await handler.HandleAsync(new GetGuestWaitlistQuery(token), ct));
        })
        .WithSummary("Une inscription en liste d'attente par son lien, avec la table proposée s'il y en a une.");

        storefront.MapPost("/waitlist/{token}/accept", async (string token, ICommandHandler<AcceptGuestWaitlistOfferCommand, BookedTableDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new AcceptGuestWaitlistOfferCommand(token), ct)))
            .RequireIdempotencyKey()
            .WithSummary("Accepte la table proposée : la réservation est créée et son lien de gestion renvoyé.");

        storefront.MapPost("/waitlist/{token}/cancel", async (string token, ICommandHandler<CancelGuestWaitlistCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new CancelGuestWaitlistCommand(token), ct);
            return TypedResults.NoContent();
        })
        .WithSummary("Retire l'inscription (ou refuse la table proposée).");

        storefront.MapGet("/reviews", async (Guid? locationId, int? take, IQueryHandler<GetStorefrontReviewsQuery, StorefrontReviews> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetStorefrontReviewsQuery(locationId, take ?? 10), ct)))
            .WithSummary("Note moyenne, répartition des notes et avis publiés par le restaurant (sans auteur).");

        storefront.MapPost("/reviews", async (StorefrontReviewRequest request, ICommandHandler<SubmitStorefrontReviewCommand, Guid> handler, CancellationToken ct) =>
            TypedResults.Created((string?)null, new StorefrontReviewCreated(
                await handler.HandleAsync(new SubmitStorefrontReviewCommand(request.OrderToken, request.Rating, request.Comment), ct))))
            .WithSummary("L'avis du client sur sa commande terminée (une fois par commande) ; le restaurant choisit de le publier.");

        storefront.MapGet("/surveys/active", async (HttpContext http, IQueryHandler<GetStorefrontSurveyQuery, StorefrontSurvey> handler, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(await handler.HandleAsync(new GetStorefrontSurveyQuery(null), ct));
        })
        .WithSummary("Le questionnaire proposé après une commande (404 s'il n'y en a pas).");

        storefront.MapGet("/surveys/{surveyId:guid}", async (Guid surveyId, IQueryHandler<GetStorefrontSurveyQuery, StorefrontSurvey> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetStorefrontSurveyQuery(surveyId), ct)))
            .WithSummary("Un questionnaire publié du restaurant : ses questions, pour y répondre.");

        storefront.MapPost("/surveys/{surveyId:guid}/responses", async (
            Guid surveyId, SurveyResponseRequest request, ICommandHandler<SubmitStorefrontSurveyCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new SubmitStorefrontSurveyCommand(surveyId, request.OrderToken, request.LocationId, request.Answers), ct);
            return TypedResults.NoContent();
        })
        .WithSummary("Dépose une réponse anonyme ; avec le jeton d'une commande terminée, une seule réponse par commande.");

        storefront.MapPost("/cart/quote", async (QuoteCartRequest request, IQueryHandler<QuoteCartQuery, CartQuoteDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(
                new QuoteCartQuery(request.LocationId, request.Mode, request.Lines, request.CouponCode, request.DeliveryZoneId, request.RewardId), ct)))
            .WithSummary("Devis du panier : lignes, coupon, frais de livraison de la zone, totaux recalculés par le serveur, minimum et moyens de paiement proposés.");

        storefront.MapGet("/locations/{locationId:guid}/slots", async (
            Guid locationId, ServiceMode? mode, Guid? zoneId, HttpContext http, IQueryHandler<ListSlotsQuery, SlotsDto> handler, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(await handler.HandleAsync(new ListSlotsQuery(locationId, mode ?? ServiceMode.Takeaway, zoneId), ct));
        })
        .WithSummary("Créneaux des 7 prochains jours (heure de retrait, ou de livraison avec la zone), avec la place restante.");

        storefront.MapPost("/orders", async (PlaceOrderRequest request, ICommandHandler<PlaceGuestOrderCommand, GuestOrderPlacedDto> handler, CancellationToken ct) =>
        {
            var placed = await handler.HandleAsync(
                new PlaceGuestOrderCommand(
                    request.LocationId, request.Mode, request.Lines, request.Payment, request.Customer, request.ScheduledForUtc, request.ExpectedTotal,
                    request.CouponCode, request.Delivery, request.RewardId, request.TableToken),
                ct);
            return TypedResults.Created($"/v1/storefront/orders/{placed.TrackingToken}", placed);
        })
        .RequireIdempotencyKey()
        .RequireHumanCheck()
        .WithSummary("Valide une commande invité (en-tête Idempotency-Key obligatoire) ; renvoie son numéro et son jeton de suivi.");

        storefront.MapGet("/orders/{token}", async (string token, HttpContext http, IQueryHandler<GetGuestOrderQuery, GuestOrderDto> handler, CancellationToken ct) =>
        {
            var order = await handler.HandleAsync(new GetGuestOrderQuery(token), ct);
            http.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(order);
        })
        .WithSummary("Suivi d'une commande par son jeton.");

        storefront.MapGet("/receipts/{token}", async (string token, HttpContext http, IQueryHandler<GetPublicReceiptQuery, PublicReceiptDto> handler, CancellationToken ct) =>
        {
            var receipt = await handler.HandleAsync(new GetPublicReceiptQuery(token), ct);
            http.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(receipt);
        })
        .WithSummary("Le reçu d'une commande par son lien (lecture seule).");

        storefront.MapPost("/orders/{token}/cancel", async (string token, ICommandHandler<CancelGuestOrderCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new CancelGuestOrderCommand(token), ct);
            return TypedResults.NoContent();
        })
        .WithSummary("Annule la commande tant que le restaurant ne l'a pas acceptée (409 ensuite).");

        storefront.MapPost("/orders/{token}/payments", async (
            string token, StartPaymentRequest request, ICommandHandler<StartGuestPaymentCommand, GuestPaymentStartedDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(
                new StartGuestPaymentCommand(token, request.Provider, request.Method, request.PayerPhone, request.ReturnUrl), ct)))
            .RequireIdempotencyKey()
            .WithSummary("Démarre (ou reprend) le paiement en ligne de la commande ; renvoie la page du prestataire où envoyer le client.");

        storefront.MapPost("/customer/code", async (
            CustomerCodeRequest request, HttpContext http, ICommandHandler<RequestCustomerCodeCommand, CustomerCodeSentDto> handler, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(await handler.HandleAsync(new RequestCustomerCodeCommand(request.Channel, request.Address, request.Language), ct));
        })
        .RequireRateLimiting(StorefrontKeyAuthentication.CodeRateLimitPolicy)
        .RequireHumanCheck()
        .WithSummary("Connexion du client : envoie un code par SMS, WhatsApp ou e-mail ; renvoie un ticket à présenter avec le code.");

        storefront.MapPost("/customer/code/verify", async (
            CustomerVerifyRequest request, HttpContext http, ICommandHandler<VerifyCustomerCodeCommand, CustomerSignInDto> handler, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(await handler.HandleAsync(
                new VerifyCustomerCodeCommand(request.Ticket, request.Code, request.Name, request.MarketingConsent, request.Language), ct));
        })
        .RequireRateLimiting(StorefrontKeyAuthentication.CodeRateLimitPolicy)
        .WithSummary("Vérifie le code : renvoie le jeton de session (en-tête X-Customer-Token), ou demande le nom d'un nouveau client.");

        storefront.MapGet("/customer", async (HttpContext http, IQueryHandler<GetCustomerAccountQuery, CustomerAccountDto> handler, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(await handler.HandleAsync(new GetCustomerAccountQuery(), ct));
        })
        .WithSummary("Le compte du client connecté : nom, coordonnées, points et récompenses (403 sinon).");

        storefront.MapGet("/customer/orders", async (
            int? take, HttpContext http, IQueryHandler<ListCustomerOrdersQuery, IReadOnlyList<CustomerOrderDto>> handler, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(await handler.HandleAsync(new ListCustomerOrdersQuery(take ?? 20), ct));
        })
        .WithSummary("Les dernières commandes du client connecté, avec le jeton de suivi des commandes passées en ligne.");

        storefront.MapPost("/customer/sign-out", async (ICommandHandler<SignOutCustomerCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new SignOutCustomerCommand(), ct);
            return TypedResults.NoContent();
        })
        .WithSummary("Déconnexion : le jeton de session cesse aussitôt de fonctionner.");

        storefront.MapPost("/kiosk/pairings", async (HttpContext http, ICommandHandler<StartKioskPairingCommand, KioskPairingStartedDto> handler, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(await handler.HandleAsync(new StartKioskPairingCommand(), ct));
        })
        .WithSummary("Borne non appairée : obtient un code à 6 chiffres à afficher et un secret à garder (10 minutes).");

        storefront.MapPost("/kiosk/pairings/{id:guid}/claim", async (
            Guid id, ClaimKioskPairingRequest request, HttpContext http, ICommandHandler<ClaimKioskPairingCommand, KioskPairingStateDto> handler, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(await handler.HandleAsync(new ClaimKioskPairingCommand(id, request.Secret), ct));
        })
        .WithSummary("Borne : demande où en est son appairage ; une fois le code saisi dans la console, reçoit son jeton d'appareil (une seule fois).");

        return storefront;
    }
}

/// <summary>Envoi du reçu : canal (WhatsApp ou SMS) et numéro au format international ; sans numéro, celui du client de la commande.</summary>
public sealed record SendReceiptRequest(ReceiptChannel Channel, string? Phone = null);
