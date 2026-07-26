// DTOs/StoreCounterRequest.cs
using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class StoreCounterRequest
{
    [JsonPropertyName("installationId")]
    public string InstallationId { get; set; } = string.Empty;

    [JsonPropertyName("counter")]
    public long Counter { get; set; }
}