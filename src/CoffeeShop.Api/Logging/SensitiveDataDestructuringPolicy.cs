using System.Reflection;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Json;

namespace CoffeeShop.Api.Logging;

public sealed class SensitiveDataDestructuringPolicy : IDestructuringPolicy, ITextFormatter
{
    private const string RedactedValue = "[REDACTED]";
    private readonly JsonFormatter _jsonFormatter = new(renderMessage: true);

    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        out LogEventPropertyValue result)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(propertyValueFactory);

        if (IsCompleteOrderPayload(value.GetType()))
        {
            result = new ScalarValue(RedactedValue);
            return true;
        }

        var properties = value.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .ToArray();
        if (!properties.Any(property => IsSensitive(property.Name)))
        {
            result = null!;
            return false;
        }

        result = new StructureValue(properties.Select(property =>
            new LogEventProperty(
                property.Name,
                IsSensitive(property.Name)
                    ? new ScalarValue(RedactedValue)
                    : propertyValueFactory.CreatePropertyValue(
                        property.GetValue(value),
                        destructureObjects: true))));
        return true;
    }

    public void Format(LogEvent logEvent, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(output);

        var properties = logEvent.Properties.Select(property => new LogEventProperty(
            property.Key,
            IsSensitive(property.Key)
                ? new ScalarValue(RedactedValue)
                : RedactNestedProperties(property.Value))).ToList();
        if (logEvent.Exception is not null)
        {
            properties.Add(new LogEventProperty(
                "ExceptionType",
                new ScalarValue(logEvent.Exception.GetType().FullName)));
        }

        var safeEvent = new LogEvent(
            logEvent.Timestamp,
            logEvent.Level,
            exception: null,
            logEvent.MessageTemplate,
            properties);
        _jsonFormatter.Format(safeEvent, output);
    }

    private static LogEventPropertyValue RedactNestedProperties(LogEventPropertyValue value) =>
        value switch
        {
            StructureValue structure => new StructureValue(
                structure.Properties.Select(property => new LogEventProperty(
                    property.Name,
                    IsSensitive(property.Name)
                        ? new ScalarValue(RedactedValue)
                        : RedactNestedProperties(property.Value))),
                structure.TypeTag),
            SequenceValue sequence => new SequenceValue(
                sequence.Elements.Select(RedactNestedProperties)),
            DictionaryValue dictionary => new DictionaryValue(
                dictionary.Elements.Select(entry => new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                    entry.Key,
                    IsSensitiveDictionaryKey(entry.Key)
                        ? new ScalarValue(RedactedValue)
                        : RedactNestedProperties(entry.Value)))),
            _ => value
        };

    private static bool IsSensitive(string propertyName)
    {
        var normalized = propertyName.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        return normalized.Contains("authorization", StringComparison.Ordinal)
            || normalized.Contains("token", StringComparison.Ordinal)
            || normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("credential", StringComparison.Ordinal)
            || normalized.Contains("connectionstring", StringComparison.Ordinal)
            || normalized == "order"
            || normalized.Contains("orderpayload", StringComparison.Ordinal)
            || normalized.Contains("orderrequest", StringComparison.Ordinal);
    }

    private static bool IsSensitiveDictionaryKey(ScalarValue key) =>
        key.Value is string propertyName && IsSensitive(propertyName);

    private static bool IsCompleteOrderPayload(Type type) =>
        !type.IsEnum
        && type.Namespace?.StartsWith("CoffeeShop.", StringComparison.Ordinal) is true
        && type.Name.Contains("Order", StringComparison.Ordinal);
}
