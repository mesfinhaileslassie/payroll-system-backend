// DTOs/BudgetDto.cs
using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class BudgetDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("department")]
    public string Department { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("approvedAt")]
    public DateTime? ApprovedAt { get; set; }  // Added this property
}