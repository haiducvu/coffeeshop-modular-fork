using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CoffeeShop.Messaging.Dapr;

public static class DaprAppChannelAuthenticationExtensions
{
    private const string TokenHeaderName = "dapr-api-token";

    public static IApplicationBuilder UseDaprAppChannelAuthentication(
        this IApplicationBuilder app)
    {
        var expectedToken = app.ApplicationServices
            .GetRequiredService<IOptions<DaprMessagingOptions>>()
            .Value
            .AppApiToken;
        var expectedTokenHash = Hash(expectedToken);

        return app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/dapr"))
            {
                await next(context);
                return;
            }

            var suppliedTokenHash = Hash(
                context.Request.Headers[TokenHeaderName].ToString());
            if (!CryptographicOperations.FixedTimeEquals(
                    expectedTokenHash,
                    suppliedTokenHash))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var isDeliveryCallback = IsDeliveryCallback(context.Request);
            try
            {
                await next(context);
            }
            catch (Exception exception) when (
                isDeliveryCallback
                && exception is JsonException or BadHttpRequestException)
            {
                await WriteDropAsync(context);
                return;
            }

            if (isDeliveryCallback
                && !context.Response.HasStarted
                && context.Response.StatusCode is
                    StatusCodes.Status400BadRequest or
                    StatusCodes.Status415UnsupportedMediaType)
            {
                await WriteDropAsync(context);
            }
        });
    }

    private static bool IsDeliveryCallback(HttpRequest request) =>
        HttpMethods.IsPost(request.Method)
        && (request.Path == "/dapr/orders/v1"
            || request.Path == "/dapr/preparation/v1");

    private static async Task WriteDropAsync(HttpContext context)
    {
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsJsonAsync(
            new DaprDeliveryResponse("DROP"),
            context.RequestAborted);
    }

    private static byte[] Hash(string value) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private sealed record DaprDeliveryResponse(string Status);
}
