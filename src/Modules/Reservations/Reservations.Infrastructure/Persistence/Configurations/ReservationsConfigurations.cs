using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Reservations.Domain;

namespace Reservations.Infrastructure.Persistence.Configurations;

internal static class ReservationsConverters
{
    public static readonly ValueConverter<TenantId, Guid> TenantKey = new(id => id.Value, value => TenantId.From(value));
    public static readonly ValueConverter<LocationId, Guid> LocationKey = new(id => id.Value, value => LocationId.From(value));
    public static readonly ValueConverter<ReservationId, Guid> ReservationKey = new(id => id.Value, value => ReservationId.From(value));
    public static readonly ValueConverter<CustomerId, Guid> CustomerKey = new(id => id.Value, value => CustomerId.From(value));
    public static readonly ValueConverter<TableSessionId, Guid> SessionKey = new(id => id.Value, value => TableSessionId.From(value));
    public static readonly ValueConverter<WaitlistEntryId, Guid> WaitlistKey = new(id => id.Value, value => WaitlistEntryId.From(value));
    // Typés nullables comme les propriétés de GuestContact ; EF n'appelle jamais un converter pour null.
    public static readonly ValueConverter<PhoneNumber?, string> Phone = new(phone => phone!.Value, value => PhoneNumber.Create(value));
    public static readonly ValueConverter<Email?, string> EmailAddress = new(email => email!.Value, value => Email.Create(value));
}

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("reservations");

        builder.HasKey(reservation => reservation.Id);
        builder.Property(reservation => reservation.Id).HasColumnName("id").HasConversion(ReservationsConverters.ReservationKey).ValueGeneratedNever();
        builder.Property(reservation => reservation.TenantId).HasColumnName("tenant_id").HasConversion(ReservationsConverters.TenantKey);
        builder.Property(reservation => reservation.LocationId).HasColumnName("location_id").HasConversion(ReservationsConverters.LocationKey);
        builder.Property(reservation => reservation.CustomerId).HasColumnName("customer_id").HasConversion(ReservationsConverters.CustomerKey);
        // Agenda et décompte de capacité : toujours par établissement et par plage de début.
        builder.HasIndex(reservation => new { reservation.TenantId, reservation.LocationId, reservation.StartAtUtc })
            .HasDatabaseName("ix_reservations_tenant_id_location_id_start_at_utc");
        // L'effacement d'un client retrouve ses réservations.
        builder.HasIndex(reservation => new { reservation.TenantId, reservation.CustomerId })
            .HasFilter("customer_id IS NOT NULL")
            .HasDatabaseName("ix_reservations_tenant_id_customer_id");

        // Instantané des coordonnées : trois colonnes, téléphone et email facultatifs (tous deux vides une fois anonymisé).
        builder.ComplexProperty(reservation => reservation.GuestContact, contact =>
        {
            contact.Property(c => c.Name).HasColumnName("guest_name").HasMaxLength(GuestContact.NameMaxLength);
            contact.Property(c => c.Phone).HasColumnName("guest_phone").HasConversion(ReservationsConverters.Phone).HasMaxLength(32);
            contact.Property(c => c.Email).HasColumnName("guest_email").HasConversion(ReservationsConverters.EmailAddress).HasMaxLength(320);
        });

        builder.Property(reservation => reservation.PartySize).HasColumnName("party_size");
        builder.Property(reservation => reservation.StartAtUtc).HasColumnName("start_at_utc");
        builder.Property(reservation => reservation.Duration).HasColumnName("duration");
        // Numériques : les valeurs des enums sont explicites et stables.
        builder.Property(reservation => reservation.Status).HasColumnName("status");
        builder.Property(reservation => reservation.CancelledBy).HasColumnName("cancelled_by");
        builder.Property(reservation => reservation.CancellationReason).HasColumnName("cancellation_reason");
        builder.Property(reservation => reservation.Notes).HasColumnName("notes").HasMaxLength(Reservation.NotesMaxLength);
        builder.Property(reservation => reservation.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(reservation => reservation.TableSessionId).HasColumnName("table_session_id").HasConversion(ReservationsConverters.SessionKey);

        // Acompte : montant et devise à plat, statut numérique ; tout est vide pour une réservation sans acompte.
        builder.Property<decimal?>("_depositAmount").HasColumnName("deposit_amount").HasPrecision(19, 3);
        builder.Property<string?>("_depositCurrency").HasColumnName("deposit_currency").HasMaxLength(3);
        builder.Property<decimal?>("_depositSettled").HasColumnName("deposit_settled").HasPrecision(19, 3);
        builder.Property(reservation => reservation.DepositStatus).HasColumnName("deposit_status");
        builder.Property(reservation => reservation.DepositDueBeforeUtc).HasColumnName("deposit_due_before_utc");
        // Le job retrouve les acomptes en retard de tous les restaurants.
        builder.HasIndex(reservation => reservation.DepositDueBeforeUtc)
            .HasFilter("status = 1 AND deposit_status = 1")
            .HasDatabaseName("ix_reservations_unpaid_deposit_due");

        // Concurrence optimiste sur la colonne système xmin de PostgreSQL (Seat et Cancel simultanés...).
        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(reservation => reservation.DepositAmount);
        builder.Ignore(reservation => reservation.DepositSettled);
        builder.Ignore(reservation => reservation.HasDeposit);
        builder.Ignore(reservation => reservation.DomainEvents);
        builder.Ignore(reservation => reservation.IsGuestAnonymized);
        builder.Ignore(reservation => reservation.Slot);
        builder.Ignore(reservation => reservation.HoldsCapacity);
    }
}

internal sealed class ReservationPolicyRowConfiguration : IEntityTypeConfiguration<ReservationPolicyRow>
{
    public void Configure(EntityTypeBuilder<ReservationPolicyRow> builder)
    {
        builder.ToTable("location_policies");

        // Une politique par établissement.
        builder.HasKey(row => new { row.TenantId, row.LocationId });
        builder.Property(row => row.TenantId).HasColumnName("tenant_id").HasConversion(ReservationsConverters.TenantKey);
        builder.Property(row => row.LocationId).HasColumnName("location_id").HasConversion(ReservationsConverters.LocationKey);

        builder.Property(row => row.MinLeadTime).HasColumnName("min_lead_time");
        builder.Property(row => row.MaxAdvance).HasColumnName("max_advance");
        builder.Property(row => row.DefaultDuration).HasColumnName("default_duration");
        builder.Property(row => row.CancellationCutoff).HasColumnName("cancellation_cutoff");
        builder.Property(row => row.MinDuration).HasColumnName("min_duration");
        builder.Property(row => row.MaxDuration).HasColumnName("max_duration");
        builder.Property(row => row.MaxPartySize).HasColumnName("max_party_size");
        builder.Property(row => row.AcceptsOnlineBookings).HasColumnName("accepts_online_bookings").HasDefaultValue(false);

        builder.Property(row => row.DepositMode).HasColumnName("deposit_mode").HasDefaultValue(DepositMode.Off);
        builder.Property(row => row.DepositBasis).HasColumnName("deposit_basis").HasDefaultValue(DepositBasis.PerGuest);
        builder.Property(row => row.DepositAmount).HasColumnName("deposit_amount").HasPrecision(19, 3);
        builder.Property(row => row.DepositCurrency).HasColumnName("deposit_currency").HasMaxLength(3);
        builder.Property(row => row.DepositMinPartySize).HasColumnName("deposit_min_party_size").HasDefaultValue(1);
        builder.Property(row => row.DepositPaymentWindow).HasColumnName("deposit_payment_window").HasDefaultValue(DepositRule.DefaultPaymentWindow);
        builder.Property(row => row.WaitlistAcceptsOnline).HasColumnName("waitlist_accepts_online").HasDefaultValue(false);
        builder.Property(row => row.WaitlistOfferWindow).HasColumnName("waitlist_offer_window").HasDefaultValue(WaitlistSettings.DefaultOfferWindow);
    }
}

internal sealed class WaitlistEntryConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    public void Configure(EntityTypeBuilder<WaitlistEntry> builder)
    {
        builder.ToTable("waitlist_entries");

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).HasColumnName("id").HasConversion(ReservationsConverters.WaitlistKey).ValueGeneratedNever();
        builder.Property(entry => entry.TenantId).HasColumnName("tenant_id").HasConversion(ReservationsConverters.TenantKey);
        builder.Property(entry => entry.LocationId).HasColumnName("location_id").HasConversion(ReservationsConverters.LocationKey);
        builder.Property(entry => entry.CustomerId).HasColumnName("customer_id").HasConversion(ReservationsConverters.CustomerKey);
        builder.ComplexProperty(entry => entry.GuestContact, contact =>
        {
            contact.Property(c => c.Name).HasColumnName("guest_name").HasMaxLength(GuestContact.NameMaxLength);
            contact.Property(c => c.Phone).HasColumnName("guest_phone").HasConversion(ReservationsConverters.Phone).HasMaxLength(32);
            contact.Property(c => c.Email).HasColumnName("guest_email").HasConversion(ReservationsConverters.EmailAddress).HasMaxLength(320);
        });
        builder.Property(entry => entry.PartySize).HasColumnName("party_size");
        builder.Property(entry => entry.DesiredStartUtc).HasColumnName("desired_start_utc");
        builder.Property(entry => entry.FlexibilityMinutes).HasColumnName("flexibility_minutes");
        builder.Property(entry => entry.Notes).HasColumnName("notes").HasMaxLength(WaitlistEntry.NotesMaxLength);
        builder.Property(entry => entry.Source).HasColumnName("source");
        builder.Property(entry => entry.Status).HasColumnName("status");
        builder.Property(entry => entry.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(entry => entry.OfferedStartUtc).HasColumnName("offered_start_utc");
        builder.Property(entry => entry.OfferExpiresAtUtc).HasColumnName("offer_expires_at_utc");
        builder.Property(entry => entry.MissedOffers).HasColumnName("missed_offers");
        builder.Property(entry => entry.ReservationId).HasColumnName("reservation_id").HasConversion(ReservationsConverters.ReservationKey);
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasIndex(entry => new { entry.TenantId, entry.LocationId, entry.DesiredStartUtc }).HasDatabaseName("ix_waitlist_entries_tenant_id_location_id_desired_start_utc");
        builder.HasIndex(entry => new { entry.TenantId, entry.CustomerId }).HasFilter("customer_id IS NOT NULL").HasDatabaseName("ix_waitlist_entries_tenant_id_customer_id");
        // Le job : offres expirées et inscriptions ouvertes dont l'heure passe.
        builder.HasIndex(entry => entry.DesiredStartUtc).HasFilter("status IN (1, 2)").HasDatabaseName("ix_waitlist_entries_open");

        builder.Ignore(entry => entry.DomainEvents);
        builder.Ignore(entry => entry.IsOpen);
        builder.Ignore(entry => entry.EarliestStartUtc);
        builder.Ignore(entry => entry.LatestStartUtc);
    }
}
