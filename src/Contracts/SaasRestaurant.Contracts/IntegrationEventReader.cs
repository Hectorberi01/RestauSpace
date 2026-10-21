using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaasRestaurant.Contracts;

public sealed class IntegrationContractException : Exception
{
    public IntegrationContractException()
    {
    }

    public IntegrationContractException(string message)
        : base(message)
    {
    }

    public IntegrationContractException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public static class IntegrationEventReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        Converters = { new MoneyAmountConverter() },
    };

    public static IntegrationEventAttribute ContractOf(Type contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return contract.GetCustomAttribute<IntegrationEventAttribute>()
            ?? throw new IntegrationContractException($"{contract.Name} is not an integration event contract.");
    }

    public static T Read<T>(string module, string type, int version, string json)
        where T : class
    {
        var contract = ContractOf(typeof(T));
        if (!contract.Matches(module, type))
        {
            throw new IntegrationContractException($"{module}.{type} is not read with {typeof(T).Name}.");
        }

        if (version > contract.Version)
        {
            throw new IntegrationContractException($"{module}.{type} version {version} is newer than {typeof(T).Name} (version {contract.Version}).");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options)
                ?? throw new IntegrationContractException($"{module}.{type} has no content.");
        }
        catch (JsonException exception)
        {
            throw new IntegrationContractException($"{module}.{type} does not match {typeof(T).Name}: {exception.Path}.");
        }
    }
}

public sealed record MoneyAmount(decimal Amount, string Currency);

public sealed class MoneyAmountConverter : JsonConverter<MoneyAmount>
{
    public override MoneyAmount Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("Amount", out var amount)
            || !root.TryGetProperty("Currency", out var currency))
        {
            throw new JsonException("A money value needs an amount and a currency.");
        }

        var code = currency.ValueKind switch
        {
            JsonValueKind.String => currency.GetString(),
            JsonValueKind.Object when currency.TryGetProperty("Code", out var nested) => nested.GetString(),
            _ => null,
        };
        return new MoneyAmount(amount.GetDecimal(), code ?? throw new JsonException("A money value needs a currency code."));
    }

    public override void Write(Utf8JsonWriter writer, MoneyAmount value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        writer.WriteNumber("Amount", value.Amount);
        writer.WriteString("Currency", value.Currency);
        writer.WriteEndObject();
    }
}

public sealed class FlexibleNumberConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetDecimal();
        }

        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("Value", out var value)
            ? value.GetDecimal()
            : throw new JsonException("A number or an object with a value was expected.");
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumberValue(value);
    }
}
