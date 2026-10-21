namespace Payments.Domain;

/// <summary>Où la commande se règle : au comptoir ou en salle (tous les moyens), ou en ligne (prestataires en ligne seuls).</summary>
public enum PaymentChannel
{
    InPerson = 1,
    Online = 2,
}

/// <summary>Ce qu'on sait de la commande au moment de proposer les moyens de paiement.</summary>
public sealed record PaymentContext(LocationReference Location, Money Amount, PaymentChannel Channel);

/// <summary>Un moyen proposé : prestataire du restaurant, moyen, et rang d'affichage dans l'établissement (le plus petit d'abord).</summary>
public sealed record AvailablePaymentMethod(
    RestaurantPaymentConfigurationId ConfigurationId,
    PaymentProviderCode Provider,
    string DisplayName,
    PaymentProviderType Type,
    PaymentMethodType Method,
    int Priority);

/// <summary>
/// Les moyens de paiement proposés pour UNE commande : configuration du restaurant active, prête et acceptant le moyen
/// dans l'établissement et pour ce montant (bornes de l'affectation) ; prestataire actif dans le catalogue, qui sait
/// encaisser ce moyen, dans cette devise, dans ce pays et pour ce montant (bornes du prestataire) ; en ligne, seuls les
/// prestataires en ligne. Aucune règle sur le nom d'un prestataire : seulement ses capacités.
/// </summary>
public static class PaymentMethodAvailability
{
    public static IReadOnlyList<AvailablePaymentMethod> For(
        PaymentContext context, IEnumerable<(RestaurantPaymentConfiguration Configuration, PaymentProvider? Provider)> configurations)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(configurations);
        DomainException.ThrowIf(!Enum.IsDefined(context.Channel), PaymentConfigurationErrors.ChannelUnknown);
        var location = context.Location;
        var amount = context.Amount;

        return configurations
            .Where(pair => pair.Configuration.TenantId == location.TenantId && pair.Provider is { IsEnabled: true })
            .Where(pair => context.Channel == PaymentChannel.InPerson || pair.Configuration.ProviderType == PaymentProviderType.Online)
            .Where(pair => pair.Provider!.Capability.Supports(location.Country) && pair.Provider.Capability.Allows(amount))
            .SelectMany(pair => pair.Configuration.EnabledMethods
                .Where(method => pair.Provider!.Capability.Supports(method) && pair.Configuration.CanTake(location.LocationId, method, amount))
                .Select(method => new AvailablePaymentMethod(
                    pair.Configuration.Id,
                    pair.Configuration.Provider,
                    pair.Provider!.DisplayName,
                    pair.Configuration.ProviderType,
                    method,
                    pair.Configuration.PriorityAt(location.LocationId, method))))
            .OrderBy(item => item.Type)
            .ThenBy(item => item.Priority)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCulture)
            .ThenBy(item => item.Method)
            .ToList();
    }
}
