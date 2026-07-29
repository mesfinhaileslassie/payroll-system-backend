// DTOs/UpdateUserRequest.cs
using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class UpdateUserRequest
{
    [JsonPropertyName("firstName")]
    public string? FirstName { get; set; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("gender")]
    public string? Gender { get; set; }

    [JsonPropertyName("position")]
    public string? Position { get; set; }

    [JsonPropertyName("isActive")]
    public bool? IsActive { get; set; }
}