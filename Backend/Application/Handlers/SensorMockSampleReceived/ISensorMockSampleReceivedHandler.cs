using Application.Handlers.SensorMockSampleReceived.Models;
using MediatR;

namespace Application.Handlers.SensorMockSampleReceived;

public interface ISensorMockSampleReceivedHandler : IRequestHandler<HandleSensorMockSampleReceivedCommand>;
