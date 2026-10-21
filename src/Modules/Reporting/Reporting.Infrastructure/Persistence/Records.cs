using Reporting.Application.Platform;
using Reporting.Domain;

namespace Reporting.Infrastructure.Persistence;

// Lignes des projections et des tables techniques du module. Ce ne sont pas des agrégats : elles n'ont pas de règle
// métier, seulement des gardes en base (clés, CHECK) et le filtre de tenant pour celles qui appartiennent à un restaurant.

/// <summary>Un compteur additif du jour (jour LOCAL du restaurant). Établissement vide : fait du restaurant.</summary>
internal sealed class DailyMetricRecord : ITenantOwned
{
    public TenantId TenantId { get; set; }

    public Guid LocationId { get; set; }

    public DateOnly Day { get; set; }

    public string Metric { get; set; } = string.Empty;

    public string Dimension { get; set; } = string.Empty;

    public string Currency { get; set; } = string.Empty;

    public decimal Value { get; set; }
}

/// <summary>Un message de l'outbox déjà compté (idempotence des compteurs).</summary>
internal sealed class ProcessedSourceRecord : ITenantOwned
{
    public string SourceKey { get; set; } = string.Empty;

    public TenantId TenantId { get; set; }

    public DateTimeOffset ProcessedAtUtc { get; set; }
}

/// <summary>Les paramètres figés d'un export (JSON).</summary>
internal sealed class ExportRequestRecord : ITenantOwned
{
    public ExportJobId ExportJobId { get; set; }

    public TenantId TenantId { get; set; }

    public string Parameters { get; set; } = string.Empty;
}

/// <summary>Le fichier d'un export, stocké en base en attendant le module Files.</summary>
internal sealed class StoredExportFileRecord : ITenantOwned
{
    public Guid Id { get; set; }

    public TenantId TenantId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public byte[] Content { get; set; } = [];

    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>PLATEFORME : l'état courant d'un restaurant. Jamais lu par une route d'un restaurant.</summary>
internal sealed class PlatformTenantRecord
{
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public PlatformTenantStatus Status { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ObservedAtUtc { get; set; }
}

/// <summary>PLATEFORME : l'abonnement courant d'un restaurant.</summary>
internal sealed class PlatformSubscriptionRecord
{
    public Guid TenantId { get; set; }

    public Guid SubscriptionId { get; set; }

    public Guid PlanId { get; set; }

    public string PlanName { get; set; } = string.Empty;

    public PlatformSubscriptionStatus Status { get; set; }

    public decimal MonthlyAmount { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public DateTimeOffset ObservedAtUtc { get; set; }
}
