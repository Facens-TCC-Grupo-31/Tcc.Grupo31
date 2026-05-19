namespace Infrastructure.Services.Configuration;

public sealed class SensorLivenessOptions
{
    public const string SectionName = "SensorLiveness";

    public TimeSpan HeartbeatTtl { get; init; } = TimeSpan.FromMinutes(3);
}