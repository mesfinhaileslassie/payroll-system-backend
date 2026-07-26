// DTOs/SalaryPayRequest.cs
using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class SalaryPayRequest
{
    [JsonPropertyName("employeeId")]
    public int EmployeeId { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;  // Finance Manager's username

    [JsonPropertyName("otp")]
    public string Otp { get; set; } = string.Empty;
}