using Locations.Domain.Locations;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Locations.Infrastructure.Persistence.Configurations;

internal sealed class RestaurantLocationConfiguration : IEntityTypeConfiguration<RestaurantLocation>
{
    public void Configure(EntityTypeBuilder<RestaurantLocation> builder)
    {
        builder.ToTable("locations");

        builder.HasKey(location => location.Id);
        builder.Property(location => location.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => LocationId.From(value))
            .ValueGeneratedNever();

        builder.Property(location => location.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => TenantId.From(value));
        // Toute requête commence par le tenant (repository + query filter).
        builder.HasIndex(location => location.TenantId).HasDatabaseName("ix_locations_tenant_id");

        builder.Property(location => location.Name)
            .HasColumnName("name")
            .HasConversion(name => name.Value, value => LocationName.Create(value))
            .HasMaxLength(LocationName.MaxLength);

        // Numérique : les valeurs de l'enum sont explicites et stables.
        builder.Property(location => location.Status).HasColumnName("status");

        builder.Property(location => location.Capacity)
            .HasColumnName("seats")
            .HasConversion(capacity => capacity.Seats, value => Capacity.Create(value));

        builder.Property(location => location.TimeZone)
            .HasColumnName("time_zone")
            .HasConversion(zone => zone.Value, value => TimeZoneId.Create(value))
            .HasMaxLength(TimeZoneId.MaxLength);

        // Value objects sans colonnes naturelles : jsonb, reconstruits par leurs factories du domaine. Ils sont
        // immuables et remplacés en bloc, donc les comparer par valeur suffit au suivi des modifications.
        builder.Property(location => location.Address)
            .HasColumnName("address")
            .HasColumnType("jsonb")
            .HasConversion(address => LocationJson.Write(address), json => LocationJson.ReadAddress(json), ByValue<Address>());

        builder.Property(location => location.Contact)
            .HasColumnName("contact")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .HasConversion(contact => LocationJson.Write(contact), json => LocationJson.ReadContact(json), ByValue<LocationContact>());

        builder.Property(location => location.Hours)
            .HasColumnName("weekly_hours")
            .HasColumnType("jsonb")
            .HasConversion(hours => LocationJson.Write(hours), json => LocationJson.ReadHours(json), ByValue<BusinessHours>());

        builder.Property(location => location.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(location => location.TemporaryClosureReason)
            .HasColumnName("temporary_closure_reason")
            .HasMaxLength(RestaurantLocation.ReasonMaxLength);
        builder.Property(location => location.ArchivedAtUtc).HasColumnName("archived_at_utc");

        // Concurrence optimiste sur la colonne système xmin de PostgreSQL.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasMany(location => location.ExceptionalSchedules)
            .WithOne()
            .HasForeignKey(ExceptionalScheduleConfiguration.LocationIdProperty)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(location => location.ExceptionalSchedules).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(location => location.DomainEvents);
        builder.Ignore(location => location.IsOperational);
    }

    private static ValueComparer<T> ByValue<T>()
        where T : ValueObject =>
        new((left, right) => Equals(left, right), value => value.GetHashCode(), value => value);
}

internal sealed class ExceptionalScheduleConfiguration : IEntityTypeConfiguration<ExceptionalSchedule>
{
    public const string LocationIdProperty = "LocationId";
    public const string LocationDateIndex = "ix_exceptional_schedules_location_id_date";

    public void Configure(EntityTypeBuilder<ExceptionalSchedule> builder)
    {
        builder.ToTable("exceptional_schedules");

        builder.HasKey(schedule => schedule.Id);
        // Jamais généré : un horaire atteint via son établissement avec un id déjà défini est une NOUVELLE ligne.
        builder.Property(schedule => schedule.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => ExceptionalScheduleId.From(value))
            .ValueGeneratedNever();

        builder.Property<LocationId>(LocationIdProperty)
            .HasColumnName("location_id")
            .HasConversion(id => id.Value, value => LocationId.From(value));

        builder.Property(schedule => schedule.Date).HasColumnName("date");
        builder.Property(schedule => schedule.Reason)
            .HasColumnName("reason")
            .HasMaxLength(ExceptionalSchedule.ReasonMaxLength);

        // Les plages vivent dans une liste privée : mappées via le champ, en jsonb, jamais modifiées après création.
        builder.Property<List<TimeRange>>("_ranges")
            .HasColumnName("ranges")
            .HasColumnType("jsonb")
            .HasConversion(
                ranges => LocationJson.Write(ranges),
                json => LocationJson.ReadRanges(json),
                new ValueComparer<List<TimeRange>>(
                    (left, right) => left!.SequenceEqual(right!),
                    ranges => ranges.Aggregate(0, (hash, range) => HashCode.Combine(hash, range.GetHashCode())),
                    ranges => ranges.ToList()));

        // Une seule exception par date et par établissement : garde-fou final contre deux ajouts concurrents.
        builder.HasIndex(LocationIdProperty, nameof(ExceptionalSchedule.Date)).IsUnique().HasDatabaseName(LocationDateIndex);

        builder.Ignore(schedule => schedule.Ranges);
        builder.Ignore(schedule => schedule.IsClosed);
    }
}
