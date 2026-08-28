// DTOs/InitiatePaymentResponse.cs

using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class InitiatePaymentResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("transactionId")]
    public string? TransactionId { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}