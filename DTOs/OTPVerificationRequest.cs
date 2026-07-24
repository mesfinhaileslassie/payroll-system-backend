using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class OTPVerificationRequest
{
    [JsonPropertyName("secretKey")]
    public string SecretKey { get; set; } = string.Empty;

    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;
}