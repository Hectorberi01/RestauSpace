using System.Text;
using Printing.Domain;

namespace Printing.Application.Rendering;

/// <summary>Les formats qu'une imprimante peut recevoir.</summary>
public static class TicketFormats
{
    /// <summary>Commandes StarPRNT (Star mC-Print3, TSP143IV…).</summary>
    public const string StarPrnt = "application/vnd.star.starprnt";

    /// <summary>Commandes ESC/POS (Epson et compatibles), pour le relais.</summary>
    public const string EscPos = "application/vnd.escpos";

    /// <summary>Texte brut : repli universel, sans gras ni code-barres.</summary>
    public const string PlainText = "text/plain";

    public static IReadOnlyList<string> Star { get; } = [StarPrnt, PlainText];
}

/// <summary>
/// Mise en page commune : 48 colonnes sur papier 80 mm (police A), 24 en double largeur. Une ligne trop longue passe à
/// la ligne ; un prix calé à droite reste sur la dernière ligne de son texte.
/// </summary>
internal static class TicketLayout
{
    public const int Columns = 48;

    public static IEnumerable<string> Wrap(TicketLine line)
    {
        var width = line.Large ? Columns / 2 : Columns;
        if (line.Kind == TicketLineKind.Separator)
        {
            yield return new string('-', width);
            yield break;
        }

        if (line.Kind == TicketLineKind.Feed)
        {
            yield return string.Empty;
            yield break;
        }

        var text = line.Text ?? string.Empty;
        var right = line.Right ?? string.Empty;
        var indent = Math.Min(text.Length - text.TrimStart(' ').Length, width / 4);
        var chunks = Chunk(text, width - indent).Select(chunk => new string(' ', indent) + chunk).ToList();
        if (chunks.Count == 0)
        {
            chunks.Add(string.Empty);
        }

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            if (i == chunks.Count - 1 && right.Length > 0)
            {
                if (chunk.Length + 1 + right.Length > width)
                {
                    yield return chunk;
                    chunk = string.Empty;
                }

                yield return chunk + new string(' ', Math.Max(1, width - chunk.Length - right.Length)) + right;
            }
            else
            {
                yield return chunk;
            }
        }
    }

    private static IEnumerable<string> Chunk(string text, int width)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = new StringBuilder();
        foreach (var word in words)
        {
            var piece = word;
            while (piece.Length > width)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                yield return piece[..width];
                piece = piece[width..];
            }

            if (current.Length > 0 && current.Length + 1 + piece.Length > width)
            {
                yield return current.ToString();
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(piece);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }
}

/// <summary>
/// Codage Windows-1252 (accents français, €, œ) sans dépendance : Latin-1 tel quel, les quelques signes typographiques
/// à leur place 1252, espaces insécables en espaces, tout le reste en « ? ».
/// </summary>
internal static class Cp1252
{
    private static readonly Dictionary<char, byte> Specials = new()
    {
        ['€'] = 0x80, ['‚'] = 0x82, ['„'] = 0x84, ['…'] = 0x85, ['‘'] = 0x91, ['’'] = 0x92, ['“'] = 0x93, ['”'] = 0x94,
        ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97, ['Œ'] = 0x8C, ['œ'] = 0x9C, ['Š'] = 0x8A, ['š'] = 0x9A, ['Ÿ'] = 0x9F,
        [' '] = 0x20, [' '] = 0x20, [' '] = 0x20,
    };

    public static void Write(List<byte> output, string text)
    {
        foreach (var c in text)
        {
            output.Add(Specials.TryGetValue(c, out var mapped) ? mapped : c is >= ' ' and <= '~' or >= '¡' and <= 'ÿ' ? (byte)c : (byte)'?');
        }
    }
}

public static class TicketRenderer
{
    private static readonly byte[] NewLine = [0x0A];

    /// <summary>Le ticket dans le format demandé ; un format inconnu donne le texte brut.</summary>
    public static byte[] Render(TicketDocument document, string format)
    {
        ArgumentNullException.ThrowIfNull(document);
        return format switch
        {
            TicketFormats.StarPrnt => StarPrnt(document),
            TicketFormats.EscPos => EscPos(document),
            _ => Encoding.UTF8.GetBytes(PlainText(document)),
        };
    }

    public static string PlainText(TicketDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var text = new StringBuilder();
        foreach (var line in document.Lines)
        {
            if (line.Kind is TicketLineKind.Barcode or TicketLineKind.QrCode)
            {
                text.Append(line.Kind == TicketLineKind.QrCode ? "[QR] " + line.Text : Center(line.Text, TicketLayout.Columns)).Append('\n');
                continue;
            }

            // Le texte brut ne double pas la largeur : une ligne en grand est alignée sur toute la largeur.
            foreach (var row in TicketLayout.Wrap(line))
            {
                text.Append(line.Align switch
                {
                    TicketAlign.Center => Center(row, TicketLayout.Columns),
                    TicketAlign.Right => row.PadLeft(TicketLayout.Columns),
                    _ => row,
                }).Append('\n');
            }
        }

        return text.Append("\n\n\n").ToString();
    }

    /// <summary>
    /// StarPRNT : ESC @ (initialisation), ESC GS t 32 (page de codes 1252), ESC GS a n (alignement), ESC E / ESC F
    /// (gras), ESC i n n (double taille), ESC b (code-barres Code 128 avec texte dessous), ESC d 3 (avance et coupe partielle).
    /// </summary>
    private static byte[] StarPrnt(TicketDocument document)
    {
        var output = new List<byte> { 0x1B, 0x40, 0x1B, 0x1D, 0x74, 0x20 };
        foreach (var line in document.Lines)
        {
            output.AddRange([0x1B, 0x1D, 0x61, Align(line.Align)]);
            if (line.Kind == TicketLineKind.Barcode)
            {
                output.AddRange([0x1B, 0x62, 0x36, 0x32, 0x32, 0x50]);
                Cp1252.Write(output, line.Text);
                output.Add(0x1E);
                output.AddRange(NewLine);
                continue;
            }

            if (line.Kind == TicketLineKind.QrCode)
            {
                // ESC GS y S : modèle 2, correction M, cellule 5 ; ESC GS y D : données ; ESC GS y P : impression.
                var data = Utf8Bytes(line.Text);
                output.AddRange([0x1B, 0x1D, 0x79, 0x53, 0x30, 0x02, 0x1B, 0x1D, 0x79, 0x53, 0x31, 0x01, 0x1B, 0x1D, 0x79, 0x53, 0x32, 0x05]);
                output.AddRange([0x1B, 0x1D, 0x79, 0x44, 0x31, 0x00, (byte)(data.Length & 0xFF), (byte)(data.Length >> 8)]);
                output.AddRange(data);
                output.AddRange([0x1B, 0x1D, 0x79, 0x50]);
                output.AddRange(NewLine);
                continue;
            }

            output.AddRange(line.Bold ? [0x1B, 0x45] : [0x1B, 0x46]);
            output.AddRange(line.Large ? [0x1B, 0x69, 0x01, 0x01] : [0x1B, 0x69, 0x00, 0x00]);
            foreach (var row in TicketLayout.Wrap(line))
            {
                Cp1252.Write(output, row);
                output.AddRange(NewLine);
            }
        }

        output.AddRange([0x1B, 0x46, 0x1B, 0x69, 0x00, 0x00, 0x1B, 0x1D, 0x61, 0x00, 0x1B, 0x64, 0x03]);
        return [.. output];
    }

    /// <summary>
    /// ESC/POS : ESC @, ESC t 16 (WPC1252), ESC a n (alignement), ESC E n (gras), GS ! (taille), GS k 73 (Code 128 jeu B,
    /// texte dessous par GS H 2), GS V 66 (avance et coupe partielle).
    /// </summary>
    private static byte[] EscPos(TicketDocument document)
    {
        var output = new List<byte> { 0x1B, 0x40, 0x1B, 0x74, 0x10 };
        foreach (var line in document.Lines)
        {
            output.AddRange([0x1B, 0x61, (byte)(Align(line.Align) - 0x30)]);
            if (line.Kind == TicketLineKind.Barcode)
            {
                var data = Encoding.ASCII.GetBytes("{B" + line.Text);
                output.AddRange([0x1D, 0x68, 0x50, 0x1D, 0x77, 0x02, 0x1D, 0x48, 0x02, 0x1D, 0x6B, 0x49, (byte)data.Length]);
                output.AddRange(data);
                output.AddRange(NewLine);
                continue;
            }

            if (line.Kind == TicketLineKind.QrCode)
            {
                // GS ( k : modèle 2, cellule 5, correction M, stockage des données, impression.
                var data = Utf8Bytes(line.Text);
                var size = data.Length + 3;
                output.AddRange([0x1D, 0x28, 0x6B, 0x04, 0x00, 0x31, 0x41, 0x32, 0x00]);
                output.AddRange([0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x43, 0x05]);
                output.AddRange([0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x45, 0x31]);
                output.AddRange([0x1D, 0x28, 0x6B, (byte)(size & 0xFF), (byte)(size >> 8), 0x31, 0x50, 0x30]);
                output.AddRange(data);
                output.AddRange([0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x51, 0x30]);
                output.AddRange(NewLine);
                continue;
            }

            output.AddRange([0x1B, 0x45, (byte)(line.Bold ? 1 : 0), 0x1D, 0x21, (byte)(line.Large ? 0x11 : 0x00)]);
            foreach (var row in TicketLayout.Wrap(line))
            {
                Cp1252.Write(output, row);
                output.AddRange(NewLine);
            }
        }

        output.AddRange([0x1B, 0x45, 0x00, 0x1D, 0x21, 0x00, 0x1B, 0x61, 0x00, 0x1D, 0x56, 0x42, 0x03]);
        return [.. output];
    }

    // StarPRNT attend '0', '1', '2' (0x30…) ; ESC/POS attend 0, 1, 2.
    private static byte Align(TicketAlign align) => align switch
    {
        TicketAlign.Center => 0x31,
        TicketAlign.Right => 0x32,
        _ => 0x30,
    };

    private static byte[] Utf8Bytes(string text) => Encoding.UTF8.GetBytes(new string(text.Where(c => !char.IsControl(c)).ToArray()));

    private static string Center(string text, int width) =>
        text.Length >= width ? text : new string(' ', (width - text.Length) / 2) + text;
}
