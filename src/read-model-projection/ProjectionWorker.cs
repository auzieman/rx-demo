using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Messaging;
using Shared.Observability;
using Shared.ReadModel;
using Shared.Telemetry;

public sealed class ProjectionWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IConfiguration _config;
    private readonly PrescriptionReadModelStore _store;
    private readonly ILogger<ProjectionWorker> _logger;
    private IConnection? _connection;
    private IModel? _channel;
    private string _queueName = RabbitMqTopology.DefaultEventsQueue;

    public ProjectionWorker(
        IConfiguration config,
        PrescriptionReadModelStore store,
        ILogger<ProjectionWorker> logger)
    {
        _config = config;
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                StartConsumer();
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RabbitMQ projection consumer startup failed; retrying");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    private void StartConsumer()
    {
        var settings = RabbitMqSettings.FromConfiguration(_config);
        _queueName = _config["Messaging:EventsQueue"] ?? RabbitMqTopology.DefaultEventsQueue;
        _connection = settings.CreateConnectionFactory().CreateConnection("rx-demo-projection-worker");
        _channel = _connection.CreateModel();
        _channel.BasicQos(0, prefetchCount: 16, global: false);
        RabbitMqTopologySetup.Declare(_channel, _config);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += HandleDeliveryAsync;
        _channel.BasicConsume(_queueName, autoAck: false, consumer);
        _logger.LogInformation("Projection worker consuming RabbitMQ queue {QueueName}", _queueName);
    }

    private async Task HandleDeliveryAsync(object sender, BasicDeliverEventArgs args)
    {
        try
        {
            if (args.BasicProperties.Type != nameof(PrescriptionChangedEvent))
                throw new ValidationException($"Unsupported event type '{args.BasicProperties.Type}'.");

            var body = RabbitMqTopologySetup.DecodeBody(args.Body);
            var message = JsonSerializer.Deserialize<RabbitMqEnvelope<PrescriptionChangedEvent>>(body, JsonOptions)?.Payload
                ?? throw new JsonException("PrescriptionChangedEvent payload missing.");

            MessageValidation.ThrowIfInvalid(message);
            await ProcessAsync(message);
            _channel?.BasicAck(args.DeliveryTag, multiple: false);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, "Dropping invalid projection event from {QueueName}", _queueName);
            Metrics.RecordError("read-model-projection", ex.GetType().Name);
            _channel?.BasicReject(args.DeliveryTag, requeue: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Projection event failed; rejecting delivery");
            Metrics.RecordError("read-model-projection", ex.GetType().Name);
            _channel?.BasicReject(args.DeliveryTag, requeue: false);
        }
    }

    private async Task ProcessAsync(PrescriptionChangedEvent message)
    {
        var sw = Stopwatch.StartNew();
        const string messageType = nameof(PrescriptionChangedEvent);
        var result = "success";

        using (_logger.BeginEventScope("rx", "projection.update",
            ("rx.id", message.RxId),
            ("messaging.system", "rabbitmq"),
            ("messaging.destination.name", _queueName),
            ("messaging.operation", "consume"),
            ("messaging.message_type", messageType),
            ("fault.mode", message.FaultMode)))
        {
            try
            {
                await ProjectionFaults.ApplyProjectionFaultAsync(message.FaultMode, _logger, CancellationToken.None);

                var view = new PrescriptionReadModel(
                    message.RxId,
                    message.Status,
                    message.Version,
                    message.ChangedAt);

                var cacheSw = Stopwatch.StartNew();
                await _store.UpsertAsync(view, TimeSpan.FromHours(24));
                cacheSw.Stop();
                Metrics.RecordCache("redis", "set", "success", cacheSw.Elapsed.TotalMilliseconds);

                _logger.LogInformation("Projection updated for RxId={RxId}", message.RxId);
            }
            catch (Exception ex)
            {
                result = "failure";
                _logger.LogError(ex, "Projection update failed for RxId={RxId}", message.RxId);
                Metrics.RecordError("read-model-projection", ex.GetType().Name);
                throw;
            }
            finally
            {
                sw.Stop();
                Metrics.RecordQueueMessage(_queueName, "consume", messageType, result, sw.Elapsed.TotalMilliseconds);
            }
        }
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}

internal static class ProjectionFaults
{
    public static async Task ApplyProjectionFaultAsync(string? faultMode, ILogger logger, CancellationToken cancellationToken)
    {
        switch (faultMode)
        {
            case "projection-fail":
                logger.LogWarning("Injecting projection failure fault");
                throw new InvalidOperationException("Injected projection failure.");
            case "projection-timeout":
                logger.LogWarning("Injecting projection timeout fault");
                throw new TimeoutException("Injected projection timeout.");
            case "cache-fail":
                logger.LogWarning("Injecting projection cache failure delay");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                throw new InvalidOperationException("Injected cache failure.");
        }
    }
}
