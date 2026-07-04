using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Http.Headers;

// Shared libs
using Shared.Observability;   // AddOtel(...)
using Shared.Telemetry;

var builder = WebApplication.CreateBuilder(args);
builder.AddRxServiceDefaults("rx-ui");

// ----------------------------------------------------------------------------
// Razor Pages
// ----------------------------------------------------------------------------
builder.Services.AddRazorPages();

// ----------------------------------------------------------------------------
// Swagger (so UseSwagger() works in dev)
// ----------------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ----------------------------------------------------------------------------
// HttpClient to API Gateway
// ----------------------------------------------------------------------------
builder.Services.AddHttpClient<RxApiClient>((sp, client) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var baseUrl = cfg["RxApi:BaseUrl"] ?? "http://localhost:8080"; // override via env in K8s
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);

    client.DefaultRequestHeaders.Accept.Clear();
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    client.DefaultRequestHeaders.UserAgent.ParseAdd("rx-ui/1.0 (+api-gateway)");
})
// Either use the standard handler…
.AddStandardResilienceHandler();

// …or customize your own (example):
// .AddResilienceHandler("rx-ui", pipeline =>
// {
//     pipeline.AddRetry(new Polly.Retry.RetryStrategyOptions
//     {
//         MaxRetryAttempts = 3,
//         Delay = TimeSpan.FromMilliseconds(500),
//         BackoffType = Polly.Retry.BackoffType.Exponential
//     });
//     pipeline.AddTimeout(TimeSpan.FromSeconds(10));
//     pipeline.AddCircuitBreaker(new Polly.CircuitBreaker.CircuitBreakerStrategyOptions
//     {
//         FailureRatio = 0.1,
//         MinimumThroughput = 20,
//         BreakDuration = TimeSpan.FromSeconds(30)
//     });
// });

// ----------------------------------------------------------------------------
// CORS (dev-only)
// ----------------------------------------------------------------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("dev", p => p
        .AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

// ----------------------------------------------------------------------------
// Middleware
// ----------------------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseCors("dev");

    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.DocumentTitle = "Rx UI";
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Rx UI v1");
    });
}

app.UseStaticFiles();
app.UseRouting();

app.MapGet("/healthz", () => Results.Ok("Healthy"));
app.MapGet("/readyz", async (RxApiClient api, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("ui.health");
    try
    {
        await api.CheckReadyAsync(cancellationToken);
        return Results.Ok(new { status = "ready", checks = new { api = "ok" } });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "UI readiness failed.");
        return Results.Json(
            new { status = "not_ready", checks = new { api = ex.GetType().Name } },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/browser-event", (
    BrowserUiEvent browserEvent,
    HttpContext httpContext,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    cancellationToken.ThrowIfCancellationRequested();
    var logger = loggerFactory.CreateLogger("ui.browser");
    var route = "/browser-event";
    var name = string.IsNullOrWhiteSpace(browserEvent.Name) ? "ui.browser.event" : browserEvent.Name.Trim();
    var page = string.IsNullOrWhiteSpace(browserEvent.Page) ? "Index" : browserEvent.Page.Trim();
    var action = string.IsNullOrWhiteSpace(browserEvent.Action) ? null : browserEvent.Action.Trim();
    var rxId = string.IsNullOrWhiteSpace(browserEvent.RxId) ? null : browserEvent.RxId.Trim();
    var selectedCount = browserEvent.SelectedCount.GetValueOrDefault();
    var durationMs = browserEvent.DurationMs.GetValueOrDefault();

    using var scope = logger.BeginEventScope("rx", name,
        ("ui.page", page),
        ("ui.action", action),
        ("ui.component", browserEvent.Component),
        ("ui.selected_count", selectedCount),
        ("ui.duration_ms", durationMs),
        ("rx.id", rxId),
        ("http.route", route),
        ("browser.user_agent", httpContext.Request.Headers.UserAgent.ToString()),
        ("client.ip", httpContext.Connection.RemoteIpAddress?.ToString()));

    var detailCount = browserEvent.Details?.Count ?? 0;
    logger.LogInformation(
        "Browser UI event {EventName} action={Action} rxId={RxId} selected={SelectedCount} details={DetailCount}",
        name,
        action,
        rxId,
        selectedCount,
        detailCount);

    Metrics.RecordApiRequest(route, "POST", "ok", Math.Max(0, durationMs));
    return Results.Accepted(route, new { status = "accepted", name });
});

app.MapRazorPages();

app.Run();

// ----------------------------------------------------------------------------
// Typed API Client
// ----------------------------------------------------------------------------
public class RxApiClient
{
    private readonly HttpClient _http;
    public RxApiClient(HttpClient http) => _http = http;

    public record RxInfo(string RxId, string Status, int Version, DateTimeOffset ChangedAt);
    public record RxListPage(IReadOnlyList<RxInfo> Items, long Total, int Page, int PageSize);
    public record ApprovePayload(string ApprovedBy, string? Notes);
    public record RefillPayload(int RefillCount);

    public async Task<RxListPage?> GetPrescriptionsAsync(int page, int pageSize, CancellationToken ct = default)
        => await _http.GetFromJsonAsync<RxListPage>($"/prescriptions?page={page}&pageSize={pageSize}", ct);

    public async Task CheckReadyAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("/readyz", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<RxInfo?> GetPrescriptionAsync(string rxId, CancellationToken ct = default)
        => await _http.GetFromJsonAsync<RxInfo>($"/prescriptions/{Uri.EscapeDataString(rxId)}", ct);

    public async Task<HttpResponseMessage> ApproveAsync(string rxId, string approvedBy, string? notes, CancellationToken ct = default)
        => await _http.PostAsJsonAsync($"/prescriptions/{Uri.EscapeDataString(rxId)}/approve",
            new ApprovePayload(approvedBy, notes), ct);

    public async Task<HttpResponseMessage> RefillAsync(string rxId, int refillCount, CancellationToken ct = default)
        => await _http.PostAsJsonAsync($"/prescriptions/{Uri.EscapeDataString(rxId)}/refill",
            new RefillPayload(refillCount), ct);
}

public sealed record BrowserUiEvent(
    string? Name,
    string? Page,
    string? Component,
    string? Action,
    string? RxId,
    int? SelectedCount,
    double? DurationMs,
    Dictionary<string, string>? Details);
