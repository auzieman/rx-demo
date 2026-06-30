using Microsoft.Extensions.Logging;

namespace Shared.Observability;

public static class CloudEventAudit
{
    public const string AuditType = "com.auzietek.rx.prescription.audit.v1";
    public const string Source = "urn:auzietek:rx-demo";

    public static void LogPrescriptionAudit(
        this ILogger logger,
        string action,
        string rxId,
        string actor,
        string outcome,
        params (string Key, object? Value)[] attributes)
    {
        var eventId = Guid.NewGuid().ToString("n");
        using var scope = logger.BeginEventScope("rx", "cloudevent.audit",
            ("cloudevents.specversion", "1.0"),
            ("cloudevents.id", eventId),
            ("cloudevents.source", Source),
            ("cloudevents.type", AuditType),
            ("cloudevents.subject", $"prescription/{rxId}"),
            ("rx.id", rxId),
            ("audit.action", action),
            ("audit.actor", actor),
            ("audit.outcome", outcome));

        logger.LogInformation(
            "CloudEvent audit action={Action} rxId={RxId} actor={Actor} outcome={Outcome} eventId={EventId}",
            action,
            rxId,
            actor,
            outcome,
            eventId);

        foreach (var (key, value) in attributes)
        {
            if (!string.IsNullOrWhiteSpace(key) && value is not null)
            {
                logger.LogInformation("CloudEvent audit attribute {Key}={Value}", key, value);
            }
        }
    }
}
