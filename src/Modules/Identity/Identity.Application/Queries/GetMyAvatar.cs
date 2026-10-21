using Identity.Application.Abstractions;

namespace Identity.Application.Queries;

/// <summary>La photo de l'utilisateur du token ; 404 s'il n'en a pas.</summary>
public sealed record GetMyAvatarQuery(Guid UserId) : IQuery<AvatarImage>;

public sealed class GetMyAvatarHandler(IUserAvatars avatars) : IQueryHandler<GetMyAvatarQuery, AvatarImage>
{
    public async Task<AvatarImage> HandleAsync(GetMyAvatarQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var image = query.UserId == Guid.Empty ? null : await avatars.FindAsync(UserId.From(query.UserId), cancellationToken);
        return image ?? throw new NotFoundException(IdentityApplicationErrors.AvatarNotFound);
    }
}
