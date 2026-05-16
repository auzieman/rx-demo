using ApiGateway.Endpoints;
using MassTransit;
using Shared.Messaging;
using Shared.Observability;
using Shared.ReadModel;

var builder = WebApplication.CreateBuilder(args);
builder.AddRxServiceDefaults("api-gateway");
builder.Services.AddSingleton<PrescriptionReadModelStore>();

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((_, cfg) =>
    {
        var rabbit = RabbitMqSettings.FromConfiguration(builder.Configuration);
        cfg.Host(rabbit.HostName, rabbit.Port, rabbit.VirtualHost, host =>
        {
            host.Username(rabbit.Username);
            host.Password(rabbit.Password);
        });
    });
});

var app = builder.Build();

app.MapPrescriptionEndpoints();
app.MapHealthEndpoints();

app.Run();
