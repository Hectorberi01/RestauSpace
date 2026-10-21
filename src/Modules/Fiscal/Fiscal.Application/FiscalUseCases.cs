using System.Globalization;
using Fiscal.Domain;

namespace Fiscal.Application;

/// <summary>Codes de permission déclarés par le module Fiscal (accordés via les rôles Staff).</summary>
public static class FiscalPermissions
{
    /// <summary>Voir le profil fiscal et les factures normalisées.</summary>
    public const string View = "fiscal.view";

    /// <summary>Saisir l'IFU, le jeton e-MECeF, choisir l'environnement, relancer une facture.</summary>
    public const string Configure = "fiscal.configure";

    public static IReadOnlyList<string> All { get; } = [View, Configure];
}

public static class FiscalApplicationErrors
{
    public static readonly DomainError ConcurrentUpdate =
        new("fiscal.concurrent_update", "The resource was changed by someone else; reload it and try again.", DomainErrorKind.Conflict);

    public static readonly DomainError TenantNotWritable = new("fiscal.tenant.not_writable", "The restaurant account is suspended or closed.");
    public static readonly DomainError ProfileNotFound = new("fiscal.profile.not_found", "The restaurant has no tax profile yet.");
    public static readonly DomainError InvoiceNotFound = new("fiscal.invoice.not_found", "The invoice does not exist.");
    public static readonly DomainError CurrencyNotSupported = new("fiscal.profile.currency_not_supported", "Standardized invoices (e-MECeF) are issued in CFA francs (XOF) only.");
    public static readonly DomainError TokenInvalid = new("fiscal.profile.token_invalid", "The e-MECeF token is not valid (copy it again from the DGI portal).");
    public static readonly DomainError TokenUnreadable = new("fiscal.profile.token_unreadable", "The e-MECeF token can no longer be read; enter it again.");
    public static readonly DomainError AuthorityRejected = new("fiscal.authority.rejected", "The DGI refused the token or the request.");
    public static readonly DomainError AuthorityUnreachable = new("fiscal.authority.unreachable", "The DGI e-MECeF service cannot be reached; try again later.");
}

/// <summary>Étapes communes : tenant du contexte, écriture d'un tenant actif, chargements dans ce tenant seulement.</summary>
public sealed class FiscalWorkspace(
    ITenantContext tenantContext,
    IFiscalTenants tenants,
    IFiscalProfileRepository profiles,
    IFiscalInvoiceRepository invoices,
    IFiscalTokenStore tokens,
    IFiscalUnitOfWork unitOfWork)
{
    public IFiscalProfileRepository Profiles => profiles;

    public IFiscalInvoiceRepository Invoices => invoices;

    public IFiscalTokenStore Tokens => tokens;

    public IFiscalTenants Tenants => tenants;

    public TenantId CurrentTenant => tenantContext.RequireTenant();

    public async Task RequireWritableTenantAsync(CancellationToken cancellationToken)
    {
        if (!await tenants.AcceptsBusinessWritesAsync(CurrentTenant, cancellationToken))
        {
            throw new DomainException(FiscalApplicationErrors.TenantNotWritable);
        }
    }

    public async Task<FiscalProfile> RequireProfileAsync(CancellationToken cancellationToken) =>
        await profiles.FindAsync(CurrentTenant, cancellationToken) ?? throw new NotFoundException(FiscalApplicationErrors.ProfileNotFound);

    public async Task<FiscalInvoice> LoadInvoiceAsync(Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await invoices.FindAsync(CurrentTenant, FiscalInvoiceId.From(id), cancellationToken))
        ?? throw new NotFoundException(FiscalApplicationErrors.InvoiceNotFound);

    /// <summary>Le jeton du restaurant, lu au coffre au moment de l'appel seulement.</summary>
    public async Task<string> TokenAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        try
        {
            return await tokens.ReadAsync(tenantId, cancellationToken) ?? throw new DomainException(FiscalErrors.TokenMissing);
        }
        catch (InvalidOperationException exception)
        {
            throw new DomainException(FiscalApplicationErrors.TokenUnreadable, exception);
        }
    }

    public Task SaveAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync(cancellationToken);
}

public sealed record FiscalProfileDto(
    string Ifu, string? Rccm, TaxRegime Regime, AibRate Aib, FiscalMode Mode, bool HasToken, DateTimeOffset? TokenUpdatedAtUtc, DateTimeOffset UpdatedAtUtc)
{
    public static FiscalProfileDto From(FiscalProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new FiscalProfileDto(profile.Ifu, profile.Rccm, profile.Regime, profile.Aib, profile.Mode, profile.HasToken, profile.TokenUpdatedAtUtc, profile.UpdatedAtUtc);
    }
}

/// <summary>Le profil fiscal du restaurant, ou null s'il n'est pas encore saisi. Le jeton n'est jamais renvoyé.</summary>
public sealed record GetFiscalProfileQuery : IQuery<FiscalProfileDto?>;

public sealed class GetFiscalProfileHandler(FiscalWorkspace workspace) : IQueryHandler<GetFiscalProfileQuery, FiscalProfileDto?>
{
    public async Task<FiscalProfileDto?> HandleAsync(GetFiscalProfileQuery query, CancellationToken cancellationToken = default) =>
        await workspace.Profiles.FindAsync(workspace.CurrentTenant, cancellationToken) is { } profile ? FiscalProfileDto.From(profile) : null;
}

/// <summary>Crée ou met à jour l'identité fiscale du restaurant (francs CFA seulement).</summary>
public sealed record ConfigureFiscalProfileCommand(string? Ifu, string? Rccm, TaxRegime Regime, AibRate Aib) : ICommand;

public sealed class ConfigureFiscalProfileHandler(FiscalWorkspace workspace, IClock clock) : ICommandHandler<ConfigureFiscalProfileCommand>
{
    public async Task HandleAsync(ConfigureFiscalProfileCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        DomainException.ThrowIf(await workspace.Tenants.CurrencyAsync(tenant, cancellationToken) != "XOF", FiscalApplicationErrors.CurrencyNotSupported);
        if (await workspace.Profiles.FindAsync(tenant, cancellationToken) is { } profile)
        {
            profile.Update(command.Ifu, command.Rccm, command.Regime, command.Aib, clock);
        }
        else
        {
            workspace.Profiles.Add(FiscalProfile.Create(tenant, command.Ifu, command.Rccm, command.Regime, command.Aib, clock));
        }

        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Range le jeton e-MECeF au coffre. Il n'est jamais relu par la console.</summary>
public sealed record SetFiscalTokenCommand(string? Token) : ICommand;

public sealed class SetFiscalTokenHandler(FiscalWorkspace workspace, IClock clock) : ICommandHandler<SetFiscalTokenCommand>
{
    public async Task HandleAsync(SetFiscalTokenCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var token = command.Token?.Trim() ?? string.Empty;
        DomainException.ThrowIf(token.Length is < 20 or > 4000 || token.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)), FiscalApplicationErrors.TokenInvalid);
        var profile = await workspace.RequireProfileAsync(cancellationToken);
        await workspace.Tokens.StoreAsync(profile.TenantId, token, cancellationToken);
        profile.MarkTokenStored(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record SetFiscalModeCommand(FiscalMode Mode) : ICommand;

public sealed class SetFiscalModeHandler(FiscalWorkspace workspace, IClock clock) : ICommandHandler<SetFiscalModeCommand>
{
    public async Task HandleAsync(SetFiscalModeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        (await workspace.RequireProfileAsync(cancellationToken)).SetMode(command.Mode, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <param name="IfuMatches">L'IFU lié au jeton est celui du profil.</param>
public sealed record FiscalConnectionDto(FiscalMode TestedMode, bool IfuMatches, string? Nim, DateTimeOffset? TokenValidUntil);

/// <summary>Vérifie le jeton auprès de la DGI (environnement choisi, ou bac à sable tant que la facturation est coupée).</summary>
public sealed record TestFiscalConnectionCommand : ICommand<FiscalConnectionDto>;

public sealed class TestFiscalConnectionHandler(FiscalWorkspace workspace, IFiscalAuthority authority) : ICommandHandler<TestFiscalConnectionCommand, FiscalConnectionDto>
{
    public async Task<FiscalConnectionDto> HandleAsync(TestFiscalConnectionCommand command, CancellationToken cancellationToken = default)
    {
        var profile = await workspace.RequireProfileAsync(cancellationToken);
        var token = await workspace.TokenAsync(profile.TenantId, cancellationToken);
        var mode = profile.IsActive ? profile.Mode : FiscalMode.Sandbox;
        try
        {
            var status = await authority.StatusAsync(mode, token, cancellationToken);
            return new FiscalConnectionDto(mode, string.Equals(status.Ifu, profile.Ifu, StringComparison.Ordinal), status.Nim, status.TokenValidUntil);
        }
        catch (FiscalAuthorityException exception)
        {
            throw new DomainException(exception.Permanent ? FiscalApplicationErrors.AuthorityRejected : FiscalApplicationErrors.AuthorityUnreachable, exception);
        }
    }
}

/// <summary>Conversion des montants des autres modules vers ceux de l'e-MECeF (francs CFA entiers).</summary>
public static class FiscalAmounts
{
    public static long Whole(decimal amount) => (long)Math.Round(amount, 0, MidpointRounding.AwayFromZero);

    /// <summary>Type de paiement e-MECeF d'un moyen de paiement (noms stables du module Payments).</summary>
    public static string PaymentType(string method) => method switch
    {
        "Cash" or "CashOnDelivery" => "ESPECES",
        "Card" or "CardOnSite" or "ApplePay" or "GooglePay" => "CARTEBANCAIRE",
        "MobileMoney" or "MobileMoneyOnSite" => "MOBILEMONEY",
        "BankTransfer" => "VIREMENT",
        _ => "AUTRE",
    };

    /// <summary>
    /// Lignes de la facture : prix unitaire entier quand il tombe juste, sinon la ligne entière en quantité 1 ; les
    /// frais de livraison en ligne à part. Un écart d'arrondi avec le total est porté sur la plus grosse ligne.
    /// </summary>
    public static IReadOnlyList<FiscalLine> Lines(FiscalProfile profile, FiscalOrder order)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(order);
        var lines = new List<FiscalLine>();
        foreach (var line in order.Lines.Where(item => item.TotalWithTax > 0 && item.Quantity > 0))
        {
            var total = Whole(line.TotalWithTax);
            var name = Name(line.Name);
            var group = profile.TaxGroupFor(line.TaxRate);
            lines.Add(line.Quantity == decimal.Truncate(line.Quantity) && total % (long)line.Quantity == 0
                ? new FiscalLine(name, total / (long)line.Quantity, line.Quantity, group)
                : new FiscalLine(Name($"{line.Quantity.ToString("0.###", CultureInfo.InvariantCulture)} x {line.Name}"), total, 1m, group));
        }

        if (order.DeliveryFee is { } fee && Whole(fee) > 0)
        {
            lines.Add(new FiscalLine("Frais de livraison", Whole(fee), 1m, profile.TaxGroupFor(0m)));
        }

        var gap = Whole(order.Total) - lines.Sum(line => (long)Math.Round(line.UnitPrice * line.Quantity, MidpointRounding.AwayFromZero));
        if (gap != 0 && lines.Count > 0)
        {
            var biggest = lines.OrderByDescending(line => line.UnitPrice * line.Quantity).First();
            var index = lines.IndexOf(biggest);
            var amount = (long)Math.Round(biggest.UnitPrice * biggest.Quantity, MidpointRounding.AwayFromZero) + gap;
            if (amount > 0)
            {
                lines[index] = biggest.Quantity == 1m ? biggest with { UnitPrice = amount } : new FiscalLine(Name($"{biggest.Quantity.ToString("0.###", CultureInfo.InvariantCulture)} x {biggest.Name}"), amount, 1m, biggest.TaxGroup);
            }
        }

        return lines;
    }

    /// <summary>Paiements de la facture ; s'ils ne tombent pas juste sur le total, un seul paiement « AUTRE ».</summary>
    public static IReadOnlyList<FiscalPayment> Payments(FiscalOrder order, long total)
    {
        ArgumentNullException.ThrowIfNull(order);
        var payments = order.Payments
            .Where(payment => payment.Amount > 0)
            .GroupBy(payment => PaymentType(payment.Method))
            .Select(group => new FiscalPayment(group.Key, Whole(group.Sum(payment => payment.Amount))))
            .Where(payment => payment.Amount > 0)
            .ToList();
        return payments.Sum(payment => payment.Amount) == total ? payments : [new FiscalPayment("AUTRE", total)];
    }

    private static string Name(string name)
    {
        var clean = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length == 0 ? "Article" : clean.Length > FiscalLine.MaxNameLength ? clean[..FiscalLine.MaxNameLength] : clean;
    }
}

/// <summary>
/// À la fin d'une commande (hôte, par l'outbox) : sa facture de vente est mise en file si le restaurant facture en
/// e-MECeF. Idempotent par commande. Renvoie l'id de la facture, ou null si rien n'est à facturer.
/// </summary>
public sealed record IssueSaleInvoiceCommand(Guid OrderId) : ICommand<Guid?>;

public sealed class IssueSaleInvoiceHandler(FiscalWorkspace workspace, IFiscalOrders orders, IClock clock) : ICommandHandler<IssueSaleInvoiceCommand, Guid?>
{
    public async Task<Guid?> HandleAsync(IssueSaleInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        if (await workspace.Profiles.FindAsync(tenant, cancellationToken) is not { IsActive: true } profile)
        {
            return null;
        }

        if (await workspace.Invoices.FindBySourceAsync(tenant, FiscalInvoiceKind.Sale, command.OrderId, cancellationToken) is { } existing)
        {
            return existing.Id.Value;
        }

        var order = await orders.FindAsync(tenant, command.OrderId, cancellationToken);
        if (order is null || order.CurrencyCode != "XOF" || FiscalAmounts.Whole(order.Total) <= 0)
        {
            return null;
        }

        var lines = FiscalAmounts.Lines(profile, order);
        var total = lines.Sum(line => (long)Math.Round(line.UnitPrice * line.Quantity, MidpointRounding.AwayFromZero));
        var invoice = FiscalInvoice.CreateSale(profile, order.OrderId, order.LocationId, order.Number, lines, FiscalAmounts.Payments(order, total), clock);
        workspace.Invoices.Add(invoice);
        await workspace.SaveAsync(cancellationToken);
        return invoice.Id.Value;
    }
}

/// <summary>
/// Après un remboursement réussi (hôte, par l'outbox) : facture d'avoir liée à la facture de vente de la commande. Rien
/// si la commande n'a pas de facture de vente (remboursée avant d'être terminée). Idempotent par remboursement.
/// </summary>
public sealed record IssueCreditNoteCommand(Guid OrderId, Guid RefundId, decimal Amount, string Method) : ICommand<Guid?>;

public sealed class IssueCreditNoteHandler(FiscalWorkspace workspace, IClock clock) : ICommandHandler<IssueCreditNoteCommand, Guid?>
{
    public async Task<Guid?> HandleAsync(IssueCreditNoteCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        if (await workspace.Profiles.FindAsync(tenant, cancellationToken) is not { IsActive: true } profile
            || await workspace.Invoices.FindBySourceAsync(tenant, FiscalInvoiceKind.Sale, command.OrderId, cancellationToken) is not { } original)
        {
            return null;
        }

        if (await workspace.Invoices.FindBySourceAsync(tenant, FiscalInvoiceKind.CreditNote, command.RefundId, cancellationToken) is { } existing)
        {
            return existing.Id.Value;
        }

        var amount = FiscalAmounts.Whole(command.Amount);
        if (amount <= 0)
        {
            return null;
        }

        var group = original.Lines.OrderByDescending(line => line.UnitPrice * line.Quantity).First().TaxGroup;
        var invoice = FiscalInvoice.CreateCreditNote(
            profile, original, command.RefundId,
            [new FiscalLine($"Remboursement commande {original.OrderNumber}", amount, 1m, group)],
            [new FiscalPayment(FiscalAmounts.PaymentType(command.Method), amount)],
            clock);
        workspace.Invoices.Add(invoice);
        await workspace.SaveAsync(cancellationToken);
        return invoice.Id.Value;
    }
}

/// <summary>
/// Un passage de la file pour une facture : envoi à la DGI, puis confirmation. Une demande expirée chez la DGI est
/// renvoyée ; un avoir attend que sa facture d'origine soit certifiée. Les échecs passagers sont reprogrammés.
/// </summary>
public sealed record ProcessFiscalInvoiceCommand(Guid InvoiceId) : ICommand<FiscalInvoiceStatus>;

public sealed class ProcessFiscalInvoiceHandler(FiscalWorkspace workspace, IFiscalAuthority authority, IClock clock)
    : ICommandHandler<ProcessFiscalInvoiceCommand, FiscalInvoiceStatus>
{
    public const string OperatorName = "Caisse";

    public async Task<FiscalInvoiceStatus> HandleAsync(ProcessFiscalInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var invoice = await workspace.LoadInvoiceAsync(command.InvoiceId, cancellationToken);
        if (!invoice.IsDue(clock.UtcNow))
        {
            return invoice.Status;
        }

        var profile = await workspace.Profiles.FindAsync(invoice.TenantId, cancellationToken);
        if (profile is not { IsActive: true })
        {
            invoice.RecordFailure("profile_disabled", "La facturation e-MECeF est coupée pour ce restaurant.", permanent: true, clock);
            await workspace.SaveAsync(cancellationToken);
            return invoice.Status;
        }

        try
        {
            var token = await workspace.TokenAsync(invoice.TenantId, cancellationToken);
            if (invoice.SubmissionExpired(clock.UtcNow))
            {
                invoice.Resubmit(clock);
            }

            if (invoice.Status == FiscalInvoiceStatus.Pending)
            {
                string? reference = null;
                if (invoice.OriginalInvoiceId is { } originalId)
                {
                    var original = await workspace.Invoices.FindAsync(invoice.TenantId, originalId, cancellationToken);
                    if (original is not { Status: FiscalInvoiceStatus.Certified, Security: { } security })
                    {
                        invoice.RecordFailure("original_pending", "La facture d'origine n'est pas encore certifiée.", permanent: original is { Status: FiscalInvoiceStatus.Failed }, clock);
                        await workspace.SaveAsync(cancellationToken);
                        return invoice.Status;
                    }

                    reference = security.CodeMecef.Replace("-", string.Empty, StringComparison.Ordinal);
                }

                var request = new EmecfInvoiceRequest(
                    profile.Ifu,
                    invoice.Kind == FiscalInvoiceKind.Sale ? "FV" : "FA",
                    profile.Aib switch { AibRate.A => "A", AibRate.B => "B", _ => null },
                    invoice.Lines.Select(line => new EmecfItem(line.Name, line.UnitPrice, line.Quantity, line.TaxGroup)).ToList(),
                    invoice.Payments.Select(payment => new EmecfPayment(payment.Type, payment.Amount)).ToList(),
                    OperatorName,
                    reference);
                invoice.MarkSubmitted(await authority.SubmitAsync(invoice.Mode, token, request, cancellationToken), clock);
                // Sauvegarde intermédiaire : si la confirmation échoue, l'uid n'est pas perdu.
                await workspace.SaveAsync(cancellationToken);
            }

            invoice.Certify(await authority.ConfirmAsync(invoice.Mode, token, invoice.Uid!, cancellationToken), clock);
        }
        catch (FiscalAuthorityException exception)
        {
            invoice.RecordFailure(exception.Code, exception.Message, exception.Permanent, clock);
        }
        catch (DomainException exception) when (exception.Error == FiscalErrors.TokenMissing || exception.Error == FiscalApplicationErrors.TokenUnreadable)
        {
            invoice.RecordFailure(exception.Error.Code, exception.Error.Message, permanent: true, clock);
        }

        await workspace.SaveAsync(cancellationToken);
        return invoice.Status;
    }
}

/// <summary>Relance à la main une facture en échec ; elle repart dans la file.</summary>
public sealed record RetryFiscalInvoiceCommand(Guid InvoiceId) : ICommand;

public sealed class RetryFiscalInvoiceHandler(FiscalWorkspace workspace, IClock clock) : ICommandHandler<RetryFiscalInvoiceCommand>
{
    public async Task HandleAsync(RetryFiscalInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        (await workspace.LoadInvoiceAsync(command.InvoiceId, cancellationToken)).Retry(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record FiscalInvoiceDto(
    Guid Id,
    Guid OrderId,
    Guid LocationId,
    string OrderNumber,
    FiscalInvoiceKind Kind,
    FiscalMode Mode,
    FiscalInvoiceStatus Status,
    long Total,
    int Attempts,
    DateTimeOffset? NextAttemptAtUtc,
    string? CodeMecef,
    string? QrCode,
    string? Counters,
    string? Nim,
    string? DgiDateTime,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CertifiedAtUtc)
{
    public static FiscalInvoiceDto From(FiscalInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        return new FiscalInvoiceDto(
            invoice.Id.Value, invoice.OrderId, invoice.LocationId, invoice.OrderNumber, invoice.Kind, invoice.Mode, invoice.Status, invoice.Total, invoice.Attempts,
            invoice.NextAttemptAtUtc, invoice.Security?.CodeMecef, invoice.Security?.QrCode, invoice.Security?.Counters, invoice.Security?.Nim,
            invoice.Security?.DateTime, invoice.ErrorCode, invoice.ErrorMessage, invoice.CreatedAtUtc, invoice.CertifiedAtUtc);
    }
}

public sealed record ListFiscalInvoicesQuery(FiscalInvoiceStatus? Status, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, int Take = 100) : IQuery<IReadOnlyList<FiscalInvoiceDto>>;

public sealed class ListFiscalInvoicesHandler(FiscalWorkspace workspace) : IQueryHandler<ListFiscalInvoicesQuery, IReadOnlyList<FiscalInvoiceDto>>
{
    public async Task<IReadOnlyList<FiscalInvoiceDto>> HandleAsync(ListFiscalInvoicesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return (await workspace.Invoices.ListAsync(workspace.CurrentTenant, query.Status, query.FromUtc, query.ToUtc, Math.Clamp(query.Take, 1, 500), cancellationToken))
            .Select(FiscalInvoiceDto.From)
            .ToList();
    }
}

/// <summary>Mentions fiscales du reçu d'une commande : l'identité du restaurant et sa facture de vente, si elle existe.</summary>
public sealed record OrderFiscalDto(string Ifu, string? Rccm, FiscalInvoiceDto? Sale);

public sealed record GetOrderFiscalQuery(Guid OrderId) : IQuery<OrderFiscalDto?>;

public sealed class GetOrderFiscalHandler(FiscalWorkspace workspace) : IQueryHandler<GetOrderFiscalQuery, OrderFiscalDto?>
{
    public async Task<OrderFiscalDto?> HandleAsync(GetOrderFiscalQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenant = workspace.CurrentTenant;
        if (await workspace.Profiles.FindAsync(tenant, cancellationToken) is not { } profile)
        {
            return null;
        }

        var sale = query.OrderId == Guid.Empty ? null : await workspace.Invoices.FindBySourceAsync(tenant, FiscalInvoiceKind.Sale, query.OrderId, cancellationToken);
        return new OrderFiscalDto(profile.Ifu, profile.Rccm, sale is null ? null : FiscalInvoiceDto.From(sale));
    }
}
