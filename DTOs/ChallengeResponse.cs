// DTOs/ChallengeResponse.cs
using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class ChallengeResponse
{
    [JsonPropertyName("challenge")]
    public string Challenge { get; set; } = string.Empty;

    [JsonPropertyName("expiresIn")]
    public int ExpiresIn { get; set; } // seconds
}