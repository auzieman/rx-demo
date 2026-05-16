using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using Shared.Observability;
using Shared.Telemetry;

public class IndexModel : PageModel
{
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
    public int[] PageSizeOptions { get; } = new[] { 10, 20, 50 };
    public int TotalPages => ListPage is null || ListPage.PageSize <= 0
        ? 1
        : Math.Max(1, (int)Math.Ceiling(ListPage.Total / (double)ListPage.PageSize));

    public async Task<IActionResult> OnPostGet()
    {
        using var scope = _logger.BeginEventScope("rx", "ui.read",
            ("rx.id", RxId),
            ("ui.page", "Index"));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = "ok";

        try
        {
            var info = await _api.GetPrescriptionAsync(RxId);
            Message = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
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
            Metrics.RecordApiRequest("/ui/detail", "GET", result, sw.Elapsed.TotalMilliseconds);
        }

        await LoadListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostApprove()
    {
        using var scope = _logger.BeginEventScope("rx", "ui.approve",
            ("rx.id", RxId),
            ("ui.page", "Index"));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = "ok";

        try
        {
            var res = await _api.ApproveAsync(RxId, approvedBy: "demo.user", notes: "UI approve");
            var body = await res.Content.ReadAsStringAsync();
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
            Metrics.RecordApiRequest("/ui/approve", "POST", result, sw.Elapsed.TotalMilliseconds);
        }

        await LoadListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRefill()
    {
        using var scope = _logger.BeginEventScope("rx", "ui.refill",
            ("rx.id", RxId),
            ("ui.page", "Index"),
            ("refill.count", RefillCount));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = "ok";

        try
        {
            var res = await _api.RefillAsync(RxId, RefillCount);
            var body = await res.Content.ReadAsStringAsync();
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
            Metrics.RecordApiRequest("/ui/refill", "POST", result, sw.Elapsed.TotalMilliseconds);
        }

        await LoadListAsync();
        return Page();
    }

    public async Task OnGet()
    {
        await LoadListAsync();
    }

    private async Task LoadListAsync()
    {
        using var scope = _logger.BeginEventScope("rx", "ui.list",
            ("ui.page", "Index"),
            ("paging.page", PageNumber),
            ("paging.page_size", PageSize));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = "ok";

        try
        {
            ListPage = await _api.GetPrescriptionsAsync(PageNumber, PageSize)
                ?? new RxApiClient.RxListPage(Array.Empty<RxApiClient.RxInfo>(), 0, PageNumber, PageSize);
            _logger.LogInformation("UI list loaded {Count} items for page {Page}", ListPage.Items.Count, ListPage.Page);
        }
        catch (Exception ex)
        {
            result = "error";
            _logger.LogError(ex, "UI list failed for page {Page}", PageNumber);
            Metrics.RecordError("rx-ui", ex.GetType().Name);
            Message ??= ex.Message;
            ListPage = new RxApiClient.RxListPage(Array.Empty<RxApiClient.RxInfo>(), 0, PageNumber, PageSize);
        }
        finally
        {
            sw.Stop();
            Metrics.RecordApiRequest("/ui/list", "GET", result, sw.Elapsed.TotalMilliseconds);
        }
    }
}
