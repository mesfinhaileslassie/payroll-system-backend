// DTOs/ChangePasswordRequest.cs
using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class ChangePasswordRequest
{
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("currentPassword")]
    public string CurrentPassword { get; set; } = string.Empty;

    [JsonPropertyName("newPassword")]
    public string NewPassword { get; set; } = string.Empty;
}