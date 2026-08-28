// DTOs/InitiatePaymentRequest.cs

using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class InitiatePaymentRequest
{
    [JsonPropertyName("employeeId")]
    public int EmployeeId { get; set; }

    [JsonPropertyName("paymentMonth")]
    public string PaymentMonth { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }
}