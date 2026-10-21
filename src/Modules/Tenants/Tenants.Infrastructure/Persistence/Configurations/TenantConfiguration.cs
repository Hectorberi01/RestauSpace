using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tenants.Domain.Tenants;

namespace Tenants.Infrastructure.Persistence.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");

        builder.HasKey(tenant => tenant.Id);
        builder.Property(tenant => tenant.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => TenantId.From(value))
            .ValueGeneratedNever();

        builder.Property(tenant => tenant.Name)
            .HasColumnName("name")
            .HasConversion(name => name.Value, value => TenantName.Create(value))
            .HasMaxLength(TenantName.MaxLength);

        // Numérique : les valeurs de l'enum sont explicites et stables.
        builder.Property(tenant => tenant.Status).HasColumnName("status");

        builder.Property(tenant => tenant.DefaultCurrency)
            .HasColumnName("default_currency")
            .HasConversion(currency => currency.Code, value => Currency.FromCode(value))
            .HasMaxLength(3)
            .IsFixedLength();

        builder.Property(tenant => tenant.TimeZone)
            .HasColumnName("time_zone")
            .HasConversion(timeZone => timeZone.Value, value => TimeZoneId.Create(value))
            .HasMaxLength(TimeZoneId.MaxLength);

        builder.ComplexProperty(tenant => tenant.Branding, branding =>
        {
            branding.Property(b => b.DisplayName)
                .HasColumnName("branding_display_name")
                .HasMaxLength(Branding.DisplayNameMaxLength);

            // AbsoluteUri, comme dans l'égalité de Branding : Uri.ToString() déséchappe et ne ferait pas l'aller-retour.
            branding.Property(b => b.LogoUrl)
                .HasColumnName("branding_logo_url")
                .HasConversion(url => url!.AbsoluteUri, value => new Uri(value, UriKind.Absolute));

            branding.Property(b => b.PrimaryColor)
                .HasColumnName("branding_primary_color")
                .HasMaxLength(7);
        });

        // Présentation publique : jsonb, relue par la fabrique du domaine.
        builder.Property(tenant => tenant.PublicProfile)
            .HasColumnName("public_profile")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .HasConversion(profile => PublicProfileJson.Write(profile), json => PublicProfileJson.Read(json), PublicProfileJson.Comparer);

        builder.Property(tenant => tenant.PricesIncludeTax).HasColumnName("prices_include_tax");
        builder.Property(tenant => tenant.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(tenant => tenant.SuspensionReason).HasColumnName("suspension_reason");
        builder.Property(tenant => tenant.SuspendedAtUtc).HasColumnName("suspended_at_utc");
        builder.Property(tenant => tenant.ClosedAtUtc).HasColumnName("closed_at_utc");

        // Concurrence optimiste sur la colonne système xmin de PostgreSQL : aucune colonne à maintenir de notre côté.
        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(tenant => tenant.DomainEvents);
        builder.Ignore(tenant => tenant.CanAcceptBusinessWrites);
    }
}

internal static class PublicProfileJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static readonly ValueComparer<PublicProfile> Comparer =
        new((left, right) => Equals(left, right), value => value.GetHashCode(), value => value);

    public static string Write(PublicProfile profile) => JsonSerializer.Serialize(
        new Row(
            profile.Tagline,
            profile.Description,
            profile.CoverImageUrl?.AbsoluteUri,
            profile.Links.Select(link => new LinkRow((int)link.Network, link.Url.AbsoluteUri)).ToList()),
        Options);

    public static PublicProfile Read(string json)
    {
        var row = JsonSerializer.Deserialize<Row>(json, Options);
        return row is null
            ? PublicProfile.Empty
            : PublicProfile.Create(
                row.Tagline,
                row.Description,
                row.CoverImageUrl is null ? null : new Uri(row.CoverImageUrl, UriKind.Absolute),
                (row.Links ?? []).Select(link => new SocialLink((SocialNetwork)link.Network, new Uri(link.Url, UriKind.Absolute))));
    }

    private sealed record Row(string? Tagline, string? Description, string? CoverImageUrl, List<LinkRow>? Links);

    private sealed record LinkRow(int Network, string Url);
}
