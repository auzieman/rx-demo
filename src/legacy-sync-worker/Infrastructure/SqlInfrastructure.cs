using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Shared.Messaging;

namespace LegacySyncWorker.Infrastructure;

public interface IDbInfrastructure
{
    Task<int> UpsertApproveAsync(ApprovePrescriptionCommand cmd);
    Task<int> UpsertRefillAsync(RefillRequestCommand cmd);
}

public sealed class SqlInfrastructure : IDbInfrastructure
{
    private readonly string _cs;
    public SqlInfrastructure(IConfiguration cfg)
    {
        _cs = cfg.GetConnectionString("LegacyDb")
             ?? cfg["ConnectionStrings:LegacyDb"]
             ?? throw new InvalidOperationException("Missing LegacyDb connection string.");
    }

    public async Task<int> UpsertApproveAsync(ApprovePrescriptionCommand cmd)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();

        var p = new DynamicParameters();
        p.Add("@RxId", cmd.RxId, DbType.String);
        p.Add("@ApprovedBy", cmd.ApprovedBy, DbType.String);
        p.Add("@ApprovedAt", cmd.ApprovedAt, DbType.DateTimeOffset);

        var jsonPayload = System.Text.Json.JsonSerializer.Serialize(cmd);
        var bytes = System.Text.Encoding.UTF8.GetByteCount(jsonPayload);

        await conn.ExecuteAsync("[rx].[UpsertPrescriptionApprove]", p, commandType: CommandType.StoredProcedure);
        return bytes;
    }

    public async Task<int> UpsertRefillAsync(RefillRequestCommand cmd)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();

        var p = new DynamicParameters();
        p.Add("@RxId", cmd.RxId, DbType.String);
        p.Add("@RefillCount", cmd.RefillCount, DbType.Int32);
        p.Add("@RequestedAt", cmd.RequestedAt, DbType.DateTimeOffset);

        var jsonPayload = System.Text.Json.JsonSerializer.Serialize(cmd);
        var bytes = System.Text.Encoding.UTF8.GetByteCount(jsonPayload);

        await conn.ExecuteAsync("[rx].[UpsertPrescriptionRefill]", p, commandType: CommandType.StoredProcedure);
        return bytes;
    }
}
