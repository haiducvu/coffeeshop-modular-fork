using Confluent.Kafka;
using CoffeeShop.Messaging.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CoffeeShop.MessagingTests.Kafka;

public sealed class KafkaClientPolicyTests
{
    [Fact]
    public void Producer_requires_all_acknowledgements_and_idempotence()
    {
        var config = KafkaClientConfigFactory.CreateProducer(CreateOptions());

        Assert.Equal(Acks.All, config.Acks);
        Assert.True(config.EnableIdempotence);
    }

    [Fact]
    public void Consumer_uses_earliest_reset_and_manual_offset_commit()
    {
        var config = KafkaClientConfigFactory.CreateConsumer(CreateOptions(), "barista");

        Assert.Equal(AutoOffsetReset.Earliest, config.AutoOffsetReset);
        Assert.False(config.EnableAutoCommit);
        Assert.Equal(10_000, config.SessionTimeoutMs);
        Assert.Equal(300_000, config.MaxPollIntervalMs);
        Assert.Equal("lesson22.barista", config.GroupId);
    }

    [Fact]
    public void Missing_bootstrap_servers_fail_options_validation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKafkaMessaging(options => options.BootstrapServers = string.Empty);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<KafkaMessagingOptions>>().Value);

        Assert.Contains("Kafka bootstrap servers are required", exception.Message);
    }

    [Fact]
    public void Retry_poll_interval_shorter_than_safe_handler_window_fails_validation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKafkaMessaging(options =>
        {
            options.BootstrapServers = "localhost:9092";
            options.Retry.MaxPollInterval = TimeSpan.FromSeconds(30);
        });
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<KafkaMessagingOptions>>().Value);

        Assert.Contains("at least five minutes", exception.Message);
    }

    private static KafkaMessagingOptions CreateOptions() => new()
    {
        BootstrapServers = "localhost:9092",
        TopicPrefix = "coffeeshop",
        ConsumerGroupPrefix = "lesson22"
    };
}
