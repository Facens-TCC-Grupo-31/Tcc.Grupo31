using MediatR;

namespace Application.Handlers.RegisterMessageReceived;

public interface IRegisterMessageReceivedHandler : IRequestHandler<HandleRegisterMessageReceivedCommand>;
