using Kitchen.Domain;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Kitchen.Infrastructure.Persistence.Configurations;

internal static class KitchenConverters
{
    public static readonly ValueConverter<TenantId, Guid> TenantKey = new(id => id.Value, value => TenantId.From(value));
    public static readonly ValueConverter<LocationId, Guid> LocationKey = new(id => id.Value, value => LocationId.From(value));
    public static readonly ValueConverter<StationId, Guid> StationKey = new(id => id.Value, value => StationId.From(value));
    public static readonly ValueConverter<StationRoutingId, Guid> RoutingKey = new(id => id.Value, value => StationRoutingId.From(value));
    public static readonly ValueConverter<KitchenTicketId, Guid> TicketKey = new(id => id.Value, value => KitchenTicketId.From(value));
    public static readonly ValueConverter<KitchenTicketItemId, Guid> ItemKey = new(id => id.Value, value => KitchenTicketItemId.From(value));
    public static readonly ValueConverter<OrderId, Guid> OrderKey = new(id => id.Value, value => OrderId.From(value));
    public static readonly ValueConverter<OrderItemId, Guid> OrderItemKey = new(id => id.Value, value => OrderItemId.From(value));

    public static readonly ValueConverter<List<StationRoute>, string> Routes =
        new(routes => KitchenJson.WriteRoutes(routes), json => KitchenJson.ReadRoutes(json));

    public static readonly ValueConverter<List<ProductCourse>, string> Courses =
        new(courses => KitchenJson.WriteCourses(courses), json => KitchenJson.ReadCourses(json));

    public static readonly ValueConverter<List<string>, string> Names =
        new(names => KitchenJson.WriteNames(names), json => KitchenJson.ReadNames(json));

    /// <summary>Une liste jsonb est modifiée sur place par le domaine : on la compare élément par élément, dans l'ordre.</summary>
    public static ValueComparer<List<T>> ListComparer<T>() => new(
        (left, right) => left!.SequenceEqual(right!),
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item!.GetHashCode())),
        list => list.ToList());
}

internal sealed class KitchenStationConfiguration : IEntityTypeConfiguration<KitchenStation>
{
    public const string NormalizedNameProperty = "NormalizedName";
    public const string LocationNameIndex = "ix_kitchen_stations_tenant_id_location_id_normalized_name";

    public void Configure(EntityTypeBuilder<KitchenStation> builder)
    {
        builder.ToTable("kitchen_stations");

        builder.HasKey(station => station.Id);
        builder.Property(station => station.Id).HasColumnName("id").HasConversion(KitchenConverters.StationKey).ValueGeneratedNever();
        builder.Property(station => station.TenantId).HasColumnName("tenant_id").HasConversion(KitchenConverters.TenantKey);
        builder.Property(station => station.LocationId).HasColumnName("location_id").HasConversion(KitchenConverters.LocationKey);

        builder.Property(station => station.Name)
            .HasColumnName("name")
            .HasConversion(name => name.Value, value => StationName.Create(value))
            .HasMaxLength(StationName.MaxLength);
        // Noms uniques par établissement quelle que soit la casse : une copie en majuscules porte l'index unique.
        builder.Property<string>(NormalizedNameProperty)
            .HasColumnName("normalized_name")
            .HasMaxLength(StationName.MaxLength)
            .HasComputedColumnSql("upper(name)", stored: true);
        builder.HasIndex(nameof(KitchenStation.TenantId), nameof(KitchenStation.LocationId), NormalizedNameProperty)
            .IsUnique()
            .HasDatabaseName(LocationNameIndex);

        builder.Property(station => station.IsActive).HasColumnName("is_active");

        // Concurrence optimiste sur la colonne système xmin de PostgreSQL.
        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(station => station.DomainEvents);
    }
}

internal sealed class StationRoutingConfiguration : IEntityTypeConfiguration<StationRouting>
{
    public const string LocationIndex = "ix_station_routings_tenant_id_location_id";

    public void Configure(EntityTypeBuilder<StationRouting> builder)
    {
        builder.ToTable("station_routings");

        builder.HasKey(routing => routing.Id);
        builder.Property(routing => routing.Id).HasColumnName("id").HasConversion(KitchenConverters.RoutingKey).ValueGeneratedNever();
        builder.Property(routing => routing.TenantId).HasColumnName("tenant_id").HasConversion(KitchenConverters.TenantKey);
        builder.Property(routing => routing.LocationId).HasColumnName("location_id").HasConversion(KitchenConverters.LocationKey);
        // Un routage par établissement : garde-fou final contre deux créations concurrentes.
        builder.HasIndex(routing => new { routing.TenantId, routing.LocationId }).IsUnique().HasDatabaseName(LocationIndex);

        builder.Property(routing => routing.DefaultStationId).HasColumnName("default_station_id").HasConversion(KitchenConverters.StationKey);
        // Routes produit -> poste : objets-valeur, une seule valeur jsonb (2 000 au plus).
        builder.Property<List<StationRoute>>("_routes")
            .HasColumnName("routes")
            .HasColumnType("jsonb")
            .HasConversion(KitchenConverters.Routes, KitchenConverters.ListComparer<StationRoute>());
        builder.Property<List<ProductCourse>>("_courses")
            .HasColumnName("courses")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .HasConversion(KitchenConverters.Courses, KitchenConverters.ListComparer<ProductCourse>());

        // Concurrence optimiste sur la colonne système xmin de PostgreSQL.
        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(routing => routing.DomainEvents);
        builder.Ignore(routing => routing.Routes);
        builder.Ignore(routing => routing.Courses);
    }
}

internal sealed class KitchenTicketConfiguration : IEntityTypeConfiguration<KitchenTicket>
{
    public const string OwnerProperty = "TicketId";
    public const string OrderIndex = "ix_kitchen_tickets_tenant_id_order_id";

    public void Configure(EntityTypeBuilder<KitchenTicket> builder)
    {
        builder.ToTable("kitchen_tickets");

        builder.HasKey(ticket => ticket.Id);
        builder.Property(ticket => ticket.Id).HasColumnName("id").HasConversion(KitchenConverters.TicketKey).ValueGeneratedNever();
        builder.Property(ticket => ticket.TenantId).HasColumnName("tenant_id").HasConversion(KitchenConverters.TenantKey);
        builder.Property(ticket => ticket.LocationId).HasColumnName("location_id").HasConversion(KitchenConverters.LocationKey);
        builder.Property(ticket => ticket.OrderId).HasColumnName("order_id").HasConversion(KitchenConverters.OrderKey);
        // Un ticket par commande : garde-fou final contre un événement d'acceptation rejoué en parallèle.
        builder.HasIndex(ticket => new { ticket.TenantId, ticket.OrderId }).IsUnique().HasDatabaseName(OrderIndex);
        // Écran cuisine : tickets ouverts d'un établissement.
        builder.HasIndex(ticket => new { ticket.TenantId, ticket.LocationId, ticket.Status })
            .HasDatabaseName("ix_kitchen_tickets_tenant_id_location_id_status");

        // Numériques : les valeurs des enums sont explicites et stables.
        builder.Property(ticket => ticket.Status).HasColumnName("status");
        builder.Property(ticket => ticket.Priority).HasColumnName("priority");
        builder.ComplexProperty(ticket => ticket.ReadinessPolicy, policy =>
            policy.Property(p => p.RequiredReadyPercentage).HasColumnName("required_ready_percentage"));

        builder.Property(ticket => ticket.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(ticket => ticket.StartedAtUtc).HasColumnName("started_at_utc");
        builder.Property(ticket => ticket.ReadyAtUtc).HasColumnName("ready_at_utc");
        builder.Property(ticket => ticket.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(ticket => ticket.CancelledAtUtc).HasColumnName("cancelled_at_utc");
        builder.Property(ticket => ticket.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(KitchenTicket.MaxCancellationReasonLength);

        // Concurrence optimiste sur la colonne système xmin de PostgreSQL.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasMany(ticket => ticket.Items)
            .WithOne()
            .HasForeignKey(OwnerProperty)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(ticket => ticket.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(ticket => ticket.DomainEvents);
        builder.Ignore(ticket => ticket.IsClosed);
        builder.Ignore(ticket => ticket.CanBeMarkedReady);
        builder.Ignore(ticket => ticket.HeldCourses);
        builder.Ignore(ticket => ticket.CanBeCompleted);
    }
}

internal sealed class KitchenTicketItemConfiguration : IEntityTypeConfiguration<KitchenTicketItem>
{
    public const string PositionProperty = "Position";

    public void Configure(EntityTypeBuilder<KitchenTicketItem> builder)
    {
        builder.ToTable("kitchen_ticket_items");

        builder.HasKey(item => item.Id);
        // Jamais généré : une ligne atteinte via son ticket avec un id déjà défini est une NOUVELLE ligne.
        builder.Property(item => item.Id).HasColumnName("id").HasConversion(KitchenConverters.ItemKey).ValueGeneratedNever();
        builder.Property<KitchenTicketId>(KitchenTicketConfiguration.OwnerProperty)
            .HasColumnName("ticket_id")
            .HasConversion(KitchenConverters.TicketKey);
        // Ordre des lignes de la commande, posé par l'unit of work.
        builder.Property<int>(PositionProperty).HasColumnName("position");

        builder.Property(item => item.OrderItemId).HasColumnName("order_item_id").HasConversion(KitchenConverters.OrderItemKey);
        builder.Property(item => item.Name).HasColumnName("name").HasMaxLength(KitchenOrderLine.MaxNameLength);
        builder.Property(item => item.VariantName).HasColumnName("variant_name").HasMaxLength(KitchenOrderLine.MaxVariantNameLength);
        builder.Property<List<string>>("_modifiers")
            .HasColumnName("modifiers")
            .HasColumnType("jsonb")
            .HasConversion(KitchenConverters.Names, KitchenConverters.ListComparer<string>());
        builder.Property(item => item.Note).HasColumnName("note").HasMaxLength(KitchenOrderLine.MaxNoteLength);
        builder.Property(item => item.Quantity).HasColumnName("quantity");
        builder.Property(item => item.StationId).HasColumnName("station_id").HasConversion(KitchenConverters.StationKey);
        builder.Property(item => item.Status).HasColumnName("status");
        builder.Property(item => item.Course).HasColumnName("course").HasDefaultValue(KitchenCourse.Direct).HasSentinel(0);
        builder.Property(item => item.IsHeld).HasColumnName("is_held");

        builder.Ignore(item => item.Modifiers);
        builder.Ignore(item => item.IsReady);
    }
}
