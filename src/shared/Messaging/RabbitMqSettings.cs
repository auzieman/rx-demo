using System.Net.Sockets;
using Microsoft.Extensions.Configuration;

namespace Shared.Messaging;

public sealed record RabbitMqSettings(
    string HostName,
    ushort Port,
    string VirtualHost,
    string Username,
    string Password)
{
    public static RabbitMqSettings FromConfiguration(IConfiguration config) => new(
        config["Messaging:HostName"] ?? "rabbitmq",
        ushort.Parse(config["Messaging:Port"] ?? "5672"),
        config["Messaging:VirtualHost"] ?? "/",
        config["Messaging:Username"] ?? "guest",
        config["Messaging:Password"] ?? "guest");
}

public static class RabbitMqDependencyCheck
{
    public static async Task CheckTcpAsync(IConfiguration config, CancellationToken cancellationToken)
    {
        var settings = RabbitMqSettings.FromConfiguration(config);
        using var client = new TcpClient();
        await client.ConnectAsync(settings.HostName, settings.Port, cancellationToken);
    }
}
