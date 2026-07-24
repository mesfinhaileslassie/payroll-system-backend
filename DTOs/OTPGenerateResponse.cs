// DTOs/OTPGenerateResponse.cs
using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class OTPGenerateResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    
    [JsonPropertyName("otp")]
    public string? OTP { get; set; }
    
    [JsonPropertyName("expiresIn")]
    public int ExpiresIn { get; set; } // seconds
}