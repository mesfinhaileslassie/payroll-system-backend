// DTOs/BudgetApproveRequest.cs
using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class BudgetApproveRequest
{
    [JsonPropertyName("otp")]
    public string OTP { get; set; } = string.Empty;
}