// /src/read-model-projection/Program.cs
using MassTransit;
using Shared.Observability;
using Shared.Messaging;
using Shared.ReadModel;

var builder = Host.CreateApplicationBuilder(args);
var config = builder.Configuration;
builder.AddRxServiceDefaults("read-model-projection");
builder.Services.AddSingleton<PrescriptionReadModelStore>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<ProjectionConsumer>();
    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbit = RabbitMqSettings.FromConfiguration(config);
        cfg.Host(rabbit.HostName, rabbit.Port, rabbit.VirtualHost, host =>
        {
            host.Username(rabbit.Username);
            host.Password(rabbit.Password);
        });
        cfg.ReceiveEndpoint(config["Messaging:EventsQueue"] ?? "rx.events", e =>
        {
            e.ConfigureConsumer<ProjectionConsumer>(context);
        });
    });
});

var host = builder.Build();
await host.RunAsync();
