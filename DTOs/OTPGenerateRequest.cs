// DTOs/OTPGenerateRequest.cs
using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class OTPGenerateRequest
{
    [JsonPropertyName("userId")]
    public int UserId { get; set; }
    
    [JsonPropertyName("deviceId")]
    public int DeviceId { get; set; }
}