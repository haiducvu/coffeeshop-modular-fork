namespace CoffeeShop.Messaging.Abstractions;

public sealed class IntegrationEventRejectedException : Exception
{
    public IntegrationEventRejectedException(
        string safeErrorCode,
        Exception? innerException = null)
        : base($"Integration event rejected with code '{safeErrorCode}'.", innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(safeErrorCode);
        if (safeErrorCode.Length > 64
            || safeErrorCode.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new ArgumentException(
                "Integration rejection code must contain only letters, digits, or hyphens and be at most 64 characters.",
                nameof(safeErrorCode));
        }

        SafeErrorCode = safeErrorCode;
    }

    public string SafeErrorCode { get; }
}
