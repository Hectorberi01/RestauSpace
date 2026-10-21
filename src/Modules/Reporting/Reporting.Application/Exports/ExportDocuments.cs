using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using Reporting.Application.Metrics;

namespace Reporting.Application.Exports;

/// <summary>Format du fichier d'un rapport exporté.</summary>
public enum ExportFormat
{
    Csv = 1,
    Xlsx = 2,
    Pdf = 3,
}

/// <summary>Les codes de type d'export (colonne <c>type</c> du job) et ce qui en découle.</summary>
public static class ExportFormats
{
    public const string ReportXlsx = "report.xlsx";
    public const string ReportPdf = "report.pdf";
    public const string AccountingCsv = "accounting.csv";

    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string PdfContentType = "application/pdf";

    public static string TypeCode(ExportFormat format) => format switch
    {
        ExportFormat.Xlsx => ReportXlsx,
        ExportFormat.Pdf => ReportPdf,
        _ => CsvReportWriter.ExportTypeCode,
    };

    public static string Extension(string typeCode) => typeCode switch
    {
        ReportXlsx => "xlsx",
        ReportPdf => "pdf",
        _ => "csv",
    };

    public static string ContentType(string typeCode) => typeCode switch
    {
        ReportXlsx => XlsxContentType,
        ReportPdf => PdfContentType,
        _ => CsvReportWriter.ContentType,
    };

    /// <summary>Le nom du fichier CSV avec l'extension du format.</summary>
    public static string FileName(string title, DateOnly from, DateOnly to, string typeCode, string? prefix = null)
    {
        var name = CsvReportWriter.FileName(title, from, to);
        return (prefix ?? string.Empty) + name[..^3] + Extension(typeCode);
    }
}

/// <summary>Libellés français des mesures, pour les fichiers lus par une personne (tableur, PDF).</summary>
public static class MetricLabels
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["sales.orders_placed"] = "Commandes passées",
        ["sales.placed_amount"] = "Montant commandé",
        ["sales.discount_amount"] = "Remises accordées",
        ["sales.tax_amount"] = "Taxes",
        ["sales.orders_completed"] = "Commandes terminées",
        ["sales.revenue"] = "Chiffre d'affaires",
        ["sales.revenue_tax"] = "TVA des ventes",
        ["sales.orders_cancelled"] = "Commandes annulées",
        ["sales.cancelled_amount"] = "Montant annulé",
        ["sales.average_ticket"] = "Panier moyen",
        ["sales.cancellation_rate"] = "Taux d'annulation (%)",
        ["sales.online_orders_completed"] = "Commandes en ligne",
        ["sales.online_revenue"] = "Chiffre d'affaires en ligne",
        ["sales.hourly_orders"] = "Commandes par heure",
        ["sales.hourly_revenue"] = "Chiffre d'affaires par heure",
        ["sales.items_sold"] = "Articles vendus",
        ["sales.product_revenue"] = "Ventes par produit",
        ["payments.captured_count"] = "Paiements encaissés",
        ["payments.captured_amount"] = "Montant encaissé",
        ["payments.failed_count"] = "Paiements échoués",
        ["payments.refund_count"] = "Remboursements",
        ["payments.refunded_amount"] = "Montant remboursé",
        ["payments.refund_rate"] = "Taux de remboursement (%)",
        ["reservations.created"] = "Réservations créées",
        ["reservations.covers_booked"] = "Couverts réservés",
        ["reservations.seated"] = "Réservations installées",
        ["reservations.covers_seated"] = "Couverts installés",
        ["reservations.cancelled"] = "Réservations annulées",
        ["reservations.no_shows"] = "Absences",
        ["reservations.no_show_rate"] = "Taux d'absence (%)",
        ["kitchen.tickets_created"] = "Tickets cuisine",
        ["kitchen.tickets_ready"] = "Tickets prêts",
        ["kitchen.tickets_cancelled"] = "Tickets annulés",
        ["kitchen.preparation_seconds"] = "Préparation cumulée (s)",
        ["kitchen.average_preparation_minutes"] = "Préparation moyenne (min)",
        ["inventory.receipts"] = "Réceptions de stock",
        ["inventory.waste_records"] = "Pertes déclarées",
        ["inventory.adjustments"] = "Ajustements de stock",
        ["inventory.low_stock_alerts"] = "Alertes stock bas",
        ["inventory.negative_stock_alerts"] = "Alertes stock négatif",
        ["promotions.applied"] = "Promotions appliquées",
        ["promotions.discount_amount"] = "Remises des promotions",
        ["promotions.reverted"] = "Promotions retirées",
        ["promotions.coupons_redeemed"] = "Codes promo utilisés",
        ["loyalty.accounts_opened"] = "Comptes fidélité ouverts",
        ["loyalty.points_earned"] = "Points gagnés",
        ["loyalty.points_redeemed"] = "Points utilisés",
        ["loyalty.points_expired"] = "Points expirés",
        ["loyalty.rewards_redeemed"] = "Récompenses utilisées",
        ["loyalty.reward_discount_amount"] = "Remises des récompenses",
    };

    public static string Of(string code) => Labels.TryGetValue(code, out var label) ? label : code;
}

/// <summary>
/// Un tableau prêt à écrire : titre, sous-titre, en-têtes et lignes. Une cellule est un texte ou un nombre ; les
/// colonnes de <see cref="NumericColumns"/> sont alignées à droite.
/// </summary>
public sealed record ExportTable(string Title, string Subtitle, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows)
{
    public IReadOnlySet<int> NumericColumns { get; init; } = new HashSet<int>();

    /// <summary>Le tableau d'un rapport : une ligne par période, établissement, mesure et détail.</summary>
    public static ExportTable FromReport(
        string title, DateOnly from, DateOnly to, IEnumerable<ReportRow> rows, bool byLocation, IReadOnlyDictionary<Guid, string> locationNames)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(locationNames);
        var columns = new List<string> { "Début", "Fin" };
        if (byLocation)
        {
            columns.Add("Établissement");
        }

        columns.AddRange(["Mesure", "Détail", "Devise", "Valeur"]);
        var lines = rows
            .Select(row =>
            {
                var cells = new List<object?> { row.PeriodStart.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), row.PeriodEnd.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) };
                if (byLocation)
                {
                    cells.Add(row.LocationId is { } id ? locationNames.GetValueOrDefault(id, id.ToString("D")[..8]) : "Tous");
                }

                var detail = row.Dimension.Length > 0 && row.Metric.StartsWith("payments.", StringComparison.Ordinal)
                    ? AccountingJournalWriter.MethodLabel(row.Dimension)
                    : row.Dimension;
                cells.AddRange([MetricLabels.Of(row.Metric), detail, row.Currency, row.Value]);
                return (IReadOnlyList<object?>)cells;
            })
            .ToList();
        var subtitle = string.Create(CultureInfo.InvariantCulture, $"Du {from:dd/MM/yyyy} au {to:dd/MM/yyyy}");
        return new ExportTable(title, subtitle, columns, lines) { NumericColumns = new HashSet<int> { columns.Count - 1 } };
    }
}

/// <summary>
/// Classeur Excel (.xlsx, Office Open XML) écrit à la main : une feuille, en-tête en gras, nombres en nombres. Les
/// textes sont des chaînes en ligne (jamais des formules) : aucune injection de formule possible.
/// </summary>
public static class XlsxWriter
{
    public static byte[] Write(ExportTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Entry(zip, "[Content_Types].xml",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>""");
            Entry(zip, "_rels/.rels",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Entry(zip, "xl/_rels/workbook.xml.rels",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
            Entry(zip, "xl/workbook.xml",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Rapport" sheetId="1" r:id="rId1"/></sheets></workbook>""");
            // Styles : 0 normal, 1 gras (en-tête et titre), 2 nombre « # ##0,00 ».
            Entry(zip, "xl/styles.xml",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="1"><numFmt numFmtId="164" formatCode="#,##0.##"/></numFmts><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="3"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs></styleSheet>""");
            Entry(zip, "xl/worksheets/sheet1.xml", Sheet(table));
        }

        return stream.ToArray();
    }

    private static string Sheet(ExportTable table)
    {
        var builder = new StringBuilder();
        builder.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        builder.Append("<cols>");
        for (var i = 0; i < table.Columns.Count; i++)
        {
            var width = Math.Clamp(table.Rows.Select(row => Text(row[i]).Length).Append(table.Columns[i].Length).Max() + 2, 8, 50);
            builder.Append(CultureInfo.InvariantCulture, $"""<col min="{i + 1}" max="{i + 1}" width="{width}" customWidth="1"/>""");
        }

        builder.Append("</cols><sheetData>");
        var row = 1;
        AppendRow(builder, row++, [table.Title], bold: true);
        AppendRow(builder, row++, [table.Subtitle], bold: false);
        row++;
        AppendRow(builder, row++, table.Columns.Cast<object?>().ToList(), bold: true);
        foreach (var line in table.Rows)
        {
            AppendRow(builder, row++, line, bold: false);
        }

        builder.Append("</sheetData></worksheet>");
        return builder.ToString();
    }

    private static void AppendRow(StringBuilder builder, int index, IReadOnlyList<object?> cells, bool bold)
    {
        builder.Append(CultureInfo.InvariantCulture, $"<row r=\"{index}\">");
        for (var column = 0; column < cells.Count; column++)
        {
            var reference = ColumnName(column) + index.ToString(CultureInfo.InvariantCulture);
            switch (cells[column])
            {
                case null:
                    break;
                case decimal number:
                    builder.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" s=\"2\"><v>{number.ToString(CultureInfo.InvariantCulture)}</v></c>");
                    break;
                case var value:
                    builder.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" t=\"inlineStr\"{(bold ? " s=\"1\"" : string.Empty)}><is><t xml:space=\"preserve\">{SecurityElement.Escape(Clean(Text(value)))}</t></is></c>");
                    break;
            }
        }

        builder.Append("</row>");
    }

    public static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            name = (char)('A' + ((n - 1) % 26)) + name;
        }

        return name;
    }

    private static string Text(object? value) => value switch
    {
        null => string.Empty,
        decimal number => number.ToString("0.##", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    // Les caractères de contrôle sont interdits en XML.
    private static string Clean(string value) => new(value.Where(character => character >= ' ' || character is '\t' or '\n').ToArray());

    private static void Entry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}

/// <summary>
/// PDF écrit à la main (PDF 1.4, polices standard Helvetica, encodage WinAnsi) : A4 paysage, titre, sous-titre, tableau
/// paginé avec en-tête répété et numéro de page. Les caractères hors WinAnsi sont remplacés par « ? ».
/// </summary>
public static class PdfWriter
{
    private const float PageWidth = 842f;
    private const float PageHeight = 595f;
    private const float Margin = 36f;
    private const float FontSize = 9f;
    private const float LineHeight = 15f;

    public static byte[] Write(ExportTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var widths = ColumnWidths(table);
        var pages = new List<string>();
        var page = new StringBuilder();
        var y = PageHeight - Margin;

        void NewPage()
        {
            if (page.Length > 0)
            {
                pages.Add(page.ToString());
            }

            page = new StringBuilder();
            y = PageHeight - Margin;
        }

        void Header()
        {
            page.Append(CultureInfo.InvariantCulture, $"0.93 g {Margin} {y - 4:0.##} {PageWidth - (2 * Margin):0.##} {LineHeight:0.##} re f 0 g\n");
            Row(page, table.Columns.Cast<object?>().ToList(), widths, table.NumericColumns, y, bold: true);
            y -= LineHeight;
        }

        Text(page, table.Title, Margin, y - 14, 16f, bold: true);
        y -= 24;
        Text(page, table.Subtitle, Margin, y - 10, 10f, bold: false);
        y -= 28;
        Header();
        foreach (var line in table.Rows)
        {
            if (y < Margin + LineHeight)
            {
                NewPage();
                Header();
            }

            Row(page, line, widths, table.NumericColumns, y, bold: false);
            y -= LineHeight;
        }

        if (table.Rows.Count == 0)
        {
            Text(page, "Aucune donnée sur la période.", Margin, y - 10, FontSize, bold: false);
        }

        pages.Add(page.ToString());
        return Assemble(pages);
    }

    private static float[] ColumnWidths(ExportTable table)
    {
        var available = PageWidth - (2 * Margin);
        var natural = table.Columns
            .Select((column, index) => Math.Max(Measure(column, bold: true), table.Rows.Select(row => Measure(Cell(row[index]), bold: false)).DefaultIfEmpty(0f).Max()) + 10f)
            .ToArray();
        var total = natural.Sum();
        return total <= available ? natural.Select(width => width + ((available - total) / natural.Length)).ToArray() : natural.Select(width => width * available / total).ToArray();
    }

    private static void Row(StringBuilder page, IReadOnlyList<object?> cells, float[] widths, IReadOnlySet<int> numeric, float y, bool bold)
    {
        var x = Margin;
        for (var i = 0; i < cells.Count && i < widths.Length; i++)
        {
            var text = Fit(Cell(cells[i]), widths[i] - 8f, bold);
            var left = numeric.Contains(i) && !bold ? x + widths[i] - 4f - Measure(text, bold) : x + 4f;
            Text(page, text, left, y, FontSize, bold);
            x += widths[i];
        }
    }

    private static string Fit(string text, float width, bool bold)
    {
        if (Measure(text, bold) <= width)
        {
            return text;
        }

        while (text.Length > 1 && Measure(text + "...", bold) > width)
        {
            text = text[..^1];
        }

        return text + "...";
    }

    private static string Cell(object? value) => value switch
    {
        null => string.Empty,
        decimal number => number.ToString("#,0.##", French),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static readonly NumberFormatInfo French = new() { NumberGroupSeparator = " ", NumberDecimalSeparator = ",", NegativeSign = "-" };

    // Largeur approchée en Helvetica : chiffres et majuscules plus larges, i/l/espaces plus étroits.
    private static float Measure(string text, bool bold) =>
        text.Sum(character => character switch
        {
            ' ' or 'i' or 'l' or 'j' or 't' or 'f' or 'I' or '.' or ',' or '\'' or ':' => 0.28f,
            >= '0' and <= '9' => 0.556f,
            'm' or 'w' or 'M' or 'W' => 0.85f,
            >= 'A' and <= 'Z' => 0.68f,
            _ => 0.52f,
        }) * FontSize * (bold ? 1.06f : 1f);

    private static void Text(StringBuilder page, string text, float x, float y, float size, bool bold) =>
        page.Append(CultureInfo.InvariantCulture, $"BT /{(bold ? "F2" : "F1")} {size:0.##} Tf {x:0.##} {y:0.##} Td ({Escape(text)}) Tj ET\n");

    /// <summary>Chaîne PDF en WinAnsi : ( ) \ échappés, octets hors ASCII en octal.</summary>
    public static string Escape(string text)
    {
        var builder = new StringBuilder();
        foreach (var character in text)
        {
            var code = WinAnsi(character);
            if (code is (byte)'(' or (byte)')' or (byte)'\\')
            {
                builder.Append('\\').Append((char)code);
            }
            else if (code < 32 || code > 126)
            {
                builder.Append('\\').Append(Convert.ToString(code, 8).PadLeft(3, '0'));
            }
            else
            {
                builder.Append((char)code);
            }
        }

        return builder.ToString();
    }

    private static byte WinAnsi(char character) => character switch
    {
        < (char)32 => (byte)' ',
        <= (char)126 => (byte)character,
        ' ' or ' ' or ' ' => (byte)' ',
        >= '¡' and <= 'ÿ' => (byte)character,
        '€' => 0x80,
        '‚' => 0x82,
        '…' => 0x85,
        '‘' => 0x91,
        '’' => 0x92,
        '“' => 0x93,
        '”' => 0x94,
        '–' => 0x96,
        '—' => 0x97,
        'Œ' => 0x8C,
        'œ' => 0x9C,
        _ => (byte)'?',
    };

    private static byte[] Assemble(IReadOnlyList<string> pages)
    {
        // Objets : 1 catalogue, 2 pages, 3 police, 4 police grasse, puis (page, contenu) par page.
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [" + string.Join(' ', pages.Select((_, i) => $"{5 + (2 * i)} 0 R")) + $"] /Count {pages.Count} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
        };
        for (var i = 0; i < pages.Count; i++)
        {
            var footer = new StringBuilder(pages[i]);
            Text(footer, $"Page {i + 1} / {pages.Count}", PageWidth - Margin - 50f, Margin / 2f, 8f, bold: false);
            var content = footer.ToString();
            objects.Add(string.Create(CultureInfo.InvariantCulture,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {6 + (2 * i)} 0 R >>"));
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream");
        }

        using var stream = new MemoryStream();
        void Write(string value) => stream.Write(Encoding.ASCII.GetBytes(value));
        Write("%PDF-1.4\n");
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(stream.Position);
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = stream.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return stream.ToArray();
    }
}
