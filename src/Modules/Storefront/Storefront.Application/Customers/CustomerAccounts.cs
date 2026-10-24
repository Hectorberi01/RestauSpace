using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Storefront.Application.Abstractions;
using Storefront.Application.Checkout;
using Storefront.Application.Queries;
using Storefront.Domain;

namespace Storefront.Application.Customers;

/// <summary>Jetons de session client (<c>cst_</c> + 32 octets aléatoires) : seule l'empreinte SHA-256 est gardée, jamais journalisés.</summary>
public static class CustomerSecrets
{
    public const string TokenPrefix = "cst_";
    public const string HeaderName = "X-Customer-Token";

    public static string NewToken() => TokenPrefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static bool LooksLikeToken(string? token) =>
        token is { Length: > 30 and < 100 } && token.StartsWith(TokenPrefix, StringComparison.Ordinal)
        && token.Skip(TokenPrefix.Length).All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static PublishableKeyHash Hash(string token) =>
        PublishableKeyHash.Create(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token))));

    public static string Hint(string token) => token[^CustomerSession.TokenHintLength..];
}

public static class CustomerAccountErrors
{
    public static readonly DomainError SignInRequired =
        new("storefront.customer.sign_in_required", "Sign in to use your account.");
    public static readonly DomainError TicketInvalid =
        new("storefront.customer.ticket_invalid", "This sign-in has expired: ask for a new code.");
    public static readonly DomainError AddressInvalid =
        new("storefront.customer.address_invalid", "Give a phone number for SMS or WhatsApp, or an e-mail address.");
    public static readonly DomainError NameRequired =
        new("storefront.customer.name_required", "Give your name to finish creating your account.");
    public static readonly DomainError CodeRequired = new("storefront.customer.code_required", "Enter the code you received.");
    public static readonly DomainError PhoneCountryNotAllowed = new(
        "storefront.customer.phone_country_not_allowed", "Codes are only sent by SMS or WhatsApp to phone numbers of the restaurant's country: use your e-mail address.");
    public static readonly DomainError PhoneLimitReached = new(
        "storefront.customer.phone_limit_reached", "Too many codes were sent by SMS or WhatsApp for this restaurant: try again later or use your e-mail address.");
    public static readonly DomainError RewardUnavailable =
        new("storefront.checkout.reward_unavailable", "This reward is not available.");
    public static readonly DomainError RewardNotAffordable =
        new("storefront.checkout.reward_not_affordable", "You do not have enough points for this reward.");
}

/// <summary>
/// Une connexion en cours, remise au client entre la demande du code et sa vérification : chiffrée et authentifiée
/// (AES-GCM), elle porte le restaurant, l'application, le défi, le canal, l'adresse où le code a été envoyé et l'échéance.
/// Le client ne peut ni la lire ni la modifier ; le serveur ne garde rien. <see cref="Verified"/> : le code est déjà
/// prouvé, il ne manque que le nom d'un nouveau client.
/// </summary>
public sealed record CustomerSignInTicket(
    Guid TenantId, Guid AppId, Guid ChallengeId, CustomerSignInChannel Channel, string Address, DateTimeOffset ExpiresAtUtc, bool Verified)
{
    public override string ToString() => $"CustomerSignInTicket({ChallengeId}, {Channel}, ***)";
}

public sealed class CustomerSignInTickets
{
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private static readonly byte[] Purpose = "storefront.customer-sign-in.v1"u8.ToArray();

    private readonly byte[] _key;

    /// <param name="secret">32 octets au moins ; la clé de chiffrement en est dérivée pour ce seul usage.</param>
    public CustomerSignInTickets(byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        if (secret.Length < 32)
        {
            throw new InvalidOperationException("The sign-in ticket key must be at least 32 bytes.");
        }

        _key = HMACSHA256.HashData(secret, Purpose);
    }

    public string Issue(CustomerSignInTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var plain = JsonSerializer.SerializeToUtf8Bytes(ticket);
        var output = new byte[NonceLength + plain.Length + TagLength];
        var nonce = output.AsSpan(0, NonceLength);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagLength);
        aes.Encrypt(nonce, plain, output.AsSpan(NonceLength, plain.Length), output.AsSpan(NonceLength + plain.Length), Purpose);
        return Base64Url.EncodeToString(output);
    }

    /// <summary>Le ticket s'il est authentique et pas expiré ; null sinon.</summary>
    public CustomerSignInTicket? Read(string? value, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 1024)
        {
            return null;
        }

        try
        {
            var bytes = Base64Url.DecodeFromChars(value);
            if (bytes.Length <= NonceLength + TagLength)
            {
                return null;
            }

            var plain = new byte[bytes.Length - NonceLength - TagLength];
            using var aes = new AesGcm(_key, TagLength);
            aes.Decrypt(bytes.AsSpan(0, NonceLength), bytes.AsSpan(NonceLength, plain.Length), bytes.AsSpan(NonceLength + plain.Length), plain, Purpose);
            var ticket = JsonSerializer.Deserialize<CustomerSignInTicket>(plain);
            return ticket is not null && ticket.ExpiresAtUtc > nowUtc ? ticket : null;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or JsonException)
        {
            return null;
        }
    }
}

/// <param name="Ticket">À renvoyer avec le code ; il expire avec le code.</param>
/// <param name="Destination">Où le code est parti, masqué (+229 •• •• •• 01, a•••@exemple.bj).</param>
public sealed record CustomerCodeSentDto(string Ticket, DateTimeOffset ExpiresAtUtc, CustomerSignInChannel Channel, string Destination)
{
    public override string ToString() => $"CustomerCodeSentDto(***, {Channel})";
}

public enum CustomerSignInStatus
{
    /// <summary>Connecté : le jeton de session est dans la réponse.</summary>
    SignedIn = 1,

    /// <summary>Code prouvé, mais aucune fiche à ce contact : renvoyer le nouveau ticket avec le nom du client.</summary>
    NameRequired = 2,
}

/// <param name="Token">Connecté seulement : à garder par l'application et à envoyer dans l'en-tête X-Customer-Token.</param>
/// <param name="Ticket">Nom requis seulement : le ticket déjà vérifié, à renvoyer avec le nom.</param>
public sealed record CustomerSignInDto(
    CustomerSignInStatus Status, string? Token = null, DateTimeOffset? ExpiresAtUtc = null, string? Ticket = null, CustomerAccountDto? Account = null)
{
    public override string ToString() => $"CustomerSignInDto({Status}, ***)";
}

public sealed record CustomerRewardDto(Guid Id, string Name, int PointCost, decimal Discount, bool Affordable);

/// <param name="PointsPerCurrencyUnit">Points gagnés par unité de la devise payée.</param>
public sealed record CustomerLoyaltyDto(bool Enabled, long Balance, decimal PointsPerCurrencyUnit, IReadOnlyList<CustomerRewardDto> Rewards);

/// <summary>Le compte du client, vu par lui-même (ses propres coordonnées).</summary>
public sealed record CustomerAccountDto(string Name, string? Email, string? Phone, string CurrencyCode, CustomerLoyaltyDto Loyalty)
{
    public override string ToString() => "CustomerAccountDto(***)";
}

/// <param name="TrackingToken">Pour rouvrir le suivi ; seulement pour une commande passée en ligne.</param>
public sealed record CustomerOrderDto(
    Guid OrderId, string Number, Guid LocationId, string LocationName, ServiceMode Mode, GuestOrderStatus Status, string CurrencyCode, decimal Total,
    DateTimeOffset CreatedAtUtc, string? TrackingToken);

internal static class CustomerAccounts
{
    public const int MaxNameLength = 100;

    /// <summary>Le client de la requête et sa fiche ; null pour un invité (ou une fiche anonymisée depuis).</summary>
    public static async Task<CustomerProfile?> CurrentAsync(
        StorefrontWorkspace workspace, IStorefrontClient client, IStorefrontCustomers customers, CancellationToken cancellationToken) =>
        client.CustomerId is { } customerId ? await customers.ProfileAsync(workspace.CurrentTenant, customerId, cancellationToken) : null;

    public static async Task<CustomerProfile> RequireAsync(
        StorefrontWorkspace workspace, IStorefrontClient client, IStorefrontCustomers customers, CancellationToken cancellationToken) =>
        await CurrentAsync(workspace, client, customers, cancellationToken) ?? throw new ForbiddenException(CustomerAccountErrors.SignInRequired);

    /// <summary>L'application de la requête, qui doit être un site ou une application (jamais une borne) active.</summary>
    public static async Task<SalesApp> AppAsync(StorefrontWorkspace workspace, IStorefrontClient client, CancellationToken cancellationToken)
    {
        var (app, _, _) = await StorefrontReader.ContextAsync(workspace, client, cancellationToken);
        DomainException.ThrowIf(app.Kind == SalesAppKind.Kiosk, CustomerSessionErrors.KioskNotAllowed);
        DomainException.ThrowIf(!app.IsActive, CustomerSessionErrors.AppDisabled);
        return app;
    }

    public static string Mask(CustomerSignInChannel channel, string address)
    {
        if (channel == CustomerSignInChannel.Email)
        {
            var at = address.IndexOf('@', StringComparison.Ordinal);
            return at <= 0 ? "•••" : address[0] + "•••" + address[at..];
        }

        var digits = new string(address.Where(char.IsAsciiDigit).ToArray());
        return digits.Length < 4 ? "•••" : "••• " + digits[^2..];
    }

    public static async Task<CustomerAccountDto> AccountAsync(
        StorefrontWorkspace workspace, IStorefrontLoyalty loyalty, CustomerProfile profile, CancellationToken cancellationToken)
    {
        var tenant = workspace.CurrentTenant;
        var restaurant = await workspace.Tenants.FindAsync(tenant, cancellationToken) ?? throw new NotFoundException(StorefrontApplicationErrors.AppNotFound);
        var summary = await loyalty.SummaryAsync(tenant, profile.CustomerId, restaurant.CurrencyCode, cancellationToken);
        return new CustomerAccountDto(
            profile.Name,
            profile.Email,
            profile.Phone,
            restaurant.CurrencyCode,
            new CustomerLoyaltyDto(
                summary.Enabled,
                summary.Balance,
                summary.PointsPerCurrencyUnit,
                summary.Rewards.Select(reward => new CustomerRewardDto(reward.RewardId, reward.Name, reward.PointCost, reward.Discount, reward.Affordable)).ToList()));
    }
}

/// <summary>
/// Le client demande un code de connexion, par SMS, WhatsApp ou e-mail. La réponse est la même que le contact soit
/// connu ou non (on ne révèle pas qui est client). Les demandes sont limitées par Notifications (délai, nombre par heure).
/// </summary>
public sealed record RequestCustomerCodeCommand(CustomerSignInChannel Channel, string? Address, string? Language = null) : ICommand<CustomerCodeSentDto>
{
    public override string ToString() => $"RequestCustomerCodeCommand({Channel}, ***)";
}

public sealed class RequestCustomerCodeHandler(
    StorefrontWorkspace workspace, IStorefrontClient client, IStorefrontOtp otp, CustomerSignInTickets tickets)
    : ICommandHandler<RequestCustomerCodeCommand, CustomerCodeSentDto>
{
    public const string Purpose = "customer_sign_in";

    public async Task<CustomerCodeSentDto> HandleAsync(RequestCustomerCodeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var app = await CustomerAccounts.AppAsync(workspace, client, cancellationToken);
        var address = command.Address?.Trim() ?? string.Empty;
        DomainException.ThrowIf(
            !Enum.IsDefined(command.Channel) || address.Length is 0 or > 254
                || (command.Channel == CustomerSignInChannel.Email) != address.Contains('@', StringComparison.Ordinal),
            CustomerAccountErrors.AddressInvalid);

        var tenant = workspace.CurrentTenant;
        var issued = await otp.RequestAsync(tenant, command.Channel, address, command.Language, cancellationToken);
        var ticket = tickets.Issue(new CustomerSignInTicket(tenant.Value, app.Id.Value, issued.ChallengeId, command.Channel, address, issued.ExpiresAtUtc, false));
        return new CustomerCodeSentDto(ticket, issued.ExpiresAtUtc, command.Channel, CustomerAccounts.Mask(command.Channel, address));
    }
}

/// <summary>
/// Le client donne le code reçu. La session s'ouvre sur la fiche dont le SEUL contact est celui prouvé : une fiche qui
/// porte aussi un autre contact, non prouvé, n'est jamais ouverte (<see cref="CustomerContactMatch"/>). Sans fiche,
/// une fiche est créée avec le nom donné (sinon <see cref="CustomerSignInStatus.NameRequired"/> et un ticket vérifié à
/// renvoyer avec le nom). Une session est ouverte pour cette application ; son jeton n'est remis qu'une fois.
/// </summary>
public sealed record VerifyCustomerCodeCommand(string? Ticket, string? Code, string? Name = null, bool MarketingConsent = false, string? Language = null)
    : ICommand<CustomerSignInDto>
{
    public override string ToString() => "VerifyCustomerCodeCommand(***)";
}

public sealed class VerifyCustomerCodeHandler(
    StorefrontWorkspace workspace,
    IStorefrontClient client,
    IStorefrontOtp otp,
    IStorefrontCustomers customers,
    IStorefrontLoyalty loyalty,
    CustomerSignInTickets tickets,
    ICustomerSessionRepository sessions,
    IStorefrontUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<VerifyCustomerCodeCommand, CustomerSignInDto>
{
    public static readonly TimeSpan NameStepLifetime = TimeSpan.FromMinutes(10);

    public async Task<CustomerSignInDto> HandleAsync(VerifyCustomerCodeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var app = await CustomerAccounts.AppAsync(workspace, client, cancellationToken);
        var tenant = workspace.CurrentTenant;
        var now = clock.UtcNow;
        var ticket = tickets.Read(command.Ticket, now);
        DomainException.ThrowIf(ticket is null || ticket.TenantId != tenant.Value || ticket.AppId != app.Id.Value, CustomerAccountErrors.TicketInvalid);

        if (!ticket!.Verified)
        {
            DomainException.ThrowIf(string.IsNullOrWhiteSpace(command.Code), CustomerAccountErrors.CodeRequired);
            await otp.VerifyAsync(tenant, ticket.ChallengeId, command.Code!, cancellationToken);
        }

        var isEmail = ticket.Channel == CustomerSignInChannel.Email;
        var email = isEmail ? ticket.Address : null;
        var phone = isEmail ? null : ticket.Address;
        var customerId = await customers.FindByContactAsync(tenant, email, phone, cancellationToken);
        if (customerId is null)
        {
            var name = command.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                var verified = ticket with { Verified = true, ExpiresAtUtc = now + NameStepLifetime };
                return new CustomerSignInDto(CustomerSignInStatus.NameRequired, Ticket: tickets.Issue(verified));
            }

            DomainException.ThrowIf(name.Length > CustomerAccounts.MaxNameLength, CustomerAccountErrors.NameRequired);
            customerId = CustomerId.From(await customers.FindOrCreateAsync(
                tenant, new GuestCustomer(name, email, phone, command.MarketingConsent, command.Language), cancellationToken));
        }
        else if (command.MarketingConsent)
        {
            await customers.GrantMarketingConsentAsync(tenant, customerId.Value, cancellationToken);
        }

        var profile = await customers.ProfileAsync(tenant, customerId.Value, cancellationToken)
            ?? throw new ForbiddenException(CustomerAccountErrors.SignInRequired);
        var token = CustomerSecrets.NewToken();
        var session = CustomerSession.Open(app, customerId.Value, CustomerSecrets.Hash(token), CustomerSecrets.Hint(token), clock);
        sessions.Add(session);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CustomerSignInDto(
            CustomerSignInStatus.SignedIn, token, session.ExpiresAtUtc, Account: await CustomerAccounts.AccountAsync(workspace, loyalty, profile, cancellationToken));
    }
}

/// <summary>Déconnexion : la session de la requête est révoquée. Sans session, rien à faire.</summary>
public sealed record SignOutCustomerCommand : ICommand;

public sealed class SignOutCustomerHandler(
    StorefrontWorkspace workspace, IStorefrontClient client, ICustomerSessionRepository sessions, IStorefrontUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<SignOutCustomerCommand>
{
    public async Task HandleAsync(SignOutCustomerCommand command, CancellationToken cancellationToken = default)
    {
        if (client.CustomerSessionId is not { } id || await sessions.FindAsync(workspace.CurrentTenant, id, cancellationToken) is not { } session)
        {
            return;
        }

        session.Revoke(clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Le compte du client connecté : nom, coordonnées, points et récompenses (403 <c>sign_in_required</c> sinon).</summary>
public sealed record GetCustomerAccountQuery : IQuery<CustomerAccountDto>;

public sealed class GetCustomerAccountHandler(
    StorefrontWorkspace workspace, IStorefrontClient client, IStorefrontCustomers customers, IStorefrontLoyalty loyalty)
    : IQueryHandler<GetCustomerAccountQuery, CustomerAccountDto>
{
    public async Task<CustomerAccountDto> HandleAsync(GetCustomerAccountQuery query, CancellationToken cancellationToken = default)
    {
        var profile = await CustomerAccounts.RequireAsync(workspace, client, customers, cancellationToken);
        return await CustomerAccounts.AccountAsync(workspace, loyalty, profile, cancellationToken);
    }
}

/// <summary>Les dernières commandes du client connecté, dans les établissements que sert l'application.</summary>
public sealed record ListCustomerOrdersQuery(int Take = 20) : IQuery<IReadOnlyList<CustomerOrderDto>>;

public sealed class ListCustomerOrdersHandler(
    StorefrontWorkspace workspace,
    IStorefrontClient client,
    IStorefrontCustomers customers,
    IStorefrontOrders orders,
    IOnlineOrderRepository onlineOrders,
    OrderTrackingTokens tokens,
    IClock clock) : IQueryHandler<ListCustomerOrdersQuery, IReadOnlyList<CustomerOrderDto>>
{
    public const int MaxTake = 50;

    public async Task<IReadOnlyList<CustomerOrderDto>> HandleAsync(ListCustomerOrdersQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var app = await CustomerAccounts.AppAsync(workspace, client, cancellationToken);
        var profile = await CustomerAccounts.RequireAsync(workspace, client, customers, cancellationToken);
        var tenant = workspace.CurrentTenant;
        var names = (await workspace.Locations.ListAsync(tenant, clock.UtcNow, cancellationToken)).ToDictionary(location => location.LocationId, location => location.Name);
        var result = new List<CustomerOrderDto>();
        foreach (var order in await orders.ListForCustomerAsync(tenant, profile.CustomerId, Math.Clamp(query.Take, 1, MaxTake), cancellationToken))
        {
            if (!app.Serves(order.LocationId))
            {
                continue;
            }

            var online = await onlineOrders.FindByOrderAsync(tenant, order.OrderId, cancellationToken);
            result.Add(new CustomerOrderDto(
                order.OrderId.Value, order.Number, order.LocationId.Value, names.GetValueOrDefault(order.LocationId, string.Empty), order.Mode, order.Status,
                order.CurrencyCode, order.Total, order.CreatedAtUtc, online is null ? null : tokens.Issue(tenant, order.OrderId)));
        }

        return result;
    }
}
