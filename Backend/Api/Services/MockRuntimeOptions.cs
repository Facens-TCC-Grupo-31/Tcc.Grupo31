namespace Api.Services;

public sealed class MockRuntimeOptions
{
    public const string SectionName = "MockRuntime";

    public bool Enabled { get; init; }
    public string ExecutablePath { get; init; } = string.Empty;
    public string BrokerUri { get; init; } = "tcp://localhost:1883";
}
