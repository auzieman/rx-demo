using Shared.Observability;
using Shared.Messaging;
using LegacySyncWorker.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
var config  = builder.Configuration;
builder.AddRxServiceDefaults("legacy-sync-worker");

// Infra services
builder.Services.AddSingleton<IDbInfrastructure, SqlInfrastructure>();
builder.Services.AddSingleton<DbMigrations>();

// Register this before the RabbitMQ consumer so SQL is ready before deliveries start.
builder.Services.AddHostedService<DbMigrationsHostedService>();
builder.Services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();
builder.Services.AddHostedService<CommandWorker>();

var host = builder.Build();
await host.RunAsync();
