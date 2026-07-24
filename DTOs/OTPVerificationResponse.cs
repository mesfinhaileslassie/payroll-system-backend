using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class OTPVerificationResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("deviceId")]
    public int? DeviceId { get; set; }

    [JsonPropertyName("valid")]
    public bool Valid { get; set; }
}