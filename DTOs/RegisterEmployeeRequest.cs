// DTOs/RegisterEmployeeRequest.cs
using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class RegisterEmployeeRequest
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("firstName")]
    public string? FirstName { get; set; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("gender")]
    public string? Gender { get; set; }

    // Position maps to Role (Payroll Officer → PayrollOfficer, Finance Manager → FinanceManager)
    [JsonPropertyName("position")]
    public string? Position { get; set; }

    [JsonPropertyName("deviceCode")]
    public string? DeviceCode { get; set; }

    [JsonPropertyName("deviceName")]
    public string? DeviceName { get; set; }
}