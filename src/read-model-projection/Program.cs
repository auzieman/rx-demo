using Shared.Observability;
using Shared.Messaging;
using Shared.ReadModel;

var builder = Host.CreateApplicationBuilder(args);
var config = builder.Configuration;
builder.AddRxServiceDefaults("read-model-projection");
builder.Services.AddSingleton<PrescriptionReadModelStore>();
builder.Services.AddHostedService<ProjectionWorker>();

var host = builder.Build();
await host.RunAsync();
