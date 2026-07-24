// DTOs/ActivationResponse.cs
using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class ActivationResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    
    [JsonPropertyName("deviceToken")]
    public string? DeviceToken { get; set; }
    
    [JsonPropertyName("secretKey")]
    public string? SecretKey { get; set; }
    
    [JsonPropertyName("status")]
    public string? Status { get; set; }
}