// /src/legacy-sync-worker/Infrastructure/Migrations.cs
using System.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;

namespace LegacySyncWorker.Infrastructure;

public sealed class DbMigrations
{
    private readonly string _cs;               // full target-DB connection string
    private readonly string _dbName;           // parsed from _cs
    private readonly string _masterCs;         // connection string to master (same server/creds)
    private readonly ILogger<DbMigrations> _logger;

    public DbMigrations(IConfiguration cfg, ILogger<DbMigrations> logger)
    {
        _cs = cfg.GetConnectionString("LegacyDb")
             ?? cfg["ConnectionStrings:LegacyDb"]
             ?? throw new InvalidOperationException("Missing LegacyDb connection string.");

        var sb = new SqlConnectionStringBuilder(_cs);

        _dbName = string.IsNullOrWhiteSpace(sb.InitialCatalog)
            ? throw new InvalidOperationException("LegacyDb connection string must include Initial Catalog (database name).")
            : sb.InitialCatalog;

        // Build a 'master' connection string using the same server & credentials.
        var master = new SqlConnectionStringBuilder(sb.ConnectionString)
        {
            InitialCatalog = "master"
        };
        _masterCs = master.ConnectionString;

        _logger = logger;
    }

    public async Task RunAsync()
    {
        // 1) Ensure the target database exists (connect to master)
        await EnsureDatabaseExistsAsync();

        // 2) Connect to the target DB and apply the schema you already had
        using var conn = new SqlConnection(_cs);
        await OpenWithRetryAsync(conn);

        // Ensure schema rx exists
        await new SqlCommand(
            "IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'rx') EXEC('CREATE SCHEMA rx');",
            conn).ExecuteNonQueryAsync();

        var ddl = @"
IF OBJECT_ID('rx.Pharmacy','U') IS NULL
CREATE TABLE rx.Pharmacy (
  PharmacyId      INT IDENTITY(1,1) PRIMARY KEY,
  Name            NVARCHAR(200) NOT NULL,
  AddressLine1    NVARCHAR(200) NULL,
  City            NVARCHAR(100) NULL,
  State           NVARCHAR(10)  NULL,
  Zip             NVARCHAR(10)  NULL,
  Lat             FLOAT NULL,
  Lng             FLOAT NULL,
  CreatedAt       DATETIMEOFFSET(7) NOT NULL DEFAULT SYSDATETIMEOFFSET()
);

IF OBJECT_ID('rx.Prescription','U') IS NULL
CREATE TABLE rx.Prescription (
  RxId             NVARCHAR(64) PRIMARY KEY,
  PatientId        NVARCHAR(64) NULL,
  DrugCode         NVARCHAR(64) NULL,
  Quantity         INT          NULL,
  RefillsRemaining INT          NULL,
  Status           NVARCHAR(32) NOT NULL DEFAULT 'Pending',
  LastUpdated      DATETIMEOFFSET(7) NOT NULL DEFAULT SYSDATETIMEOFFSET()
);

IF OBJECT_ID('rx.PrescriptionEvent','U') IS NULL
CREATE TABLE rx.PrescriptionEvent (
  EventId    INT IDENTITY(1,1) PRIMARY KEY,
  RxId       NVARCHAR(64) NOT NULL,
  EventType  NVARCHAR(64) NOT NULL,
  OccurredAt DATETIMEOFFSET(7) NOT NULL,
  Payload    NVARCHAR(MAX) NULL
);

-- Approve SP
IF OBJECT_ID('rx.UpsertPrescriptionApprove','P') IS NULL
EXEC('CREATE PROCEDURE rx.UpsertPrescriptionApprove @RxId NVARCHAR(64), @ApprovedBy NVARCHAR(64), @ApprovedAt DATETIMEOFFSET(7) AS
BEGIN
  SET NOCOUNT ON;
  IF NOT EXISTS (SELECT 1 FROM rx.Prescription WHERE RxId=@RxId)
    INSERT INTO rx.Prescription (RxId, Status, LastUpdated) VALUES (@RxId, ''Pending'', SYSDATETIMEOFFSET());
  UPDATE rx.Prescription SET Status=''Approved'', LastUpdated = @ApprovedAt WHERE RxId=@RxId;
  INSERT INTO rx.PrescriptionEvent (RxId, EventType, OccurredAt, Payload) VALUES (@RxId, ''Approved'', @ApprovedAt, NULL);
END');

-- Refill SP
IF OBJECT_ID('rx.UpsertPrescriptionRefill','P') IS NULL
EXEC('CREATE PROCEDURE rx.UpsertPrescriptionRefill @RxId NVARCHAR(64), @RefillCount INT, @RequestedAt DATETIMEOFFSET(7) AS
BEGIN
  SET NOCOUNT ON;
  IF NOT EXISTS (SELECT 1 FROM rx.Prescription WHERE RxId=@RxId)
    INSERT INTO rx.Prescription (RxId, Status, RefillsRemaining, LastUpdated) VALUES (@RxId, ''Pending'', 0, SYSDATETIMEOFFSET());
  UPDATE rx.Prescription
    SET RefillsRemaining = ISNULL(RefillsRemaining,0) + @RefillCount,
        Status=''Refilled'',
        LastUpdated = @RequestedAt
    WHERE RxId=@RxId;
  INSERT INTO rx.PrescriptionEvent (RxId, EventType, OccurredAt, Payload) VALUES (@RxId, ''Refilled'', @RequestedAt, NULL);
END');
";

        await new SqlCommand(ddl, conn).ExecuteNonQueryAsync();
        _logger.LogInformation("rx schema initialized (Pharmacy, Prescription, PrescriptionEvent, SPs)");
    }

    private async Task EnsureDatabaseExistsAsync()
    {
        using var master = new SqlConnection(_masterCs);
        await OpenWithRetryAsync(master);

        // Check existence
        using var check = new SqlCommand("SELECT DB_ID(@db)", master);
        check.Parameters.Add(new SqlParameter("@db", SqlDbType.NVarChar, 128) { Value = _dbName });
        var exists = await check.ExecuteScalarAsync();

        if (exists is null || exists == DBNull.Value)
        {
            _logger.LogInformation("Database '{DbName}' not found. Creating…", _dbName);
            using var create = new SqlCommand($"CREATE DATABASE [{_dbName}]", master);
            await create.ExecuteNonQueryAsync();
            _logger.LogInformation("Database '{DbName}' created.", _dbName);
        }
        else
        {
            _logger.LogInformation("Database '{DbName}' already exists.", _dbName);
        }
    }

    private static async Task OpenWithRetryAsync(SqlConnection conn)
    {
        var delay = TimeSpan.FromSeconds(2);
        for (var attempt = 1; attempt <= 30; attempt++)
        {
            try { await conn.OpenAsync(); return; }
            catch (SqlException)
            {
                await Task.Delay(delay);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 1.5, 10));
            }
        }
        throw new TimeoutException("Unable to connect to SQL Server after retries.");
    }
}

public sealed class DbMigrationsHostedService : IHostedService
{
    private readonly DbMigrations _m;
    public DbMigrationsHostedService(DbMigrations m) { _m = m; }
    public async Task StartAsync(CancellationToken ct) => await _m.RunAsync();
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
