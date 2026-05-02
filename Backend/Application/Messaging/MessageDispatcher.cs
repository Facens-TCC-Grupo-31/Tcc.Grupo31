using Application.Common.Constants;
using Application.Handlers.RegisterMessageReceived;
using Application.Handlers.SensorSampleReceived.Models;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Messaging;

public sealed class MessageDispatcher(
    IServiceScopeFactory scopeFactory,
    ILogger<MessageDispatcher> logger) : IMessageDispatcher
{
    public async Task DispatchAsync(string topic, string payload, CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IMediator mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        switch (topic)
        {
            case MqttTopics.Register:
                await mediator.Send(new HandleRegisterMessageReceivedCommand(payload), cancellationToken);
                break;

            case MqttTopics.Samples:
                await mediator.Send(new HandleSensorSampleReceivedCommand(payload), cancellationToken);
                break;

            default:
                logger.LogWarning("Ignoring message on unknown topic {Topic}", topic);
                break;
        }
    }
}
