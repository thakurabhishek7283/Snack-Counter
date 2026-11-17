using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SnackCounter.Domain.Common;

namespace SnackCounter.Api.Http;

/// <summary>API JSON shape: ids as plain GUID strings, money as {amount, currency}, months as "YYYY-MM", enums as names.</summary>
public static class ApiJson
{
    public static void Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new StronglyTypedIdConverterFactory());
        options.Converters.Add(new CurrencyJsonConverter());
        options.Converters.Add(new MoneyJsonConverter());
        options.Converters.Add(new BudgetMonthJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter());
    }
}

/// <summary>Any id in the domain's Common namespace shaped like <c>record struct XId(Guid Value)</c>.</summary>
public sealed class StronglyTypedIdConverterFactory : JsonConverterFactory
{
    private static readonly ConcurrentDictionary<Type, bool> Supported = new();

    public override bool CanConvert(Type typeToConvert) => Supported.GetOrAdd(typeToConvert, t =>
        t.IsValueType &&
        t.Namespace == typeof(OfficeId).Namespace &&
        t.Name.EndsWith("Id", StringComparison.Ordinal) &&
        t.GetProperty("Value")?.PropertyType == typeof(Guid) &&
        t.GetConstructor([typeof(Guid)]) is not null);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(IdConverter<>).MakeGenericType(typeToConvert))!;

    private sealed class IdConverter<T> : JsonConverter<T> where T : struct
    {
        private static readonly ConstructorInfo Ctor = typeof(T).GetConstructor([typeof(Guid)])!;
        private static readonly PropertyInfo ValueProperty = typeof(T).GetProperty("Value")!;

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            (T)Ctor.Invoke([reader.GetGuid()]);

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue((Guid)ValueProperty.GetValue(value)!);
    }
}

public sealed class CurrencyJsonConverter : JsonConverter<Currency>
{
    public override Currency Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Currency.TryParse(reader.GetString(), out var currency) ? currency : throw new JsonException("Invalid currency code.");

    public override void Write(Utf8JsonWriter writer, Currency value, JsonSerializerOptions options) => writer.WriteStringValue(value.Code);
}

public sealed class BudgetMonthJsonConverter : JsonConverter<BudgetMonth>
{
    public override BudgetMonth Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        BudgetMonth.TryParse(reader.GetString(), out var month) ? month : throw new JsonException("Expected YYYY-MM.");

    public override void Write(Utf8JsonWriter writer, BudgetMonth value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}

public sealed class MoneyJsonConverter : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        return new Money(root.GetProperty("amount").GetDecimal(), Currency.Of(root.GetProperty("currency").GetString()!));
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("amount", value.Amount);
        writer.WriteString("currency", value.Currency.Code);
        writer.WriteEndObject();
    }
}
