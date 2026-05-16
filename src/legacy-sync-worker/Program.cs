// /src/legacy-sync-worker/Program.cs
using MassTransit;
using Shared.Observability;
using Shared.Messaging;
using LegacySyncWorker.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
var config  = builder.Configuration;
builder.AddRxServiceDefaults("legacy-sync-worker");

// Infra services
builder.Services.AddSingleton<IDbInfrastructure, SqlInfrastructure>();
builder.Services.AddSingleton<DbMigrations>();

// Register this before MassTransit so SQL is ready before queue consumers attach.
builder.Services.AddHostedService<DbMigrationsHostedService>();

// Message bus registration is isolated behind shared settings so the transport can be swapped later.
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<ApproveConsumer>();
    x.AddConsumer<RefillConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbit = RabbitMqSettings.FromConfiguration(config);
        cfg.Host(rabbit.HostName, rabbit.Port, rabbit.VirtualHost, host =>
        {
            host.Username(rabbit.Username);
            host.Password(rabbit.Password);
        });

        // Global protection
        cfg.UseKillSwitch(o => o
            .SetActivationThreshold(20)
            .SetTripThreshold(0.20)
            .SetRestartTimeout(m: 1));

        cfg.UseCircuitBreaker(cb =>
        {
            cb.ActiveThreshold = 5;
            cb.TrackingPeriod  = TimeSpan.FromSeconds(30);
            cb.ResetInterval   = TimeSpan.FromSeconds(60);
            cb.TripThreshold   = 15;
        });

        cfg.ReceiveEndpoint(config["Messaging:QueueName"] ?? "rx.commands", e =>
        {
            e.UseConsumeFilter(typeof(MessageValidationFilter<>), context);

            e.UseMessageRetry(r =>
            {
                r.Handle<TimeoutException>();
                r.Handle<OperationCanceledException>();
                r.Ignore<System.ComponentModel.DataAnnotations.ValidationException>();
                r.Intervals(TimeSpan.FromSeconds(1),
                            TimeSpan.FromSeconds(5),
                            TimeSpan.FromSeconds(10));
            });

            e.UseDelayedRedelivery(r => r.Intervals(
                TimeSpan.FromSeconds(30),
                TimeSpan.FromMinutes(2)));

            e.UseInMemoryOutbox(context);

            e.ConfigureConsumer<ApproveConsumer>(context);
            e.ConfigureConsumer<RefillConsumer>(context);
        });
    });
});

var host = builder.Build();
await host.RunAsync();
