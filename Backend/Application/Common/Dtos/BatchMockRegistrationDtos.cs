namespace Application.Common.Dtos;

public sealed class BatchMockRegistrationRequestDto
{
    public required IReadOnlyList<MockRegistrationRequestDto> Items { get; init; }
    public bool ContinueOnError { get; init; } = true;
}

public sealed class BatchMockRegistrationResponseDto
{
    public required int RequestedCount { get; init; }
    public required int SucceededCount { get; init; }
    public required int FailedCount { get; init; }
    public required IReadOnlyList<BatchMockRegistrationItemResultDto> Results { get; init; }
}

public sealed class BatchMockRegistrationItemResultDto
{
    public required int Index { get; init; }
    public required bool Success { get; init; }
    public long? SensorId { get; init; }
    public string? ProvisioningToken { get; init; }
    public string? MqttBrokerUri { get; init; }
    public string Ssid { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string? Error { get; init; }
}