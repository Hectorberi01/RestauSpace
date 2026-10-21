using Printing.Api;
using Printing.Application.Abstractions;
using SaasRestaurant.BuildingBlocks.Application;
using Storefront.Application.Abstractions;

namespace SaasRestaurant.Host;

internal static partial class HostComposition
{
    public static WebApplicationBuilder AddPrintingComposition(this WebApplicationBuilder builder, string connectionString)
    {
        // --- Printing : les imprimantes de tickets des établissements. Une imprimante Star CloudPRNT interroge l'API
        // elle-même ; une imprimante ESC/POS du réseau local passe par le relais (tools/print-relay). Chacune s'authentifie
        // par un jeton prt_… placé dans son URL, qui donne le tenant ; seule son empreinte est gardée. Une commande passée
        // à une borne imprime son ticket client (StorefrontTicketsFromPrinting). Lecture printing.view, gestion printing.manage.
        builder.Services.AddPrintingModule(builder.ConnectionStringFor("Printing", connectionString));
        builder.Services.AddScoped<IPrintingTenants, PrintingTenantsFromTenants>();
        builder.Services.AddScoped<IPrintingLocations, PrintingLocationsFromLocations>();
        builder.Services.AddScoped<IStorefrontTickets, StorefrontTicketsFromPrinting>();
        // Bons de cuisine imprimés à la création du ticket de cuisine ; reçus d'une commande demandés depuis la console.
        builder.Services.AddScoped<Printing.Application.Commands.IPrintingReceipts, PrintingReceiptsFromOrders>();
        builder.Services.AddScoped<IOutboxConsumer, KitchenPrintFlow>();
        return builder;
    }
}
