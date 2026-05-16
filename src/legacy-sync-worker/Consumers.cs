// /src/legacy-sync-worker/Consumers.cs
using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.Logging;
using Shared.Messaging;
using Shared.Observability;
using Shared.Telemetry;
using LegacySyncWorker.Infrastructure;

public sealed class ApproveConsumer : IConsumer<ApprovePrescriptionCommand>
{
    private readonly IDbInfrastructure _db;
    private readonly ILogger<ApproveConsumer> _logger;

    public ApproveConsumer(IDbInfrastructure db, ILogger<ApproveConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ApprovePrescriptionCommand> ctx)
    {
        var sw = Stopwatch.StartNew();
        const string queueName = "rx.commands";
        const string messageType = nameof(ApprovePrescriptionCommand);
        string result = "success";
        long payloadBytes = 0;

        using (_logger.BeginEventScope("rx", "worker.approve",
            ("rx.id", ctx.Message.RxId),
            ("messaging.system", "rabbitmq"),
            ("messaging.destination.name", ctx.ReceiveContext.InputAddress.AbsolutePath),
            ("messaging.operation", "consume"),
            ("messaging.message_type", messageType),
            ("fault.mode", ctx.Message.FaultMode)))
        {
            try
            {
                WorkerFaults.ApplyWorkerFault(ctx.Message.FaultMode, ctx.GetRetryAttempt(), _logger);

                // Persist business change
                payloadBytes = await _db.UpsertApproveAsync(ctx.Message);

                _logger.LogInformation("Approve processed for RxId={RxId}", ctx.Message.RxId);

                if (ctx.Message.FaultMode == "publish-fail")
                    throw new InvalidOperationException("Injected publish failure after DB write.");

                // Publish domain event (positional args)
                await ctx.Publish(new PrescriptionChangedEvent(
                    ctx.Message.RxId,
                    "Approved",
                    1,
                    DateTimeOffset.UtcNow,
                    ctx.Message.FaultMode));

                Metrics.RecordPublished("rx.events", nameof(PrescriptionChangedEvent));
            }
            catch (Exception ex)
            {
                result = "failure";

                _logger.LogError(ex, "Approve failed for RxId={RxId}", ctx.Message.RxId);
                Metrics.RecordError("legacy-sync-worker", ex.GetType().Name);

                // Let MassTransit retry/deliver per configured policies
                throw;
            }
            finally
            {
                sw.Stop();

                Metrics.RecordQueueMessage(queueName, "consume", messageType, result, sw.Elapsed.TotalMilliseconds);
                Metrics.RecordDbOperation("Approve", result, sw.Elapsed.TotalMilliseconds, payloadBytes);
            }
        }
    }
}

public sealed class RefillConsumer : IConsumer<RefillRequestCommand>
{
    private readonly IDbInfrastructure _db;
    private readonly ILogger<RefillConsumer> _logger;

    public RefillConsumer(IDbInfrastructure db, ILogger<RefillConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<RefillRequestCommand> ctx)
    {
        var sw = Stopwatch.StartNew();
        const string queueName = "rx.commands";
        const string messageType = nameof(RefillRequestCommand);
        string result = "success";
        long payloadBytes = 0;

        using (_logger.BeginEventScope("rx", "worker.refill",
            ("rx.id", ctx.Message.RxId),
            ("messaging.system", "rabbitmq"),
            ("messaging.destination.name", ctx.ReceiveContext.InputAddress.AbsolutePath),
            ("messaging.operation", "consume"),
            ("messaging.message_type", messageType),
            ("fault.mode", ctx.Message.FaultMode)))
        {
            try
            {
                WorkerFaults.ApplyWorkerFault(ctx.Message.FaultMode, ctx.GetRetryAttempt(), _logger);

                // Persist business change
                payloadBytes = await _db.UpsertRefillAsync(ctx.Message);

                _logger.LogInformation("Refill processed for RxId={RxId}", ctx.Message.RxId);

                if (ctx.Message.FaultMode == "publish-fail")
                    throw new InvalidOperationException("Injected publish failure after DB write.");

                // Publish domain event (positional args)
                await ctx.Publish(new PrescriptionChangedEvent(
                    ctx.Message.RxId,
                    "Refilled",
                    1,
                    DateTimeOffset.UtcNow,
                    ctx.Message.FaultMode));

                Metrics.RecordPublished("rx.events", nameof(PrescriptionChangedEvent));
            }
            catch (Exception ex)
            {
                result = "failure";

                _logger.LogError(ex, "Refill failed for RxId={RxId}", ctx.Message.RxId);
                Metrics.RecordError("legacy-sync-worker", ex.GetType().Name);
                throw;
            }
            finally
            {
                sw.Stop();

                Metrics.RecordQueueMessage(queueName, "consume", messageType, result, sw.Elapsed.TotalMilliseconds);
                Metrics.RecordDbOperation("Refill", result, sw.Elapsed.TotalMilliseconds, payloadBytes);
            }
        }
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
