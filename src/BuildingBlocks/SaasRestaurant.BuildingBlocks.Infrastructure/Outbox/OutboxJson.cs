using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaasRestaurant.BuildingBlocks.Infrastructure;

internal static class OutboxJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter(), new UriConverter(), new ValueWrapperConverterFactory() },
    };

    /// <summary>
    /// La forme échappée, celle que stockent les lignes. Par défaut, c'est le texte tel que saisi, qui peut contenir
    /// des caractères que jsonb refuse (NUL).
    /// </summary>
    private sealed class UriConverter : JsonConverter<Uri>
    {
        public override Uri Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(reader.GetString()!, UriKind.Absolute);

        public override void Write(Utf8JsonWriter writer, Uri value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.AbsoluteUri);
    }

    /// <summary>
    /// Les ids typés et les objets à valeur unique (TenantId, UserId, Email...) sont écrits sous forme de leur valeur
    /// au lieu de <c>{"Value": ...}</c>. Écriture seule : l'outbox n'est jamais relue vers des types du domaine.
    /// </summary>
    private sealed class ValueWrapperConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => ValueProperty(typeToConvert) is not null;

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(
                typeof(ValueWrapperConverter<>).MakeGenericType(typeToConvert), ValueProperty(typeToConvert))!;

        private static PropertyInfo? ValueProperty(Type type)
        {
            if (type.IsPrimitive || type.IsEnum || type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
            {
                return null;
            }

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            return properties is [{ Name: "Value" } value] ? value : null;
        }
    }

    private sealed class ValueWrapperConverter<T>(PropertyInfo valueProperty) : JsonConverter<T>
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException($"{typeof(T).Name} is write-only in the outbox.");

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, valueProperty.GetValue(value), valueProperty.PropertyType, options);
    }
}
