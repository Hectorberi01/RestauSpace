using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Application.Commands;

// Le profil appartient à l'utilisateur du token : l'id vient toujours de l'access token, jamais de la requête.
// Rien de ce qui est saisi ici (nom, téléphone, ville, photo) n'est journalisé ni publié.

/// <summary>Prénom, nom, téléphone (E.164) et ville, tous facultatifs.</summary>
public sealed record UpdateMyProfileCommand(Guid UserId, string? FirstName, string? LastName, string? Phone, string? City) : ICommand;

public sealed class UpdateMyProfileHandler(IUserRepository users, IIdentityUnitOfWork unitOfWork, IClock clock) : ICommandHandler<UpdateMyProfileCommand>
{
    public async Task HandleAsync(UpdateMyProfileCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var user = await ProfileSteps.LoadAsync(users, command.UserId, cancellationToken);
        var phone = string.IsNullOrWhiteSpace(command.Phone) ? null : PhoneNumber.Create(command.Phone);
        user.UpdateProfile(command.FirstName, command.LastName, phone, command.City, clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Langue de l'interface, fuseau horaire IANA, emails d'actualités de la plateforme.</summary>
public sealed record ChangeMyPreferencesCommand(Guid UserId, string? Language, string? TimeZone, bool ProductNewsEmails) : ICommand;

public sealed class ChangeMyPreferencesHandler(IUserRepository users, IIdentityUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<ChangeMyPreferencesCommand>
{
    public async Task HandleAsync(ChangeMyPreferencesCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var user = await ProfileSteps.LoadAsync(users, command.UserId, cancellationToken);
        user.ChangePreferences(command.Language, TimeZoneId.Create(command.TimeZone), command.ProductNewsEmails, clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Remplace la photo de profil (JPEG, PNG ou WebP, 512 Ko au plus).</summary>
public sealed record SetMyAvatarCommand(Guid UserId, byte[]? Content) : ICommand;

public sealed class SetMyAvatarHandler(IUserRepository users, IUserAvatars avatars, IIdentityUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<SetMyAvatarCommand>
{
    public async Task HandleAsync(SetMyAvatarCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var image = AvatarImage.Create(command.Content);
        var user = await ProfileSteps.LoadAsync(users, command.UserId, cancellationToken);
        user.ChangeAvatar(hasAvatar: true, clock);
        await avatars.SaveAsync(user.Id, image, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed record RemoveMyAvatarCommand(Guid UserId) : ICommand;

public sealed class RemoveMyAvatarHandler(IUserRepository users, IUserAvatars avatars, IIdentityUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<RemoveMyAvatarCommand>
{
    public async Task HandleAsync(RemoveMyAvatarCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var user = await ProfileSteps.LoadAsync(users, command.UserId, cancellationToken);
        user.ChangeAvatar(hasAvatar: false, clock);
        await avatars.RemoveAsync(user.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

internal static class ProfileSteps
{
    public static async Task<User> LoadAsync(IUserRepository users, Guid userId, CancellationToken cancellationToken) =>
        (userId == Guid.Empty ? null : await users.FindAsync(UserId.From(userId), cancellationToken))
        ?? throw new NotFoundException(IdentityApplicationErrors.NotFound);
}
