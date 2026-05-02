using Application.Handlers.RegisterMessageReceived;
using Application.Messaging;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Application.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(HandleRegisterMessageReceivedCommand).Assembly));

        services.AddSingleton<IMessageDispatcher, MessageDispatcher>();

        return services;
    }
}
