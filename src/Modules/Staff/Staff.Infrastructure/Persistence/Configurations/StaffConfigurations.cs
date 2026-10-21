using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Staff.Domain;

namespace Staff.Infrastructure.Persistence.Configurations;

internal static class StaffConverters
{
    public static readonly ValueConverter<TenantId, Guid> Tenant = new(id => id.Value, value => TenantId.From(value));
    public static readonly ValueConverter<UserId, Guid> User = new(id => id.Value, value => UserId.From(value));
    public static readonly ValueConverter<StaffMemberId, Guid> Member = new(id => id.Value, value => StaffMemberId.From(value));
    public static readonly ValueConverter<RoleId, Guid> Role = new(id => id.Value, value => RoleId.From(value));
    public static readonly ValueConverter<LocationId, Guid> Location = new(id => id.Value, value => LocationId.From(value));
}

internal sealed class StaffMemberConfiguration : IEntityTypeConfiguration<StaffMember>
{
    public const string TenantUserIndex = "ix_staff_members_tenant_id_user_id";
    public const string MemberLocationIndex = "ix_staff_assignments_staff_member_id_location_id";
    public const string OwnerProperty = "StaffMemberId";

    public void Configure(EntityTypeBuilder<StaffMember> builder)
    {
        builder.ToTable("staff_members");

        builder.HasKey(member => member.Id);
        builder.Property(member => member.Id).HasColumnName("id").HasConversion(StaffConverters.Member).ValueGeneratedNever();
        builder.Property(member => member.TenantId).HasColumnName("tenant_id").HasConversion(StaffConverters.Tenant);
        builder.Property(member => member.UserId).HasColumnName("user_id").HasConversion(StaffConverters.User);
        // Un profil par utilisateur et tenant, Left compris : garde-fou ultime contre deux invitations concurrentes.
        builder.HasIndex(member => new { member.TenantId, member.UserId }).IsUnique().HasDatabaseName(TenantUserIndex);
        // « Mes restaurants » lit par utilisateur, tous tenants confondus.
        builder.HasIndex(member => member.UserId).HasDatabaseName("ix_staff_members_user_id");

        // Numérique : les valeurs de l'enum sont explicites et stables.
        builder.Property(member => member.Status).HasColumnName("status");
        builder.Property(member => member.InvitedAtUtc).HasColumnName("invited_at_utc");
        builder.Property(member => member.TenantWideRoleId).HasColumnName("tenant_wide_role_id").HasConversion(StaffConverters.Role);
        builder.Property(member => member.PinHash).HasColumnName("pin_hash").HasMaxLength(128);
        builder.Property(member => member.PinFailures).HasColumnName("pin_failures");
        builder.Property(member => member.PinLockedUntilUtc).HasColumnName("pin_locked_until_utc");

        // Concurrence optimiste sur la colonne système xmin de PostgreSQL.
        builder.Property<uint>("Version").IsRowVersion();

        builder.OwnsMany(member => member.Assignments, assignment =>
        {
            assignment.ToTable("staff_assignments");
            assignment.WithOwner().HasForeignKey(OwnerProperty);
            assignment.Property<StaffMemberId>(OwnerProperty).HasColumnName("staff_member_id").HasConversion(StaffConverters.Member);
            assignment.Property<long>("Id").HasColumnName("id");
            assignment.HasKey("Id");
            assignment.Property(a => a.LocationId).HasColumnName("location_id").HasConversion(StaffConverters.Location);
            assignment.Property(a => a.RoleId).HasColumnName("role_id").HasConversion(StaffConverters.Role);
            // Un rôle par membre et par établissement.
            assignment.HasIndex(OwnerProperty, nameof(StaffAssignment.LocationId)).IsUnique().HasDatabaseName(MemberLocationIndex);
        });
        builder.Navigation(member => member.Assignments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(member => member.DomainEvents);
        builder.Ignore(member => member.EffectiveTenantRole);
        builder.Ignore(member => member.HasPin);
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public const string TenantNameIndex = "ix_roles_tenant_id_normalized_name";
    public const string NormalizedNameProperty = "NormalizedName";
    public const string OwnerProperty = "RoleId";

    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");

        builder.HasKey(role => role.Id);
        builder.Property(role => role.Id).HasColumnName("id").HasConversion(StaffConverters.Role).ValueGeneratedNever();
        builder.Property(role => role.TenantId).HasColumnName("tenant_id").HasConversion(StaffConverters.Tenant);

        builder.Property(role => role.Name)
            .HasColumnName("name")
            .HasConversion(name => name.Value, value => RoleName.Create(value))
            .HasMaxLength(RoleName.MaxLength);
        // Noms de rôle uniques par tenant quelle que soit la casse : une copie stockée en majuscules porte l'index unique.
        builder.Property<string>(NormalizedNameProperty)
            .HasColumnName("normalized_name")
            .HasMaxLength(RoleName.MaxLength)
            .HasComputedColumnSql("upper(name)", stored: true);
        builder.HasIndex(nameof(Role.TenantId), NormalizedNameProperty).IsUnique().HasDatabaseName(TenantNameIndex);

        builder.Property(role => role.IsSystem).HasColumnName("is_system");

        // Concurrence optimiste sur la colonne système xmin de PostgreSQL.
        builder.Property<uint>("Version").IsRowVersion();

        builder.OwnsMany(role => role.Permissions, permission =>
        {
            permission.ToTable("role_permissions");
            permission.WithOwner().HasForeignKey(OwnerProperty);
            permission.Property<RoleId>(OwnerProperty).HasColumnName("role_id").HasConversion(StaffConverters.Role);
            permission.Property(p => p.Value).HasColumnName("code").HasMaxLength(PermissionCode.MaxLength);
            permission.HasKey(OwnerProperty, nameof(PermissionCode.Value));
        });
        builder.Navigation(role => role.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(role => role.DomainEvents);
    }
}

internal sealed class TillConfiguration : IEntityTypeConfiguration<Till>
{
    public void Configure(EntityTypeBuilder<Till> builder)
    {
        builder.ToTable("tills");
        builder.HasKey(till => till.Id);
        builder.Property(till => till.Id).HasColumnName("id").HasConversion(id => id.Value, value => TillId.From(value)).ValueGeneratedNever();
        builder.Property(till => till.TenantId).HasColumnName("tenant_id").HasConversion(StaffConverters.Tenant);
        builder.Property(till => till.LocationId).HasColumnName("location_id").HasConversion(StaffConverters.Location);
        builder.Property(till => till.Name).HasColumnName("name").HasMaxLength(Till.NameMaxLength);
        builder.Property(till => till.AutoLockMinutes).HasColumnName("auto_lock_minutes");
        builder.Property(till => till.SecretHash).HasColumnName("secret_hash").HasMaxLength(128);
        builder.Property(till => till.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(till => till.LastUsedAtUtc).HasColumnName("last_used_at_utc");
        builder.Property(till => till.RevokedAtUtc).HasColumnName("revoked_at_utc");
        builder.HasIndex(till => till.TenantId).HasDatabaseName("ix_tills_tenant_id");
        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(till => till.IsActive);
        builder.Ignore(till => till.DomainEvents);
    }
}

internal sealed class TimeEntryConfiguration : IEntityTypeConfiguration<TimeEntry>
{
    public const string OpenEntryIndex = "ix_time_entries_tenant_id_staff_member_id_open";

    public void Configure(EntityTypeBuilder<TimeEntry> builder)
    {
        builder.ToTable("time_entries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).HasColumnName("id").HasConversion(id => id.Value, value => TimeEntryId.From(value)).ValueGeneratedNever();
        builder.Property(entry => entry.TenantId).HasColumnName("tenant_id").HasConversion(StaffConverters.Tenant);
        builder.Property(entry => entry.StaffMemberId).HasColumnName("staff_member_id").HasConversion(StaffConverters.Member);
        builder.Property(entry => entry.LocationId).HasColumnName("location_id").HasConversion(StaffConverters.Location);
        builder.Property(entry => entry.ClockedInAtUtc).HasColumnName("clocked_in_at_utc");
        builder.Property(entry => entry.ClockedOutAtUtc).HasColumnName("clocked_out_at_utc");
        builder.Property(entry => entry.ClosedAutomatically).HasColumnName("closed_automatically");
        // Une seule présence ouverte par membre.
        builder.HasIndex(entry => new { entry.TenantId, entry.StaffMemberId })
            .IsUnique()
            .HasFilter("clocked_out_at_utc IS NULL")
            .HasDatabaseName(OpenEntryIndex);
        builder.HasIndex(entry => new { entry.TenantId, entry.ClockedInAtUtc }).HasDatabaseName("ix_time_entries_tenant_id_clocked_in_at_utc");
        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(entry => entry.IsOpen);
        builder.Ignore(entry => entry.DomainEvents);
    }
}
