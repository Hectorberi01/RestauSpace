using Tenants.Application.Abstractions;
using Tenants.Domain.Tenants;

namespace Tenants.Application.Commands;

public sealed record SocialLinkInput(SocialNetwork Network, string? Url);

/// <summary>Remplace toute la présentation publique : un champ omis ou vide est effacé.</summary>
public sealed record UpdateTenantPublicProfileCommand(
    Guid TenantId,
    string? Tagline,
    string? Description,
    string? CoverImageUrl,
    IReadOnlyList<SocialLinkInput>? Links) : ICommand;

public sealed class UpdateTenantPublicProfileHandler(ITenantRepository tenants, ITenantsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<UpdateTenantPublicProfileCommand>
{
    public async Task HandleAsync(UpdateTenantPublicProfileCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var profile = PublicProfile.Create(
            command.Tagline,
            command.Description,
            ParseUrl(command.CoverImageUrl, TenantErrors.ProfileImageUrlInvalid),
            (command.Links ?? [])
                .Where(link => !string.IsNullOrWhiteSpace(link.Url))
                .Select(link => new SocialLink(link.Network, ParseUrl(link.Url, TenantErrors.ProfileLinkInvalid)!)));

        var tenant = await tenants.FindAsync(TenantId.From(command.TenantId), cancellationToken)
            ?? throw new NotFoundException(TenantApplicationErrors.NotFound);

        tenant.UpdatePublicProfile(profile, clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static Uri? ParseUrl(string? value, DomainError error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        DomainException.ThrowIf(!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri), error);
        return uri;
    }
}
