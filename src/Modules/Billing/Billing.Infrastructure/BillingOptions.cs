namespace Billing.Infrastructure;

/// <summary>Réglages techniques du module (section <c>Billing</c> de la configuration de l'hôte).</summary>
public sealed class BillingOptions
{
    /// <summary>Faux : le job d'échéance ne tourne pas (tests, instance sans tâche de fond).</summary>
    public bool RunWorkers { get; set; } = true;

    /// <summary>Intervalle entre deux passages du job d'échéance.</summary>
    public TimeSpan DueInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Abonnements traités par passage.</summary>
    public int DueBatchSize { get; set; } = 200;

    /// <summary>Règlements en ligne en attente relus par passage.</summary>
    public int CheckoutBatchSize { get; set; } = 100;

    /// <summary>L'émetteur des factures d'abonnement (la société qui exploite la plateforme).</summary>
    public InvoiceIssuerOptions InvoiceIssuer { get; set; } = new();
}

public sealed class InvoiceIssuerOptions
{
    public string Name { get; set; } = "RestauSpace";

    public string? Address { get; set; }

    /// <summary>Identifiant fiscal (IFU, numéro de TVA).</summary>
    public string? TaxId { get; set; }

    /// <summary>Immatriculation (RCCM, SIRET).</summary>
    public string? RegistrationNumber { get; set; }

    public string? Email { get; set; }

    /// <summary>Mention de bas de page (régime de TVA, conditions de paiement).</summary>
    public string? Footer { get; set; }
}
