using MediatR;

namespace Application.Handlers.SensorSampleReceived.Models;

public record HandleSensorSampleReceivedCommand(string Payload) : IRequest;
