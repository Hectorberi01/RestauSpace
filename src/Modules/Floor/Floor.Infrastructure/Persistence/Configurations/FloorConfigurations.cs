using Floor.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Floor.Infrastructure.Persistence.Configurations;

internal static class FloorConverters
{
    public static readonly ValueConverter<TenantId, Guid> TenantKey = new(id => id.Value, value => TenantId.From(value));
    public static readonly ValueConverter<LocationId, Guid> LocationKey = new(id => id.Value, value => LocationId.From(value));
    public static readonly ValueConverter<DiningAreaId, Guid> DiningAreaKey = new(id => id.Value, value => DiningAreaId.From(value));
    public static readonly ValueConverter<TableId, Guid> TableKey = new(id => id.Value, value => TableId.From(value));
    public static readonly ValueConverter<TableSessionId, Guid> SessionKey = new(id => id.Value, value => TableSessionId.From(value));
    public static readonly ValueConverter<TableSessionId?, Guid?> OptionalSessionKey =
        new(id => id.HasValue ? (Guid?)id.Value.Value : null, value => value.HasValue ? (TableSessionId?)TableSessionId.From(value.Value) : null);
    public static readonly ValueConverter<WaitingPartyId, Guid> WaitingPartyKey = new(id => id.Value, value => WaitingPartyId.From(value));
    public static readonly ValueConverter<PhoneNumber?, string> Phone = new(phone => phone!.Value, value => PhoneNumber.Create(value));
    public static readonly ValueConverter<ReservationReference, Guid> ReservationKey =
        new(reservation => reservation.Value, value => ReservationReference.From(value));
}

internal sealed class DiningAreaConfiguration : IEntityTypeConfiguration<DiningArea>
{
    public const string OwnerProperty = "DiningAreaId";

    public void Configure(EntityTypeBuilder<DiningArea> builder)
    {
        builder.ToTable("dining_areas");

        builder.HasKey(area => area.Id);
        builder.Property(area => area.Id).HasColumnName("id").HasConversion(FloorConverters.DiningAreaKey).ValueGeneratedNever();
        builder.Property(area => area.TenantId).HasColumnName("tenant_id").HasConversion(FloorConverters.TenantKey);
        builder.Property(area => area.LocationId).HasColumnName("location_id").HasConversion(FloorConverters.LocationKey);
        // Le plan de salle se lit par tenant, presque toujours par établissement.
        builder.HasIndex(area => new { area.TenantId, area.LocationId }).HasDatabaseName("ix_dining_areas_tenant_id_location_id");

        builder.Property(area => area.Name).HasColumnName("name").HasMaxLength(DiningArea.NameMaxLength);

        // Concurrence optimiste sur la colonne système xmin de PostgreSQL.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasMany(area => area.Tables)
            .WithOne()
            .HasForeignKey(OwnerProperty)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(area => area.Tables).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(area => area.DomainEvents);
        builder.Ignore(area => area.SeatsInService);
    }
}

internal sealed class RestaurantTableConfiguration : IEntityTypeConfiguration<RestaurantTable>
{
    public const string PositionProperty = "Position";
    public const string NormalizedLabelProperty = "NormalizedLabel";
    public const string AreaLabelIndex = "ix_restaurant_tables_dining_area_id_normalized_label";

    public void Configure(EntityTypeBuilder<RestaurantTable> builder)
    {
        builder.ToTable("restaurant_tables");

        builder.HasKey(table => table.Id);
        // Jamais généré : une table atteinte via sa zone avec un id déjà défini est une NOUVELLE ligne.
        builder.Property(table => table.Id).HasColumnName("id").HasConversion(FloorConverters.TableKey).ValueGeneratedNever();
        builder.Property<DiningAreaId>(DiningAreaConfiguration.OwnerProperty)
            .HasColumnName("dining_area_id")
            .HasConversion(FloorConverters.DiningAreaKey);
        // Position dans la liste de la zone (ordre d'ajout), posée par l'unit of work.
        builder.Property<int>(PositionProperty).HasColumnName("position");

        // Forme canonique (TableLabel) ; l'unicité dans la zone ignore la casse : une copie en majuscules porte l'index.
        builder.Property(table => table.Label)
            .HasColumnName("label")
            .HasConversion(label => label.Value, value => TableLabel.Create(value))
            .HasMaxLength(TableLabel.MaxLength);
        builder.Property<string>(NormalizedLabelProperty)
            .HasColumnName("normalized_label")
            .HasMaxLength(TableLabel.MaxLength)
            .HasComputedColumnSql("upper(label)", stored: true);
        builder.HasIndex(DiningAreaConfiguration.OwnerProperty, NormalizedLabelProperty).IsUnique().HasDatabaseName(AreaLabelIndex);

        builder.Property(table => table.Capacity).HasColumnName("capacity");
        // Jeton du QR de la table : unique (une commande par QR ne peut viser qu'une table).
        builder.Property(table => table.QrToken).HasColumnName("qr_token").HasMaxLength(TableQrTokens.MaxLength);
        builder.HasIndex(table => table.QrToken).IsUnique().HasFilter("qr_token IS NOT NULL").HasDatabaseName("ix_restaurant_tables_qr_token");
        // Numérique : les valeurs de l'enum sont explicites et stables.
        builder.Property(table => table.Status).HasColumnName("status");

        builder.OwnsOne(table => table.Layout, layout =>
        {
            layout.Property(l => l.X).HasColumnName("layout_x");
            layout.Property(l => l.Y).HasColumnName("layout_y");
            layout.Property(l => l.Width).HasColumnName("layout_width");
            layout.Property(l => l.Height).HasColumnName("layout_height");
            layout.Property(l => l.Shape).HasColumnName("layout_shape");
            layout.Property(l => l.Rotation).HasColumnName("layout_rotation").HasDefaultValue(0);
        });
        builder.Navigation(table => table.Layout).IsRequired(false);

        builder.Ignore(table => table.IsInService);
    }
}

internal sealed class TableSessionConfiguration : IEntityTypeConfiguration<TableSession>
{
    public const string OpenSessionPerTableIndex = "ix_table_sessions_tenant_id_table_id_open";

    public void Configure(EntityTypeBuilder<TableSession> builder)
    {
        builder.ToTable("table_sessions");

        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).HasColumnName("id").HasConversion(FloorConverters.SessionKey).ValueGeneratedNever();
        builder.Property(session => session.TenantId).HasColumnName("tenant_id").HasConversion(FloorConverters.TenantKey);
        builder.Property(session => session.LocationId).HasColumnName("location_id").HasConversion(FloorConverters.LocationKey);
        builder.Property(session => session.TableId).HasColumnName("table_id").HasConversion(FloorConverters.TableKey);
        builder.Property(session => session.GuestCount).HasColumnName("guest_count");
        builder.Property(session => session.Reservation).HasColumnName("reservation_id").HasConversion(FloorConverters.ReservationKey);
        builder.Property(session => session.OpenedAtUtc).HasColumnName("opened_at_utc");
        builder.Property(session => session.ClosedAtUtc).HasColumnName("closed_at_utc");

        // Une seule session ouverte par table : garde-fou final contre deux installations simultanées.
        builder.HasIndex(session => new { session.TenantId, session.TableId })
            .IsUnique()
            .HasFilter("closed_at_utc IS NULL")
            .HasDatabaseName(OpenSessionPerTableIndex);
        // Les sessions en cours se lisent par établissement.
        builder.HasIndex(session => new { session.TenantId, session.LocationId })
            .HasFilter("closed_at_utc IS NULL")
            .HasDatabaseName("ix_table_sessions_tenant_id_location_id_open");

        // Concurrence optimiste sur la colonne système xmin de PostgreSQL.
        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(session => session.DomainEvents);
        builder.Ignore(session => session.IsOpen);
        builder.Ignore(session => session.IsWalkIn);
    }
}

internal sealed class SessionOrderConfiguration : IEntityTypeConfiguration<SessionOrder>
{
    public void Configure(EntityTypeBuilder<SessionOrder> builder)
    {
        builder.ToTable(SessionOrder.Table);
        builder.HasKey(order => new { order.TenantId, order.OrderId });
        builder.Property(order => order.TenantId).HasColumnName("tenant_id").HasConversion(FloorConverters.TenantKey);
        builder.Property(order => order.OrderId).HasColumnName("order_id");
        builder.Property(order => order.TableSessionId).HasColumnName("table_session_id").HasConversion(FloorConverters.OptionalSessionKey);
        builder.Property(order => order.IsClosed).HasColumnName("is_closed");
        builder.HasIndex(order => new { order.TenantId, order.TableSessionId })
            .HasDatabaseName("ix_session_orders_open")
            .HasFilter("is_closed = FALSE");
    }
}

internal sealed class WaitingPartyConfiguration : IEntityTypeConfiguration<WaitingParty>
{
    public void Configure(EntityTypeBuilder<WaitingParty> builder)
    {
        builder.ToTable("waiting_parties");

        builder.HasKey(party => party.Id);
        builder.Property(party => party.Id).HasColumnName("id").HasConversion(FloorConverters.WaitingPartyKey).ValueGeneratedNever();
        builder.Property(party => party.TenantId).HasColumnName("tenant_id").HasConversion(FloorConverters.TenantKey);
        builder.Property(party => party.LocationId).HasColumnName("location_id").HasConversion(FloorConverters.LocationKey);
        builder.Property(party => party.Name).HasColumnName("name").HasMaxLength(WaitingParty.NameMaxLength);
        builder.Property(party => party.Phone).HasColumnName("phone").HasConversion(FloorConverters.Phone).HasMaxLength(32);
        builder.Property(party => party.PartySize).HasColumnName("party_size");
        builder.Property(party => party.Note).HasColumnName("note").HasMaxLength(WaitingParty.NoteMaxLength);
        builder.Property(party => party.QuotedMinutes).HasColumnName("quoted_minutes");
        builder.Property(party => party.Status).HasColumnName("status");
        builder.Property(party => party.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(party => party.NotifiedAtUtc).HasColumnName("notified_at_utc");
        builder.Property(party => party.NotificationCount).HasColumnName("notification_count");
        builder.Property(party => party.ClosedAtUtc).HasColumnName("closed_at_utc");
        builder.Property(party => party.TableSessionId).HasColumnName("table_session_id").HasConversion(FloorConverters.OptionalSessionKey);
        builder.Property<uint>("Version").IsRowVersion();

        // La file d'un établissement, par arrivée.
        builder.HasIndex(party => new { party.TenantId, party.LocationId, party.CreatedAtUtc }).HasDatabaseName("ix_waiting_parties_tenant_id_location_id_created_at_utc");

        builder.Ignore(party => party.DomainEvents);
        builder.Ignore(party => party.IsOpen);
    }
}
