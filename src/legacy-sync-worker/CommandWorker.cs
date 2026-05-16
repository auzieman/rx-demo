using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json;
using LegacySyncWorker.Infrastructure;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Messaging;
using Shared.Observability;
using Shared.Telemetry;

public sealed class CommandWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10)
    ];

    private readonly IConfiguration _config;
    private readonly IDbInfrastructure _db;
    private readonly IRabbitMqPublisher _publisher;
    private readonly ILogger<CommandWorker> _logger;
    private IConnection? _connection;
    private IModel? _channel;
    private string _queueName = RabbitMqTopology.DefaultCommandsQueue;

    public CommandWorker(
        IConfiguration config,
        IDbInfrastructure db,
        IRabbitMqPublisher publisher,
        ILogger<CommandWorker> logger)
    {
        _config = config;
        _db = db;
        _publisher = publisher;
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
                _logger.LogWarning(ex, "RabbitMQ command consumer startup failed; retrying");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    private void StartConsumer()
    {
        var settings = RabbitMqSettings.FromConfiguration(_config);
        _queueName = _config["Messaging:QueueName"] ?? RabbitMqTopology.DefaultCommandsQueue;
        _connection = settings.CreateConnectionFactory().CreateConnection("rx-demo-command-worker");
        _channel = _connection.CreateModel();
        _channel.BasicQos(0, prefetchCount: 8, global: false);
        RabbitMqTopologySetup.Declare(_channel, _config);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += HandleDeliveryAsync;
        _channel.BasicConsume(_queueName, autoAck: false, consumer);
        _logger.LogInformation("Command worker consuming RabbitMQ queue {QueueName}", _queueName);
    }

    private async Task HandleDeliveryAsync(object sender, BasicDeliverEventArgs args)
    {
        try
        {
            var body = RabbitMqTopologySetup.DecodeBody(args.Body);
            switch (args.BasicProperties.Type)
            {
                case nameof(ApprovePrescriptionCommand):
                    await ProcessWithRetryAsync(
                        JsonSerializer.Deserialize<RabbitMqEnvelope<ApprovePrescriptionCommand>>(body, JsonOptions)?.Payload
                            ?? throw new JsonException("ApprovePrescriptionCommand payload missing."),
                        ProcessApproveAsync);
                    break;
                case nameof(RefillRequestCommand):
                    await ProcessWithRetryAsync(
                        JsonSerializer.Deserialize<RabbitMqEnvelope<RefillRequestCommand>>(body, JsonOptions)?.Payload
                            ?? throw new JsonException("RefillRequestCommand payload missing."),
                        ProcessRefillAsync);
                    break;
                default:
                    throw new ValidationException($"Unsupported command type '{args.BasicProperties.Type}'.");
            }

            _channel?.BasicAck(args.DeliveryTag, multiple: false);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, "Dropping invalid command from {QueueName}", _queueName);
            Metrics.RecordError("legacy-sync-worker", ex.GetType().Name);
            _channel?.BasicReject(args.DeliveryTag, requeue: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command failed after local retries; rejecting delivery");
            Metrics.RecordError("legacy-sync-worker", ex.GetType().Name);
            _channel?.BasicReject(args.DeliveryTag, requeue: false);
        }
    }

    private static async Task ProcessWithRetryAsync<T>(T command, Func<T, int, Task> handler)
        where T : class
    {
        MessageValidation.ThrowIfInvalid(command);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await handler(command, attempt);
                return;
            }
            catch (ValidationException)
            {
                throw;
            }
            catch when (attempt < RetryDelays.Length)
            {
                await Task.Delay(RetryDelays[attempt]);
            }
        }
    }

    private async Task ProcessApproveAsync(ApprovePrescriptionCommand command, int retryAttempt)
    {
        var sw = Stopwatch.StartNew();
        const string messageType = nameof(ApprovePrescriptionCommand);
        var result = "success";
        long payloadBytes = 0;

        using (_logger.BeginEventScope("rx", "worker.approve",
            ("rx.id", command.RxId),
            ("messaging.system", "rabbitmq"),
            ("messaging.destination.name", _queueName),
            ("messaging.operation", "consume"),
            ("messaging.message_type", messageType),
            ("messaging.retry_attempt", retryAttempt),
            ("fault.mode", command.FaultMode)))
        {
            try
            {
                WorkerFaults.ApplyWorkerFault(command.FaultMode, retryAttempt, _logger);
                payloadBytes = await _db.UpsertApproveAsync(command);
                _logger.LogInformation("Approve processed for RxId={RxId}", command.RxId);

                if (command.FaultMode == "publish-fail")
                    throw new InvalidOperationException("Injected publish failure after DB write.");

                await _publisher.PublishAsync(RabbitMqTopology.EventsExchange, new PrescriptionChangedEvent(
                    command.RxId,
                    "Approved",
                    1,
                    DateTimeOffset.UtcNow,
                    command.FaultMode));

                Metrics.RecordPublished(RabbitMqTopology.EventsExchange, nameof(PrescriptionChangedEvent));
            }
            catch (Exception ex)
            {
                result = "failure";
                _logger.LogError(ex, "Approve failed for RxId={RxId}", command.RxId);
                Metrics.RecordError("legacy-sync-worker", ex.GetType().Name);
                throw;
            }
            finally
            {
                sw.Stop();
                Metrics.RecordQueueMessage(_queueName, "consume", messageType, result, sw.Elapsed.TotalMilliseconds);
                Metrics.RecordDbOperation("Approve", result, sw.Elapsed.TotalMilliseconds, payloadBytes);
            }
        }
    }

    private async Task ProcessRefillAsync(RefillRequestCommand command, int retryAttempt)
    {
        var sw = Stopwatch.StartNew();
        const string messageType = nameof(RefillRequestCommand);
        var result = "success";
        long payloadBytes = 0;

        using (_logger.BeginEventScope("rx", "worker.refill",
            ("rx.id", command.RxId),
            ("messaging.system", "rabbitmq"),
            ("messaging.destination.name", _queueName),
            ("messaging.operation", "consume"),
            ("messaging.message_type", messageType),
            ("messaging.retry_attempt", retryAttempt),
            ("fault.mode", command.FaultMode)))
        {
            try
            {
                WorkerFaults.ApplyWorkerFault(command.FaultMode, retryAttempt, _logger);
                payloadBytes = await _db.UpsertRefillAsync(command);
                _logger.LogInformation("Refill processed for RxId={RxId}", command.RxId);

                if (command.FaultMode == "publish-fail")
                    throw new InvalidOperationException("Injected publish failure after DB write.");

                await _publisher.PublishAsync(RabbitMqTopology.EventsExchange, new PrescriptionChangedEvent(
                    command.RxId,
                    "Refilled",
                    1,
                    DateTimeOffset.UtcNow,
                    command.FaultMode));

                Metrics.RecordPublished(RabbitMqTopology.EventsExchange, nameof(PrescriptionChangedEvent));
            }
            catch (Exception ex)
            {
                result = "failure";
                _logger.LogError(ex, "Refill failed for RxId={RxId}", command.RxId);
                Metrics.RecordError("legacy-sync-worker", ex.GetType().Name);
                throw;
            }
            finally
            {
                sw.Stop();
                Metrics.RecordQueueMessage(_queueName, "consume", messageType, result, sw.Elapsed.TotalMilliseconds);
                Metrics.RecordDbOperation("Refill", result, sw.Elapsed.TotalMilliseconds, payloadBytes);
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

internal static class WorkerFaults
{
    public static void ApplyWorkerFault(string? faultMode, int retryAttempt, ILogger logger)
    {
        switch (faultMode)
        {
            case "worker-transient-once" when retryAttempt == 0:
                logger.LogWarning("Injecting transient worker fault on first attempt");
                throw new TimeoutException("Injected transient worker timeout on first attempt.");
            case "worker-timeout":
                logger.LogWarning("Injecting persistent worker timeout fault");
                throw new TimeoutException("Injected persistent worker timeout.");
            case "worker-fail":
                logger.LogWarning("Injecting worker failure fault");
                throw new InvalidOperationException("Injected worker failure.");
        }
    }
}
