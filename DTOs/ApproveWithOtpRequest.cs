// DTOs/ApproveWithOtpRequest.cs
using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class ApproveWithOtpRequest
{
    [JsonPropertyName("employeeId")]
    public int EmployeeId { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("otp")]
    public string Otp { get; set; } = string.Empty;
}