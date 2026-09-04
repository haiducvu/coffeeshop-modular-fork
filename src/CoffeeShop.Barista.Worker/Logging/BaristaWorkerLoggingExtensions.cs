using Microsoft.Extensions.Logging;

namespace CoffeeShop.Barista.Worker.Logging;

public static class BaristaWorkerLoggingExtensions
{
    public static ILoggingBuilder AddBaristaWorkerLogging(this ILoggingBuilder logging)
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
