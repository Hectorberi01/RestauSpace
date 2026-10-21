using Fiscal.Api;
using Fiscal.Application;
using SaasRestaurant.BuildingBlocks.Application;

namespace SaasRestaurant.Host;

internal static partial class HostComposition
{
    public static WebApplicationBuilder AddFiscalComposition(this WebApplicationBuilder builder, string connectionString)
    {
        // --- Fiscal : facture normalisée e-MECeF (DGI du Bénin). Chaque restaurant saisit son IFU, son régime et son jeton
        // e-MECeF (chiffré avec Fiscal:TokenKey, 32 octets en base64, OBLIGATOIRE et à garder secrète), puis choisit le bac à
        // sable ou la production. Une commande terminée demande sa facture de vente, un remboursement sa facture d'avoir
        // (FiscalFlow, par l'outbox) ; la file du module les envoie à la DGI et rapporte le code MECeF et le QR code du
        // reçu. Fiscal:SandboxBaseUrl et Fiscal:ProductionBaseUrl : adresses de l'API DGI. Lecture fiscal.view, réglages
        // fiscal.configure.
        var fiscal = builder.Configuration.GetSection("Fiscal");
        var tokenKey = fiscal["TokenKey"];
        if (string.IsNullOrWhiteSpace(tokenKey))
        {
            throw new InvalidOperationException("'Fiscal:TokenKey' is not configured (32 bytes, base64).");
        }

        builder.Services.AddFiscalModule(builder.ConnectionStringFor("Fiscal", connectionString), options =>
        {
            options.TokenKey = Convert.FromBase64String(tokenKey);
            if (fiscal["SandboxBaseUrl"] is { Length: > 0 } sandbox)
            {
                options.SandboxBaseUrl = new Uri(sandbox.EndsWith('/') ? sandbox : sandbox + "/");
            }

            if (fiscal["ProductionBaseUrl"] is { Length: > 0 } production)
            {
                options.ProductionBaseUrl = new Uri(production.EndsWith('/') ? production : production + "/");
            }
        });
        builder.Services.AddScoped<IFiscalTenants, FiscalTenantsFromTenants>();
        builder.Services.AddScoped<IFiscalOrders, FiscalOrdersFromOrders>();
        builder.Services.AddScoped<IOutboxConsumer, FiscalFlow>();
        return builder;
    }
}
