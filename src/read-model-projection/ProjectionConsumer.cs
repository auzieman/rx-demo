// /src/read-model-projection/ProjectionConsumer.cs
using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.Logging;
using Shared.Messaging;
using Shared.Observability;
using Shared.ReadModel;
using Shared.Telemetry;

public sealed class ProjectionConsumer : IConsumer<PrescriptionChangedEvent>
{
    private readonly PrescriptionReadModelStore _store;
    private readonly ILogger<ProjectionConsumer> _logger;

    public ProjectionConsumer(PrescriptionReadModelStore store, ILogger<ProjectionConsumer> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PrescriptionChangedEvent> ctx)
    {
        var sw = Stopwatch.StartNew();
        const string queueName = "rx.events";
        const string messageType = nameof(PrescriptionChangedEvent);
        string result = "success";

        using (_logger.BeginEventScope("rx", "projection.update",
            ("rx.id", ctx.Message.RxId),
            ("messaging.system", "rabbitmq"),
            ("messaging.destination.name", ctx.ReceiveContext.InputAddress.AbsolutePath),
            ("messaging.operation", "consume"),
            ("messaging.message_type", messageType),
            ("fault.mode", ctx.Message.FaultMode)))
        {
            try
            {
                await ProjectionFaults.ApplyProjectionFaultAsync(ctx.Message.FaultMode, _logger, CancellationToken.None);

                var view = new PrescriptionReadModel(
                    ctx.Message.RxId,
                    ctx.Message.Status,
                    ctx.Message.Version,
                    ctx.Message.ChangedAt);

                // Update read model (15-minute TTL)
                var cacheSw = Stopwatch.StartNew();
                await _store.UpsertAsync(view, TimeSpan.FromMinutes(15));
                cacheSw.Stop();
                Metrics.RecordCache("redis", "set", "success", cacheSw.Elapsed.TotalMilliseconds);

                _logger.LogInformation("Projection updated for RxId={RxId}", ctx.Message.RxId);
            }
            catch (Exception ex)
            {
                result = "failure";

                _logger.LogError(ex, "Projection update failed for RxId={RxId}", ctx.Message.RxId);
                Metrics.RecordError("read-model-projection", ex.GetType().Name);
                throw; // let MT retry/redelivery handle it
            }
            finally
            {
                sw.Stop();
                Metrics.RecordQueueMessage(queueName, "consume", messageType, result, sw.Elapsed.TotalMilliseconds);
            }
        }
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
