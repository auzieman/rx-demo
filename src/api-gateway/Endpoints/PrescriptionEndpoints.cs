using System.Diagnostics;
using Shared.Messaging;
using Shared.Observability;
using Shared.ReadModel;
using Shared.Telemetry;

namespace ApiGateway.Endpoints;

public static class PrescriptionEndpoints
{
    public static IEndpointRouteBuilder MapPrescriptionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/prescriptions", ListPrescriptionsAsync);
        app.MapGet("/prescriptions/{rxId}", ReadPrescriptionAsync);
        app.MapPost("/prescriptions/{rxId}/approve", ApprovePrescriptionAsync);
        app.MapPost("/prescriptions/{rxId}/refill", RefillPrescriptionAsync);
        app.MapGet("/fault-modes", () => Results.Ok(new
        {
            api = FaultModes.ApiModes,
            worker = FaultModes.WorkerModes,
            projection = FaultModes.ProjectionModes
        }));

        return app;
    }

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", () => Results.Ok("Healthy"));
        app.MapGet("/readyz", ReadyAsync);
        return app;
    }

    private static async Task<IResult> ReadyAsync(
        PrescriptionReadModelStore store,
        IConfiguration config,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("api.health");
        var checks = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["redis"] = "unknown",
            ["rabbitmq"] = "unknown"
        };

        await RunCheckAsync("redis", checks, () => store.PingAsync());
        await RunCheckAsync("rabbitmq", checks, () => RabbitMqDependencyCheck.CheckTcpAsync(config, cancellationToken));

        var ready = checks.Values.All(static status => status == "ok");
        if (!ready)
            logger.LogWarning("API readiness failed: {Checks}", checks);

        return ready
            ? Results.Ok(new { status = "ready", checks })
            : Results.Json(new { status = "not_ready", checks }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> ListPrescriptionsAsync(
        int? page,
        int? pageSize,
        PrescriptionReadModelStore store,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("api");
        const string route = "/prescriptions";
        var safePage = page.GetValueOrDefault(1);
        var safePageSize = pageSize.GetValueOrDefault(10);

        using var scope = logger.BeginEventScope("rx", "api.list",
            ("http.route", route),
            ("http.method", "GET"),
            ("paging.page", safePage),
            ("paging.page_size", safePageSize));

        return await TrackApiRequestAsync(route, "GET", logger, async () =>
        {
            var payload = await store.ListAsync(safePage, safePageSize);
            logger.LogInformation(
                "Prescription list returned {Count} items for page {Page}",
                payload.Items.Count,
                payload.Page);
            return Results.Ok(payload);
        });
    }

    private static async Task<IResult> ReadPrescriptionAsync(
        string rxId,
        string? faultMode,
        PrescriptionReadModelStore store,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("api");
        const string route = "/prescriptions/{id}";

        using var scope = logger.BeginEventScope("rx", "api.read",
            ("rx.id", rxId),
            ("http.route", route),
            ("http.method", "GET"),
            ("fault.mode", faultMode));

        return await TrackApiRequestAsync(route, "GET", logger, async () =>
        {
            await ApiFaults.ApplyAsync(faultMode, logger, CancellationToken.None);
            var payload = await store.GetAsync(rxId)
                ?? new PrescriptionReadModel(rxId, "Pending", 0, DateTimeOffset.UtcNow);
            logger.LogInformation("Prescription read succeeded for {RxId}", rxId);
            return Results.Ok(payload);
        });
    }

    private static async Task<IResult> ApprovePrescriptionAsync(
        string rxId,
        ApprovePayload payload,
        IRabbitMqPublisher publisher,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("api");
        const string route = "/prescriptions/{id}/approve";

        using var scope = logger.BeginEventScope("rx", "api.approve",
            ("rx.id", rxId),
            ("http.route", route),
            ("http.method", "POST"),
            ("fault.mode", payload.FaultMode));

        return await TrackApiRequestAsync(route, "POST", logger, async () =>
        {
            await ApiFaults.ApplyAsync(payload.FaultMode, logger, CancellationToken.None);

            if (string.IsNullOrWhiteSpace(payload.ApprovedBy))
                return Results.BadRequest(new { error = "ApprovedBy required" });

            await publisher.PublishAsync(RabbitMqTopology.CommandsExchange, new ApprovePrescriptionCommand(
                rxId,
                payload.ApprovedBy,
                DateTimeOffset.UtcNow,
                payload.Notes,
                payload.FaultMode));

            Metrics.RecordPublished(RabbitMqTopology.CommandsExchange, nameof(ApprovePrescriptionCommand));
            logger.LogInformation("Approve command queued for {RxId}", rxId);
            logger.LogPrescriptionAudit(
                "approve",
                rxId,
                payload.ApprovedBy,
                "accepted",
                ("messaging.exchange", RabbitMqTopology.CommandsExchange),
                ("messaging.message_type", nameof(ApprovePrescriptionCommand)));
            return Results.Accepted($"/prescriptions/{rxId}", new { rxId, status = "ApproveQueued" });
        });
    }

    private static async Task<IResult> RefillPrescriptionAsync(
        string rxId,
        RefillPayload payload,
        IRabbitMqPublisher publisher,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("api");
        const string route = "/prescriptions/{id}/refill";

        using var scope = logger.BeginEventScope("rx", "api.refill",
            ("rx.id", rxId),
            ("http.route", route),
            ("http.method", "POST"),
            ("fault.mode", payload.FaultMode));

        return await TrackApiRequestAsync(route, "POST", logger, async () =>
        {
            await ApiFaults.ApplyAsync(payload.FaultMode, logger, CancellationToken.None);

            if (payload.RefillCount < 1)
                return Results.BadRequest(new { error = "RefillCount must be >= 1" });

            await publisher.PublishAsync(RabbitMqTopology.CommandsExchange, new RefillRequestCommand(
                rxId,
                payload.RefillCount,
                DateTimeOffset.UtcNow,
                payload.FaultMode));

            Metrics.RecordPublished(RabbitMqTopology.CommandsExchange, nameof(RefillRequestCommand));
            logger.LogInformation("Refill command queued for {RxId}", rxId);
            logger.LogPrescriptionAudit(
                "refill",
                rxId,
                "rx-ui",
                "accepted",
                ("messaging.exchange", RabbitMqTopology.CommandsExchange),
                ("messaging.message_type", nameof(RefillRequestCommand)),
                ("refill.count", payload.RefillCount));
            return Results.Accepted($"/prescriptions/{rxId}", new { rxId, status = "RefillQueued" });
        });
    }

    private static async Task<IResult> TrackApiRequestAsync(
        string route,
        string method,
        ILogger logger,
        Func<Task<IResult>> handler)
    {
        var sw = Stopwatch.StartNew();
        var result = "ok";

        try
        {
            var response = await handler();
            if (response is IStatusCodeHttpResult { StatusCode: >= 400 and < 500 })
                result = "bad_request";
            return response;
        }
        catch (Exception ex)
        {
            result = "error";
            logger.LogError(ex, "Request failed for route {Route}", route);
            Metrics.RecordError("api-gateway", ex.GetType().Name);
            return Results.StatusCode(500);
        }
        finally
        {
            sw.Stop();
            Metrics.RecordApiRequest(route, method, result, sw.Elapsed.TotalMilliseconds);
        }
    }

    private static async Task RunCheckAsync(
        string name,
        IDictionary<string, string> checks,
        Func<Task> check)
    {
        try
        {
            await check();
            checks[name] = "ok";
        }
        catch (Exception ex)
        {
            checks[name] = ex.GetType().Name;
        }
    }
}

public record ApprovePayload(string ApprovedBy, string? Notes, string? FaultMode = null);
public record RefillPayload(int RefillCount = 1, string? FaultMode = null);
