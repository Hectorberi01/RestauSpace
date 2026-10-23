using Payments.Application.Abstractions;

namespace Payments.Infrastructure.Webhooks;

/// <summary>Par défaut, sans hôte qui relie Billing : rien.</summary>
internal sealed class NoPlatformCollectionListener : IPlatformCollectionListener
{
    public Task TransactionChangedAsync(string provider, string providerReference, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
