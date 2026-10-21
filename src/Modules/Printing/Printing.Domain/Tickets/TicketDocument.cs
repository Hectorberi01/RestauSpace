namespace Printing.Domain;

public enum TicketLineKind
{
    Text = 1,

    /// <summary>Trait sur toute la largeur.</summary>
    Separator = 2,

    /// <summary>Code-barres Code 128 du texte (numéro de commande).</summary>
    Barcode = 3,

    /// <summary>Ligne vide.</summary>
    Feed = 4,

    /// <summary>QR code du texte (facture normalisée : celui rendu par la DGI).</summary>
    QrCode = 5,
}

public enum TicketAlign
{
    Left = 1,
    Center = 2,
    Right = 3,
}

/// <param name="Right">Texte calé à droite sur la même ligne (prix) ; seulement pour une ligne de texte alignée à gauche.</param>
/// <param name="Large">Double largeur et double hauteur (numéro de commande, total).</param>
public sealed record TicketLine(TicketLineKind Kind, string Text = "", TicketAlign Align = TicketAlign.Left, bool Bold = false, bool Large = false, string? Right = null);

/// <summary>
/// Un ticket décrit indépendamment de l'imprimante : des lignes de texte, des traits, un code-barres. Chaque protocole
/// (StarPRNT, ESC/POS, texte) le traduit à l'impression. Rien d'autre qu'un texte court par ligne : aucune image, aucune
/// commande d'imprimante fournie par l'appelant.
/// </summary>
public sealed class TicketDocument
{
    public const int MaxLines = 400;
    public const int MaxTextLength = 120;
    public const int MaxQrLength = 400;

    private TicketDocument(IReadOnlyList<TicketLine> lines) => Lines = lines;

    public IReadOnlyList<TicketLine> Lines { get; }

    public static TicketDocument Create(IEnumerable<TicketLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var list = lines.ToList();
        DomainException.ThrowIf(list.Count is 0 or > MaxLines, PrintJobErrors.DocumentInvalid);
        foreach (var line in list)
        {
            DomainException.ThrowIf(
                line is null || !Enum.IsDefined(line.Kind) || !Enum.IsDefined(line.Align)
                || (line.Text?.Length ?? 0) > (line.Kind == TicketLineKind.QrCode ? MaxQrLength : MaxTextLength) || (line.Right?.Length ?? 0) > MaxTextLength
                || (line.Kind == TicketLineKind.QrCode && string.IsNullOrEmpty(line.Text))
                || (line.Text ?? string.Empty).Any(char.IsControl) || (line.Right ?? string.Empty).Any(char.IsControl)
                || (line.Kind == TicketLineKind.Barcode && !IsBarcodeText(line.Text)),
                PrintJobErrors.DocumentInvalid);
        }

        return new TicketDocument(list);
    }

    /// <summary>Un code-barres ne porte que des chiffres, des lettres majuscules et des tirets (numéro de commande).</summary>
    public static bool IsBarcodeText(string? text) =>
        text is { Length: > 0 and <= 30 } && text.All(c => c is (>= '0' and <= '9') or (>= 'A' and <= 'Z') or '-');
}
