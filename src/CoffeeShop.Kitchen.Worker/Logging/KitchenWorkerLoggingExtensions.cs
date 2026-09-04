using Microsoft.Extensions.Logging;

namespace CoffeeShop.Kitchen.Worker.Logging;

public static class KitchenWorkerLoggingExtensions
{
    public static ILoggingBuilder AddKitchenWorkerLogging(this ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
            options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions
            {
                Indented = false
            };
        });
        return logging;
    }
}
