// DTOs/UpdateDeviceRequest.cs
using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class UpdateDeviceRequest
{
    [JsonPropertyName("deviceName")]
    public string DeviceName { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}