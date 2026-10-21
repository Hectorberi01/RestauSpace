using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Reporting.Application.Commands;
using Reporting.Application.Exports;
using Reporting.Application.Metrics;
using Reporting.Application.Queries;
using M = Reporting.Application.Metrics.ReportingMetrics;

namespace Reporting.Application;

public sealed class ExportDocumentsTests
{
    private static readonly DateOnly Jan15 = new(2026, 1, 15);
    private static readonly DateOnly Jan16 = new(2026, 1, 16);

    private readonly ReportingFixture _fixture = new();

    private static ReportRow Row(DateOnly day, string metric, decimal value, string dimension = "", string currency = "XOF") =>
        new(day, day, null, metric, dimension, currency, value);

    private static ExportTable Table(int rows = 2) => ExportTable.FromReport(
        "Ventes « été »", Jan15, Jan16,
        Enumerable.Range(0, rows).Select(i => Row(Jan15, M.Revenue, 1234.5m + i)).ToList(), byLocation: false, new Dictionary<Guid, string>());

    // --- Excel

    [Fact]
    public void Xlsx_is_a_workbook_with_labels_and_numbers()
    {
        var bytes = XlsxWriter.Write(Table());

        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
        Assert.NotNull(zip.GetEntry("xl/workbook.xml"));
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var sheet = reader.ReadToEnd();
        Assert.Contains("Ventes « été »", sheet, StringComparison.Ordinal);
        Assert.Contains("Chiffre d&apos;affaires", sheet, StringComparison.Ordinal);
        Assert.Contains("<v>1234.5</v>", sheet, StringComparison.Ordinal);
        Assert.Contains("<v>1235.5</v>", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void Xlsx_text_is_never_a_formula_and_is_escaped()
    {
        var table = new ExportTable("T", "S", ["A"], [new object?[] { "=HYPERLINK(\"x\")<b>" }]);
        using var zip = new ZipArchive(new MemoryStream(XlsxWriter.Write(table)));
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var sheet = reader.ReadToEnd();

        Assert.DoesNotContain("<f>", sheet, StringComparison.Ordinal);
        Assert.Contains("t=\"inlineStr\"", sheet, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void Xlsx_column_names_go_past_z()
    {
        Assert.Equal(["A", "Z", "AA", "AZ", "BA"], new[] { 0, 25, 26, 51, 52 }.Select(XlsxWriter.ColumnName));
    }

    // --- PDF

    [Fact]
    public void Pdf_is_well_formed_with_a_correct_cross_reference_table()
    {
        var bytes = PdfWriter.Write(Table());
        var text = Encoding.Latin1.GetString(bytes);

        Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
        var startxref = int.Parse(Regex.Match(text, @"startxref\n(\d+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith("xref", text[startxref..], StringComparison.Ordinal);
        var offsets = Regex.Matches(text[startxref..], @"(\d{10}) 00000 n").Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        for (var i = 0; i < offsets.Count; i++)
        {
            Assert.StartsWith($"{i + 1} 0 obj", text[offsets[i]..], StringComparison.Ordinal);
        }

        Assert.Contains("1 234,5", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_paginates_long_tables()
    {
        var text = Encoding.Latin1.GetString(PdfWriter.Write(Table(rows: 120)));

        Assert.Matches(@"/Count [3-9]", text);
        Assert.Contains("Page 1 / ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_text_is_escaped_and_encoded_in_win_ansi() =>
        Assert.Equal(@"\(a\) \\ \351t\351 \222 \200 ?", PdfWriter.Escape("(a) \\ été ’ € ☃"));

    // --- Comptabilité

    [Fact]
    public void The_accounting_journal_is_balanced_per_entry_and_uses_the_plan()
    {
        var rows = new[]
        {
            Row(Jan15, M.Revenue, 11_800m), Row(Jan15, M.RevenueTax, 1_800m),
            Row(Jan15, M.CapturedAmount, 8_000m, "cash"), Row(Jan15, M.CapturedAmount, 3_800m, "mobile_money_on_site"),
            Row(Jan15, M.RefundedAmount, 500m, "cash"),
            Row(Jan16, M.Revenue, 2_000m),
        };
        var plan = new AccountingPlan(SalesAccount: "706000", PaymentAccounts: new Dictionary<string, string> { ["cash"] = "571000" });

        var lines = AccountingJournalWriter.Lines(rows, plan);

        foreach (var entry in lines.GroupBy(line => line.Number))
        {
            Assert.Equal(entry.Sum(line => line.Debit), entry.Sum(line => line.Credit));
        }

        Assert.Contains(lines, line => line is { Account: "706000", Credit: 10_000m });
        Assert.Contains(lines, line => line is { Account: "443100", Credit: 1_800m });
        Assert.Contains(lines, line => line is { Account: "571000", Debit: 8_000m });
        Assert.Contains(lines, line => line is { Account: "571000", Credit: 500m });
        Assert.Contains(lines, line => line is { Account: "585200", Debit: 3_800m });
        Assert.Contains(lines, line => line is { Journal: "VT", Account: "411100", Debit: 2_000m } && line.Date == Jan16);
    }

    [Fact]
    public void The_accounting_file_is_semicolon_separated_with_decimal_commas()
    {
        var csv = Encoding.UTF8.GetString(AccountingJournalWriter.Write([Row(Jan15, M.Revenue, 1_180.5m), Row(Jan15, M.RevenueTax, 180.5m)], AccountingPlan.Default))
            .TrimStart('﻿');
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("JournalCode;JournalLib;EcritureNum;EcritureDate;CompteNum", lines[0], StringComparison.Ordinal);
        Assert.Equal("VT;Ventes;VT-20260115-XOF;20260115;411100;Clients;VT-20260115-XOF;Ventes du 15/01/2026;1180,50;0,00;XOF", lines[1]);
        Assert.Equal(4, lines.Length);
    }

    [Fact]
    public void A_plan_with_an_invalid_code_is_refused()
    {
        var refused = Assert.Throws<DomainException>(() => new AccountingPlan(SalesAccount: "70 1;").Validate());
        Assert.Equal(ReportingApplicationErrors.AccountingPlanInvalid, refused.Error);
        AccountingPlan.Default.Validate();
    }

    // --- Cas d'usage

    [Fact]
    public async Task An_export_can_be_requested_as_excel_or_pdf()
    {
        await _fixture.FactsAsync("orders:1", _fixture.Clock.UtcNow, null, new ReportingFactInput(M.Revenue, 12.5m, null, "EUR"));
        var xlsx = await _fixture.RequestExport.HandleAsync(new RequestExportCommand(null, [M.Revenue], null, Jan15, Jan15, Format: ExportFormat.Xlsx));
        var pdf = await _fixture.RequestExport.HandleAsync(new RequestExportCommand(null, [M.Revenue], null, Jan15, Jan15, Format: ExportFormat.Pdf));

        await _fixture.Generate.HandleAsync(new GenerateExportCommand(xlsx));
        await _fixture.Generate.HandleAsync(new GenerateExportCommand(pdf));
        var excel = await _fixture.Download.HandleAsync(new GetExportFileQuery(xlsx));
        var document = await _fixture.Download.HandleAsync(new GetExportFileQuery(pdf));

        Assert.Equal(("rapport-20260115-20260115.xlsx", ExportFormats.XlsxContentType), (excel.FileName, excel.ContentType));
        Assert.Equal(("rapport-20260115-20260115.pdf", "application/pdf"), (document.FileName, document.ContentType));
        Assert.Equal((byte)'P', excel.Content[0]);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(document.Content, 0, 4), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_accounting_export_writes_the_journal_of_the_period()
    {
        await _fixture.FactsAsync("orders:1", _fixture.Clock.UtcNow, null,
            new ReportingFactInput(M.Revenue, 118m, null, "EUR"), new ReportingFactInput(M.RevenueTax, 18m, null, "EUR"));
        await _fixture.FactsAsync("payments:1", _fixture.Clock.UtcNow, null, new ReportingFactInput(M.CapturedAmount, 118m, "cash", "EUR"));

        var id = await _fixture.RequestAccounting.HandleAsync(new RequestAccountingExportCommand(Jan15, Jan15));
        await _fixture.Generate.HandleAsync(new GenerateExportCommand(id));
        var file = await _fixture.Download.HandleAsync(new GetExportFileQuery(id));
        var csv = Encoding.UTF8.GetString(file.Content);

        Assert.Equal(("journal-comptable-20260115-20260115.csv", "text/csv"), (file.FileName, file.ContentType));
        Assert.Contains(";701100;Ventes;", csv, StringComparison.Ordinal);
        Assert.Contains(";571100;Espèces;", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_accounting_export_refuses_an_invalid_plan()
    {
        var refused = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.RequestAccounting.HandleAsync(new RequestAccountingExportCommand(Jan15, Jan15, new AccountingPlan(VatAccount: ""))));
        Assert.Equal(ReportingApplicationErrors.AccountingPlanInvalid, refused.Error);
    }
}
