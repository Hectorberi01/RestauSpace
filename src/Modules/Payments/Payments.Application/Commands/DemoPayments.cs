using Payments.Domain;

namespace Payments.Application.Commands;

/// <summary>
/// Démo publique des modèles : le visiteur valide le paiement simulé (prestataire <c>sandbox</c>). Le paiement est
/// encaissé pour son montant, comme si le prestataire l'avait confirmé. Seulement pour un restaurant de démonstration ;
/// rejouable.
/// </summary>
public sealed record CaptureSandboxPaymentCommand(string? Reference) : ICommand
{
    public const string Provider = "sandbox";

    public override string ToString() => "CaptureSandboxPaymentCommand (***)";
}

public sealed class CaptureSandboxPaymentHandler(PaymentsWorkspace workspace, IClock clock, IDemoTenants? demo = null)
    : ICommandHandler<CaptureSandboxPaymentCommand>
{
    public async Task HandleAsync(CaptureSandboxPaymentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        if (demo is null || !demo.IsDemo(tenant))
        {
            throw new ForbiddenException(PaymentsApplicationErrors.DemoOnly);
        }

        DomainException.ThrowIf(string.IsNullOrWhiteSpace(command.Reference), PaymentsApplicationErrors.ProviderResultInvalid);
        var reference = ProviderReference.Create(command.Reference);
        var payment = await workspace.Payments.FindByProviderReferenceAsync(
            tenant, PaymentProviderCode.Create(CaptureSandboxPaymentCommand.Provider), reference, cancellationToken)
            ?? throw new NotFoundException(PaymentsApplicationErrors.PaymentNotFound);
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return;
        }

        payment.Capture(reference, payment.Amount, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Le prestataire simulé n'est permis qu'aux restaurants de démonstration (hors développement).</summary>
internal static class SandboxGuard
{
    public static void Ensure(IDemoTenants? demo, TenantId tenant, PaymentProviderCode provider) =>
        DomainException.ThrowIf(
            provider == PaymentProviderCode.Create(CaptureSandboxPaymentCommand.Provider) && demo is not null && !demo.AllowsSandbox(tenant),
            PaymentsApplicationErrors.SandboxNotAllowed);
}
