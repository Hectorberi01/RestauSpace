using System.Globalization;
using System.Text;
using Reporting.Application.Metrics;

namespace Reporting.Application.Exports;

/// <summary>
/// Le plan de comptes d'un export comptable. Par défaut, le plan SYSCOHADA : 411 clients, 701 ventes, 443 TVA facturée,
/// 571 caisse, 585 virements de fonds (cartes, mobile money), 521 banque, 419 avances clients (cartes cadeaux). Les
/// moyens de paiement sont désignés par leur code de mesure (<c>cash</c>, <c>card_on_site</c>, <c>mobile_money</c>...).
/// </summary>
public sealed record AccountingPlan(
    string SalesJournal = "VT",
    string TreasuryJournal = "TR",
    string CustomersAccount = "411100",
    string SalesAccount = "701100",
    string VatAccount = "443100",
    string DefaultPaymentAccount = "585000",
    IReadOnlyDictionary<string, string>? PaymentAccounts = null)
{
    public const int MaxCodeLength = 20;

    public static AccountingPlan Default { get; } = new();

    public static IReadOnlyDictionary<string, string> DefaultPaymentAccounts { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["cash"] = "571100",
        ["card_on_site"] = "585100",
        ["mobile_money_on_site"] = "585200",
        ["meal_voucher"] = "585300",
        ["bank_transfer"] = "521100",
        ["gift_card"] = "419100",
        ["delivery_platform"] = "411200",
        ["cash_on_delivery"] = "571100",
        ["card"] = "585400",
        ["apple_pay"] = "585400",
        ["google_pay"] = "585400",
        ["pay_pal"] = "585500",
        ["mobile_money"] = "585600",
    };

    public string PaymentAccount(string method) =>
        PaymentAccounts?.GetValueOrDefault(method) ?? DefaultPaymentAccounts.GetValueOrDefault(method) ?? DefaultPaymentAccount;

    /// <summary>Codes de comptes et de journaux : 1 à 20 lettres ou chiffres.</summary>
    public void Validate()
    {
        var codes = new[] { SalesJournal, TreasuryJournal, CustomersAccount, SalesAccount, VatAccount, DefaultPaymentAccount }
            .Concat(PaymentAccounts?.Values ?? []);
        DomainException.ThrowIf(
            codes.Any(code => code is not { Length: > 0 and <= MaxCodeLength } || !code.All(char.IsAsciiLetterOrDigit))
            || (PaymentAccounts?.Count ?? 0) > 50
            || (PaymentAccounts?.Keys.Any(key => key is not { Length: > 0 and <= 40 } || !key.All(c => char.IsAsciiLetterLower(c) || c == '_')) ?? false),
            ReportingApplicationErrors.AccountingPlanInvalid);
    }
}

/// <summary>
/// Journal comptable d'une période, à importer dans un logiciel de comptabilité (CSV point-virgule, virgule décimale,
/// UTF-8 avec BOM ; colonnes du fichier des écritures comptables). Par jour et par devise :
/// <list type="bullet">
/// <item>ventes (VT) : débit clients = chiffre d'affaires TTC des commandes terminées ; crédit ventes = HT ; crédit TVA ;</item>
/// <item>trésorerie (TR) : par moyen de paiement, débit du compte du moyen et crédit clients pour les encaissements,
/// l'inverse pour les remboursements.</item>
/// </list>
/// Chaque écriture est équilibrée. Aucune donnée personnelle : seulement des totaux.
/// </summary>
public static class AccountingJournalWriter
{
    public const string ContentType = "text/csv";

    public static readonly IReadOnlyList<string> Metrics =
        [ReportingMetrics.Revenue, ReportingMetrics.RevenueTax, ReportingMetrics.CapturedAmount, ReportingMetrics.RefundedAmount];

    private static readonly string[] Header =
        ["JournalCode", "JournalLib", "EcritureNum", "EcritureDate", "CompteNum", "CompteLib", "PieceRef", "EcritureLib", "Debit", "Credit", "Idevise"];

    public sealed record Line(string Journal, string JournalLabel, string Number, DateOnly Date, string Account, string AccountLabel, string Label, decimal Debit, decimal Credit, string Currency);

    public static IReadOnlyList<Line> Lines(IEnumerable<ReportRow> rows, AccountingPlan plan)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(plan);
        var lines = new List<Line>();
        foreach (var day in rows.GroupBy(row => (row.PeriodStart, row.Currency)).OrderBy(group => group.Key.PeriodStart).ThenBy(group => group.Key.Currency, StringComparer.Ordinal))
        {
            var (date, currency) = day.Key;
            decimal Sum(string metric) => day.Where(row => row.Metric == metric).Sum(row => row.Value);
            var stamp = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

            var revenue = Sum(ReportingMetrics.Revenue);
            var tax = Math.Min(Sum(ReportingMetrics.RevenueTax), revenue);
            if (revenue != 0m)
            {
                var number = $"{plan.SalesJournal}-{stamp}-{currency}";
                var label = "Ventes du " + date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                lines.Add(new(plan.SalesJournal, "Ventes", number, date, plan.CustomersAccount, "Clients", label, revenue, 0m, currency));
                lines.Add(new(plan.SalesJournal, "Ventes", number, date, plan.SalesAccount, "Ventes", label, 0m, revenue - tax, currency));
                if (tax != 0m)
                {
                    lines.Add(new(plan.SalesJournal, "Ventes", number, date, plan.VatAccount, "TVA facturée", label, 0m, tax, currency));
                }
            }

            var methods = day.Where(row => row.Metric is ReportingMetrics.CapturedAmount or ReportingMetrics.RefundedAmount)
                .Select(row => row.Dimension)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal);
            foreach (var method in methods)
            {
                var captured = day.Where(row => row.Metric == ReportingMetrics.CapturedAmount && row.Dimension == method).Sum(row => row.Value);
                var refunded = day.Where(row => row.Metric == ReportingMetrics.RefundedAmount && row.Dimension == method).Sum(row => row.Value);
                var number = $"{plan.TreasuryJournal}-{stamp}-{currency}";
                var account = plan.PaymentAccount(method);
                var name = MethodLabel(method);
                if (captured != 0m)
                {
                    var label = $"Encaissements {name} du {date:dd/MM/yyyy}";
                    lines.Add(new(plan.TreasuryJournal, "Trésorerie", number, date, account, name, label, captured, 0m, currency));
                    lines.Add(new(plan.TreasuryJournal, "Trésorerie", number, date, plan.CustomersAccount, "Clients", label, 0m, captured, currency));
                }

                if (refunded != 0m)
                {
                    var label = $"Remboursements {name} du {date:dd/MM/yyyy}";
                    lines.Add(new(plan.TreasuryJournal, "Trésorerie", number, date, plan.CustomersAccount, "Clients", label, refunded, 0m, currency));
                    lines.Add(new(plan.TreasuryJournal, "Trésorerie", number, date, account, name, label, 0m, refunded, currency));
                }
            }
        }

        return lines;
    }

    public static byte[] Write(IEnumerable<ReportRow> rows, AccountingPlan plan)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(';', Header)).Append("\r\n");
        foreach (var line in Lines(rows, plan))
        {
            builder.Append(string.Join(';',
                Cell(line.Journal), Cell(line.JournalLabel), Cell(line.Number), line.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                Cell(line.Account), Cell(line.AccountLabel), Cell(line.Number), Cell(line.Label), Amount(line.Debit), Amount(line.Credit), Cell(line.Currency)))
                .Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(builder.ToString())];
    }

    public static string MethodLabel(string method) => method switch
    {
        "cash" => "Espèces",
        "card_on_site" => "Carte (terminal)",
        "mobile_money_on_site" => "Mobile money",
        "meal_voucher" => "Titres-restaurant",
        "bank_transfer" => "Virements",
        "gift_card" => "Cartes cadeaux et avoirs",
        "delivery_platform" => "Plateformes de livraison",
        "cash_on_delivery" => "Espèces encaissées à la livraison",
        "card" or "apple_pay" or "google_pay" => "Cartes en ligne",
        "pay_pal" => "PayPal",
        "mobile_money" => "Mobile money en ligne",
        "" => "Autres moyens",
        _ => method,
    };

    private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string Cell(string value)
    {
        var safe = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' ? "'" + value : value;
        return safe.IndexOfAny([';', '"', '\r', '\n']) >= 0 ? "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : safe;
    }
}
