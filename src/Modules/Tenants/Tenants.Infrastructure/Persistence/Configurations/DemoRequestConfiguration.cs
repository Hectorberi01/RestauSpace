using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tenants.Domain.Prospects;

namespace Tenants.Infrastructure.Persistence.Configurations;

/// <summary>Agrégat <see cref="DemoRequest"/> : table <c>demo_requests</c>, demandes du site vitrine (données personnelles).</summary>
internal sealed class DemoRequestConfiguration : IEntityTypeConfiguration<DemoRequest>
{
    public void Configure(EntityTypeBuilder<DemoRequest> builder)
    {
        builder.ToTable("demo_requests", table =>
            table.HasCheckConstraint("ck_demo_requests_contact", "email IS NOT NULL OR phone IS NOT NULL"));

        builder.HasKey(request => request.Id);
        builder.Property(request => request.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => DemoRequestId.From(value))
            .ValueGeneratedNever();

        builder.Property(request => request.ContactName).HasColumnName("contact_name").HasMaxLength(DemoRequest.NameMaxLength);
        builder.Property(request => request.RestaurantName).HasColumnName("restaurant_name").HasMaxLength(DemoRequest.NameMaxLength);
        builder.Property(request => request.Email)
            .HasColumnName("email")
            .HasConversion(email => email!.Value, value => Email.Create(value))
            .HasMaxLength(Email.MaxLength);
        builder.Property(request => request.Phone)
            .HasColumnName("phone")
            .HasConversion(phone => phone!.Value, value => PhoneNumber.Create(value))
            .HasMaxLength(16);
        builder.Property(request => request.Country).HasColumnName("country").HasMaxLength(DemoRequest.CountryMaxLength);
        builder.Property(request => request.PlanInterest).HasColumnName("plan_interest").HasMaxLength(DemoRequest.PlanMaxLength);
        builder.Property(request => request.Message).HasColumnName("message").HasMaxLength(DemoRequest.MessageMaxLength);
        // Numérique : les valeurs de l'enum sont explicites et stables.
        builder.Property(request => request.Status).HasColumnName("status");
        builder.Property(request => request.Note).HasColumnName("note").HasMaxLength(DemoRequest.NoteMaxLength);
        builder.Property(request => request.SubmittedAtUtc).HasColumnName("submitted_at_utc");
        builder.Property(request => request.UpdatedAtUtc).HasColumnName("updated_at_utc");

        // Liste du back-office : par statut, de la plus récente à la plus ancienne.
        builder.HasIndex(request => new { request.Status, request.SubmittedAtUtc }).HasDatabaseName("ix_demo_requests_status_submitted_at_utc");
        builder.HasIndex(request => request.SubmittedAtUtc).HasDatabaseName("ix_demo_requests_submitted_at_utc");

        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(request => request.DomainEvents);
    }
}
