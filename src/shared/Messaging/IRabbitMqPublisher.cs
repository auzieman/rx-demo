namespace Shared.Messaging;

public interface IRabbitMqPublisher : IAsyncDisposable
{
    Task PublishAsync<T>(
        string exchange,
        T message,
        CancellationToken cancellationToken = default)
        where T : class;
}
