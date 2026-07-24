// DTOs/LoginResponse.cs (updated)
using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class LoginResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("userId")]
    public int? UserId { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }   // NEW: "Admin" or "Employee"

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }
}