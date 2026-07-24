// DTOs/ActivationRequest.cs
using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class ActivationRequest
{
    [JsonPropertyName("deviceId")]
    public int DeviceId { get; set; }
    
    [JsonPropertyName("activationCode")]
    public string ActivationCode { get; set; } = string.Empty;
}