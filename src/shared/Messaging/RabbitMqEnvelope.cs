namespace Shared.Messaging;

public sealed record RabbitMqEnvelope<T>(
    string MessageId,
    string MessageType,
    DateTimeOffset CreatedAt,
    T Payload);
