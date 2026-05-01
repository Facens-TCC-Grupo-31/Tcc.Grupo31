using Application.Handlers.SensorSampleReceived.Models;
using MediatR;

namespace Application.Handlers.SensorSampleReceived;

public interface ISensorSampleReceivedHandler : IRequestHandler<HandleSensorSampleReceivedCommand>;
