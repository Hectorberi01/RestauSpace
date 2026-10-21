using Tenants.Application.Abstractions;
using Tenants.Domain.Tenants;

namespace Tenants.Application.Commands;

/// <summary>Remplace tout le branding : un logo ou une couleur omis est supprimé, pas conservé.</summary>
public sealed record UpdateTenantBrandingCommand(
    Guid TenantId,
    string? DisplayName,
    string? LogoUrl = null,
    string? PrimaryColor = null) : ICommand;

public sealed class UpdateTenantBrandingHandler(ITenantRepository tenants, ITenantsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<UpdateTenantBrandingCommand>
{
    public async Task HandleAsync(UpdateTenantBrandingCommand command, CancellationToken cancellationToken = default)
    {
        var branding = Branding.Create(command.DisplayName, ParseLogoUrl(command.LogoUrl), command.PrimaryColor);

        var tenant = await tenants.FindAsync(TenantId.From(command.TenantId), cancellationToken);

        if (tenant == null)
        {
            throw new NotFoundException(TenantApplicationErrors.NotFound);
        }
        
        tenant.UpdateBranding(branding, clock);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static Uri? ParseLogoUrl(string? logoUrl)
    {
        if (string.IsNullOrWhiteSpace(logoUrl))
        {
            return null;
        }

        // Le schéma (http/https) est vérifié par Branding.Create.
        DomainException.ThrowIf(
            !Uri.TryCreate(logoUrl.Trim(), UriKind.Absolute, out var uri), TenantErrors.BrandingLogoUrlInvalid);
        return uri;
    }
}
