using Payments.Application.Abstractions;
using Payments.Domain;

namespace Payments.Application.Commands;

// Encaissement d'une commande : au comptoir (argent déjà reçu) ou en ligne (le prestataire confirmera par webhook).
// Le moyen doit être accepté par le restaurant ET proposé par la plateforme dans la devise de la commande. Une commande
// peut avoir plusieurs paiements (addition partagée) ; leur somme ne dépasse jamais son total.

/// <summary>Commun aux deux encaissements : commande payable, moyen accepté, reste à payer, montant (tout le reste par défaut).</summary>
internal static class Collection
{
    public static async Task<(OrderReference Order, Money Amount, RestaurantPaymentConfiguration Configuration)> PrepareAsync(
        PaymentsWorkspace workspace,
        IPaymentOrderLock orderLock,
        Guid orderId,
        PaymentProviderCode provider,
        PaymentProviderType type,
        decimal? amount,
        CancellationToken cancellationToken)
    {
        var (order, value, configuration, _) = await PrepareAsync(workspace, orderLock, orderId, provider, type, amount, null, cancellationToken);
        return (order, value, configuration);
    }

    /// <summary>
    /// Avec <paramref name="items"/> (addition partagée par article), le montant est celui des lignes choisies, jamais
    /// une saisie : chaque ligne doit être sur la commande et ses unités pas déjà réglées par un autre paiement.
    /// </summary>
    public static async Task<(OrderReference Order, Money Amount, RestaurantPaymentConfiguration Configuration, IReadOnlyList<PaymentItemShare> Items)> PrepareAsync(
        PaymentsWorkspace workspace,
        IPaymentOrderLock orderLock,
        Guid orderId,
        PaymentProviderCode provider,
        PaymentProviderType type,
        decimal? amount,
        IReadOnlyList<PaymentItemShare>? items,
        CancellationToken cancellationToken)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var order = await workspace.RequireOrderAsync(tenant, orderId, cancellationToken);
        DomainException.ThrowIf(!order.AcceptsPayments, PaymentsApplicationErrors.OrderNotPayable);
        var currency = order.Total.Currency;
        var (configuration, _) = await workspace.RequireAcceptedConfigurationAsync(tenant, provider, type, currency, cancellationToken);

        // Verrou AVANT la lecture des paiements existants : sinon deux caisses verraient le même reste à payer.
        await orderLock.LockAsync(tenant, order.OrderId, cancellationToken);
        var existing = await workspace.Payments.ListByOrderAsync(tenant, order.OrderId, cancellationToken);
        var due = order.Total - OrderBalance.Committed(existing, currency);
        DomainException.ThrowIf(!due.IsPositive, PaymentsApplicationErrors.NothingDue);

        var covered = items is { Count: > 0 } ? items : [];
        Money value;
        if (covered.Count > 0)
        {
            DomainException.ThrowIf(amount is not null, PaymentsApplicationErrors.ItemsWithAmount);
            value = ItemCoverage.AmountOf(order, existing, covered, currency);
        }
        else
        {
            value = amount is { } requested ? Money.Of(requested, currency) : due;
        }

        // Le domaine refuse un montant nul, négatif ou au-delà du reste à payer.
        return (OrderReference.Create(tenant, order.OrderId, order.LocationId, due), value, configuration, covered);
    }

    /// <summary>
    /// Le moyen demandé ; à défaut, le seul que la configuration retient (espèces, terminal...). Avec plusieurs moyens
    /// possibles (carte ou Apple Pay...), il doit être précisé. Le domaine vérifie ensuite qu'il est bien accepté.
    /// </summary>
    public static PaymentMethodType MethodOf(RestaurantPaymentConfiguration configuration, PaymentMethodType? requested) =>
        requested ?? (configuration.EnabledMethods.Count == 1
            ? configuration.EnabledMethods[0]
            : throw new DomainException(PaymentsApplicationErrors.PaymentMethodRequired));
}

/// <summary>
/// Argent déjà reçu au comptoir par un moyen hors ligne accepté par le restaurant (espèces, terminal externe, mobile
/// money vérifié...) : paiement créé et encaissé d'un coup. <paramref name="Reference"/> : n° de ticket, de transaction
/// ou de caisse saisi par l'opérateur. Montant : tout le reste à payer s'il est omis. <paramref name="PaymentMethod"/> :
/// facultatif si le prestataire n'en offre qu'un. Renvoie l'id du paiement.
/// </summary>
/// <param name="Items">Facultatif, à la place du montant : les lignes réglées (addition partagée par article).</param>
public sealed record RecordOfflinePaymentCommand(
    Guid OrderId,
    string? Provider,
    string? Reference,
    decimal? Amount = null,
    PaymentMethodType? PaymentMethod = null,
    IReadOnlyList<PaymentItemShareInput>? Items = null) : ICommand<Guid>;

public sealed record PaymentItemShareInput(Guid ItemId, int Quantity);

/// <remarks>
/// Carte cadeau ou avoir (<see cref="PaymentMethodType.GiftCard"/>) : <paramref name="Reference"/> est le code de la
/// carte, débitée avant la sauvegarde ; la référence gardée désigne la carte et le mouvement, jamais le code. Si le
/// paiement ne peut pas être enregistré, le débit est annulé.
/// </remarks>
public sealed class RecordOfflinePaymentHandler(PaymentsWorkspace workspace, IPaymentOrderLock orderLock, IPaymentStoredValue storedValue, IClock clock)
    : ICommandHandler<RecordOfflinePaymentCommand, Guid>
{
    public async Task<Guid> HandleAsync(RecordOfflinePaymentCommand command, CancellationToken cancellationToken = default)
    {
        var provider = PaymentProviderCode.Create(command.Provider);
        var isGiftCard = command.PaymentMethod == PaymentMethodType.GiftCard || provider.Value == GiftCards.ProviderCode;
        var reference = isGiftCard ? null : ProviderReference.Create(command.Reference);
        DomainException.ThrowIf(isGiftCard && string.IsNullOrWhiteSpace(command.Reference), PaymentsApplicationErrors.GiftCardCodeRequired);

        var (order, amount, configuration, items) = await Collection.PrepareAsync(
            workspace, orderLock, command.OrderId, provider, PaymentProviderType.OnSite, command.Amount, ItemCoverage.Of(command.Items), cancellationToken);
        var method = Collection.MethodOf(configuration, command.PaymentMethod);
        if (method != PaymentMethodType.GiftCard)
        {
            return await SaveAsync(configuration, method, order, amount, reference ?? ProviderReference.Create(command.Reference), items, cancellationToken);
        }

        configuration.EnsureCanTake(order.LocationId, method, amount);
        var tenant = workspace.CurrentTenant;
        var movement = Guid.CreateVersion7();
        var movementReference = GiftCards.PaymentMovement(movement);
        var debit = await storedValue.RedeemAsync(tenant, command.Reference!, amount, movementReference, cancellationToken);
        try
        {
            return await SaveAsync(
                configuration, method, order, amount, ProviderReference.Create(GiftCards.PaymentReference(debit.CardId, movement)), items, cancellationToken);
        }
        catch (Exception)
        {
            await storedValue.ReverseAsync(tenant, debit.CardId, movementReference, CancellationToken.None);
            throw;
        }
    }

    private async Task<Guid> SaveAsync(
        RestaurantPaymentConfiguration configuration, PaymentMethodType method, OrderReference order, Money amount, ProviderReference reference,
        IReadOnlyList<PaymentItemShare> items, CancellationToken cancellationToken)
    {
        var payment = Payment.RecordOffline(configuration, method, order, amount, reference, clock);
        if (items.Count > 0)
        {
            payment.Cover(items);
        }

        workspace.Payments.Add(payment);
        await workspace.SaveAsync(cancellationToken);
        return payment.Id.Value;
    }
}

public sealed record OnlinePaymentStarted(Guid PaymentId, string ProviderReference, Uri? CheckoutUrl);

/// <summary>
/// Paiement en ligne sur le compte marchand du restaurant : le paiement est créé, l'intention ouverte chez le
/// prestataire et la tentative enregistrée. Le paiement reste en attente jusqu'au webhook du prestataire. Montant : tout
/// le reste à payer s'il est omis. <paramref name="PaymentMethod"/> : facultatif si le prestataire n'en offre qu'un.
/// </summary>
/// <param name="PayerPhone">Facultatif : le numéro du payeur, exigé par un prestataire qui pousse la demande sur son téléphone.</param>
/// <param name="ReturnUrl">
/// Facultatif, posé par le serveur (jamais par une route du personnel) : la page de retour d'une application de vente en
/// ligne, déjà vérifiée par l'appelant. Sans elle, la page de retour de la plateforme.
/// </param>
public sealed record StartOnlinePaymentCommand(
    Guid OrderId,
    string? Provider,
    decimal? Amount = null,
    PaymentMethodType? PaymentMethod = null,
    string? PayerPhone = null,
    Uri? ReturnUrl = null,
    IReadOnlyList<PaymentItemShareInput>? Items = null)
    : ICommand<OnlinePaymentStarted>;

public sealed class StartOnlinePaymentHandler(PaymentsWorkspace workspace, IPaymentOrderLock orderLock, IClock clock)
    : ICommandHandler<StartOnlinePaymentCommand, OnlinePaymentStarted>
{
    public const string GatewayUnavailableReason = "Payment provider unavailable";

    public async Task<OnlinePaymentStarted> HandleAsync(StartOnlinePaymentCommand command, CancellationToken cancellationToken = default)
    {
        var provider = PaymentProviderCode.Create(command.Provider);
        var (order, amount, configuration, items) = await Collection.PrepareAsync(
            workspace, orderLock, command.OrderId, provider, PaymentProviderType.Online, command.Amount, ItemCoverage.Of(command.Items), cancellationToken);
        var tenant = workspace.CurrentTenant;
        var payment = Payment.Create(configuration, Collection.MethodOf(configuration, command.PaymentMethod), order, amount, clock);
        if (items.Count > 0)
        {
            payment.Cover(items);
        }
        var gateway = await workspace.GatewayForAsync(configuration, cancellationToken);
        workspace.Payments.Add(payment);
        await workspace.SaveAsync(cancellationToken);

        GatewayCheckout checkout;
        try
        {
            checkout = await Gateways.StartAsync(
                gateway,
                new GatewayPaymentRequest(
                    tenant, payment.Id, payment.OrderId, amount, configuration.Id.Value, PayerPhones.Normalize(command.PayerPhone), command.ReturnUrl),
                cancellationToken);
        }
        catch (Exception)
        {
            payment.Cancel(GatewayUnavailableReason, clock);
            await workspace.SaveAsync(CancellationToken.None);
            throw;
        }

        payment.RegisterAttempt(checkout.Reference, clock);
        await workspace.SaveAsync(CancellationToken.None);
        return new OnlinePaymentStarted(payment.Id.Value, checkout.Reference.Value, checkout.CheckoutUrl);
    }
}

/// <summary>
/// Nouvel essai d'un paiement en ligne échoué (même montant, nouvelle intention chez le même prestataire). Le moyen doit
/// être encore accepté par le restaurant et proposé par la plateforme.
/// </summary>
/// <param name="ReturnUrl">Comme pour <see cref="StartOnlinePaymentCommand"/> : posé par le serveur seulement.</param>
public sealed record RetryPaymentCommand(Guid PaymentId, string? PayerPhone = null, Uri? ReturnUrl = null) : ICommand<OnlinePaymentStarted>;

public sealed class RetryPaymentHandler(PaymentsWorkspace workspace, IClock clock)
    : ICommandHandler<RetryPaymentCommand, OnlinePaymentStarted>
{
    public async Task<OnlinePaymentStarted> HandleAsync(RetryPaymentCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var payment = await workspace.LoadAsync(tenant, command.PaymentId, cancellationToken);
        DomainException.ThrowIf(payment.Status != PaymentStatus.Failed, PaymentsApplicationErrors.RetryNotAllowed);
        var (configuration, _) = await workspace.RequireAcceptedConfigurationAsync(
            tenant, payment.Provider, PaymentProviderType.Online, payment.Currency, cancellationToken);
        // Le moyen doit être encore accepté dans l'établissement, pour ce montant (affectations et bornes ont pu changer).
        configuration.EnsureCanTake(payment.LocationId, payment.Method, payment.Amount);
        var gateway = await workspace.GatewayForAsync(configuration, cancellationToken);

        // Son montant est resté réservé pendant l'échec : pas besoin de recalculer le reste à payer.
        var checkout = await Gateways.StartAsync(
            gateway,
            new GatewayPaymentRequest(
                tenant, payment.Id, payment.OrderId, payment.Amount, configuration.Id.Value, PayerPhones.Normalize(command.PayerPhone), command.ReturnUrl),
            cancellationToken);
        payment.RegisterAttempt(checkout.Reference, clock);

        await workspace.SaveAsync(cancellationToken);
        return new OnlinePaymentStarted(payment.Id.Value, checkout.Reference.Value, checkout.CheckoutUrl);
    }
}

/// <summary>
/// Abandon d'un paiement pas encore encaissé (client parti, autre moyen choisi) : libère sa part de la commande. Une
/// intention encore ouverte est annulée chez le prestataire après la sauvegarde (relais de l'outbox).
/// </summary>
public sealed record CancelPaymentCommand(Guid PaymentId, string? Reason) : ICommand;

public sealed class CancelPaymentHandler(PaymentsWorkspace workspace, IClock clock) : ICommandHandler<CancelPaymentCommand>
{
    public async Task HandleAsync(CancelPaymentCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var payment = await workspace.LoadAsync(workspace.CurrentTenant, command.PaymentId, cancellationToken);
        payment.Cancel(command.Reason ?? string.Empty, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Numéro du payeur : chiffres seuls (indicatif compris, sans « + »), 8 à 15 chiffres ; null s'il est absent.</summary>
internal static class PayerPhones
{
    public static string? Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
        if (phone.TrimStart().StartsWith("00", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }

        DomainException.ThrowIf(digits.Length is < 8 or > 15 || phone.Any(c => !(char.IsAsciiDigit(c) || c is '+' or ' ' or '-' or '.' or '(' or ')')),
            PaymentsApplicationErrors.PayerPhoneInvalid);
        return digits;
    }
}

internal static class Gateways
{
    /// <summary>Un prestataire injoignable ou qui refuse devient une erreur métier lisible (422), sans rien enregistrer.</summary>
    public static async Task<GatewayCheckout> StartAsync(
        IPaymentGateway gateway, GatewayPaymentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await gateway.StartAsync(request, cancellationToken);
        }
        catch (PaymentGatewayException exception)
        {
            throw new DomainException(PaymentsApplicationErrors.GatewayFailed, exception);
        }
    }
}

/// <summary>Addition partagée par article : ce que chaque paiement d'une commande a déjà réglé, ligne par ligne.</summary>
internal static class ItemCoverage
{
    public static IReadOnlyList<PaymentItemShare>? Of(IReadOnlyList<PaymentItemShareInput>? items) =>
        items?.Select(item => new PaymentItemShare(item.ItemId, item.Quantity)).ToList();

    /// <summary>Unités réglées ou réservées par les paiements qui comptent encore (ni annulés, ni remboursés en entier).</summary>
    public static Dictionary<Guid, int> Covered(IEnumerable<Payment> payments)
    {
        var covered = new Dictionary<Guid, int>();
        foreach (var payment in payments.Where(payment => payment.Status is not (PaymentStatus.Cancelled or PaymentStatus.Refunded)))
        {
            foreach (var item in payment.CoveredItems)
            {
                covered[item.ItemId] = covered.GetValueOrDefault(item.ItemId) + item.Quantity;
            }
        }

        return covered;
    }

    /// <summary>
    /// Montant des unités choisies : la part de chaque ligne, au prorata de ses unités ; les dernières unités d'une ligne
    /// prennent ce qui reste de la ligne, pour que la somme des parts redonne exactement son montant.
    /// </summary>
    public static Money AmountOf(PaymentOrder order, IReadOnlyCollection<Payment> existing, IReadOnlyList<PaymentItemShare> items, Currency currency)
    {
        var lines = (order.Lines ?? []).ToDictionary(line => line.ItemId);
        var covered = Covered(existing);
        var total = Money.Zero(currency);
        foreach (var item in items)
        {
            var line = lines.GetValueOrDefault(item.ItemId) ?? throw new DomainException(PaymentsApplicationErrors.ItemNotOnOrder);
            var already = covered.GetValueOrDefault(item.ItemId);
            DomainException.ThrowIf(already + item.Quantity > line.Quantity, PaymentsApplicationErrors.ItemAlreadyPaid);
            var before = Money.Rounded(line.Amount.Amount * already / line.Quantity, currency);
            var after = already + item.Quantity == line.Quantity
                ? line.Amount
                : Money.Rounded(line.Amount.Amount * (already + item.Quantity) / line.Quantity, currency);
            total += after - before;
        }

        return total;
    }
}
