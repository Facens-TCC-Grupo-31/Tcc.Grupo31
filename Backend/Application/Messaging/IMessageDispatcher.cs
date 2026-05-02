namespace Application.Messaging;

public interface IMessageDispatcher
{
    Task DispatchAsync(string topic, string payload, CancellationToken cancellationToken);
}
