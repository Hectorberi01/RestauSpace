using Payments.Application.Abstractions;
using Payments.Domain;

namespace Payments.Application.Commands;

/// <summary>
/// Restaurant : vérifie le compte marchand d'une configuration en ligne par un appel sans effet chez le prestataire.
/// Rien n'est encaissé ni enregistré. La réponse ne contient ni identifiant ni message du prestataire : seulement le
/// résultat, l'environnement et les réglages manquants.
/// </summary>
public sealed record TestPaymentConfigurationCommand(Guid ConfigurationId) : ICommand<PaymentConfigurationTestDto>;

/// <param name="Outcome">
/// <c>verified</c>, <c>credentials_rejected</c>, <c>credentials_missing</c>, <c>credentials_incomplete</c>,
/// <c>credentials_unreadable</c>, <c>provider_unavailable</c>, <c>provider_error</c> ou <c>not_verifiable</c>.
/// </param>
/// <param name="ProviderStatus">Statut HTTP du prestataire quand il a répondu par une erreur.</param>
public sealed record PaymentConfigurationTestDto(
    string Outcome, bool? IsSandbox, int? ProviderStatus, IReadOnlyList<string> Warnings, DateTimeOffset TestedAtUtc)
{
    public bool Succeeded => Outcome == PaymentConfigurationTestOutcomes.Verified;
}

public static class PaymentConfigurationTestOutcomes
{
    public const string Verified = "verified";
    public const string CredentialsRejected = "credentials_rejected";
    public const string CredentialsMissing = "credentials_missing";
    public const string CredentialsIncomplete = "credentials_incomplete";
    public const string CredentialsUnreadable = "credentials_unreadable";
    public const string ProviderUnavailable = "provider_unavailable";
    public const string ProviderError = "provider_error";
    public const string NotVerifiable = "not_verifiable";
}

public sealed class TestPaymentConfigurationHandler(PaymentsWorkspace workspace, IClock clock)
    : ICommandHandler<TestPaymentConfigurationCommand, PaymentConfigurationTestDto>
{
    public async Task<PaymentConfigurationTestDto> HandleAsync(TestPaymentConfigurationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var configuration = await workspace.LoadConfigurationAsync(workspace.CurrentTenant, command.ConfigurationId, cancellationToken);
        DomainException.ThrowIf(configuration.ProviderType != PaymentProviderType.Online, PaymentsApplicationErrors.TestNotApplicable);
        DomainException.ThrowIf(workspace.Integrations.Find(configuration.Provider) is null, PaymentsApplicationErrors.IntegrationMissing);

        if (CredentialReferences.Of(configuration) is null)
        {
            return Result(PaymentConfigurationTestOutcomes.CredentialsMissing);
        }

        IPaymentGateway gateway;
        try
        {
            gateway = await workspace.GatewayForAsync(configuration, cancellationToken);
        }
        catch (DomainException)
        {
            return Result(PaymentConfigurationTestOutcomes.CredentialsUnreadable);
        }
        catch (PaymentGatewayException)
        {
            return Result(PaymentConfigurationTestOutcomes.CredentialsIncomplete);
        }

        try
        {
            var check = await gateway.CheckCredentialsAsync(cancellationToken);
            var outcome = check.Status switch
            {
                GatewayCredentialStatus.Verified => PaymentConfigurationTestOutcomes.Verified,
                GatewayCredentialStatus.Rejected => PaymentConfigurationTestOutcomes.CredentialsRejected,
                _ => PaymentConfigurationTestOutcomes.NotVerifiable,
            };
            return Result(outcome, check.IsSandbox, warnings: check.Warnings);
        }
        catch (PaymentGatewayException exception)
        {
            return Result(
                exception.IsPermanent ? PaymentConfigurationTestOutcomes.ProviderError : PaymentConfigurationTestOutcomes.ProviderUnavailable,
                status: exception.StatusCode);
        }
    }

    private PaymentConfigurationTestDto Result(string outcome, bool? isSandbox = null, int? status = null, IReadOnlyList<string>? warnings = null) =>
        new(outcome, isSandbox, status, warnings ?? [], clock.UtcNow);
}
