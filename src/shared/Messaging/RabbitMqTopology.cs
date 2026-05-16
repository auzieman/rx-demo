namespace Shared.Messaging;

public static class RabbitMqTopology
{
    public const string CommandsExchange = "rx.commands";
    public const string EventsExchange = "rx.events";

    public const string DefaultCommandsQueue = "rx.commands";
    public const string DefaultEventsQueue = "rx.events";

    public static string RoutingKeyFor<T>() => typeof(T).Name;
}
