using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class DeviceRegistrationRequest
{
    [JsonPropertyName("deviceCode")]
    public string DeviceCode { get; set; } = string.Empty;

    [JsonPropertyName("deviceName")]
    public string? DeviceName { get; set; }

    [JsonPropertyName("employeeUsername")]
    public string EmployeeUsername { get; set; } = string.Empty;
}