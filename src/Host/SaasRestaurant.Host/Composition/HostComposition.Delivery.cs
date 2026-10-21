using Delivery.Api;
using Delivery.Application.Abstractions;
using SaasRestaurant.BuildingBlocks.Application;

namespace SaasRestaurant.Host;

internal static partial class HostComposition
{
    public static WebApplicationBuilder AddDeliveryComposition(this WebApplicationBuilder builder, string connectionString)
    {
        // --- Delivery : retrait et livraison. Réglages par établissement (retrait, livraison, délais, zones avec frais et
        // minimum de commande) et une fiche par commande (client, adresse, livreur, statuts). Suivi delivery.operate,
        // réglages delivery.configure. Une commande prête passe sa fiche de retrait à « prête à retirer » et une commande
        // annulée annule sa fiche (DeliveryOrderFlow). Les frais de livraison entrent dans le total (DeliveryFeeFlow).
        // Fiches livreurs (delivery.couriers) : salarié ou indépendant, tarif par course figé sur chaque livraison.
        // Paiement à la livraison : au départ, le reste à payer devient le montant à encaisser ; à la remise, l'encaissement
        // du livreur est enregistré sur la commande (DeliveryCollectionFlow). Remise du livreur (delivery.remit) : espèces
        // rendues moins tarifs, écart, et mouvement de la caisse ouverte (CourierRemittanceFlow).
        builder.Services.AddDeliveryModule(builder.ConnectionStringFor("Delivery", connectionString));
        builder.Services.AddScoped<IDeliveryTenants, DeliveryTenantsFromTenants>();
        builder.Services.AddScoped<IDeliveryLocations, DeliveryLocationsFromLocations>();
        builder.Services.AddScoped<IDeliveryOrders, DeliveryOrdersFromOrders>();
        builder.Services.AddScoped<IDeliveryPayments, DeliveryPaymentsFromPayments>();
        builder.Services.AddScoped<IOutboxConsumer, DeliveryOrderFlow>();
        builder.Services.AddScoped<IOutboxConsumer, DeliveryFeeFlow>();
        builder.Services.AddScoped<IOutboxConsumer, DeliveryCollectionFlow>();
        builder.Services.AddScoped<IOutboxConsumer, CourierRemittanceFlow>();
        return builder;
    }
}
