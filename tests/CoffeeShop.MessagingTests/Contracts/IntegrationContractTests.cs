using System.Text.Json;
using System.Text.Json.Nodes;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;

namespace CoffeeShop.MessagingTests.Contracts;

public sealed class IntegrationContractTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public void Order_placed_v1_matches_the_published_json_fixture()
    {
        var envelope = new IntegrationEventEnvelope<OrderPlacedV1>(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            OrderPlacedV1.EventType,
            OrderPlacedV1.EventVersion,
            DateTimeOffset.Parse("2026-08-26T01:02:03+00:00"),
            "order-workflow-11111111",
            null,
            new OrderPlacedV1(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                [
                    new OrderLineItemV1(
                        Guid.Parse("33333333-3333-3333-3333-333333333333"),
                        "Latte",
                        "Barista"),
                    new OrderLineItemV1(
                        Guid.Parse("44444444-4444-4444-4444-444444444444"),
                        "Croissant",
                        "Kitchen")
                ]));

        var actual = JsonNode.Parse(JsonSerializer.Serialize(envelope, JsonOptions));
        var expected = JsonNode.Parse(ReadFixture("order-placed-v1.json"));

        Assert.True(JsonNode.DeepEquals(expected, actual));
        Assert.Equal("coffeeshop.order-placed", OrderPlacedV1.EventType);
        Assert.Equal(1, OrderPlacedV1.EventVersion);
    }

    [Fact]
    public void Order_item_prepared_v1_matches_the_published_json_fixture()
    {
        var envelope = new IntegrationEventEnvelope<OrderItemPreparedV1>(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            OrderItemPreparedV1.EventType,
            OrderItemPreparedV1.EventVersion,
            DateTimeOffset.Parse("2026-08-26T01:02:08+00:00"),
            "order-workflow-11111111",
            "11111111-1111-1111-1111-111111111111",
            new OrderItemPreparedV1(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "Latte",
                "Barista",
                "barista",
                DateTimeOffset.Parse("2026-08-26T01:02:08+00:00")));

        var actual = JsonNode.Parse(JsonSerializer.Serialize(envelope, JsonOptions));
        var expected = JsonNode.Parse(ReadFixture("order-item-prepared-v1.json"));

        Assert.True(JsonNode.DeepEquals(expected, actual));
        Assert.Equal("coffeeshop.order-item-prepared", OrderItemPreparedV1.EventType);
        Assert.Equal(1, OrderItemPreparedV1.EventVersion);
    }

    [Fact]
    public void Missing_required_envelope_field_is_rejected()
    {
        const string missingEventVersion = """
            {
              "messageId": "11111111-1111-1111-1111-111111111111",
              "eventType": "coffeeshop.order-placed",
              "occurredAtUtc": "2026-08-26T01:02:03+00:00",
              "correlationId": "order-workflow-11111111",
              "causationId": null,
              "payload": {
                "orderId": "22222222-2222-2222-2222-222222222222",
                "items": []
              }
            }
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<IntegrationEventEnvelope<OrderPlacedV1>>(
                missingEventVersion,
                JsonOptions));
    }

    private static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
}
