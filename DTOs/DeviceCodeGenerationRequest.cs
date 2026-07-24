// DTOs/DeviceCodeGenerationRequest.cs
using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class DeviceCodeGenerationRequest
{
    [JsonPropertyName("deviceId")]
    public int DeviceId { get; set; }
}