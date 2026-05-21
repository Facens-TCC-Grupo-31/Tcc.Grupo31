namespace Application.Common.Enums;

/// <summary>
/// Output format options for the collection route endpoint.
/// </summary>
public enum CollectionRouteOutputFormat
{
    /// <summary>
    /// Standard JSON format with coordinates and sensor metadata.
    /// </summary>
    Json = 0,

    /// <summary>
    /// GeoJSON FeatureCollection format with layered features (route, depot, sensors, stops).
    /// </summary>
    GeoJson = 1
}
