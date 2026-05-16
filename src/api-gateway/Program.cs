using ApiGateway.Endpoints;
using Shared.Messaging;
using Shared.Observability;
using Shared.ReadModel;

var builder = WebApplication.CreateBuilder(args);
builder.AddRxServiceDefaults("api-gateway");
builder.Services.AddSingleton<PrescriptionReadModelStore>();
builder.Services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();

var app = builder.Build();

app.MapPrescriptionEndpoints();
app.MapHealthEndpoints();

app.Run();
