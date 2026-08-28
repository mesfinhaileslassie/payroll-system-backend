// DTOs/SalaryPayRequest.cs

using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class SalaryPayRequest
{
    [JsonPropertyName("employeeId")]
    public int EmployeeId { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("paymentMonth")]
    public string PaymentMonth { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("otp")]
    public string Otp { get; set; } = string.Empty;

    // ✅ NEW: Counter used to generate the OTP
    [JsonPropertyName("counter")]
    public long Counter { get; set; }
}