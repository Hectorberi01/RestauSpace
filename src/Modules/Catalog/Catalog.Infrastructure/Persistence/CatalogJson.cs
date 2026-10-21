using System.Globalization;
using System.Text.Json;
using Catalog.Domain;

namespace Catalog.Infrastructure.Persistence;

/// <summary>Listes ordonnées stockées en une seule valeur jsonb (tableau de guids, de codes ou de nombres).</summary>
internal static class CatalogJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string WriteIds(IEnumerable<Guid> ids) => JsonSerializer.Serialize(ids.ToList(), Options);

    public static List<Guid> ReadIds(string json) => JsonSerializer.Deserialize<List<Guid>>(json, Options) ?? new List<Guid>();

    public static string WriteCodes(IEnumerable<string> codes) => JsonSerializer.Serialize(codes.ToList(), Options);

    public static List<string?> ReadCodes(string json) => JsonSerializer.Deserialize<List<string?>>(json, Options) ?? new List<string?>();

    public static string WriteChannels(IEnumerable<OnlineChannel> channels) => JsonSerializer.Serialize(channels.Select(channel => (int)channel).ToList(), Options);

    public static List<OnlineChannel> ReadChannels(string json) =>
        (JsonSerializer.Deserialize<List<int>>(json, Options) ?? new List<int>())
            .Select(value => Enum.IsDefined((OnlineChannel)value) ? (OnlineChannel)value : throw new InvalidOperationException($"Unknown online channel {value} in storage."))
            .ToList();

    public static string WritePriceOverrides(IEnumerable<PriceOverride> overrides) => JsonSerializer.Serialize(
        overrides.Select(item => new PriceOverrideRow(item.LocationId?.Value, item.Channel is { } channel ? (int)channel : null, item.BasePrice.Amount, item.BasePrice.Currency.Code)).ToList(),
        Options);

    // Relu par les fabriques du domaine : un id vide, un canal ou une devise inconnus échouent au chargement.
    public static List<PriceOverride> ReadPriceOverrides(string json) =>
        (JsonSerializer.Deserialize<List<PriceOverrideRow>>(json, Options) ?? new List<PriceOverrideRow>())
            .Select(row => new PriceOverride(
                row.LocationId is { } location ? LocationId.From(location) : null,
                row.Channel is { } channel ? (Enum.IsDefined((PriceChannel)channel) ? (PriceChannel)channel : throw new InvalidOperationException($"Unknown price channel {channel} in storage.")) : null,
                Money.Of(row.Amount, Currency.FromCode(row.Currency))))
            .ToList();

    public static string WriteSlots(IEnumerable<MenuSlot> slots) => JsonSerializer.Serialize(
        slots.Select(slot => new SlotRow((int)slot.Day, slot.Start.ToString("HH:mm", CultureInfo.InvariantCulture), slot.End.ToString("HH:mm", CultureInfo.InvariantCulture))).ToList(),
        Options);

    public static List<MenuSlot> ReadSlots(string json) =>
        (JsonSerializer.Deserialize<List<SlotRow>>(json, Options) ?? new List<SlotRow>())
            .Select(row => new MenuSlot(
                (DayOfWeek)row.Day,
                TimeOnly.ParseExact(row.Start, "HH:mm", CultureInfo.InvariantCulture),
                TimeOnly.ParseExact(row.End, "HH:mm", CultureInfo.InvariantCulture)))
            .ToList();

    public static string WriteFormulaSteps(IEnumerable<FormulaStep> steps) => JsonSerializer.Serialize(
        steps.Select(step => new FormulaStepRow(
                step.Id,
                step.Name.Value,
                step.MinChoices,
                step.MaxChoices,
                step.Choices.Select(choice => new FormulaChoiceRow(
                    choice.TenantId.Value, choice.ProductId.Value, choice.VariantId?.Value, choice.Supplement.Amount, choice.Supplement.Currency.Code)).ToList()))
            .ToList(),
        Options);

    // Relu par les fabriques du domaine : une étape ou un supplément invalides stockés échouent au chargement.
    public static List<FormulaStep> ReadFormulaSteps(string json) =>
        (JsonSerializer.Deserialize<List<FormulaStepRow>>(json, Options) ?? new List<FormulaStepRow>())
            .Select(row => FormulaStep.Create(
                row.Id,
                CatalogName.Create(row.Name),
                row.MinChoices,
                row.MaxChoices,
                row.Choices.Select(choice => FormulaChoice.Restore(
                        TenantId.From(choice.TenantId),
                        ProductId.From(choice.ProductId),
                        choice.VariantId is { } variant ? VariantId.From(variant) : null,
                        Money.Of(choice.Supplement, Currency.FromCode(choice.Currency))))
                    .ToList()))
            .ToList();

    internal sealed record FormulaStepRow(Guid Id, string Name, int MinChoices, int MaxChoices, List<FormulaChoiceRow> Choices);

    internal sealed record FormulaChoiceRow(Guid TenantId, Guid ProductId, Guid? VariantId, decimal Supplement, string Currency);

    internal sealed record SlotRow(int Day, string Start, string End);

    internal sealed record PriceOverrideRow(Guid? LocationId, int? Channel, decimal Amount, string Currency);
}
