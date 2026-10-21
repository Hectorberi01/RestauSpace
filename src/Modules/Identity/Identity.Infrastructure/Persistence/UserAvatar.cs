using Identity.Application.Abstractions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence;

/// <summary>Photo de profil d'un compte : une ligne par compte, hors de l'agrégat pour ne pas la charger à chaque connexion.</summary>
public sealed class UserAvatar
{
    // Réservé à l'ORM.
    private UserAvatar()
    {
        Content = [];
        ContentType = null!;
    }

    internal UserAvatar(UserId userId, AvatarImage image)
    {
        UserId = userId;
        Content = image.Content;
        ContentType = image.ContentType;
    }

    public UserId UserId { get; private set; }

    public byte[] Content { get; private set; }

    public string ContentType { get; private set; }

    internal void Replace(AvatarImage image)
    {
        Content = image.Content;
        ContentType = image.ContentType;
    }
}

internal sealed class UserAvatarConfiguration : IEntityTypeConfiguration<UserAvatar>
{
    public void Configure(EntityTypeBuilder<UserAvatar> builder)
    {
        builder.ToTable("user_avatars", table =>
            table.HasCheckConstraint("ck_user_avatars_size", $"octet_length(content) BETWEEN 1 AND {AvatarImage.MaxBytes}"));
        builder.HasKey(avatar => avatar.UserId);
        builder.Property(avatar => avatar.UserId)
            .HasColumnName("user_id")
            .HasConversion(id => id.Value, value => UserId.From(value))
            .ValueGeneratedNever();
        builder.Property(avatar => avatar.Content).HasColumnName("content");
        builder.Property(avatar => avatar.ContentType).HasColumnName("content_type").HasMaxLength(32);
    }
}

internal sealed class UserAvatars(IdentityDbContext context) : IUserAvatars
{
    public async Task<AvatarImage?> FindAsync(UserId userId, CancellationToken cancellationToken = default) =>
        await context.UserAvatars.AsNoTracking().SingleOrDefaultAsync(avatar => avatar.UserId == userId, cancellationToken) is { } avatar
            ? AvatarImage.Restore(avatar.Content, avatar.ContentType)
            : null;

    public async Task SaveAsync(UserId userId, AvatarImage image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        var existing = await context.UserAvatars.SingleOrDefaultAsync(avatar => avatar.UserId == userId, cancellationToken);
        if (existing is null)
        {
            context.UserAvatars.Add(new UserAvatar(userId, image));
        }
        else
        {
            existing.Replace(image);
        }
    }

    public async Task RemoveAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var existing = await context.UserAvatars.SingleOrDefaultAsync(avatar => avatar.UserId == userId, cancellationToken);
        if (existing is not null)
        {
            context.UserAvatars.Remove(existing);
        }
    }
}
