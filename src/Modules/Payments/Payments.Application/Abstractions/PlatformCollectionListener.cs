namespace Payments.Application.Abstractions;

/// <summary>
/// Prévenu quand un webhook signé du compte de la PLATEFORME annonce une transaction (abonnements). L'hôte relit
/// l'encaissement correspondant côté Billing. Par défaut : rien.
/// </summary>
public interface IPlatformCollectionListener
{
    Task TransactionChangedAsync(string provider, string providerReference, CancellationToken cancellationToken = default);
}
