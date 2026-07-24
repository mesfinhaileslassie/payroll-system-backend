// DTOs/VerifyChallengeRequest.cs
using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class VerifyChallengeRequest
{
    [JsonPropertyName("installationId")]
    public string InstallationId { get; set; } = string.Empty;

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;

    [JsonPropertyName("challenge")]
    public string Challenge { get; set; } = string.Empty;
}