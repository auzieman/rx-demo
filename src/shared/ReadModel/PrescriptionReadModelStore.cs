using System.Text.Json;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace Shared.ReadModel;

public sealed record PrescriptionReadModel(
    string RxId,
    string Status,
    int Version,
    DateTimeOffset ChangedAt);

public sealed record PrescriptionListPage(
    IReadOnlyList<PrescriptionReadModel> Items,
    long Total,
    int Page,
    int PageSize);

public sealed class PrescriptionReadModelStore
{
    private const string IndexKey = "rx:index";
    private const string KeyPrefix = "rx:";

    private readonly ConnectionMultiplexer _mux;

    public PrescriptionReadModelStore(IConfiguration cfg)
    {
        _mux = ConnectionMultiplexer.Connect(cfg["Cache:Redis"] ?? "localhost:6379");
    }

    public async Task UpsertAsync(PrescriptionReadModel model, TimeSpan ttl)
    {
        var db = _mux.GetDatabase();
        var key = GetKey(model.RxId);
        var json = JsonSerializer.Serialize(model);

        await db.StringSetAsync(key, json, ttl);
        await db.SortedSetAddAsync(IndexKey, model.RxId, model.ChangedAt.ToUnixTimeMilliseconds());
    }

    public async Task<PrescriptionReadModel?> GetAsync(string rxId)
    {
        var db = _mux.GetDatabase();
        var value = await db.StringGetAsync(GetKey(rxId));
        if (!value.HasValue)
            return null;

        return JsonSerializer.Deserialize<PrescriptionReadModel>(value.ToString());
    }

    public async Task<PrescriptionListPage> ListAsync(int page, int pageSize)
    {
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 100);
        var db = _mux.GetDatabase();

        var start = (safePage - 1) * safePageSize;
        var stop = start + safePageSize - 1;

        var ids = await db.SortedSetRangeByRankAsync(IndexKey, start, stop, Order.Descending);
        var items = new List<PrescriptionReadModel>(ids.Length);

        foreach (var id in ids)
        {
            if (!id.HasValue)
                continue;

            var item = await GetAsync(id!);
            if (item is not null)
                items.Add(item);
        }

        var total = await db.SortedSetLengthAsync(IndexKey);
        return new PrescriptionListPage(items, total, safePage, safePageSize);
    }

    public async Task PingAsync()
    {
        var db = _mux.GetDatabase();
        await db.PingAsync();
    }

    private static string GetKey(string rxId) => $"{KeyPrefix}{rxId}";
}
