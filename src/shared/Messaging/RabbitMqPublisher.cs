using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace Shared.Messaging;

public sealed class RabbitMqPublisher : IRabbitMqPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IConfiguration _config;
    private readonly object _sync = new();
    private IConnection? _connection;
    private IModel? _channel;

    public RabbitMqPublisher(IConfiguration config)
    {
        _config = config;
    }

    public Task PublishAsync<T>(
        string exchange,
        T message,
        CancellationToken cancellationToken = default)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        MessageValidation.ThrowIfInvalid(message);

        var body = JsonSerializer.SerializeToUtf8Bytes(
            new RabbitMqEnvelope<T>(
                Guid.NewGuid().ToString("n"),
                typeof(T).Name,
                DateTimeOffset.UtcNow,
                message),
            JsonOptions);

        lock (_sync)
        {
            var channel = EnsureChannel();
            var properties = channel.CreateBasicProperties();
            properties.ContentType = "application/json";
            properties.DeliveryMode = 2;
            properties.MessageId = Guid.NewGuid().ToString("n");
            properties.Type = typeof(T).Name;
            properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            channel.BasicPublish(
                exchange,
                RabbitMqTopology.RoutingKeyFor<T>(),
                mandatory: false,
                basicProperties: properties,
                body: body);
        }

        return Task.CompletedTask;
    }

    private IModel EnsureChannel()
    {
        if (_channel?.IsOpen == true)
            return _channel;

        _channel?.Dispose();
        _connection?.Dispose();

        var settings = RabbitMqSettings.FromConfiguration(_config);
        _connection = settings.CreateConnectionFactory().CreateConnection("rx-demo-publisher");
        _channel = _connection.CreateModel();
        RabbitMqTopologySetup.Declare(_channel, _config);
        return _channel;
    }

    public ValueTask DisposeAsync()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        return ValueTask.CompletedTask;
    }
}

public static class RabbitMqTopologySetup
{
    public static void Declare(IModel channel, IConfiguration config)
    {
        var commandsQueue = config["Messaging:QueueName"] ?? RabbitMqTopology.DefaultCommandsQueue;
        var eventsQueue = config["Messaging:EventsQueue"] ?? RabbitMqTopology.DefaultEventsQueue;

        channel.ExchangeDeclare(RabbitMqTopology.CommandsExchange, ExchangeType.Direct, durable: true, autoDelete: false);
        channel.ExchangeDeclare(RabbitMqTopology.EventsExchange, ExchangeType.Direct, durable: true, autoDelete: false);

        channel.QueueDeclare(commandsQueue, durable: true, exclusive: false, autoDelete: false);
        channel.QueueBind(commandsQueue, RabbitMqTopology.CommandsExchange, nameof(ApprovePrescriptionCommand));
        channel.QueueBind(commandsQueue, RabbitMqTopology.CommandsExchange, nameof(RefillRequestCommand));

        channel.QueueDeclare(eventsQueue, durable: true, exclusive: false, autoDelete: false);
        channel.QueueBind(eventsQueue, RabbitMqTopology.EventsExchange, nameof(PrescriptionChangedEvent));
    }

    public static string DecodeBody(ReadOnlyMemory<byte> body) =>
        Encoding.UTF8.GetString(body.Span);
}
