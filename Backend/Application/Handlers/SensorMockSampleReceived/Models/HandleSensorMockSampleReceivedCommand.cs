using MediatR;

namespace Application.Handlers.SensorMockSampleReceived.Models;

public record HandleSensorMockSampleReceivedCommand(string Payload) : IRequest;
