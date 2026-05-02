namespace Infrastructure.Mqtt.Configuration;

public class MqttOptions
{
    public const string SectionName = "Mqtt";

    public string Broker { get; init; } = default!;
    public UInt16 Port { get; init; }
}
