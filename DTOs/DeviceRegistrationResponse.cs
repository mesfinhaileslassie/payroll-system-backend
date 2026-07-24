// DTOs/DeviceRegistrationResponse.cs
using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class DeviceRegistrationResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    
    [JsonPropertyName("activationCode")]
    public string? ActivationCode { get; set; }
    
    [JsonPropertyName("deviceToken")]
    public string? DeviceToken { get; set; }
    
    [JsonPropertyName("deviceId")]
    public int? DeviceId { get; set; }
}