// DTOs/LoginRequest.cs
using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class LoginRequest
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;
    
    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}