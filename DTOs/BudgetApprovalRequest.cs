// DTOs/BudgetApprovalRequest.cs
using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class BudgetApprovalRequest
{
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("department")]
    public string Department { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public int UserId { get; set; }
}