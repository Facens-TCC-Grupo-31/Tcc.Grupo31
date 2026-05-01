using MediatR;

namespace Application.Handlers.RegisterMessageReceived;

public record HandleRegisterMessageReceivedCommand(string Payload) : IRequest;
