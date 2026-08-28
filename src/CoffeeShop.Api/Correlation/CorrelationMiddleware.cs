using System.Diagnostics;
using CoffeeShop.Messaging.Abstractions;

namespace CoffeeShop.Api.Correlation;

public sealed class CorrelationMiddleware(
    RequestDelegate next,
    ILogger<CorrelationMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";
    private const int MaximumInboundLength = 128;

    public async Task InvokeAsync(
        HttpContext context,
        IMessageIdentityAccessor identityAccessor)
    {
        if (!IsValidInboundHeader(context.Request.Headers[HeaderName]))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var activity = Activity.Current;
        var identity = new MessageIdentity(
            Guid.NewGuid().ToString("D"),
            null,
            activity?.IdFormat == ActivityIdFormat.W3C ? activity.Id : null,
            activity?.TraceStateString);
        context.Response.Headers[HeaderName] = identity.CorrelationId;

        using var identityScope = identityAccessor.Push(identity);
        using var logScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = identity.CorrelationId,
            ["CausationId"] = identity.CausationId
        });
        await next(context);
    }

    private static bool IsValidInboundHeader(Microsoft.Extensions.Primitives.StringValues values)
    {
        if (values.Count == 0)
        {
            return true;
        }

        return values.Count == 1
            && values[0] is { Length: <= MaximumInboundLength } value
            && Guid.TryParseExact(value, "D", out _);
    }
}
