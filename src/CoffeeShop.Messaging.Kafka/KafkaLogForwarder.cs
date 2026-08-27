using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace CoffeeShop.Messaging.Kafka;

internal static class KafkaLogForwarder
{
    internal static void Log(ILogger logger, LogMessage message)
    {
        var level = message.Level switch
        {
            SyslogLevel.Emergency or SyslogLevel.Alert or SyslogLevel.Critical =>
                LogLevel.Critical,
            SyslogLevel.Error => LogLevel.Error,
            SyslogLevel.Warning => LogLevel.Warning,
            SyslogLevel.Notice or SyslogLevel.Info => LogLevel.Information,
            _ => LogLevel.Debug
        };
        logger.Log(
            level,
            "librdkafka {KafkaClientName} {KafkaFacility}: {KafkaMessage}",
            message.Name,
            message.Facility,
            message.Message);
    }
}
