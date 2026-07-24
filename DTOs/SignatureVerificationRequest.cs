// DTOs/SignatureVerificationRequest.cs
using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class SignatureVerificationRequest
{
    [JsonPropertyName("deviceId")]
    public int DeviceId { get; set; }
    
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;
}