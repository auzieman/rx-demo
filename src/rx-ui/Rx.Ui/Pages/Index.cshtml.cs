using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using Shared.Observability;
using Shared.Telemetry;

[IgnoreAntiforgeryToken]
public class IndexModel : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly RxApiClient _api;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(RxApiClient api, ILogger<IndexModel> logger)
    {
        _api = api;
        _logger = logger;
    }

    [BindProperty]
    public string RxId { get; set; } = "RX-12345";

    [BindProperty]
    public int RefillCount { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 10;

    public string? Message { get; set; }
    public RxApiClient.RxListPage? ListPage { get; set; }
    public string? ListError { get; set; }
    public bool HasListError => !string.IsNullOrWhiteSpace(ListError);
    public bool HasListItems => ListPage?.Items?.Any() == true;
    public int[] PageSizeOptions { get; } = new[] { 10, 20, 50 };
    public int TotalPages => ListPage is null || ListPage.PageSize <= 0
        ? 1
        : Math.Max(1, (int)Math.Ceiling(ListPage.Total / (double)ListPage.PageSize));

    public Task<IActionResult> OnPostGet()
    {
        return OnPostLookup();
    }

    public async Task<IActionResult> OnPostLookup()
    {
        var request = await ReadOperationRequestAsync();
        ApplyOperationRequest(request);
        var route = CurrentRoute("/lookup");

        using var scope = _logger.BeginEventScope("rx", "ui.read",
            ("rx.id", RxId),
            ("ui.page", "Index"),
            ("ui.route", route),
            ("http.route", route));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = "ok";
        object? payload = null;

        try
        {
            var info = await _api.GetPrescriptionAsync(RxId);
            payload = info;
            Message = JsonSerializer.Serialize(info, JsonOptions);
            _logger.LogInformation("UI read completed for {RxId}", RxId);
        }
        catch (Exception ex)
        {
            result = "error";
            _logger.LogError(ex, "UI read failed for {RxId}", RxId);
            Metrics.RecordError("rx-ui", ex.GetType().Name);
            Message = ex.Message;
        }
        finally
        {
            sw.Stop();
            Metrics.RecordApiRequest(route, "POST", result, sw.Elapsed.TotalMilliseconds);
        }

        if (WantsJsonResponse(request))
        {
            return JsonOperationResult("lookup", route, result, payload);
        }

        await LoadListAsync(route);
        return Page();
    }

    public async Task<IActionResult> OnPostApprove()
    {
        var request = await ReadOperationRequestAsync();
        ApplyOperationRequest(request);
        var approvedBy = string.IsNullOrWhiteSpace(request?.ApprovedBy) ? "demo.user" : request.ApprovedBy;
        var notes = string.IsNullOrWhiteSpace(request?.Notes) ? "UI approve" : request.Notes;
        var route = CurrentRoute("/approve");

        using var scope = _logger.BeginEventScope("rx", "ui.approve",
            ("rx.id", RxId),
            ("ui.page", "Index"),
            ("ui.route", route),
            ("http.route", route),
            ("approval.user", approvedBy));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = "ok";
        int? upstreamStatus = null;
        string? body = null;

        try
        {
            var res = await _api.ApproveAsync(RxId, approvedBy: approvedBy, notes: notes);
            upstreamStatus = (int)res.StatusCode;
            body = await res.Content.ReadAsStringAsync();
            result = res.IsSuccessStatusCode ? "ok" : $"http_{(int)res.StatusCode}";
            Message = $"Status: {(int)res.StatusCode} {res.StatusCode}\n\n{body}";
            _logger.LogInformation("UI approve completed for {RxId} with {StatusCode}", RxId, (int)res.StatusCode);
        }
        catch (Exception ex)
        {
            result = "error";
            _logger.LogError(ex, "UI approve failed for {RxId}", RxId);
            Metrics.RecordError("rx-ui", ex.GetType().Name);
            Message = ex.Message;
        }
        finally
        {
            sw.Stop();
            Metrics.RecordApiRequest(route, "POST", result, sw.Elapsed.TotalMilliseconds);
        }

        if (WantsJsonResponse(request))
        {
            return JsonOperationResult("approve", route, result, body, upstreamStatus);
        }

        await LoadListAsync(route);
        return Page();
    }

    public async Task<IActionResult> OnPostRefill()
    {
        var request = await ReadOperationRequestAsync();
        ApplyOperationRequest(request);
        var route = CurrentRoute("/refill");

        using var scope = _logger.BeginEventScope("rx", "ui.refill",
            ("rx.id", RxId),
            ("ui.page", "Index"),
            ("ui.route", route),
            ("http.route", route),
            ("refill.count", RefillCount));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = "ok";
        int? upstreamStatus = null;
        string? body = null;

        try
        {
            var res = await _api.RefillAsync(RxId, RefillCount);
            upstreamStatus = (int)res.StatusCode;
            body = await res.Content.ReadAsStringAsync();
            result = res.IsSuccessStatusCode ? "ok" : $"http_{(int)res.StatusCode}";
            Message = $"Status: {(int)res.StatusCode} {res.StatusCode}\n\n{body}";
            _logger.LogInformation("UI refill completed for {RxId} with {StatusCode}", RxId, (int)res.StatusCode);
        }
        catch (Exception ex)
        {
            result = "error";
            _logger.LogError(ex, "UI refill failed for {RxId}", RxId);
            Metrics.RecordError("rx-ui", ex.GetType().Name);
            Message = ex.Message;
        }
        finally
        {
            sw.Stop();
            Metrics.RecordApiRequest(route, "POST", result, sw.Elapsed.TotalMilliseconds);
        }

        if (WantsJsonResponse(request))
        {
            return JsonOperationResult("refill", route, result, body, upstreamStatus);
        }

        await LoadListAsync(route);
        return Page();
    }

    public async Task OnGet()
    {
        await LoadListAsync(CurrentRoute("/"));
    }

    public async Task OnGetLookup()
    {
        await LoadListAsync(CurrentRoute("/lookup"));
    }

    public async Task OnGetApprove()
    {
        await LoadListAsync(CurrentRoute("/approve"));
    }

    public async Task OnGetRefill()
    {
        await LoadListAsync(CurrentRoute("/refill"));
    }

    private async Task LoadListAsync(string route)
    {
        using var scope = _logger.BeginEventScope("rx", "ui.list",
            ("ui.page", "Index"),
            ("ui.route", route),
            ("http.route", route),
            ("paging.page", PageNumber),
            ("paging.page_size", PageSize));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = "ok";

        try
        {
            ListPage = await _api.GetPrescriptionsAsync(PageNumber, PageSize)
                ?? new RxApiClient.RxListPage(Array.Empty<RxApiClient.RxInfo>(), 0, PageNumber, PageSize);
            ListError = null;
            _logger.LogInformation("UI list loaded {Count} items for page {Page}", ListPage.Items.Count, ListPage.Page);
        }
        catch (Exception ex)
        {
            result = "error";
            _logger.LogError(ex, "UI list failed for page {Page}", PageNumber);
            Metrics.RecordError("rx-ui", ex.GetType().Name);
            Message ??= ex.Message;
            ListError = $"{ex.GetType().Name}: {ex.Message}";
            ListPage = new RxApiClient.RxListPage(Array.Empty<RxApiClient.RxInfo>(), 0, PageNumber, PageSize);
        }
        finally
        {
            sw.Stop();
            Metrics.RecordApiRequest(route, "GET", result, sw.Elapsed.TotalMilliseconds);
        }
    }

    private async Task<UiOperationRequest?> ReadOperationRequestAsync()
    {
        if (!IsJsonRequest())
        {
            return null;
        }

        try
        {
            return await JsonSerializer.DeserializeAsync<UiOperationRequest>(Request.Body, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON UI operation payload");
            Message = ex.Message;
            return null;
        }
    }

    private void ApplyOperationRequest(UiOperationRequest? request)
    {
        if (request is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.RxId))
        {
            RxId = request.RxId;
        }

        if (request.RefillCount is > 0)
        {
            RefillCount = request.RefillCount.Value;
        }
    }

    private bool WantsJsonResponse(UiOperationRequest? request)
    {
        return request is not null
            || IsJsonRequest()
            || Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsJsonRequest()
    {
        return Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true;
    }

    private string CurrentRoute(string fallback)
    {
        var path = Request.Path.Value;
        return string.IsNullOrWhiteSpace(path) ? fallback : path;
    }

    private JsonResult JsonOperationResult(
        string operation,
        string route,
        string result,
        object? payload,
        int? upstreamStatusCode = null)
    {
        var ok = result == "ok";
        return new JsonResult(new
        {
            operation,
            route,
            rxId = RxId,
            refillCount = RefillCount,
            result,
            message = Message,
            upstreamStatusCode,
            payload
        })
        {
            StatusCode = ok ? StatusCodes.Status200OK : StatusCodes.Status502BadGateway
        };
    }

    private sealed record UiOperationRequest(
        string? RxId,
        int? RefillCount,
        string? ApprovedBy,
        string? Notes);
}
