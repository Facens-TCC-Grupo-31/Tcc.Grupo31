namespace Infrastructure.Services.Configuration;

public sealed class RoutingOptions
{
    public const string SectionName = "Routing";

    public string DepotKey { get; init; } = "default";
    public double DepotLatitude { get; init; }
    public double DepotLongitude { get; init; }
    public float FillThreshold { get; init; } = 0.8f;
}
